using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestRateSmoothing
{
    /// <summary>
    /// Middleware that smooths per-client request throughput by applying delay based on a rolling sliding window.
    /// </summary>
    /// <remarks>
    /// Reviewer note:
    /// This implementation uses a rolling window approximation with fixed-size time buckets.
    /// It intentionally does not reject requests; it only delays them.
    /// Hysteresis is used to prevent oscillation where the delay affects the measured request count.
    /// </remarks>
    public sealed class RequestRateSmoothing
    {
        private const string SharedBucketClientKey = "__missing_client_key__";

        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<RequestRateSmoothing> _logger;
        private readonly IOptionsMonitor<RequestRateSmoothingOptions> _optionsMonitor;

        private readonly ConcurrentDictionary<string, ClientState> _clients = new(StringComparer.OrdinalIgnoreCase);
        private long _requestNumber;

        private volatile Snapshot _snapshot;

        /// <summary>
        /// Initializes a new instance of the <see cref="RequestRateSmoothing"/> class.
        /// </summary>
        /// <param name="next">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor.</param>
        public RequestRateSmoothing(
            RequestDelegate next,
            IDeferredLogger<RequestRateSmoothing> logger,
            IOptionsMonitor<RequestRateSmoothingOptions> optionsMonitor)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));

            _snapshot = Snapshot.From(_optionsMonitor.CurrentValue);

            _optionsMonitor.OnChange(o =>
            {
                _snapshot = Snapshot.From(o);
                _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(RequestRateSmoothing));
            });
        }

        /// <summary>
        /// Processes the current request.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var snap = _snapshot;

            // Fast path: no steps configured => no smoothing behavior.
            if (snap.Steps.Length == 0)
            {
                await _next(context);
                return;
            }

            var resolvedKey = context.GetRemoteIpAddress();

            string clientKey;
            bool isSharedBucket;

            if (string.IsNullOrWhiteSpace(resolvedKey))
            {
                if (snap.MissingKeyBehavior == MissingClientKeyBehavior.Bypass)
                {
                    await _next(context);
                    return;
                }

                clientKey = SharedBucketClientKey;
                isSharedBucket = true;
            }
            else
            {
                clientKey = resolvedKey!;
                isSharedBucket = false;
            }

            var nowMs = Environment.TickCount64;

            // Reviewer note:
            // We want a reliable "is new client" signal. ConcurrentDictionary.GetOrAdd does not expose whether
            // it added or reused. This TryGetValue/TryAdd pattern is a common, low-contention alternative.
            var isNewClient = false;
            if (!_clients.TryGetValue(clientKey, out var state))
            {
                var newState = new ClientState(snap.BucketCount, nowMs, snap.BucketMs);
                if (_clients.TryAdd(clientKey, newState))
                {
                    state = newState;
                    isNewClient = true;
                }
                else
                {
                    state = _clients[clientKey];
                }
            }

            int delayMs;
            int totalInWindowSnapshot;
            int oldLevelSnapshot;
            int newLevelSnapshot;
            bool levelChanged;
            bool logFirstSeenBelowFirstStep;

            // New: optional per-request observation logging
            bool logObservation;
            int currentBucketCountSnapshot;
            double requestRatePerSecondSnapshot;

            lock (state.Gate)
            {
                state.LastSeenMs = nowMs;

                AdvanceWindow(state, nowMs, snap.BucketMs, snap.BucketCount);

                // Record this request in the current bucket.
                state.Buckets[state.BucketIndex]++;
                state.TotalInWindow++;

                oldLevelSnapshot = state.Level;

                var desiredLevel = ResolveLevel(state.TotalInWindow, snap.Steps);

                // Step-up immediately.
                if (desiredLevel > state.Level)
                {
                    state.Level = desiredLevel;
                    state.HoldUntilMs = nowMs + snap.HoldMs;
                    state.LastStepDownMs = nowMs;
                }
                // Step-down slowly (hold + interval + hysteresis).
                else if (desiredLevel < state.Level)
                {
                    if (nowMs >= state.HoldUntilMs &&
                        snap.StepDownEveryMs > 0 &&
                        (nowMs - state.LastStepDownMs) >= snap.StepDownEveryMs)
                    {
                        var currentUpThreshold = snap.Steps[state.Level - 1].Exceeds;
                        var downThreshold = (int)Math.Floor(currentUpThreshold * snap.StepDownHysteresisRatio);

                        // Reviewer note: downThreshold <= upThreshold is expected; ratio controls "stickiness".
                        if (state.TotalInWindow <= downThreshold)
                        {
                            state.Level--;
                            state.LastStepDownMs = nowMs;
                        }
                    }
                }

                newLevelSnapshot = state.Level;
                levelChanged = newLevelSnapshot != oldLevelSnapshot;

                delayMs = GetDelayForLevel(newLevelSnapshot, snap);

                totalInWindowSnapshot = state.TotalInWindow;

                // First-seen log: only if the client is still below the first threshold (level 0).
                logFirstSeenBelowFirstStep = isNewClient && newLevelSnapshot == 0;

                // New:
                // "Current bucket count" is cheap (no scan).
                // "Rate" is approximated over the effective window size (TotalInWindow / windowSeconds).
                logObservation = snap.LogLevelClientSmoothingObservation != LogLevel.None;
                currentBucketCountSnapshot = state.Buckets[state.BucketIndex];

                var windowSeconds = snap.WindowMs <= 0 ? 0.0 : snap.WindowMs / 1000.0;
                requestRatePerSecondSnapshot = windowSeconds <= 0.0 ? 0.0 : (totalInWindowSnapshot / windowSeconds);
            }

            // Log outside the lock to minimize contention.
            if (logFirstSeenBelowFirstStep)
            {
                var firstThreshold = snap.Steps.Length > 0 ? snap.Steps[0].Exceeds : 0;

                LogAt(
                    snap.LogLevelClientObservedBelowFirstStep,
                    "Request smoothing observed new client {ClientKey} below first step (count={Count}, firstThreshold={FirstThreshold}, windowMs={WindowMs}, sharedBucket={SharedBucket}).",
                    () => clientKey,
                    () => totalInWindowSnapshot,
                    () => firstThreshold,
                    () => snap.WindowMs,
                    () => isSharedBucket);
            }

            if (levelChanged)
            {
                var maxLevel = snap.Steps.Length;
                var isEnteringMax = newLevelSnapshot == maxLevel && oldLevelSnapshot != maxLevel;

                var levelForThisChange = isEnteringMax
                    ? snap.LogLevelClientEnteredMaxSmoothingLevel
                    : snap.LogLevelClientSmoothingLevelChanged;

                var oldDelayMs = GetDelayForLevel(oldLevelSnapshot, snap);
                var newDelayMs = GetDelayForLevel(newLevelSnapshot, snap);

                LogAt(
                    levelForThisChange,
                    "Request smoothing level changed for {ClientKey}: {OldLevel}->{NewLevel} (delayMs {OldDelay}->{NewDelay}, count={Count}, windowMs={WindowMs}, maxLevel={MaxLevel}, sharedBucket={SharedBucket}).",
                    () => clientKey,
                    () => oldLevelSnapshot,
                    () => newLevelSnapshot,
                    () => oldDelayMs,
                    () => newDelayMs,
                    () => totalInWindowSnapshot,
                    () => snap.WindowMs,
                    () => maxLevel,
                    () => isSharedBucket);
            }

            // New: optional per-request observation log (cheap rate approximation)
            if (logObservation)
            {
                LogAt(
                    snap.LogLevelClientSmoothingObservation,
                    "Request smoothing observe {ClientKey}: level={Level} delayMs={DelayMs} countWindow={CountWindow} ratePerSec~={RatePerSec} countBucket={CountBucket} windowMs={WindowMs} bucketMs={BucketMs} sharedBucket={SharedBucket}.",
                    () => clientKey,
                    () => newLevelSnapshot,
                    () => delayMs,
                    () => totalInWindowSnapshot,
                    () => requestRatePerSecondSnapshot.ToString("0.###"),
                    () => currentBucketCountSnapshot,
                    () => snap.WindowMs,
                    () => snap.BucketMs,
                    () => isSharedBucket);
            }

            if (delayMs > 0)
            {
                try
                {
                    await Task.Delay(delayMs, context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    // Reviewer note: If the client disconnects, avoid doing further work.
                    return;
                }
            }

            // Opportunistic cleanup (cheap trigger, sweep is occasional).
            var n = Interlocked.Increment(ref _requestNumber);
            if (snap.CleanupEveryNRequests > 0 && (n % snap.CleanupEveryNRequests) == 0)
            {
                CleanupStaleClients(nowMs, snap.ForgetAfterIdleMs);
            }

            await _next(context);
        }

        private void LogAt(LogLevel level, string messageTemplate, params Func<object?>[] args)
        {
            if (level == LogLevel.None)
            {
                return;
            }

            // Reviewer note:
            // Keep this mapping narrow to the methods most likely exposed by IDeferredLogger.
            // Trace is mapped to Debug; Critical is mapped to Error.
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    _logger.LogDebug(messageTemplate, args);
                    break;

                case LogLevel.Information:
                    _logger.LogInformation(messageTemplate, args);
                    break;

                case LogLevel.Warning:
                    _logger.LogWarning(messageTemplate, args);
                    break;

                case LogLevel.Error:
                case LogLevel.Critical:
                    _logger.LogError(messageTemplate, args);
                    break;

                default:
                    _logger.LogInformation(messageTemplate, args);
                    break;
            }
        }

        private static int GetDelayForLevel(int level, Snapshot snap)
        {
            if (level <= 0)
            {
                return 0;
            }

            var idx = level - 1;
            if ((uint)idx >= (uint)snap.Steps.Length)
            {
                return 0;
            }

            var delay = snap.Steps[idx].DelayMs;

            if (delay > 0 && snap.ClampDelayMs > 0 && delay > snap.ClampDelayMs)
            {
                delay = snap.ClampDelayMs;
            }

            return delay;
        }

        private static int ResolveLevel(int totalInWindow, Step[] stepsSortedAscending)
        {
            // steps are sorted ascending by Exceeds, so scan backwards for the highest threshold exceeded.
            for (int i = stepsSortedAscending.Length - 1; i >= 0; i--)
            {
                if (totalInWindow > stepsSortedAscending[i].Exceeds)
                {
                    return i + 1; // level is 1-based (0 means "no delay")
                }
            }

            return 0;
        }

        private static void AdvanceWindow(ClientState state, long nowMs, long bucketMs, int bucketCount)
        {
            // Reviewer note: BucketStartMs represents the start time of the current bucket.
            // We advance buckets when time moved forward. Old buckets are cleared and subtracted from TotalInWindow.
            var elapsedMs = nowMs - state.BucketStartMs;
            if (elapsedMs < bucketMs)
            {
                return;
            }

            var advance = (int)(elapsedMs / bucketMs);

            if (advance >= bucketCount)
            {
                // Too much time passed; the entire window is stale.
                Array.Clear(state.Buckets, 0, state.Buckets.Length);
                state.TotalInWindow = 0;

                state.BucketIndex = (state.BucketIndex + (advance % bucketCount)) % bucketCount;
                state.BucketStartMs += (long)advance * bucketMs;
                return;
            }

            for (int i = 0; i < advance; i++)
            {
                state.BucketIndex = (state.BucketIndex + 1) % bucketCount;

                var old = state.Buckets[state.BucketIndex];
                if (old != 0)
                {
                    state.TotalInWindow -= old;
                    state.Buckets[state.BucketIndex] = 0;
                }
            }

            state.BucketStartMs += (long)advance * bucketMs;
        }

        private void CleanupStaleClients(long nowMs, long forgetAfterIdleMs)
        {
            if (forgetAfterIdleMs <= 0)
            {
                return;
            }

            foreach (var kvp in _clients)
            {
                var state = kvp.Value;
                long lastSeen;

                lock (state.Gate)
                {
                    lastSeen = state.LastSeenMs;
                }

                if ((nowMs - lastSeen) >= forgetAfterIdleMs)
                {
                    _clients.TryRemove(kvp.Key, out _);
                }
            }
        }

        private sealed class ClientState
        {
            public ClientState(int bucketCount, long nowMs, long bucketMs)
            {
                Gate = new object();
                Buckets = new int[bucketCount];

                // Reviewer note: Align start to the current bucket boundary to avoid drift.
                BucketStartMs = bucketMs <= 0 ? nowMs : (nowMs - (nowMs % bucketMs));
                BucketIndex = 0;

                LastSeenMs = nowMs;

                Level = 0;
                HoldUntilMs = 0;
                LastStepDownMs = 0;
            }

            public object Gate { get; }
            public int[] Buckets { get; }

            public long BucketStartMs;
            public int BucketIndex;

            public int TotalInWindow;

            public int Level;
            public long HoldUntilMs;
            public long LastStepDownMs;

            public long LastSeenMs;
        }

        private readonly struct Step
        {
            public Step(int exceeds, int delayMs)
            {
                Exceeds = exceeds;
                DelayMs = delayMs;
            }

            public int Exceeds { get; }
            public int DelayMs { get; }
        }

        private sealed class Snapshot
        {
            public long BucketMs { get; private set; }
            public int BucketCount { get; private set; }
            public long WindowMs { get; private set; }

            public long HoldMs { get; private set; }
            public long StepDownEveryMs { get; private set; }
            public double StepDownHysteresisRatio { get; private set; }

            public long ForgetAfterIdleMs { get; private set; }
            public int CleanupEveryNRequests { get; private set; }

            public int ClampDelayMs { get; private set; }
            public MissingClientKeyBehavior MissingKeyBehavior { get; private set; }

            public LogLevel LogLevelClientObservedBelowFirstStep { get; private set; }
            public LogLevel LogLevelClientSmoothingLevelChanged { get; private set; }
            public LogLevel LogLevelClientEnteredMaxSmoothingLevel { get; private set; }

            // New
            public LogLevel LogLevelClientSmoothingObservation { get; private set; }

            public Step[] Steps { get; private set; } = Array.Empty<Step>();

            public static Snapshot From(RequestRateSmoothingOptions? o)
            {
                o ??= new RequestRateSmoothingOptions();

                var windowMsRaw = (long)Math.Max(1, o.WindowSize.TotalMilliseconds);
                var bucketMs = (long)Math.Max(1, o.BucketSize.TotalMilliseconds);

                // Reviewer note: bucketCount is ceil(window/bucket). Effective window becomes bucketCount * bucketMs.
                var bucketCount = (int)Math.Max(1, (windowMsRaw + bucketMs - 1) / bucketMs);
                var windowMs = (long)bucketCount * bucketMs;

                var holdMs = (long)Math.Max(0, o.HoldLevelAfterIncreaseFor.TotalMilliseconds);
                var stepDownEveryMs = (long)Math.Max(0, o.StepDownEvery.TotalMilliseconds);

                var ratio = o.StepDownHysteresisRatio;
                if (double.IsNaN(ratio) || double.IsInfinity(ratio))
                {
                    ratio = 0.8;
                }
                ratio = Math.Max(0.0, Math.Min(1.0, ratio));

                var idleMs = (long)Math.Max(0, o.ForgetClientAfterNoRequestsFor.TotalMilliseconds);
                var cleanupN = o.RunStaleClientCleanupEveryNRequests <= 0 ? 0 : o.RunStaleClientCleanupEveryNRequests;

                var clamp = o.ClampDelayToAtMostMilliseconds < 0 ? 0 : o.ClampDelayToAtMostMilliseconds;

                var steps = (o.Steps?.ToArray() ?? Array.Empty<RequestRateSmoothingStep>())
                   .Where(s => s != null)
                   .Select(s => new Step(Math.Max(0, s.ExceedsRequestsInWindow), Math.Max(0, s.DelayMilliseconds)))
                   .Where(s => s.Exceeds > 0 || s.DelayMs > 0)
                   .OrderBy(s => s.Exceeds)
                   .ToArray();

                return new Snapshot
                {
                    BucketMs = bucketMs,
                    BucketCount = bucketCount,
                    WindowMs = windowMs,

                    HoldMs = holdMs,
                    StepDownEveryMs = stepDownEveryMs,
                    StepDownHysteresisRatio = ratio,

                    ForgetAfterIdleMs = idleMs,
                    CleanupEveryNRequests = cleanupN,

                    ClampDelayMs = clamp,
                    MissingKeyBehavior = o.MissingClientKeyBehavior,

                    LogLevelClientObservedBelowFirstStep = o.LogLevelClientObservedBelowFirstStep,
                    LogLevelClientSmoothingLevelChanged = o.LogLevelClientSmoothingLevelChanged,
                    LogLevelClientEnteredMaxSmoothingLevel = o.LogLevelClientEnteredMaxSmoothingLevel,

                    LogLevelClientSmoothingObservation = o.LogLevelClientSmoothingObservation,

                    Steps = steps,
                };
            }
        }
    }
}
