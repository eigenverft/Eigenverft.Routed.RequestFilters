using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading.Tasks;

using Eigenverft.Routed.RequestFilters.GenericExtensions.HttpResponseExtensions;
using Eigenverft.Routed.RequestFilters.Middleware.Abstractions;
using Eigenverft.Routed.RequestFilters.Middleware.RemoteIpAddressContext;
using Eigenverft.Routed.RequestFilters.Services.DeferredLogger;
using Eigenverft.Routed.RequestFilters.Services.FilteringEvent;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eigenverft.Routed.RequestFilters.Middleware.CidrFiltering
{
    /// <summary>
    /// Middleware that filters requests by comparing the remote IP address against configured CIDR allow/deny lists.
    /// </summary>
    /// <remarks>
    /// Evaluation rules:
    /// <list type="bullet">
    /// <item>Whitelist match: at least one CIDR in <see cref="CidrFilteringOptions.Whitelist"/> contains the remote IP.</item>
    /// <item>Blacklist match: at least one CIDR in <see cref="CidrFilteringOptions.Blacklist"/> contains the remote IP.</item>
    /// <item>If both match, <see cref="CidrFilteringOptions.FilterPriority"/> decides.</item>
    /// </list>
    /// Loopback requests are bypassed and always allowed.
    /// </remarks>
    public class CidrFiltering
    {
        private readonly RequestDelegate _next;
        private readonly IDeferredLogger<CidrFiltering> _logger;
        private readonly IOptionsMonitor<CidrFilteringOptions> _optionsMonitor;
        private readonly IFilteringEventStorage _filteringEventStorage;

        // Cache #1: parsed CIDR ranges and negative parse results (avoid repeated parsing).
        // Cache #2: "IsInList" results for a specific IP + specific CIDR list (avoid repeated membership checks).
        // SizeLimit is used to cap growth; each entry is assigned size 1.
        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10_000 });

        /// <summary>
        /// Initializes a new instance of the <see cref="CidrFiltering"/> class.
        /// </summary>
        /// <param name="nextMiddleware">The next middleware in the pipeline.</param>
        /// <param name="logger">The deferred logger instance.</param>
        /// <param name="optionsMonitor">The options monitor for <see cref="CidrFilteringOptions"/>.</param>
        /// <param name="filteringEventStorage">The central filtering event storage.</param>
        public CidrFiltering(RequestDelegate nextMiddleware, IDeferredLogger<CidrFiltering> logger, IOptionsMonitor<CidrFilteringOptions> optionsMonitor, IFilteringEventStorage filteringEventStorage)
        {
            _next = nextMiddleware ?? throw new ArgumentNullException(nameof(nextMiddleware));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _filteringEventStorage = filteringEventStorage ?? throw new ArgumentNullException(nameof(filteringEventStorage));

            _optionsMonitor.OnChange(_ => _logger.LogDebug("Configuration for {MiddlewareName} updated.", () => nameof(CidrFiltering)));
        }

        /// <summary>
        /// Processes the current request by classifying the remote IP address and applying the configured policy.
        /// </summary>
        /// <param name="context">The current http context.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            CidrFilteringOptions options = _optionsMonitor.CurrentValue;

            IPAddress? remoteIp = context.Connection.RemoteIpAddress;

            // Normalize IPv4-mapped IPv6 so IPv4 CIDRs can match.
            if (remoteIp != null && remoteIp.IsIPv4MappedToIPv6)
            {
                remoteIp = remoteIp.MapToIPv4();
            }

            // Loopback bypass: never filter localhost traffic.
            if (remoteIp != null && IPAddress.IsLoopback(remoteIp))
            {
                _logger.LogDebug("Bypassing {MiddlewareName} for loopback remote IP '{RemoteIp}'.", () => nameof(CidrFiltering), () => remoteIp.ToString());
                await _next(context);
                return;
            }

            // Observed = remote IP string (or empty if unknown).
            string observed = remoteIp?.ToString() ?? string.Empty;

            FilterMatchKind matchKind = Classify(remoteIp, options);

            if (matchKind == FilterMatchKind.Whitelist)
            {
                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(CidrFiltering),
                    context.TraceIdentifier,
                    matchKind,
                    isAllowed: true,
                    observed,
                    loggedForEvaluator: false,
                    options.LogLevelWhitelist,
                    options.LogLevelBlacklist,
                    options.LogLevelUnmatched);

                if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level))
                {
                    _logger.Log(log.Level, log.MessageTemplate, log.Args);
                }

                await _next(context);
                return;
            }

            if (matchKind == FilterMatchKind.Blacklist)
            {
                if (options.RecordBlacklistedRequests)
                {
                    await _filteringEventStorage.StoreAsync(new FilteringEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        EventSource = nameof(CidrFiltering),
                        MatchKind = matchKind,
                        RemoteIpAddress = context.GetRemoteIpAddress(),
                        ObservedValue = observed
                    });
                }

                bool isAllowed = options.AllowBlacklistedRequests;

                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(CidrFiltering),
                    context.TraceIdentifier,
                    matchKind,
                    isAllowed,
                    observed,
                    options.RecordBlacklistedRequests,
                    options.LogLevelWhitelist,
                    options.LogLevelBlacklist,
                    options.LogLevelUnmatched);

                if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level))
                {
                    _logger.Log(log.Level, log.MessageTemplate, log.Args);
                }

                if (isAllowed)
                {
                    await _next(context);
                    return;
                }

                await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
                return;
            }

            if (matchKind == FilterMatchKind.Unmatched)
            {
                if (options.RecordUnmatchedRequests)
                {
                    await _filteringEventStorage.StoreAsync(new FilteringEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        EventSource = nameof(CidrFiltering),
                        MatchKind = matchKind,
                        RemoteIpAddress = context.GetRemoteIpAddress(),
                        ObservedValue = observed
                    });
                }

                bool isAllowed = options.AllowUnmatchedRequests;

                FilterDecisionLogEntry log = FilterDecisionLogBuilder.Create(
                    nameof(CidrFiltering),
                    context.TraceIdentifier,
                    matchKind,
                    isAllowed,
                    observed,
                    options.RecordUnmatchedRequests,
                    options.LogLevelWhitelist,
                    options.LogLevelBlacklist,
                    options.LogLevelUnmatched);

                if (log.Level != LogLevel.None && _logger.IsEnabled(log.Level))
                {
                    _logger.Log(log.Level, log.MessageTemplate, log.Args);
                }

                if (isAllowed)
                {
                    await _next(context);
                    return;
                }

                await context.Response.WriteDefaultStatusCodeAnswerEx(options.BlockStatusCode);
                return;
            }

            _logger.LogCritical(
                "ATTENTION: {MiddlewareName} received an unexpected {EnumType} value '{EnumValue}'. Your filtering logic was extended but this middleware was not updated. This should not happen.",
                () => nameof(CidrFiltering),
                () => nameof(FilterMatchKind),
                () => matchKind);

            await _next(context);
        }

        private static FilterMatchKind Classify(IPAddress? remoteIp, CidrFilteringOptions options)
        {
            if (remoteIp == null)
            {
                return FilterMatchKind.Unmatched;
            }

            bool anyWhitelist = IsInList(remoteIp, options.Whitelist);
            bool anyBlacklist = IsInList(remoteIp, options.Blacklist);

            if (!anyWhitelist && !anyBlacklist)
            {
                return FilterMatchKind.Unmatched;
            }

            if (anyWhitelist && anyBlacklist)
            {
                return options.FilterPriority == FilterPriority.Whitelist
                    ? FilterMatchKind.Whitelist
                    : FilterMatchKind.Blacklist;
            }

            return anyWhitelist ? FilterMatchKind.Whitelist : FilterMatchKind.Blacklist;
        }

        private static bool IsInList(IPAddress ip, string[]? cidrList)
        {
            if (cidrList == null || cidrList.Length == 0)
            {
                return false;
            }

            // Trim entries once to avoid surprises (config sometimes has spaces).
            // "*" short-circuits: match-all.
            if (cidrList.Any(x => string.Equals((x ?? string.Empty).Trim(), "*", StringComparison.Ordinal)))
            {
                return true;
            }

            // Cache the overall "ip in list?" result for this IP + this list (order-independent).
            string listKey = BuildIsInListCacheKey(ip, cidrList);
            if (_cache.TryGetValue(listKey, out bool cached))
            {
                return cached;
            }

            bool result = false;

            for (int i = 0; i < cidrList.Length; i++)
            {
                string cidr = (cidrList[i] ?? string.Empty).Trim();
                if (cidr.Length == 0)
                {
                    continue;
                }

                if (TryGetCachedRange(cidr, out CidrRange range))
                {
                    if (IsInRange(ip, range))
                    {
                        result = true;
                        break;
                    }
                }
            }

            _cache.Set(
                listKey,
                result,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                    Size = 1
                });

            return result;
        }

        private static string BuildIsInListCacheKey(IPAddress ip, string[] cidrList)
        {
            // Ensure order-independent key.
            string[] sorted = cidrList
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => x.Length != 0)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            string joined = string.Join(",", sorted);
            return $"Cidr:IsInList:{ip}|{joined}";
        }

        private static bool TryGetCachedRange(string cidr, out CidrRange range)
        {
            // Cache key includes "cidr:" prefix to avoid collisions with IsInList keys.
            string key = $"Cidr:Range:{cidr}";

            if (_cache.TryGetValue(key, out CidrRangeCacheEntry cached))
            {
                range = cached.Range;
                return cached.IsValid;
            }

            bool ok = TryParseCidr(cidr, out range);

            _cache.Set(
                key,
                new CidrRangeCacheEntry(ok, range),
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                    Size = 1
                });

            return ok;
        }

        private static bool IsInRange(IPAddress ip, CidrRange range)
        {
            // Normalize IPv4-mapped IPv6 input.
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            // Address family mismatch cannot match.
            if (ip.AddressFamily != range.AddressFamily)
            {
                return false;
            }

            BigInteger ipValue = ToUnsignedBigInteger(ip);

            return ipValue >= range.LowerInclusive && ipValue <= range.UpperInclusive;
        }

        private static bool TryParseCidr(string cidr, out CidrRange range)
        {
            range = default;

            if (string.IsNullOrWhiteSpace(cidr))
            {
                return false;
            }

            string[] parts = cidr.Split('/');
            if (parts.Length != 2)
            {
                return false;
            }

            if (!IPAddress.TryParse(parts[0].Trim(), out IPAddress? baseIp))
            {
                return false;
            }

            if (!int.TryParse(parts[1].Trim(), out int prefixLength))
            {
                return false;
            }

            if (baseIp.IsIPv4MappedToIPv6)
            {
                baseIp = baseIp.MapToIPv4();
            }

            int bitLength = baseIp.AddressFamily == AddressFamily.InterNetwork ? 32 :
                            baseIp.AddressFamily == AddressFamily.InterNetworkV6 ? 128 :
                            0;

            if (bitLength == 0)
            {
                return false;
            }

            if (prefixLength < 0 || prefixLength > bitLength)
            {
                return false;
            }

            BigInteger ipValue = ToUnsignedBigInteger(baseIp);

            // max = 2^bitLength - 1
            BigInteger maxValue = (BigInteger.One << bitLength) - 1;

            // mask = top prefixLength bits set
            BigInteger mask = ~((BigInteger.One << (bitLength - prefixLength)) - 1) & maxValue;

            BigInteger lower = ipValue & mask;
            BigInteger upper = lower | (~mask & maxValue);

            range = new CidrRange(baseIp.AddressFamily, lower, upper);
            return true;
        }

        private static BigInteger ToUnsignedBigInteger(IPAddress ip)
        {
            byte[] bytes = ip.GetAddressBytes();

            // BigInteger expects little-endian; reverse.
            byte[] littleEndian = (byte[])bytes.Clone();
            Array.Reverse(littleEndian);

            // Ensure unsigned: add a zero sign byte if highest bit would set sign.
            if (littleEndian.Length > 0 && littleEndian[littleEndian.Length - 1] >= 0x80)
            {
                Array.Resize(ref littleEndian, littleEndian.Length + 1);
                littleEndian[littleEndian.Length - 1] = 0;
            }

            return new BigInteger(littleEndian);
        }

        private readonly struct CidrRange
        {
            public CidrRange(AddressFamily addressFamily, BigInteger lowerInclusive, BigInteger upperInclusive)
            {
                AddressFamily = addressFamily;
                LowerInclusive = lowerInclusive;
                UpperInclusive = upperInclusive;
            }

            public AddressFamily AddressFamily { get; }

            public BigInteger LowerInclusive { get; }

            public BigInteger UpperInclusive { get; }
        }

        private readonly struct CidrRangeCacheEntry
        {
            public CidrRangeCacheEntry(bool isValid, CidrRange range)
            {
                IsValid = isValid;
                Range = range;
            }

            public bool IsValid { get; }

            public CidrRange Range { get; }
        }
    }
}
