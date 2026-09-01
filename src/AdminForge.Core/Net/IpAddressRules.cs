using System.Net;
using System.Net.Sockets;

namespace AdminForge.Core.Net;

/// <summary>
/// Classifies IP addresses that a public instance must never be talked into
/// contacting. Shared by every tool that takes a user-supplied target, so the rule
/// lives in exactly one place and gets one set of tests.
/// </summary>
public static class IpAddressRules
{
    /// <summary>
    /// True when the address belongs to a range that is not a legitimate public
    /// target: loopback, private, link-local (including cloud instance metadata at
    /// 169.254.169.254), carrier-grade NAT, unique-local, multicast, benchmarking,
    /// documentation, or the unspecified address.
    /// </summary>
    /// <param name="address">The address to classify.</param>
    public static bool IsPrivateOrReserved(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // An IPv4-mapped IPv6 address (::ffff:127.0.0.1) must be judged on its IPv4 value,
        // otherwise it is a trivial bypass of every rule below.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsReservedV4(address),
            AddressFamily.InterNetworkV6 => IsReservedV6(address),
            _ => true,
        };
    }

    private static bool IsReservedV4(IPAddress address)
    {
        Span<byte> b = stackalloc byte[4];

        if (!address.TryWriteBytes(b, out _))
        {
            return true;
        }

        return b[0] switch
        {
            0 => true,                                        // 0.0.0.0/8   unspecified / this network
            10 => true,                                       // 10.0.0.0/8  private
            127 => true,                                      // 127.0.0.0/8 loopback
            100 when b[1] is >= 64 and <= 127 => true,         // 100.64/10   carrier-grade NAT
            169 when b[1] == 254 => true,                      // 169.254/16  link-local + metadata
            172 when b[1] is >= 16 and <= 31 => true,          // 172.16/12   private
            192 when b[1] == 168 => true,                      // 192.168/16  private
            192 when b[1] == 0 && b[2] is 0 or 2 => true,       // 192.0.0/24, 192.0.2/24 (TEST-NET-1)
            192 when b[1] == 88 && b[2] == 99 => true,          // 192.88.99/24 6to4 relay anycast
            198 when b[1] is 18 or 19 => true,                 // 198.18/15   benchmarking
            198 when b[1] == 51 && b[2] == 100 => true,         // 198.51.100/24 TEST-NET-2
            203 when b[1] == 0 && b[2] == 113 => true,          // 203.0.113/24  TEST-NET-3
            >= 224 => true,                                    // multicast, reserved, broadcast
            _ => false,
        };
    }

    private static bool IsReservedV6(IPAddress address)
    {
        if (IPAddress.IPv6Loopback.Equals(address)
            || IPAddress.IPv6Any.Equals(address)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast
            || address.IsIPv6Teredo)
        {
            return true;
        }

        Span<byte> b = stackalloc byte[16];

        if (!address.TryWriteBytes(b, out _))
        {
            return true;
        }

        // fc00::/7 unique local
        if ((b[0] & 0xFE) == 0xFC)
        {
            return true;
        }

        // 2001:db8::/32 documentation
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)
        {
            return true;
        }

        // 64:ff9b::/96 NAT64 — resolves to an embedded IPv4 we cannot vet here.
        return b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B;
    }
}
