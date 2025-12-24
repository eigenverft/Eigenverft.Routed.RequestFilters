using System.Net.Sockets;

namespace Eigenverft.Routed.RequestFilters.GenericExtensions.IPAddressExtensions
{
    /// <summary>
    /// Provides extension methods for working with IP addresses in a reverse proxy context.
    /// </summary>
    /// <remarks>
    /// The main entry point is <see cref="GetIpInfo"/>, which converts an IP address into a simple tuple
    /// containing the protocol version and a normalized textual representation.
    ///
    /// <para>Example:</para>
    /// <code>
    /// var (version, remoteIp) = context.Connection.RemoteIpAddress.GetIpInfo();
    /// </code>
    /// </remarks>
    public static partial class IPAddressExtensions
    {
        /// <summary>
        /// Defines the IP protocol versions that can be reported for a given address.
        /// </summary>
        /// <remarks>
        /// The enumeration indicates whether an address is treated as IPv4, IPv6,
        /// or remains unspecified when the address family is not recognized.
        /// </remarks>
        public enum IpVersion
        {
            /// <summary>
            /// The IP version is not known or the address family is not supported.
            /// </summary>
            Unknown,

            /// <summary>
            /// The address is an IPv4 address or an IPv4-mapped IPv6 address.
            /// </summary>
            IPv4,

            /// <summary>
            /// The address is a native IPv6 address.
            /// </summary>
            IPv6
        }

        /// <summary>
        /// Determines the IP version and extracts a normalized remote IP string for the specified address.
        /// </summary>
        /// <remarks>
        /// The method supports IPv4, IPv6, and IPv4-mapped IPv6 addresses. For IPv4-mapped IPv6,
        /// the address is converted to an IPv4 representation. For IPv6, any scope identifier
        /// segment after a percent sign is removed.
        ///
        /// When the input address is null or the address family is not supported, the method returns
        /// <see cref="IpVersion.Unknown"/> and a null string.
        ///
        /// <para>Example:</para>
        /// <code>
        /// var (version, remoteIp) = remoteAddress.GetIpInfo();
        /// if (version == IPAddressExtensions.IpVersion.IPv4)
        /// {
        ///     // Handle IPv4-specific logic
        /// }
        /// </code>
        /// </remarks>
        /// <param name="address">The IP address to analyze.</param>
        /// <returns>
        /// A tuple whose <c>Version</c> indicates the detected protocol version and whose <c>RemoteIp</c>
        /// contains the normalized textual representation, or null when unavailable.
        /// </returns>
        public static (IpVersion Version, string? RemoteIp) GetIpInfo(this System.Net.IPAddress? address)
        {
            if (address is null)
            {
                return (IpVersion.Unknown, null);
            }

            // Normalize IPv4-mapped IPv6 to IPv4.
            if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            IpVersion version;
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                version = IpVersion.IPv4;
            }
            else if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                version = IpVersion.IPv6;
            }
            else
            {
                return (IpVersion.Unknown, null);
            }

            var text = address.ToString();

            // Strip IPv6 scope identifier if present.
            if (version == IpVersion.IPv6)
            {
                var percentIndex = text.IndexOf('%');
                if (percentIndex >= 0)
                {
                    text = text.Substring(0, percentIndex);
                }
            }

            return (version, text);
        }

        /// <summary>
        /// Converts an IP address into a normalized textual representation.
        /// </summary>
        /// <remarks>
        /// This is a convenience wrapper around <see cref="GetIpInfo"/> for scenarios where only
        /// the textual representation is needed.
        ///
        /// <para>Example:</para>
        /// <code>
        /// var remoteIpText = context.Connection.RemoteIpAddress.ToNormalizedAddressString();
        /// </code>
        /// </remarks>
        /// <param name="address">The IP address to convert.</param>
        /// <returns>
        /// The normalized textual representation of the address, or null when the address is not available
        /// or the family is not supported.
        /// </returns>
        public static string? ToNormalizedAddressString(this System.Net.IPAddress? address)
        {
            var (_, remoteIp) = address.GetIpInfo();
            return remoteIp;
        }
    }
}