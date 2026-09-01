using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace AdminForge.Tools.Network.SubnetCalculator;

/// <summary>A parsed network and everything derived from it.</summary>
/// <param name="Address">The address as entered, before masking.</param>
/// <param name="Network">The network address.</param>
/// <param name="Prefix">The prefix length.</param>
/// <param name="IsIPv6">Whether this is an IPv6 network.</param>
public sealed record SubnetFacts(IPAddress Address, IPAddress Network, int Prefix, bool IsIPv6)
{
    /// <summary>Total width of an address in bits: 32 or 128.</summary>
    public int Bits => IsIPv6 ? 128 : 32;

    /// <summary>Every address in the range, including network and broadcast.</summary>
    public BigInteger TotalAddresses => BigInteger.One << (Bits - Prefix);

    /// <summary>The last address in the range. For IPv4 this is the broadcast address.</summary>
    public IPAddress LastAddress => SubnetMath.LastAddress(Network, Prefix);

    /// <summary>The dotted-decimal mask. IPv6 networks have no equivalent notation.</summary>
    public IPAddress? Mask => IsIPv6 ? null : SubnetMath.MaskFor(Prefix, false);

    /// <summary>The inverse mask, as used in ACLs. IPv4 only.</summary>
    public IPAddress? Wildcard => IsIPv6 ? null : SubnetMath.WildcardFor(Prefix);
}

/// <summary>
/// Address arithmetic shared by the subnet tools. Works on the raw address bytes so
/// IPv4 and IPv6 follow the same code path.
/// </summary>
public static class SubnetMath
{
    /// <summary>
    /// Parse a network written as CIDR, as an address plus a dotted-decimal mask, or as
    /// a bare address (which is treated as a single host).
    /// </summary>
    /// <param name="value">The user's input.</param>
    /// <param name="facts">The parsed network.</param>
    /// <param name="error">Why parsing failed.</param>
    public static bool TryParse(string value, out SubnetFacts? facts, out string? error)
    {
        facts = null;
        error = null;

        string input = (value ?? string.Empty).Trim();

        if (input.Length == 0)
        {
            error = "Enter a network, for example 10.20.0.0/22.";
            return false;
        }

        string addressPart;
        string? suffix = null;

        int slash = input.IndexOf('/');

        if (slash >= 0)
        {
            addressPart = input[..slash].Trim();
            suffix = input[(slash + 1)..].Trim();
        }
        else
        {
            string[] parts = input.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            addressPart = parts[0];
            suffix = parts.Length > 1 ? parts[1] : null;
        }

        if (!IPAddress.TryParse(addressPart, out IPAddress? address))
        {
            error = $"'{addressPart}' is not a valid IP address.";
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        bool isV6 = address.AddressFamily == AddressFamily.InterNetworkV6;
        int bits = isV6 ? 128 : 32;
        int prefix;

        if (suffix is null)
        {
            prefix = bits;
        }
        else if (int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            prefix = parsed;
        }
        else if (!isV6 && IPAddress.TryParse(suffix, out IPAddress? mask) && TryPrefixFromMask(mask, out int fromMask))
        {
            prefix = fromMask;
        }
        else
        {
            error = $"'{suffix}' is not a valid prefix length or subnet mask.";
            return false;
        }

        if (prefix < 0 || prefix > bits)
        {
            error = $"A prefix length for {(isV6 ? "IPv6" : "IPv4")} must be between 0 and {bits}.";
            return false;
        }

        facts = new SubnetFacts(address, NetworkAddress(address, prefix), prefix, isV6);
        return true;
    }

    /// <summary>Mask the host bits off an address.</summary>
    /// <param name="address">The address.</param>
    /// <param name="prefix">The prefix length.</param>
    public static IPAddress NetworkAddress(IPAddress address, int prefix)
    {
        byte[] bytes = address.GetAddressBytes();
        ApplyMask(bytes, prefix, setHostBits: false);
        return new IPAddress(bytes);
    }

    /// <summary>The highest address in the range.</summary>
    /// <param name="network">The network address.</param>
    /// <param name="prefix">The prefix length.</param>
    public static IPAddress LastAddress(IPAddress network, int prefix)
    {
        byte[] bytes = network.GetAddressBytes();
        ApplyMask(bytes, prefix, setHostBits: true);
        return new IPAddress(bytes);
    }

    /// <summary>Build the subnet mask for a prefix length.</summary>
    /// <param name="prefix">The prefix length.</param>
    /// <param name="isIPv6">Whether to build a 128-bit mask.</param>
    public static IPAddress MaskFor(int prefix, bool isIPv6)
    {
        byte[] bytes = new byte[isIPv6 ? 16 : 4];

        for (int i = 0; i < prefix; i++)
        {
            bytes[i / 8] |= (byte)(0x80 >> (i % 8));
        }

        return new IPAddress(bytes);
    }

    /// <summary>Build the inverse (ACL) mask for an IPv4 prefix length.</summary>
    /// <param name="prefix">The prefix length.</param>
    public static IPAddress WildcardFor(int prefix)
    {
        byte[] bytes = MaskFor(prefix, false).GetAddressBytes();

        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)~bytes[i];
        }

