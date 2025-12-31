// -----------------------------------------------------------------------------
// EncodedAppSettingsLayerStandalone.cs
//
// Self-contained helpers to:
//   1) Encode sensitive string values "at rest" inside appsettings.json (and optional appsettings.{Environment}.json)
//   2) Load those JSON files via a decoding JSON configuration provider (decode happens in-memory on load/reload)
//
// Reviewer notes:
//   - This file is intentionally standalone: no external Guard/Toolkit dependencies.
//   - It provides a deterministic appsettings-style layering: common first, then environment override (if present).
//   - It rejects ambiguous casing duplicates on case-sensitive file systems.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.Routed.RequestFilters.Utilities.Settings.Encoding.AppSettingsLayering
{
    /// <summary>
    /// Adds an "encoded appsettings layer": encode values at rest on disk, then load JSON with decoding enabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layering mirrors the default behavior: common settings first, then environment override settings second.
    /// The environment override file name is derived from the common file name, for example:
    /// <c>appsettings.json</c> plus <c>appsettings.Development.json</c>.
    /// </para>
    /// <para>
    /// Encoding modifies the JSON files on disk. Decoding happens in-memory during provider load and reload.
    /// </para>
    /// </remarks>
    public static class EncodedAppSettingsLayerExtensions
    {
        /// <summary>
        /// Do-it-all helper: encodes values at rest in the common appsettings JSON file and (if present) the environment override file,
        /// then loads both files with decoding enabled.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Reviewer note: Call this once early. If you also keep the default appsettings loading stack, you may load the same
        /// JSON files twice, which can confuse precedence.
        /// </para>
        /// <para>Example:</para>
        /// <code><![CDATA[
        /// var settingsPath = Path.Combine(defaultDirs["ApplicationSettings"], "appsettings.json");
        ///
        /// builder.Configuration.AddEncodedAppSettingsLayer(
        ///     commonAppSettingsJsonPath: settingsPath,
        ///     hostEnvironment: builder.Environment,
        ///     keyPathPattern: "*Passw*",
        ///     encode: SettingsValueEncoders.EncodeDpapiMachineBase64,
        ///     optionalCommon: false,
        ///     optionalEnvironment: true,
        ///     reloadOnChange: true,
        ///     nullAsEmpty: true,
        ///     encodeEnvironmentFileIfPresent: true,
        ///     enableEncodingStep: true);
        /// ]]></code>
        /// </remarks>
        /// <param name="configuration">The configuration manager to mutate and add providers to.</param>
        /// <param name="commonAppSettingsJsonPath">Full path to the common appsettings file, for example <c>.../appsettings.json</c>.</param>
        /// <param name="hostEnvironment">The host environment used to locate the environment override file.</param>
        /// <param name="keyPathPattern">Glob pattern matched against full key paths (for example <c>*Passw*</c>).</param>
        /// <param name="encode">Encoder function that produces the persisted encoded value.</param>
        /// <param name="optionalCommon">When <see langword="true"/>, missing common file is allowed on load (encoding still requires it if enabled).</param>
        /// <param name="optionalEnvironment">When <see langword="true"/>, missing environment override file is allowed.</param>
        /// <param name="reloadOnChange">When <see langword="true"/>, JSON files are reloaded on change.</param>
        /// <param name="nullAsEmpty">When <see langword="true"/>, JSON null is treated as empty string and encoded.</param>
        /// <param name="encodeEnvironmentFileIfPresent">When <see langword="true"/>, also encodes the environment override file if it exists.</param>
        /// <param name="enableEncodingStep">When <see langword="true"/>, performs the encoding step (mutates disk). Disable to load-only.</param>
        /// <returns>The same <see cref="ConfigurationManager"/> for chaining.</returns>
        public static ConfigurationManager AddEncodedAppSettingsLayer(
            this ConfigurationManager configuration,
            string commonAppSettingsJsonPath,
            IHostEnvironment hostEnvironment,
            string keyPathPattern,
            Func<string, string> encode,
            bool optionalCommon = false,
            bool optionalEnvironment = true,
            bool reloadOnChange = true,
            bool nullAsEmpty = true,
            bool encodeEnvironmentFileIfPresent = true,
            bool enableEncodingStep = true)
        {
            EvfGuard.NotNull(configuration);
            EvfGuard.NotNull(hostEnvironment);
            EvfGuard.NotNullOrWhiteSpace(commonAppSettingsJsonPath);
            EvfGuard.NotNullOrWhiteSpace(keyPathPattern);
            EvfGuard.NotNull(encode);

            if (enableEncodingStep)
            {
                _ = configuration.EncodeAppSettingsLayerValuesInPlace(
                    commonAppSettingsJsonPath: commonAppSettingsJsonPath,
                    hostEnvironment: hostEnvironment,
                    keyPathPattern: keyPathPattern,
                    encode: encode,
                    nullAsEmpty: nullAsEmpty,
                    encodeEnvironmentFileIfPresent: encodeEnvironmentFileIfPresent);
            }

            _ = ((IConfigurationBuilder)configuration).AddDecodedAppSettingsLayer(
                commonAppSettingsJsonPath: commonAppSettingsJsonPath,
                hostEnvironment: hostEnvironment,
                optionalCommon: optionalCommon,
                optionalEnvironment: optionalEnvironment,
                reloadOnChange: reloadOnChange);

            return configuration;
        }

        /// <summary>
        /// Encodes matching values in the common appsettings JSON file and (if present) the environment override file.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Reviewer note: This mutates JSON files on disk. Ensure file ACLs and deployment process allow it.
        /// </para>
        /// </remarks>
        /// <param name="configuration">Configuration manager used for convenience.</param>
        /// <param name="commonAppSettingsJsonPath">Full path to the common appsettings file.</param>
        /// <param name="hostEnvironment">Host environment used to locate the override file.</param>
        /// <param name="keyPathPattern">Glob pattern matched against full key paths.</param>
        /// <param name="encode">Encoder function.</param>
        /// <param name="nullAsEmpty">Treat JSON null as empty string and encode it.</param>
        /// <param name="encodeEnvironmentFileIfPresent">Also encodes environment override file if found.</param>
        /// <returns>Total number of updated values across processed files.</returns>
        public static int EncodeAppSettingsLayerValuesInPlace(
            this ConfigurationManager configuration,
            string commonAppSettingsJsonPath,
            IHostEnvironment hostEnvironment,
            string keyPathPattern,
            Func<string, string> encode,
            bool nullAsEmpty = true,
            bool encodeEnvironmentFileIfPresent = true)
        {
            EvfGuard.NotNull(configuration);
            EvfGuard.NotNull(hostEnvironment);
            EvfGuard.NotNullOrWhiteSpace(commonAppSettingsJsonPath);
            EvfGuard.NotNullOrWhiteSpace(keyPathPattern);
            EvfGuard.NotNull(encode);

            if (!File.Exists(commonAppSettingsJsonPath))
            {
                throw new FileNotFoundException("Common appsettings file not found.", commonAppSettingsJsonPath);
            }

            var updated = 0;

            updated += JsonSettingsFileEncoder.EncodeStringValues(
                jsonFilePath: commonAppSettingsJsonPath,
                keyPathPattern: keyPathPattern,
                encode: encode,
                nullAsEmpty: nullAsEmpty);

            if (encodeEnvironmentFileIfPresent &&
                AppSettingsEnvironmentFileResolver.TryResolveEnvironmentOverride(
                    commonAppSettingsJsonPath,
                    hostEnvironment.EnvironmentName,
                    out var environmentJsonPath))
            {
                updated += JsonSettingsFileEncoder.EncodeStringValues(
                    jsonFilePath: environmentJsonPath,
                    keyPathPattern: keyPathPattern,
                    encode: encode,
                    nullAsEmpty: nullAsEmpty);
            }

            return updated;
        }

        /// <summary>
        /// Adds a layered JSON configuration (common file plus optional environment override file) using decoding JSON providers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Precedence is last-added-wins: environment override values win over common values for the same keys.
        /// </para>
        /// </remarks>
        /// <param name="builder">Configuration builder.</param>
        /// <param name="commonAppSettingsJsonPath">Full path to common appsettings JSON file.</param>
        /// <param name="hostEnvironment">Host environment used to locate the override file.</param>
        /// <param name="optionalCommon">When <see langword="true"/>, common file is optional.</param>
        /// <param name="optionalEnvironment">When <see langword="true"/>, environment override file is optional.</param>
        /// <param name="reloadOnChange">When <see langword="true"/>, reloads when the file changes.</param>
        /// <returns>The same builder for chaining.</returns>
        public static IConfigurationBuilder AddDecodedAppSettingsLayer(
            this IConfigurationBuilder builder,
            string commonAppSettingsJsonPath,
            IHostEnvironment hostEnvironment,
            bool optionalCommon = false,
            bool optionalEnvironment = true,
            bool reloadOnChange = true)
        {
            EvfGuard.NotNull(builder);
            EvfGuard.NotNull(hostEnvironment);
            EvfGuard.NotNullOrWhiteSpace(commonAppSettingsJsonPath);

            builder.AddJsonFileWithDecodingStandalone(
                path: commonAppSettingsJsonPath,
                optional: optionalCommon,
                reloadOnChange: reloadOnChange);

            if (AppSettingsEnvironmentFileResolver.TryResolveEnvironmentOverride(
                    commonAppSettingsJsonPath,
                    hostEnvironment.EnvironmentName,
                    out var environmentJsonPath))
            {
                builder.AddJsonFileWithDecodingStandalone(
                    path: environmentJsonPath,
                    optional: false,
                    reloadOnChange: reloadOnChange);

                return builder;
            }

            if (!optionalEnvironment)
            {
                var expected = AppSettingsEnvironmentFileResolver.GetExpectedEnvironmentOverridePath(
                    commonAppSettingsJsonPath,
                    hostEnvironment.EnvironmentName);

                throw new FileNotFoundException("Environment appsettings file not found (required).", expected);
            }

            return builder;
        }
    }

    /// <summary>
    /// Minimal guard helpers to keep this file standalone.
    /// </summary>
    internal static class EvfGuard
    {
        public static void NotNull(object? value)
        {
            ArgumentNullException.ThrowIfNull(value);
        }

        public static void NotNullOrWhiteSpace(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value must not be null, empty, or whitespace.", nameof(value));
            }
        }
    }

    /// <summary>
    /// Resolves appsettings-style environment override JSON files next to a common appsettings JSON file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On case-sensitive file systems, multiple casing variants can exist at once. This resolver rejects ambiguous matches.
    /// </para>
    /// </remarks>
    internal static class AppSettingsEnvironmentFileResolver
    {
        public static bool TryResolveEnvironmentOverride(string commonAppSettingsJsonPath, string environmentName, out string environmentAppSettingsJsonPath)
        {
            environmentAppSettingsJsonPath = string.Empty;

            EvfGuard.NotNullOrWhiteSpace(commonAppSettingsJsonPath);

            environmentName ??= string.Empty;
            if (string.IsNullOrWhiteSpace(environmentName))
            {
                return false;
            }

            var dir = Path.GetDirectoryName(commonAppSettingsJsonPath) ?? string.Empty;
            var file = Path.GetFileName(commonAppSettingsJsonPath);
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(file))
            {
                return false;
            }

            var baseName = Path.GetFileNameWithoutExtension(file);
            var ext = Path.GetExtension(file);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".json";
            }

            var candidates = new[]
            {
                Path.Combine(dir, $"{baseName}.{environmentName}{ext}"),
                Path.Combine(dir, $"{baseName}.{environmentName.ToLowerInvariant()}{ext}"),
                Path.Combine(dir, $"{baseName}.{environmentName.ToUpperInvariant()}{ext}"),
            }
            .Distinct(StringComparer.Ordinal)
            .ToArray();

            var existing = candidates.Where(File.Exists).ToArray();

            if (existing.Length == 0)
            {
                return false;
            }

            if (existing.Length > 1)
            {
                throw new InvalidOperationException(
                    "Multiple environment appsettings files were found. Remove duplicates to keep precedence deterministic. Found: " +
                    string.Join(", ", existing));
            }

            environmentAppSettingsJsonPath = existing[0];
            return true;
        }

        public static string GetExpectedEnvironmentOverridePath(string commonAppSettingsJsonPath, string environmentName)
        {
            environmentName ??= string.Empty;

            var dir = Path.GetDirectoryName(commonAppSettingsJsonPath) ?? string.Empty;
            var file = Path.GetFileName(commonAppSettingsJsonPath);

            var baseName = Path.GetFileNameWithoutExtension(file);
            var ext = Path.GetExtension(file);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".json";
            }

            return Path.Combine(dir, $"{baseName}.{environmentName}{ext}");
        }
    }

    /// <summary>
    /// Encodes string values inside a JSON settings file by matching full key paths.
    /// </summary>
    internal static class JsonSettingsFileEncoder
    {
        public static int EncodeStringValues(string jsonFilePath, string keyPathPattern, Func<string, string> encode, bool nullAsEmpty = true)
        {
            EvfGuard.NotNullOrWhiteSpace(jsonFilePath);
            EvfGuard.NotNullOrWhiteSpace(keyPathPattern);
            EvfGuard.NotNull(encode);

            if (!File.Exists(jsonFilePath))
            {
                throw new FileNotFoundException("Settings file not found.", jsonFilePath);
            }

            var jsonText = File.ReadAllText(jsonFilePath);

            var docOptions = new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            };

            JsonNode? root = JsonNode.Parse(jsonText, nodeOptions: null, documentOptions: docOptions);
            if (root is null)
            {
                throw new InvalidDataException("Parsed JSON root was null.");
            }

            var matcher = new KeyPathGlobMatcher(keyPathPattern);

            var updated = 0;
            WalkAndEncode(root, currentPath: string.Empty, matcher, encode, nullAsEmpty, ref updated);

            var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            WriteAtomically(jsonFilePath, output);
            return updated;
        }

        private static void WalkAndEncode(JsonNode node, string currentPath, KeyPathGlobMatcher matcher, Func<string, string> encode, bool nullAsEmpty, ref int updated)
        {
            if (node is JsonObject obj)
            {
                foreach (var propName in obj.Select(k => k.Key).ToList())
                {
                    var propValue = obj[propName];
                    var propPath = CombinePath(currentPath, propName);

                    if (matcher.IsMatch(propPath))
                    {
                        if (propValue is null)
                        {
                            if (nullAsEmpty)
                            {
                                obj[propName] = encode(string.Empty);
                                updated++;
                            }
                        }
                        else if (propValue is JsonValue && TryGetString(propValue, out var current))
                        {
                            var text = current ?? string.Empty;

                            // Reviewer note: Avoid double-encoding.
                            if (!EncodedValueFormat.TryUnwrap(text, out _, out _))
                            {
                                obj[propName] = encode(text);
                                updated++;
                            }
                        }
                    }

                    if (propValue is not null && propValue is not JsonValue)
                    {
                        WalkAndEncode(propValue, propPath, matcher, encode, nullAsEmpty, ref updated);
                    }
                }

                return;
            }

            if (node is JsonArray arr)
            {
                for (var i = 0; i < arr.Count; i++)
                {
                    var item = arr[i];
                    var itemPath = CombinePath(currentPath, i.ToString());

                    if (matcher.IsMatch(itemPath))
                    {
                        if (item is null)
                        {
                            if (nullAsEmpty)
                            {
                                arr[i] = encode(string.Empty);
                                updated++;
                            }
                        }
                        else if (item is JsonValue jv && TryGetString(jv, out var current))
                        {
                            var text = current ?? string.Empty;

                            if (!EncodedValueFormat.TryUnwrap(text, out _, out _))
                            {
                                arr[i] = encode(text);
                                updated++;
                            }
                        }
                    }

                    if (item is not null && item is not JsonValue)
                    {
                        WalkAndEncode(item, itemPath, matcher, encode, nullAsEmpty, ref updated);
                    }
                }
            }
        }

        private static string CombinePath(string prefix, string segment)
        {
            return string.IsNullOrEmpty(prefix) ? segment : $"{prefix}:{segment}";
        }

        private static bool TryGetString(JsonNode valueNode, out string? value)
        {
            try
            {
                value = valueNode.GetValue<string?>();
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        private static void WriteAtomically(string path, string content)
        {
            var dir = Path.GetDirectoryName(path) ?? ".";
            var tmp = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            File.WriteAllText(tmp, content);

            // Reviewer note: Prefer Replace when possible; fallback to Move(overwrite) when Replace is unsupported.
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, destinationBackupFileName: null);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    // Fall through.
                }
                catch (IOException)
                {
                    // Fall through.
                }

                File.Move(tmp, path, overwrite: true);
                return;
            }

            File.Move(tmp, path);
        }

        private sealed class KeyPathGlobMatcher
        {
            private readonly Regex _regex;

            public KeyPathGlobMatcher(string globPattern)
            {
                EvfGuard.NotNullOrWhiteSpace(globPattern);

                var regex = "^" + Regex.Escape(globPattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                _regex = new Regex(regex, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            }

            public bool IsMatch(string keyPath)
            {
                keyPath ??= string.Empty;
                return _regex.IsMatch(keyPath);
            }
        }
    }

    /// <summary>
    /// Adds JSON configuration providers that decode encoded values during provider load and reload.
    /// </summary>
    internal static class JsonFileWithDecodingStandaloneExtensions
    {
        /// <summary>
        /// Adds a JSON file configuration provider that decodes encoded values during provider load and reload.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Reviewer note: This supports absolute paths by calling <see cref="JsonConfigurationSource.ResolveFileProvider"/>.
        /// </para>
        /// </remarks>
        public static IConfigurationBuilder AddJsonFileWithDecodingStandalone(this IConfigurationBuilder builder, string path, bool optional = false, bool reloadOnChange = false)
        {
            EvfGuard.NotNull(builder);
            EvfGuard.NotNullOrWhiteSpace(path);

            var source = new DecodingJsonConfigurationSource
            {
                Path = path,
                Optional = optional,
                ReloadOnChange = reloadOnChange,
            };

            source.ResolveFileProvider();
            builder.Add(source);
            return builder;
        }

        private sealed class DecodingJsonConfigurationSource : JsonConfigurationSource
        {
            public override IConfigurationProvider Build(IConfigurationBuilder builder)
            {
                EnsureDefaults(builder);
                ResolveFileProvider();
                return new DecodingJsonConfigurationProvider(this);
            }
        }

        private sealed class DecodingJsonConfigurationProvider : JsonConfigurationProvider
        {
            public DecodingJsonConfigurationProvider(JsonConfigurationSource source)
                : base(source)
            {
            }

            public override void Load(Stream stream)
            {
                base.Load(stream);

                if (Data.Count == 0)
                {
                    return;
                }

                foreach (var key in Data.Keys.ToList())
                {
                    var current = Data[key];
                    if (current is null)
                    {
                        continue;
                    }

                    if (EncodedValueDecoder.TryDecode(current, out var clearText))
                    {
                        Data[key] = clearText;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Supported value encodings for persisted encoded-at-rest strings.
    /// </summary>
    public enum ValueEncoding
    {
        Base64 = 0,
        DpapiMachine = 1,
        DpapiMachineBase64 = 2,
    }

    /// <summary>
    /// Persisted format for encoded values: <c>enc:token:payload</c>.
    /// </summary>
    public static class EncodedValueFormat
    {
        private const string Prefix = "enc:";

        private static readonly IReadOnlyDictionary<ValueEncoding, string> EncodingToToken =
            new Dictionary<ValueEncoding, string>
            {
                { ValueEncoding.Base64, "q7m2n4" },
                { ValueEncoding.DpapiMachine, "x1p9d0" },
                { ValueEncoding.DpapiMachineBase64, "k4v8s2" },
            };

        private static readonly IReadOnlyDictionary<string, ValueEncoding> TokenToEncoding =
            EncodingToToken.ToDictionary(k => k.Value, v => v.Key, StringComparer.OrdinalIgnoreCase);

        public static string Wrap(ValueEncoding encoding, string payload)
        {
            payload ??= string.Empty;
            var token = ToToken(encoding);
            return Prefix + token + ":" + payload;
        }

        public static bool TryUnwrap(string value, out ValueEncoding encoding, out string payload)
        {
            encoding = default;
            payload = string.Empty;

            value ??= string.Empty;

            if (!value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rest = value.Substring(Prefix.Length);
            var idx = rest.IndexOf(':');
            if (idx <= 0)
            {
                return false;
            }

            var token = rest.Substring(0, idx);
            payload = rest.Substring(idx + 1);

            return TryParseToken(token, out encoding);
        }

        private static string ToToken(ValueEncoding encoding)
        {
            return EncodingToToken.TryGetValue(encoding, out var token)
                ? token
                : encoding.ToString().ToLowerInvariant();
        }

        private static bool TryParseToken(string token, out ValueEncoding encoding)
        {
            token ??= string.Empty;

            if (TokenToEncoding.TryGetValue(token, out encoding))
            {
                return true;
            }

            // Backward compatibility: allow enum-name tokens if present.
            if (Enum.TryParse(token, ignoreCase: true, out encoding))
            {
                return true;
            }

            encoding = default;
            return false;
        }
    }

    internal static class Base64Url
    {
        public static string Encode(byte[] bytes)
        {
            bytes ??= Array.Empty<byte>();
            var s = Convert.ToBase64String(bytes);
            return s.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        public static bool TryDecode(string s, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            s ??= string.Empty;

            try
            {
                var padded = s.Replace('-', '+').Replace('_', '/');
                var mod = padded.Length % 4;
                if (mod != 0)
                {
                    padded = padded.PadRight(padded.Length + (4 - mod), '=');
                }

                bytes = Convert.FromBase64String(padded);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class DpapiMachineShim
    {
        private const string NotAvailableMessage =
            "Windows DPAPI (ProtectedData/DataProtectionScope) is not available. " +
            "This feature requires Windows and System.Security.Cryptography.ProtectedData.";

        private static readonly Lazy<Impl?> ImplInstance = new(CreateImpl);

        public static bool IsAvailable => ImplInstance.Value is not null;

        public static byte[] ProtectLocalMachine(byte[] plainBytes)
        {
            EvfGuard.NotNull(plainBytes);

            var impl = ImplInstance.Value;
            if (impl is null)
            {
                throw new PlatformNotSupportedException(NotAvailableMessage);
            }

            return impl.ProtectLocalMachine(plainBytes);
        }

        public static bool TryUnprotectLocalMachine(byte[] protectedBytes, out byte[] plainBytes)
        {
            plainBytes = Array.Empty<byte>();
            if (protectedBytes is null)
            {
                return false;
            }

            var impl = ImplInstance.Value;
            return impl is not null && impl.TryUnprotectLocalMachine(protectedBytes, out plainBytes);
        }

        private static Impl? CreateImpl()
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var protectedDataType =
                TryLoadType("System.Security.Cryptography.ProtectedData", "System.Security.Cryptography.ProtectedData") ??
                TryLoadType("System.Security", "System.Security.Cryptography.ProtectedData");

            var scopeType =
                TryLoadType("System.Security.Cryptography.ProtectedData", "System.Security.Cryptography.DataProtectionScope") ??
                TryLoadType("System.Security", "System.Security.Cryptography.DataProtectionScope");

            if (protectedDataType is null || scopeType is null)
            {
                return null;
            }

            var protect = protectedDataType.GetMethod(
                "Protect",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(byte[]), typeof(byte[]), scopeType },
                modifiers: null);

            var unprotect = protectedDataType.GetMethod(
                "Unprotect",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(byte[]), typeof(byte[]), scopeType },
                modifiers: null);

            if (protect is null || unprotect is null)
            {
                return null;
            }

            object localMachine;
            try
            {
                localMachine = Enum.Parse(scopeType, "LocalMachine", ignoreCase: true);
            }
            catch
            {
                return null;
            }

            return new Impl(protect, unprotect, localMachine);
        }

        private static Type? TryLoadType(string assemblyName, string typeFullName)
        {
            try
            {
                var asm = Assembly.Load(new AssemblyName(assemblyName));
                return asm.GetType(typeFullName, throwOnError: false, ignoreCase: false);
            }
            catch
            {
                return null;
            }
        }

        private sealed class Impl
        {
            private readonly MethodInfo _protect;
            private readonly MethodInfo _unprotect;
            private readonly object _localMachineScope;

            public Impl(MethodInfo protect, MethodInfo unprotect, object localMachineScope)
            {
                _protect = protect;
                _unprotect = unprotect;
                _localMachineScope = localMachineScope;
            }

            public byte[] ProtectLocalMachine(byte[] plainBytes)
            {
                try
                {
                    return (byte[])_protect.Invoke(obj: null, parameters: new object?[] { plainBytes, null, _localMachineScope })!;
                }
                catch (TargetInvocationException tie) when (tie.InnerException is PlatformNotSupportedException)
                {
                    throw new PlatformNotSupportedException(NotAvailableMessage, tie.InnerException);
                }
            }

            public bool TryUnprotectLocalMachine(byte[] protectedBytes, out byte[] plainBytes)
            {
                plainBytes = Array.Empty<byte>();

                try
                {
                    plainBytes = (byte[])_unprotect.Invoke(obj: null, parameters: new object?[] { protectedBytes, null, _localMachineScope })!;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Encoders that produce values persisted in the <see cref="EncodedValueFormat"/> wrapper.
    /// </summary>
    public static class SettingsValueEncoders
    {
        public static string EncodeBase64(string clearText)
        {
            clearText ??= string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(clearText);
            var payload = Convert.ToBase64String(bytes);
            return EncodedValueFormat.Wrap(ValueEncoding.Base64, payload);
        }

        public static string EncodeDpapiMachine(string clearText)
        {
            clearText ??= string.Empty;
            var plainBytes = System.Text.Encoding.UTF8.GetBytes(clearText);
            var protectedBytes = DpapiMachineShim.ProtectLocalMachine(plainBytes);
            var payload = Convert.ToBase64String(protectedBytes);
            return EncodedValueFormat.Wrap(ValueEncoding.DpapiMachine, payload);
        }

        public static string EncodeDpapiMachineBase64(string clearText)
        {
            clearText ??= string.Empty;
            var plainBytes = System.Text.Encoding.UTF8.GetBytes(clearText);
            var protectedBytes = DpapiMachineShim.ProtectLocalMachine(plainBytes);
            var payload = Base64Url.Encode(protectedBytes);
            return EncodedValueFormat.Wrap(ValueEncoding.DpapiMachineBase64, payload);
        }
    }

    internal static class EncodedValueDecoder
    {
        public static bool TryDecode(string value, out string clearText)
        {
            clearText = value ?? string.Empty;
            value ??= string.Empty;

            var current = value;
            var changed = false;

            for (var depth = 0; depth < 5; depth++)
            {
                if (!TryDecodeSingle(current, out var next))
                {
                    break;
                }

                changed = true;
                current = next;
            }

            clearText = current;
            return changed;
        }

        private static bool TryDecodeSingle(string value, out string clearText)
        {
            clearText = value ?? string.Empty;
            value ??= string.Empty;

            if (!EncodedValueFormat.TryUnwrap(value, out var enc, out var payload))
            {
                return false;
            }

            return enc switch
            {
                ValueEncoding.Base64 => TryDecodeBase64Payload(payload, out clearText),
                ValueEncoding.DpapiMachine => TryDecodeDpapiMachinePayload(payload, out clearText),
                ValueEncoding.DpapiMachineBase64 => TryDecodeDpapiMachineBase64Payload(payload, out clearText),
                _ => false,
            };
        }

        private static bool TryDecodeBase64Payload(string payload, out string clearText)
        {
            clearText = string.Empty;
            payload ??= string.Empty;

            try
            {
                var bytes = Convert.FromBase64String(payload);
                clearText = System.Text.Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDecodeDpapiMachinePayload(string payload, out string clearText)
        {
            clearText = string.Empty;
            payload ??= string.Empty;

            byte[] protectedBytes;
            try
            {
                protectedBytes = Convert.FromBase64String(payload);
            }
            catch
            {
                return false;
            }

            if (!DpapiMachineShim.TryUnprotectLocalMachine(protectedBytes, out var plainBytes))
            {
                return false;
            }

            clearText = System.Text.Encoding.UTF8.GetString(plainBytes);
            return true;
        }

        private static bool TryDecodeDpapiMachineBase64Payload(string payload, out string clearText)
        {
            clearText = string.Empty;
            payload ??= string.Empty;

            if (!Base64Url.TryDecode(payload, out var protectedBytes))
            {
                return false;
            }

            if (!DpapiMachineShim.TryUnprotectLocalMachine(protectedBytes, out var plainBytes))
            {
                return false;
            }

            clearText = System.Text.Encoding.UTF8.GetString(plainBytes);
            return true;
        }
    }
}
