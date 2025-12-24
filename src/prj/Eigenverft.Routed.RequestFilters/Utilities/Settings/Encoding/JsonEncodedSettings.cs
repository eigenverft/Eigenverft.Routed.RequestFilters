using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using CommunityToolkit.Diagnostics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.Routed.RequestFilters.Utilities.Settings.Encoding
{
    /// <summary>
    /// Supported value encodings for persisting encoded-at-rest strings in JSON settings.
    /// </summary>
    public enum ValueEncoding
    {
        /// <summary>
        /// Base64 of UTF-8 bytes. Intended as a broadly compatible default.
        /// </summary>
        Base64 = 0,

        /// <summary>
        /// Windows DPAPI machine scope (LocalMachine). Payload is Base64 of protected bytes.
        /// </summary>
        DpapiMachine = 1,

        /// <summary>
        /// Windows DPAPI machine scope (LocalMachine). Payload is Base64Url of protected bytes (no padding).
        /// </summary>
        DpapiMachineBase64 = 2,
    }

    /// <summary>
    /// Defines the persisted string format for encoded values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keep <see langword="summary"/> short for IntelliSense, and put a tiny copy-pasteable snippet in remarks.
    /// The persisted format is <c>enc:token:payload</c>.
    /// </para>
    /// <para>Usage:</para>
    /// <code><![CDATA[
    /// var wrapped = EncodedValueFormat.Wrap(ValueEncoding.Base64, "SGVsbG8=");
    /// // wrapped == "enc:q7m2n4:SGVsbG8=" (token is opaque)
    /// ]]></code>
    /// </remarks>
    public static class EncodedValueFormat
    {
        private const string Prefix = "enc:";

        /// <summary>
        /// Maps <see cref="ValueEncoding"/> values to opaque persisted tokens.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The goal is to avoid persisting human-readable enum names like <c>base64</c> or <c>dpapimachine</c>.
        /// Tokens must not contain <c>:</c> because <c>:</c> is the delimiter in <c>enc:token:payload</c>.
        /// </para>
        /// <para>
        /// Keep these tokens stable once you have persisted data. If you ever rotate tokens, you must keep
        /// the old tokens recognized in <see cref="TryParseToken"/> for backward compatibility.
        /// </para>
        /// </remarks>
        private static readonly IReadOnlyDictionary<ValueEncoding, string> EncodingToToken =
            new Dictionary<ValueEncoding, string>
            {
                // Choose any opaque tokens you like (random-looking, base64url-ish, etc.).
                { ValueEncoding.Base64, "q7m2n4" },
                { ValueEncoding.DpapiMachine, "x1p9d0" },
                { ValueEncoding.DpapiMachineBase64, "k4v8s2" },
            };

        /// <summary>
        /// Reverse lookup from token to <see cref="ValueEncoding"/>.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, ValueEncoding> TokenToEncoding =
            EncodingToToken.ToDictionary(k => k.Value, v => v.Key, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Wraps a payload using the format <c>enc:token:payload</c>.
        /// </summary>
        /// <param name="encoding">The payload encoding token.</param>
        /// <param name="payload">The already encoded or protected payload.</param>
        /// <returns>The wrapped encoded value.</returns>
        public static string Wrap(ValueEncoding encoding, string payload)
        {
            payload ??= string.Empty;

            var token = ToToken(encoding);
            var wrapped = Prefix + token + ":" + payload;
            return wrapped;
        }

        /// <summary>
        /// Tries to unwrap a value in the format <c>enc:token:payload</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Keep <see langword="summary"/> short for IntelliSense, and put a tiny copy-pasteable snippet in remarks.
        /// When this returns <see langword="true"/>, <paramref name="payload"/> is the raw payload and must still be decoded.
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// if (EncodedValueFormat.TryUnwrap(input, out var enc, out var payload))
        /// {
        ///     // decode payload based on enc
        /// }
        /// ]]></code>
        /// </remarks>
        /// <param name="value">The raw value from configuration.</param>
        /// <param name="encoding">The parsed encoding token.</param>
        /// <param name="payload">The extracted payload (not decoded yet).</param>
        /// <returns><see langword="true"/> when the prefix and token are recognized; otherwise <see langword="false"/>.</returns>
        public static bool TryUnwrap(string value, out ValueEncoding encoding, out string payload)
        {
            encoding = default;
            payload = string.Empty;

            value ??= string.Empty;

            if (!value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) { return false; }

            var rest = value.Substring(Prefix.Length);
            var idx = rest.IndexOf(':');
            if (idx <= 0) { return false; }

            var token = rest.Substring(0, idx);
            payload = rest.Substring(idx + 1);

            var ok = TryParseToken(token, out encoding);
            return ok;
        }

        private static string ToToken(ValueEncoding encoding)
        {
            if (EncodingToToken.TryGetValue(encoding, out var token))
            {
                return token;
            }

            // Fallback: preserve old behavior for any future enum values.
            return encoding.ToString().ToLowerInvariant();
        }

        private static bool TryParseToken(string token, out ValueEncoding encoding)
        {
            token ??= string.Empty;

            // 1) Preferred: opaque tokens.
            if (TokenToEncoding.TryGetValue(token, out encoding))
            {
                return true;
            }

            // Also allow normalized comparisons against the opaque tokens.
            var normalized = NormalizeToken(token);
            foreach (var kvp in TokenToEncoding)
            {
                if (string.Equals(normalized, NormalizeToken(kvp.Key), StringComparison.OrdinalIgnoreCase))
                {
                    encoding = kvp.Value;
                    return true;
                }
            }

            // 2) Backward compatibility: old enum-name tokens like "base64" or "dpapimachine".
            if (Enum.TryParse(token, ignoreCase: true, out encoding))
            {
                return true;
            }

            var normalizedEnumToken = NormalizeToken(token);
            foreach (var name in Enum.GetNames(typeof(ValueEncoding)))
            {
                if (string.Equals(normalizedEnumToken, NormalizeToken(name), StringComparison.OrdinalIgnoreCase))
                {
                    encoding = (ValueEncoding)Enum.Parse(typeof(ValueEncoding), name, ignoreCase: true);
                    return true;
                }
            }

            encoding = default;
            return false;
        }

        private static string NormalizeToken(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch == '-' || ch == '_' || ch == ' ')
                {
                    continue;
                }

                sb.Append(ch);
            }

            var normalized = sb.ToString();
            return normalized;
        }
    }

    /// <summary>
    /// Minimal Base64Url helper (RFC 4648): URL-safe alphabet and no padding.
    /// </summary>
    internal static class Base64Url
    {
        /// <summary>
        /// Encodes bytes as Base64Url without padding.
        /// </summary>
        /// <param name="bytes">Input bytes.</param>
        /// <returns>Base64Url string.</returns>
        public static string Encode(byte[] bytes)
        {
            bytes ??= Array.Empty<byte>();

            var s = Convert.ToBase64String(bytes);
            s = s.Replace('+', '-').Replace('/', '_').TrimEnd('=');
            return s;
        }

        /// <summary>
        /// Tries to decode a Base64Url string (with optional missing padding) to bytes.
        /// </summary>
        /// <param name="s">Base64Url string.</param>
        /// <param name="bytes">Decoded bytes.</param>
        /// <returns><see langword="true"/> when decoding succeeded; otherwise <see langword="false"/>.</returns>
        public static bool TryDecode(string s, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            s ??= string.Empty;

            try
            {
                var padded = s.Replace('-', '+').Replace('_', '/');

                // Restore padding.
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

    /// <summary>
    /// Late-bound access to Windows DPAPI machine-scope protection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keep <see langword="summary"/> short for IntelliSense, and put a tiny copy-pasteable snippet in remarks.
    /// This avoids compile-time references to DPAPI types so the library can build on non-Windows targets.
    /// </para>
    /// <para>Usage:</para>
    /// <code><![CDATA[
    /// if (DpapiMachineShim.IsAvailable)
    /// {
    ///     var protectedBytes = DpapiMachineShim.ProtectLocalMachine(plainBytes);
    /// }
    /// ]]></code>
    /// </remarks>
    internal static class DpapiMachineShim
    {
        private const string NotAvailableMessage =
            "Windows DPAPI (ProtectedData/DataProtectionScope) is not available. " +
            "This feature requires Windows and the presence of System.Security.Cryptography.ProtectedData.";

        private static readonly Lazy<Impl?> ImplInstance = new(CreateImpl);

        /// <summary>
        /// Gets whether DPAPI machine scope is available at runtime.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                var available = ImplInstance.Value is not null;
                return available;
            }
        }

        /// <summary>
        /// Protects data using Windows DPAPI machine scope (LocalMachine).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Keep <see langword="summary"/> short for IntelliSense, and put a tiny copy-pasteable snippet in remarks.
        /// This throws on non-Windows, or when the DPAPI types are not present.
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// var protectedBytes = DpapiMachineShim.ProtectLocalMachine(plainBytes);
        /// ]]></code>
        /// </remarks>
        /// <param name="plainBytes">Plain bytes to protect.</param>
        /// <returns>Protected bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="plainBytes"/> is <see langword="null"/>.</exception>
        /// <exception cref="PlatformNotSupportedException">Thrown when DPAPI is not available.</exception>
        public static byte[] ProtectLocalMachine(byte[] plainBytes)
        {
            Guard.IsNotNull(plainBytes);

            var impl = ImplInstance.Value;
            if (impl is null)
            {
                throw new PlatformNotSupportedException(NotAvailableMessage);
            }

            var protectedBytes = impl.ProtectLocalMachine(plainBytes);
            return protectedBytes;
        }

        /// <summary>
        /// Tries to unprotect data using Windows DPAPI machine scope (LocalMachine).
        /// </summary>
        /// <param name="protectedBytes">Protected bytes.</param>
        /// <param name="plainBytes">Unprotected bytes when successful.</param>
        /// <returns><see langword="true"/> when unprotected; otherwise <see langword="false"/>.</returns>
        public static bool TryUnprotectLocalMachine(byte[] protectedBytes, out byte[] plainBytes)
        {
            plainBytes = Array.Empty<byte>();

            if (protectedBytes is null)
            {
                return false;
            }

            var impl = ImplInstance.Value;
            if (impl is null)
            {
                return false;
            }

            var ok = impl.TryUnprotectLocalMachine(protectedBytes, out plainBytes);
            return ok;
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

            var impl = new Impl(protect, unprotect, localMachine);
            return impl;
        }

        private static Type? TryLoadType(string assemblyName, string typeFullName)
        {
            try
            {
                var asm = Assembly.Load(new AssemblyName(assemblyName));
                var t = asm.GetType(typeFullName, throwOnError: false, ignoreCase: false);
                return t;
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
                    var result = (byte[])_protect.Invoke(obj: null, parameters: new object?[] { plainBytes, null, _localMachineScope })!;
                    return result;
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
    /// Encoders for producing encoded-at-rest strings for JSON settings.
    /// </summary>
    public static class SettingsValueEncoders
    {
        /// <summary>
        /// Encodes clear text as Base64 of UTF-8 bytes using the standard format.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Keep <see langword="summary"/> short for IntelliSense, and put a tiny copy-pasteable snippet in remarks.
        /// The payload is Base64 and the wrapper is <c>enc:token:</c>.
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// var encoded = SettingsValueEncoders.EncodeBase64("secret");
        /// // encoded == "enc:q7m2n4:c2VjcmV0" (token is opaque)
        /// ]]></code>
        /// </remarks>
        /// <param name="clearText">The clear text to encode.</param>
        /// <returns>The wrapped encoded value.</returns>
        public static string EncodeBase64(string clearText)
        {
            clearText ??= string.Empty;

            var bytes = System.Text.Encoding.UTF8.GetBytes(clearText);
            var payload = Convert.ToBase64String(bytes);
            var wrapped = EncodedValueFormat.Wrap(ValueEncoding.Base64, payload);
            return wrapped;
        }

        /// <summary>
        /// Encodes clear text using Windows DPAPI machine scope and wraps it using the standard format.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Keep <see langword="summary"/> short for IntelliSense, and put a tiny copy-pasteable snippet in remarks.
        /// This is late-bound via reflection so the library can compile cross-platform.
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// var encoded = SettingsValueEncoders.EncodeDpapiMachine("secret");
        /// // On Windows: "enc:x1p9d0:...."
        /// // On non-Windows: throws PlatformNotSupportedException
        /// ]]></code>
        /// </remarks>
        /// <param name="clearText">The clear text to protect.</param>
        /// <returns>The wrapped encoded value.</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown when DPAPI is not available.</exception>
        public static string EncodeDpapiMachine(string clearText)
        {
            clearText ??= string.Empty;

            var plainBytes = System.Text.Encoding.UTF8.GetBytes(clearText);
            var protectedBytes = DpapiMachineShim.ProtectLocalMachine(plainBytes);
            var payload = Convert.ToBase64String(protectedBytes);
            var wrapped = EncodedValueFormat.Wrap(ValueEncoding.DpapiMachine, payload);
            return wrapped;
        }

        /// <summary>
        /// Encodes clear text using Windows DPAPI machine scope and wraps it using the standard format,
        /// with a Base64Url payload.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Keep <see langword="summary"/> short for IntelliSense, and put a tiny copy-pasteable snippet in remarks.
        /// Base64Url avoids characters like <c>+</c>, <c>/</c> and padding <c>=</c>.
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// var encoded = SettingsValueEncoders.EncodeDpapiMachineBase64("secret");
        /// // On Windows: "enc:k4v8s2:...." (payload is Base64Url)
        /// ]]></code>
        /// </remarks>
        /// <param name="clearText">The clear text to protect.</param>
        /// <returns>The wrapped encoded value.</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown when DPAPI is not available.</exception>
        public static string EncodeDpapiMachineBase64(string clearText)
        {
            clearText ??= string.Empty;

            var plainBytes = System.Text.Encoding.UTF8.GetBytes(clearText);
            var protectedBytes = DpapiMachineShim.ProtectLocalMachine(plainBytes);
            var payload = Base64Url.Encode(protectedBytes);
            var wrapped = EncodedValueFormat.Wrap(ValueEncoding.DpapiMachineBase64, payload);
            return wrapped;
        }
    }

    /// <summary>
    /// Decodes encoded-at-rest values produced by <see cref="SettingsValueEncoders"/>.
    /// </summary>
    public static class EncodedValueDecoder
    {
        /// <summary>
        /// Tries to decode an encoded value back to clear text.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Keep <see langword="summary"/> short for IntelliSense, and put a tiny copy-pasteable snippet in remarks.
        /// If <paramref name="value"/> is not recognized as encoded, the method returns <see langword="false"/> and echoes the input.
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// if (EncodedValueDecoder.TryDecode(value, out var clearText))
        /// {
        ///     value = clearText;
        /// }
        /// ]]></code>
        /// </remarks>
        /// <param name="value">The value from configuration.</param>
        /// <param name="clearText">The decoded clear text.</param>
        /// <returns><see langword="true"/> when decoded; otherwise <see langword="false"/>.</returns>
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

            if (!EncodedValueFormat.TryUnwrap(value, out var encoding, out var payload))
            {
                return false;
            }

            var decoded = encoding switch
            {
                ValueEncoding.Base64 => TryDecodeBase64Payload(payload, out clearText),
                ValueEncoding.DpapiMachine => TryDecodeDpapiMachinePayload(payload, out clearText),
                ValueEncoding.DpapiMachineBase64 => TryDecodeDpapiMachineBase64Payload(payload, out clearText),
                _ => false,
            };

            return decoded;
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

    /// <summary>
    /// Encodes string values inside a JSON settings file by matching full key paths.
    /// </summary>
    public static class JsonSettingsFileEncoder
    {
        /// <summary>
        /// Encodes matching JSON string values in a JSON file.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Keep <see langword="summary"/> short for IntelliSense, and put a tiny copy-pasteable snippet in remarks.
        /// Matching is done against full key paths like <c>Auth:Password</c>.
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// var updated = JsonSettingsFileEncoder.EncodeStringValues(
        ///     jsonFilePath: "appsettings.json",
        ///     keyPathPattern: "Auth:Password",
        ///     encode: SettingsValueEncoders.EncodeBase64);
        /// ]]></code>
        /// </remarks>
        /// <param name="jsonFilePath">Full path to the JSON file.</param>
        /// <param name="keyPathPattern">Glob pattern matched against full keys.</param>
        /// <param name="encode">Encoder function producing an encoded string.</param>
        /// <param name="nullAsEmpty">When <see langword="true"/>, a JSON null is treated as an empty string.</param>
        /// <returns>The number of values updated.</returns>
        /// <exception cref="FileNotFoundException">Thrown when <paramref name="jsonFilePath"/> does not exist.</exception>
        /// <exception cref="InvalidDataException">Thrown when parsing produces a null JSON root.</exception>
        public static int EncodeStringValues(string jsonFilePath, string keyPathPattern, Func<string, string> encode, bool nullAsEmpty = true)
        {
            Guard.IsNotNullOrWhiteSpace(jsonFilePath);
            Guard.IsNotNullOrWhiteSpace(keyPathPattern);
            Guard.IsNotNull(encode);

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
            var combined = string.IsNullOrEmpty(prefix) ? segment : $"{prefix}:{segment}";
            return combined;
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

            if (File.Exists(path))
            {
                File.Replace(tmp, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        private sealed class KeyPathGlobMatcher
        {
            private readonly Regex _regex;

            /// <summary>
            /// Creates a glob matcher for IConfiguration-style key paths.
            /// </summary>
            /// <param name="globPattern">The glob pattern to compile.</param>
            /// <exception cref="ArgumentException">Thrown when <paramref name="globPattern"/> is null, empty, or whitespace.</exception>
            public KeyPathGlobMatcher(string globPattern)
            {
                Guard.IsNotNullOrWhiteSpace(globPattern);

                var regex = "^" + Regex.Escape(globPattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                _regex = new Regex(regex, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            }

            /// <summary>
            /// Checks whether the key path matches the glob pattern.
            /// </summary>
            /// <param name="keyPath">The full key path.</param>
            /// <returns><see langword="true"/> when matched; otherwise <see langword="false"/>.</returns>
            public bool IsMatch(string keyPath)
            {
                keyPath ??= string.Empty;

                var match = _regex.IsMatch(keyPath);
                return match;
            }
        }
    }

    /// <summary>
    /// Convenience extension methods for encoded JSON configuration workflows.
    /// </summary>
    public static class JsonEncodedConfigurationExtensions
    {
        /// <summary>
        /// Encodes matching string values in a JSON file before it is loaded.
        /// </summary>
        /// <param name="configuration">The configuration manager.</param>
        /// <param name="jsonFilePath">Full path to the JSON file.</param>
        /// <param name="keyPathPattern">Glob pattern against full key paths.</param>
        /// <param name="encode">Encoder function.</param>
        /// <param name="nullAsEmpty">Treat JSON null as empty string and encode it.</param>
        /// <returns>The number of values updated.</returns>
        public static int EncodeStringValues(this ConfigurationManager configuration, string jsonFilePath, string keyPathPattern, Func<string, string> encode, bool nullAsEmpty = true)
        {
            Guard.IsNotNull(configuration);

            var updated = JsonSettingsFileEncoder.EncodeStringValues(
                jsonFilePath: jsonFilePath,
                keyPathPattern: keyPathPattern,
                encode: encode,
                nullAsEmpty: nullAsEmpty);

            return updated;
        }

        private sealed class DecoderState
        {
            public readonly object Gate = new();

            public readonly HashSet<string> Targets = new(StringComparer.OrdinalIgnoreCase);

            public IDisposable? Subscription;
        }

        private static readonly ConditionalWeakTable<ConfigurationManager, DecoderState> StateTable = new();

        /// <summary>
        /// Post-decodes encoded values in-memory for the JSON provider that loaded the specified file.
        /// </summary>
        /// <param name="configuration">The configuration manager.</param>
        /// <param name="jsonFileNameOrPath">The same path (or file name) used in AddJsonFile.</param>
        /// <returns>The same configuration manager for chaining.</returns>
        public static ConfigurationManager DecodeEncodedValuesFromJson(this ConfigurationManager configuration, string jsonFileNameOrPath)
        {
            Guard.IsNotNull(configuration);
            Guard.IsNotNullOrWhiteSpace(jsonFileNameOrPath);

            var state = StateTable.GetOrCreateValue(configuration);
            lock (state.Gate)
            {
                state.Targets.Add(jsonFileNameOrPath);
            }

            void ApplyAll()
            {
                if (configuration is not IConfigurationRoot root)
                {
                    return;
                }

                var sources = ((IConfigurationBuilder)configuration).Sources.ToList();
                var providers = root.Providers.ToList();
                var count = Math.Min(sources.Count, providers.Count);

                string[] targets;
                lock (state.Gate)
                {
                    targets = state.Targets.ToArray();
                }

                for (var i = 0; i < count; i++)
                {
                    if (sources[i] is not JsonConfigurationSource jsonSource)
                    {
                        continue;
                    }

                    if (!targets.Any(t => PathMatches(jsonSource.Path, t)))
                    {
                        continue;
                    }

                    DecodeValuesInProvider(providers[i]);
                }
            }

            ApplyAll();

            lock (state.Gate)
            {
                if (state.Subscription is null && configuration is IConfigurationRoot root)
                {
                    state.Subscription = ChangeToken.OnChange(root.GetReloadToken, ApplyAll);
                }
            }

            return configuration;
        }

        private static bool PathMatches(string? sourcePath, string requested)
        {
            sourcePath ??= string.Empty;
            requested ??= string.Empty;

            var reqFile = Path.GetFileName(requested);
            var srcFile = Path.GetFileName(sourcePath);

            if (!string.IsNullOrWhiteSpace(reqFile) &&
                string.Equals(requested, reqFile, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(srcFile, reqFile, StringComparison.OrdinalIgnoreCase);
            }

            sourcePath = sourcePath.Replace('\\', '/');
            requested = requested.Replace('\\', '/');

            var matched = sourcePath.EndsWith(requested, StringComparison.OrdinalIgnoreCase);
            return matched;
        }

        private static void DecodeValuesInProvider(IConfigurationProvider provider)
        {
            var data = TryGetProviderDataDictionary(provider);
            if (data is null || data.Count == 0)
            {
                return;
            }

            foreach (var key in data.Keys.ToList())
            {
                var current = data[key];
                if (current is null)
                {
                    continue;
                }

                if (EncodedValueDecoder.TryDecode(current, out var clearText))
                {
                    data[key] = clearText;
                }
            }
        }

        private static IDictionary<string, string?>? TryGetProviderDataDictionary(IConfigurationProvider provider)
        {
            Guard.IsNotNull(provider);

            var t = provider.GetType();
            while (t is not null)
            {
                var prop = t.GetProperty("Data", BindingFlags.Instance | BindingFlags.NonPublic);
                if (prop is not null)
                {
                    var value = prop.GetValue(provider) as IDictionary<string, string?>;
                    return value;
                }

                t = t.BaseType;
            }

            return null;
        }
    }

    /// <summary>
    /// Adds JSON configuration providers that decode encoded values during provider load/reload.
    /// </summary>
    public static class JsonFileDecodingProviderExtensions
    {
        /// <summary>
        /// Adds a JSON file configuration provider that decodes encoded values during provider load/reload.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Keep <see langword="summary"/> short for IntelliSense, and put a tiny copy-pasteable snippet in remarks.
        /// This is options-safe because decoding happens inside the provider load path.
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// builder.Configuration.AddJsonFileWithDecoding(
        ///     path: "appsettings.json",
        ///     optional: false,
        ///     reloadOnChange: true);
        /// ]]></code>
        /// </remarks>
        /// <param name="builder">The configuration builder.</param>
        /// <param name="path">Path to JSON file.</param>
        /// <param name="optional">Whether the file is optional.</param>
        /// <param name="reloadOnChange">Whether to reload when the file changes.</param>
        /// <returns>The same builder for chaining.</returns>
        public static IConfigurationBuilder AddJsonFileWithDecoding(this IConfigurationBuilder builder, string path, bool optional = false, bool reloadOnChange = false)
        {
            Guard.IsNotNull(builder);
            Guard.IsNotNullOrWhiteSpace(path);

            var source = new DecodingJsonConfigurationSource
            {
                Path = path,
                Optional = optional,
                ReloadOnChange = reloadOnChange,
            };

            // This is the key: makes absolute paths work by creating the correct PhysicalFileProvider.
            source.ResolveFileProvider();

            builder.Add(source);
            return builder;
        }

        private sealed class DecodingJsonConfigurationSource : JsonConfigurationSource
        {
            public override IConfigurationProvider Build(IConfigurationBuilder builder)
            {
                EnsureDefaults(builder);
                ResolveFileProvider(); // important for absolute paths
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
}