        return new IPAddress(bytes);
    }

    /// <summary>Derive a prefix length from a dotted-decimal mask, rejecting non-contiguous masks.</summary>
    /// <param name="mask">The mask.</param>
    /// <param name="prefix">The derived prefix length.</param>
    public static bool TryPrefixFromMask(IPAddress mask, out int prefix)
    {
        prefix = 0;

        if (mask.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        uint value = ToUInt32(mask);
        bool seenZero = false;

        for (int i = 31; i >= 0; i--)
        {
            bool bit = ((value >> i) & 1) == 1;

            if (bit)
            {
                // A one after a zero means the mask is not contiguous, e.g. 255.0.255.0.
                if (seenZero)
                {
                    return false;
                }

                prefix++;
            }
            else
            {
                seenZero = true;
            }
        }

        return true;
    }

    /// <summary>Offset an address by a number of addresses.</summary>
    /// <param name="address">The starting address.</param>
    /// <param name="offset">How far to move.</param>
    public static IPAddress Add(IPAddress address, BigInteger offset)
    {
        byte[] bytes = address.GetAddressBytes();
        BigInteger value = ToBigInteger(bytes) + offset;
        return FromBigInteger(value, bytes.Length);
    }

    /// <summary>The address as an unsigned big-endian integer.</summary>
    /// <param name="address">The address.</param>
    public static BigInteger ToBigInteger(IPAddress address) => ToBigInteger(address.GetAddressBytes());

    /// <summary>Render an IPv4 address as dotted binary, one octet per group.</summary>
    /// <param name="address">The address.</param>
    public static string ToBinary(IPAddress address) =>
        string.Join('.', address.GetAddressBytes().Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));

    private static void ApplyMask(byte[] bytes, int prefix, bool setHostBits)
    {
        for (int i = 0; i < bytes.Length * 8; i++)
        {
            if (i < prefix)
            {
                continue;
            }

            int index = i / 8;
            byte bit = (byte)(0x80 >> (i % 8));

            if (setHostBits)
            {
                bytes[index] |= bit;
            }
            else
            {
                bytes[index] &= (byte)~bit;
            }
        }
    }

    private static uint ToUInt32(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static BigInteger ToBigInteger(byte[] bigEndian)
    {
        // A leading zero byte keeps BigInteger from reading the value as negative.
        byte[] littleEndian = new byte[bigEndian.Length + 1];

        for (int i = 0; i < bigEndian.Length; i++)
        {
            littleEndian[i] = bigEndian[bigEndian.Length - 1 - i];
        }

        return new BigInteger(littleEndian);
    }

    private static IPAddress FromBigInteger(BigInteger value, int byteCount)
    {
        byte[] littleEndian = value.ToByteArray();
        byte[] bigEndian = new byte[byteCount];

        for (int i = 0; i < byteCount; i++)
        {
            bigEndian[byteCount - 1 - i] = i < littleEndian.Length ? littleEndian[i] : (byte)0;
        }

        return new IPAddress(bigEndian);
    }
}
