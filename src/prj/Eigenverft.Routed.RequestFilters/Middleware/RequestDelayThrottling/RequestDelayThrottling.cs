using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.RequestDelayThrottling
{
    /// <summary>
    /// Middleware that applies per-client request delays based on request frequency.
    /// </summary>
    public sealed class RequestDelayThrottling
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<RequestDelayThrottling> _logger;
        private readonly IOptionsMonitor<RequestDelayThrottlingOptions> _optionsMonitor;

        private readonly ConcurrentDictionary<string, ClientState> _clients = new(StringComparer.OrdinalIgnoreCase);
        private long _requestNumber;

        private volatile Snapshot _snapshot;

        /// <summary>
        /// Initializes a new instance of the <see cref="RequestDelayThrottling"/> class.
        /// </summary>
        /// <param name="next">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor.</param>
        public RequestDelayThrottling(RequestDelegate next, IDeferredLogger<RequestDelayThrottling> logger, IOptionsMonitor<RequestDelayThrottlingOptions> optionsMonitor)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));

            _snapshot = Snapshot.From(_optionsMonitor.CurrentValue);

            _optionsMonitor.OnChange(o =>
            {
                _snapshot = Snapshot.From(o);
                _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(RequestDelayThrottling));
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

            // Fast path: no steps configured => no throttling behavior.
            if (snap.Steps.Length == 0)
            {
                await _next(context);
                return;
            }

            // Use your existing normalized remote IP accessor if present.
            var clientKey = context.GetRemoteIpAddress() ?? string.Empty;

            var nowMs = Environment.TickCount64;

            var state = _clients.GetOrAdd(clientKey, _ => new ClientState());

            int delayMs;
            lock (state.Gate)
            {
                state.LastSeenMs = nowMs;

                if ((nowMs - state.WindowStartMs) >= snap.WindowMs)
                {
                    state.WindowStartMs = nowMs;
                    state.CountInWindow = 0;
                }

                state.CountInWindow++;

                delayMs = ResolveDelayMs(state.CountInWindow, snap.Steps);

                if (delayMs > 0 && snap.ClampDelayMs > 0 && delayMs > snap.ClampDelayMs)
                {
                    delayMs = snap.ClampDelayMs;
                }
            }

            if (delayMs > 0)
            {
                try
                {
                    await Task.Delay(delayMs, context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    // If the client disconnects, do not waste work.
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

        private static int ResolveDelayMs(int countInWindow, Step[] stepsSortedAscending)
        {
            // steps are sorted ascending by Exceeds, so scan backwards for the highest threshold exceeded.
            for (int i = stepsSortedAscending.Length - 1; i >= 0; i--)
            {
                if (countInWindow > stepsSortedAscending[i].Exceeds)
                {
                    return stepsSortedAscending[i].DelayMs;
                }
            }

            return 0;
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
            public readonly object Gate = new();

            public long WindowStartMs = Environment.TickCount64;
            public int CountInWindow;
            public long LastSeenMs = Environment.TickCount64;
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
            public long WindowMs { get; private set; }
            public long ForgetAfterIdleMs { get; private set; }
            public int CleanupEveryNRequests { get; private set; }
            public int ClampDelayMs { get; private set; }
            public Step[] Steps { get; private set; } = Array.Empty<Step>();

            public static Snapshot From(RequestDelayThrottlingOptions? o)
            {
                o ??= new RequestDelayThrottlingOptions();

                var windowMs = (long)Math.Max(1, o.CountRequestsWithin.TotalMilliseconds);
                var idleMs = (long)Math.Max(0, o.ForgetClientAfterNoRequestsFor.TotalMilliseconds);
                var cleanupN = o.RunStaleClientCleanupEveryNRequests <= 0 ? 0 : o.RunStaleClientCleanupEveryNRequests;
                var clamp = o.ClampDelayToAtMostMilliseconds < 0 ? 0 : o.ClampDelayToAtMostMilliseconds;

                var steps = (o.DelaySteps ?? Array.Empty<RequestDelayStep>())
                    .Where(s => s != null)
                    .Select(s => new Step(Math.Max(0, s.Exceeds), Math.Max(0, s.DelayMilliseconds)))
                    .OrderBy(s => s.Exceeds)
                    .ToArray();

                return new Snapshot
                {
                    WindowMs = windowMs,
                    ForgetAfterIdleMs = idleMs,
                    CleanupEveryNRequests = cleanupN,
                    ClampDelayMs = clamp,
                    Steps = steps,
                };
            }
        }
    }
}