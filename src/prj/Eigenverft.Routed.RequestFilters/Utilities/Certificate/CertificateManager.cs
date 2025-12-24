using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Eigenverft.Routed.RequestFilters.Utilities.Certificate
{
    /// <summary>
    /// Provides helpers for creating and loading self-signed certificates (net6.0).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Intended for development, test environments, and internal tooling. Self-signed certificates are not trusted by
    /// default and typically require explicit trust configuration for TLS usage.
    /// </para>
    /// <para>
    /// The implementation persists PKCS#12 (PFX) files on disk and imports certificates using
    /// <see cref="X509KeyStorageFlags.Exportable"/> and <see cref="X509KeyStorageFlags.UserKeySet"/>.
    /// </para>
    /// <para>Usage:</para>
    /// <code><![CDATA[
    /// var subject = new CertificateManager.SubjectProfile { CommonName = "localhost", CountryOrRegion = "DE" };
    /// var cert = CertificateManager.LoadOrCreateSelfSignedCertificate(
    ///     pfxDirectoryPath: Path.Combine(AppContext.BaseDirectory, "certs"),
    ///     pfxFileName: "dev-localhost.pfx",
    ///     password: "change-me",
    ///     subjectProfile: subject,
    ///     sanNames: new List<string> { "localhost" },
    ///     validityPeriodYears: 2,
    ///     purpose: CertificateManager.CertificatePurpose.TlsServer,
    ///     cryptoProfile: CertificateManager.CertificateCryptoProfile.ModernEcdsaP256Sha256);
    /// ]]></code>
    /// </remarks>
    public static class CertificateManager
    {
        /// <summary>
        /// Describes the intended usage of a certificate in terms of Extended Key Usage (EKU) and Key Usage flags.
        /// </summary>
        public enum CertificatePurpose
        {
            /// <summary>
            /// TLS server authentication (EKU: serverAuth).
            /// </summary>
            TlsServer,

            /// <summary>
            /// TLS client authentication (EKU: clientAuth).
            /// </summary>
            TlsClient,

            /// <summary>
            /// TLS server and client authentication (EKU: serverAuth and clientAuth).
            /// </summary>
            TlsServerAndClient,

            /// <summary>
            /// Code signing (EKU: codeSigning).
            /// </summary>
            CodeSigning,

            /// <summary>
            /// Email protection (EKU: emailProtection).
            /// </summary>
            EmailProtection
        }

        /// <summary>
        /// Describes a cryptographic profile, including key type/size (RSA or ECDSA) and the digest algorithm.
        /// </summary>
        public enum CertificateCryptoProfile
        {
            /// <summary>
            /// RSA 2048 with SHA-256. Intended as a broadly compatible default.
            /// </summary>
            CompatibilityRsa2048Sha256,

            /// <summary>
            /// RSA 3072 with SHA-256. A stronger RSA option with wide compatibility.
            /// </summary>
            CompatibilityRsa3072Sha256,

            /// <summary>
            /// ECDSA P-256 with SHA-256. A modern option with smaller keys and good performance.
            /// </summary>
            ModernEcdsaP256Sha256,

            /// <summary>
            /// ECDSA P-384 with SHA-384. A modern option with stronger parameters than P-256.
            /// </summary>
            ModernEcdsaP384Sha384
        }

        /// <summary>
        /// Represents subject Distinguished Name (DN) fields. Empty or whitespace values are skipped.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The DN is assembled in the common "CN, O, OU, L, ST, C" order. Values are escaped by
        /// <see cref="EscapeDistinguishedNameValue"/> for DN safety.
        /// </para>
        /// </remarks>
        public sealed class SubjectProfile
        {
            /// <summary>
            /// Common Name (CN). Often a host name for TLS server certificates.
            /// </summary>
            public string? CommonName { get; set; }

            /// <summary>
            /// Organization (O).
            /// </summary>
            public string? OrganizationName { get; set; }

            /// <summary>
            /// Organizational Unit (OU).
            /// </summary>
            public string? OrganizationalUnitName { get; set; }

            /// <summary>
            /// Locality (L), typically a city.
            /// </summary>
            public string? LocalityName { get; set; }

            /// <summary>
            /// State or Province (ST).
            /// </summary>
            public string? StateOrProvinceName { get; set; }

            /// <summary>
            /// Country/Region (C), typically an ISO 3166-1 alpha-2 code (for example: DE, US).
            /// </summary>
            public string? CountryOrRegion { get; set; }
        }

        /// <summary>
        /// Loads a valid PFX from disk or generates and persists a new self-signed certificate.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the PFX exists and can be imported using <paramref name="password"/>, it is returned when it is not expired.
        /// Otherwise a new certificate is created, written to disk, and reloaded.
        /// </para>
        /// <para>
        /// The certificate is imported with <see cref="X509KeyStorageFlags.Exportable"/> and <see cref="X509KeyStorageFlags.UserKeySet"/>.
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// var subject = new CertificateManager.SubjectProfile { CommonName = "localhost", CountryOrRegion = "DE" };
        /// var cert = CertificateManager.LoadOrCreateSelfSignedCertificate(
        ///     pfxDirectoryPath: Path.Combine(AppContext.BaseDirectory, "certs"),
        ///     pfxFileName: "dev-localhost.pfx",
        ///     password: "change-me",
        ///     subjectProfile: subject,
        ///     sanNames: new List<string> { "localhost" },
        ///     validityPeriodYears: 2,
        ///     purpose: CertificateManager.CertificatePurpose.TlsServer,
        ///     cryptoProfile: CertificateManager.CertificateCryptoProfile.ModernEcdsaP256Sha256);
        /// ]]></code>
        /// </remarks>
        /// <param name="pfxDirectoryPath">Directory path for the PFX file.</param>
        /// <param name="pfxFileName">PFX file name (for example: mycert.pfx).</param>
        /// <param name="password">Password used to protect the PFX and to import it.</param>
        /// <param name="subjectProfile">Subject DN components used to build the certificate subject.</param>
        /// <param name="sanNames">
        /// Subject Alternative Name (SAN) DNS names. Required when <paramref name="purpose"/> is
        /// <see cref="CertificatePurpose.TlsServer"/> or <see cref="CertificatePurpose.TlsServerAndClient"/>.
        /// </param>
        /// <param name="validityPeriodYears">Validity period in years.</param>
        /// <param name="purpose">Intended certificate purpose (controls EKU and Key Usage).</param>
        /// <param name="cryptoProfile">Cryptographic profile (RSA/ECDSA and digest algorithm).</param>
        /// <returns>
        /// A loaded <see cref="X509Certificate2"/> instance containing the private key.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when required string parameters are null/empty/whitespace or when SAN rules for server TLS are violated.
        /// </exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="subjectProfile"/> or <paramref name="sanNames"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="validityPeriodYears"/> is not positive.</exception>
        /// <exception cref="IOException">May be thrown when reading/writing the PFX file fails.</exception>
        /// <exception cref="UnauthorizedAccessException">May be thrown when file system permissions prevent reading/writing.</exception>
        public static X509Certificate2 LoadOrCreateSelfSignedCertificate(
            string pfxDirectoryPath,
            string pfxFileName,
            string password,
            SubjectProfile subjectProfile,
            List<string> sanNames,
            int validityPeriodYears,
            CertificatePurpose purpose,
            CertificateCryptoProfile cryptoProfile)
        {
            if (string.IsNullOrWhiteSpace(pfxDirectoryPath)) { throw new ArgumentException("Path is required.", nameof(pfxDirectoryPath)); }
            if (string.IsNullOrWhiteSpace(pfxFileName)) { throw new ArgumentException("File name is required.", nameof(pfxFileName)); }
            if (string.IsNullOrWhiteSpace(password)) { throw new ArgumentException("Password is required.", nameof(password)); }
            if (subjectProfile == null) { throw new ArgumentNullException(nameof(subjectProfile)); }
            if (sanNames == null) { throw new ArgumentNullException(nameof(sanNames)); }
            if (validityPeriodYears <= 0) { throw new ArgumentOutOfRangeException(nameof(validityPeriodYears), "Must be positive."); }

            string filePath = Path.Combine(pfxDirectoryPath, pfxFileName);

            if (File.Exists(filePath))
            {
                try
                {
                    X509Certificate2 existing = ImportPkcs12Certificate(filePath, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
                    if (existing.NotAfter > DateTime.UtcNow) { return existing; }
                }
                catch (CryptographicException)
                {
                    // Intentionally ignored: fall back to generating a fresh certificate.
                }
            }

            X509Certificate2 created = CreateSelfSignedCertificate(subjectProfile, sanNames, validityPeriodYears, password, purpose, cryptoProfile);

            if (!Directory.Exists(pfxDirectoryPath)) { Directory.CreateDirectory(pfxDirectoryPath); }

            byte[] pfxBytes = created.Export(X509ContentType.Pfx, password);
            File.WriteAllBytes(filePath, pfxBytes);

            X509Certificate2 loaded = ImportPkcs12Certificate(filePath, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
            return loaded;
        }

        public static X509Certificate2 LoadOrCreateSelfSignedCertificate(string pfxDirectoryPath,string pfxFileName, string password, SubjectProfile subjectProfile)
        {
            ArgumentNullException.ThrowIfNull(subjectProfile.CommonName);
            List<string> sanNames = new List<string> { subjectProfile.CommonName };
            var retval = LoadOrCreateSelfSignedCertificate(pfxDirectoryPath, pfxFileName, password, subjectProfile, sanNames, 2, CertificatePurpose.TlsServer, CertificateCryptoProfile.CompatibilityRsa2048Sha256);
            return retval;
        }

        /// <summary>
        /// Creates a self-signed certificate with purpose-driven defaults for EKU and Key Usage.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The certificate validity begins at UTC now minus one day to reduce failures caused by clock skew.
        /// For TLS server usage, SAN DNS entries are required because modern TLS stacks commonly ignore the CN for host validation.
        /// </para>
        /// <para>
        /// The returned certificate is re-imported from PFX bytes via <see cref="ImportPkcs12Certificate"/> to provide a
        /// consistent import path and to ensure the private key is present on the returned instance.
        /// </para>
        /// <para>Usage:</para>
        /// <code><![CDATA[
        /// var subject = new CertificateManager.SubjectProfile { CommonName = "localhost", CountryOrRegion = "DE" };
        /// var cert = CertificateManager.CreateSelfSignedCertificate(
        ///     subjectProfile: subject,
        ///     sanNames: new List<string> { "localhost" },
        ///     validityPeriodYears: 2,
        ///     password: "change-me",
        ///     purpose: CertificateManager.CertificatePurpose.TlsServer,
        ///     cryptoProfile: CertificateManager.CertificateCryptoProfile.ModernEcdsaP256Sha256);
        /// ]]></code>
        /// </remarks>
        /// <param name="subjectProfile">Subject DN components used to build the certificate subject.</param>
        /// <param name="sanNames">SAN DNS names to include for TLS server usage.</param>
        /// <param name="validityPeriodYears">Validity period in years.</param>
        /// <param name="password">Password used to protect the PFX export/import.</param>
        /// <param name="purpose">Intended certificate purpose (controls EKU and Key Usage).</param>
        /// <param name="cryptoProfile">Cryptographic profile (RSA/ECDSA and digest algorithm).</param>
        /// <returns>A new self-signed <see cref="X509Certificate2"/> instance including the private key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="subjectProfile"/> or <paramref name="sanNames"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="validityPeriodYears"/> is not positive.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when the built subject DN is empty, or when SAN is required for the requested <paramref name="purpose"/>.
        /// </exception>
        public static X509Certificate2 CreateSelfSignedCertificate(SubjectProfile subjectProfile, List<string> sanNames, int validityPeriodYears, string password, CertificatePurpose purpose, CertificateCryptoProfile cryptoProfile)
        {
            if (subjectProfile == null) { throw new ArgumentNullException(nameof(subjectProfile)); }
            if (sanNames == null) { throw new ArgumentNullException(nameof(sanNames)); }
            if (validityPeriodYears <= 0) { throw new ArgumentOutOfRangeException(nameof(validityPeriodYears), "Must be positive."); }

            if (purpose == CertificatePurpose.TlsServer || purpose == CertificatePurpose.TlsServerAndClient)
            {
                if (sanNames.Count == 0) { throw new ArgumentException("SAN DNS names required for TLS server certificates.", nameof(sanNames)); }
            }

            string subjectDistinguishedName = BuildSubjectDistinguishedName(subjectProfile);
            if (string.IsNullOrWhiteSpace(subjectDistinguishedName)) { throw new ArgumentException("SubjectProfile produced an empty DN.", nameof(subjectProfile)); }

            HashAlgorithmName hashAlgorithm;
            X509KeyUsageFlags keyUsages;
            OidCollection ekuOids;
            bool ekuCritical;
            ResolvePurposeDefaults(purpose, cryptoProfile, out hashAlgorithm, out keyUsages, out ekuOids, out ekuCritical);

            X509Certificate2 cert;
            if (IsRsaCryptoProfile(cryptoProfile))
            {
                using (RSA rsa = RSA.Create(GetRsaKeySize(cryptoProfile)))
                {
                    CertificateRequest req = new CertificateRequest(subjectDistinguishedName, rsa, hashAlgorithm, RSASignaturePadding.Pkcs1);
                    ApplyCertificateExtensions(req, purpose, sanNames, keyUsages, ekuOids, ekuCritical);
                    cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(validityPeriodYears));
                }
            }
            else
            {
                using (ECDsa ecdsa = ECDsa.Create(GetEcdsaCurve(cryptoProfile)))
                {
                    CertificateRequest req = new CertificateRequest(subjectDistinguishedName, ecdsa, hashAlgorithm);
                    ApplyCertificateExtensions(req, purpose, sanNames, keyUsages, ekuOids, ekuCritical);
                    cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(validityPeriodYears));
                }
            }

            byte[] pfxBytes = cert.Export(X509ContentType.Pfx, password);
            X509Certificate2 loaded = ImportPkcs12Certificate(pfxBytes, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
            return loaded;
        }

        /// <summary>
        /// Applies common X.509 extensions based on the intended <paramref name="purpose"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Adds Basic Constraints, Key Usage, Enhanced Key Usage (EKU), Subject Key Identifier (SKI), and (for TLS server
        /// purposes) Subject Alternative Name (SAN) DNS names.
        /// </para>
        /// </remarks>
        /// <param name="request">The certificate request to extend.</param>
        /// <param name="purpose">The intended purpose.</param>
        /// <param name="sanNames">SAN DNS names to add when server TLS is requested.</param>
        /// <param name="keyUsages">Key usage flags to apply.</param>
        /// <param name="ekuOids">EKU OIDs to apply.</param>
        /// <param name="ekuCritical">Whether the EKU extension is marked critical.</param>
        private static void ApplyCertificateExtensions(CertificateRequest request, CertificatePurpose purpose, List<string> sanNames, X509KeyUsageFlags keyUsages, OidCollection ekuOids, bool ekuCritical)
        {
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsages, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(ekuOids, ekuCritical));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            if (purpose == CertificatePurpose.TlsServer || purpose == CertificatePurpose.TlsServerAndClient)
            {
                SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
                foreach (string dnsName in sanNames)
                {
                    san.AddDnsName(dnsName);
                }

                request.CertificateExtensions.Add(san.Build());
            }
        }

        /// <summary>
        /// Builds the subject Distinguished Name string from the provided profile.
        /// </summary>
        /// <param name="subject">The subject profile.</param>
        /// <returns>A DN string in "key=value" form separated by commas.</returns>
        private static string BuildSubjectDistinguishedName(SubjectProfile subject)
        {
            List<string> parts = new List<string>(6);

            AddDistinguishedNamePart(parts, "CN", subject.CommonName);
            AddDistinguishedNamePart(parts, "O", subject.OrganizationName);
            AddDistinguishedNamePart(parts, "OU", subject.OrganizationalUnitName);
            AddDistinguishedNamePart(parts, "L", subject.LocalityName);
            AddDistinguishedNamePart(parts, "ST", subject.StateOrProvinceName);
            AddDistinguishedNamePart(parts, "C", subject.CountryOrRegion);

            string dn = string.Join(", ", parts);
            return dn;
        }

        /// <summary>
        /// Adds a DN component to <paramref name="parts"/> when <paramref name="value"/> is non-empty.
        /// </summary>
        /// <param name="parts">Target list of DN parts.</param>
        /// <param name="key">The DN attribute key (for example: CN, O, OU).</param>
        /// <param name="value">The DN attribute value.</param>
        private static void AddDistinguishedNamePart(List<string> parts, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) { return; }
            string escaped = EscapeDistinguishedNameValue(value);
            string part = string.Format("{0}={1}", key, escaped);
            parts.Add(part);
        }

        /// <summary>
        /// Escapes a DN value so it can be safely embedded into a DN string.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The escaping performed here targets common DN special characters. If strict RFC-compliant DN handling is required
        /// for all edge cases, consider using a dedicated DN builder.
        /// </para>
        /// </remarks>
        /// <param name="value">The raw DN attribute value.</param>
        /// <returns>The escaped value.</returns>
        private static string EscapeDistinguishedNameValue(string value)
        {
            string escaped = value;
            escaped = escaped.Replace("\\", "\\\\");
            escaped = escaped.Replace("+", "\\+");
            escaped = escaped.Replace(",", "\\,");
            escaped = escaped.Replace(";", "\\;");
            escaped = escaped.Replace("\"", "\\\"");
            escaped = escaped.Replace("<", "\\<");
            escaped = escaped.Replace(">", "\\>");
            return escaped;
        }

        /// <summary>
        /// Resolves purpose defaults for digest algorithm, key usage, EKU OIDs, and EKU criticality.
        /// </summary>
        /// <remarks>
        /// <para>
        /// EKU OIDs used: serverAuth (1.3.6.1.5.5.7.3.1), clientAuth (1.3.6.1.5.5.7.3.2), codeSigning (1.3.6.1.5.5.7.3.3),
        /// emailProtection (1.3.6.1.5.5.7.3.4).
        /// </para>
        /// </remarks>
        /// <param name="purpose">The intended purpose.</param>
        /// <param name="cryptoProfile">The crypto profile used to derive the digest algorithm and RSA/ECDSA behavior.</param>
        /// <param name="hash">Resolved hash algorithm name.</param>
        /// <param name="keyUsages">Resolved key usage flags.</param>
        /// <param name="ekuOids">Resolved EKU OID collection.</param>
        /// <param name="ekuCritical">Whether the EKU extension is marked critical.</param>
        private static void ResolvePurposeDefaults(CertificatePurpose purpose, CertificateCryptoProfile cryptoProfile, out HashAlgorithmName hash, out X509KeyUsageFlags keyUsages, out OidCollection ekuOids, out bool ekuCritical)
        {
            hash = GetHashAlgorithmForProfile(cryptoProfile);
            ekuCritical = true;
            ekuOids = new OidCollection();

            bool isRsa = IsRsaCryptoProfile(cryptoProfile);

            switch (purpose)
            {
                case CertificatePurpose.TlsServer:
                    ekuOids.Add(new Oid("1.3.6.1.5.5.7.3.1")); // serverAuth
                    keyUsages = isRsa ? (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment) : X509KeyUsageFlags.DigitalSignature;
                    break;

                case CertificatePurpose.TlsClient:
                    ekuOids.Add(new Oid("1.3.6.1.5.5.7.3.2")); // clientAuth
                    keyUsages = X509KeyUsageFlags.DigitalSignature;
                    break;

                case CertificatePurpose.TlsServerAndClient:
                    ekuOids.Add(new Oid("1.3.6.1.5.5.7.3.1")); // serverAuth
                    ekuOids.Add(new Oid("1.3.6.1.5.5.7.3.2")); // clientAuth
                    keyUsages = isRsa ? (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment) : X509KeyUsageFlags.DigitalSignature;
                    break;

                case CertificatePurpose.CodeSigning:
                    ekuOids.Add(new Oid("1.3.6.1.5.5.7.3.3")); // codeSigning
                    keyUsages = X509KeyUsageFlags.DigitalSignature;
                    break;

                case CertificatePurpose.EmailProtection:
                    ekuOids.Add(new Oid("1.3.6.1.5.5.7.3.4")); // emailProtection
                    keyUsages = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unsupported purpose.");
            }
        }

        /// <summary>
        /// Determines whether a crypto profile uses RSA.
        /// </summary>
        /// <param name="cryptoProfile">The crypto profile.</param>
        /// <returns><c>true</c> when RSA is used; otherwise <c>false</c> for ECDSA profiles.</returns>
        private static bool IsRsaCryptoProfile(CertificateCryptoProfile cryptoProfile)
        {
            bool isRsa = cryptoProfile == CertificateCryptoProfile.CompatibilityRsa2048Sha256 || cryptoProfile == CertificateCryptoProfile.CompatibilityRsa3072Sha256;
            return isRsa;
        }

        /// <summary>
        /// Maps RSA profiles to key sizes.
        /// </summary>
        /// <param name="cryptoProfile">The RSA crypto profile.</param>
        /// <returns>The RSA key size in bits.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="cryptoProfile"/> is not an RSA profile.</exception>
        private static int GetRsaKeySize(CertificateCryptoProfile cryptoProfile)
        {
            int keySize;

            switch (cryptoProfile)
            {
                case CertificateCryptoProfile.CompatibilityRsa2048Sha256:
                    keySize = 2048;
                    break;

                case CertificateCryptoProfile.CompatibilityRsa3072Sha256:
                    keySize = 3072;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(cryptoProfile), cryptoProfile, "Not an RSA profile.");
            }

            return keySize;
        }

        /// <summary>
        /// Maps ECDSA profiles to named curves.
        /// </summary>
        /// <param name="cryptoProfile">The ECDSA crypto profile.</param>
        /// <returns>The resolved <see cref="ECCurve"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="cryptoProfile"/> is not an ECDSA profile.</exception>
        private static ECCurve GetEcdsaCurve(CertificateCryptoProfile cryptoProfile)
        {
            ECCurve curve;

            switch (cryptoProfile)
            {
                case CertificateCryptoProfile.ModernEcdsaP256Sha256:
                    curve = ECCurve.NamedCurves.nistP256;
                    break;

                case CertificateCryptoProfile.ModernEcdsaP384Sha384:
                    curve = ECCurve.NamedCurves.nistP384;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(cryptoProfile), cryptoProfile, "Not an ECDSA profile.");
            }

            return curve;
        }

        /// <summary>
        /// Resolves the digest algorithm associated with the selected crypto profile.
        /// </summary>
        /// <param name="cryptoProfile">The crypto profile.</param>
        /// <returns>The resolved <see cref="HashAlgorithmName"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="cryptoProfile"/> is not recognized.</exception>
        private static HashAlgorithmName GetHashAlgorithmForProfile(CertificateCryptoProfile cryptoProfile)
        {
            HashAlgorithmName hash;

            switch (cryptoProfile)
            {
                case CertificateCryptoProfile.CompatibilityRsa2048Sha256:
                case CertificateCryptoProfile.CompatibilityRsa3072Sha256:
                case CertificateCryptoProfile.ModernEcdsaP256Sha256:
                    hash = HashAlgorithmName.SHA256;
                    break;

                case CertificateCryptoProfile.ModernEcdsaP384Sha384:
                    hash = HashAlgorithmName.SHA384;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(cryptoProfile), cryptoProfile, "Unsupported crypto profile.");
            }

            return hash;
        }

        private static X509Certificate2 ImportPkcs12Certificate(byte[] pfxBytes, string password, X509KeyStorageFlags x509KeyStorageFlags)
        {
            // NOTE (net6.0 compatibility):
            // The X509Certificate2(byte[], string, X509KeyStorageFlags) ctor is marked obsolete (SYSLIB0057) in newer TFMs,
            // but it remains the most compatible way to import PKCS#12 across net6.0 runtimes and platforms.
            // Newer targets can use the recommended non-obsolete import APIs; this pragma is intentionally scoped to this call.
#pragma warning disable SYSLIB0057 // Type or member is obsolete
            X509Certificate2 cert = new X509Certificate2(pfxBytes, password, x509KeyStorageFlags);
#pragma warning restore SYSLIB0057 // Type or member is obsolete

            return cert;
        }

        /// <summary>
        /// Imports a PKCS#12 (PFX) file into an <see cref="X509Certificate2"/> using the configured key storage flags.
        /// </summary>
        /// <param name="pfxFilePath">Full path to the PFX file.</param>
        /// <param name="password">PFX password.</param>
        /// <param name="x509KeyStorageFlags">Key storage flags to apply during import.</param>
        /// <returns>An imported certificate instance.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="pfxFilePath"/> is null/empty/whitespace.</exception>
        /// <exception cref="FileNotFoundException">Thrown when <paramref name="pfxFilePath"/> does not exist.</exception>
        /// <remarks>
        /// Reviewer note:
        /// This overload exists for convenience when the certificate is stored on disk. It loads the file bytes and
        /// delegates to the byte-based import to keep the actual import logic in one place.
        /// </remarks>
        /// <example>
        /// <code>
        /// var cert = ImportPkcs12Certificate("C:\\certs\\app.pfx", "secret", X509KeyStorageFlags.MachineKeySet);
        /// </code>
        /// </example>
        private static X509Certificate2 ImportPkcs12Certificate(string pfxFilePath, string password, X509KeyStorageFlags x509KeyStorageFlags)
        {
            if (string.IsNullOrWhiteSpace(pfxFilePath))
            {
                throw new ArgumentException("PFX file path must not be null/empty.", nameof(pfxFilePath));
            }

            if (!File.Exists(pfxFilePath))
            {
                throw new FileNotFoundException("PFX file not found.", pfxFilePath);
            }

            var pfxBytes = File.ReadAllBytes(pfxFilePath);
            return ImportPkcs12Certificate(pfxBytes, password, x509KeyStorageFlags);
        }

    }
}
