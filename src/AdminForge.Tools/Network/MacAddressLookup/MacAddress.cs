using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace AdminForge.Tools.Network.MacAddressLookup;

/// <summary>
/// A 48-bit IEEE 802 MAC address, parsed from any of the notations an admin meets in
/// the wild: colon or hyphen separated octets, Cisco's dotted groups, or bare hex.
/// </summary>
public readonly record struct MacAddress
{
    private readonly ulong _value;

    private MacAddress(ulong value) => _value = value;

    /// <summary>The address as a 48-bit number, first octet most significant.</summary>
    public ulong Value => _value;

    /// <summary>The first octet, which carries both flag bits.</summary>
    public byte FirstOctet => (byte)(_value >> 40);

    /// <summary>
    /// The I/G bit, the least significant bit of the first octet. Set means a group
    /// (multicast or broadcast) address rather than one interface.
    /// </summary>
    public bool IsMulticast => (FirstOctet & 0x01) != 0;

    /// <summary>
    /// The U/L bit, the second least significant bit of the first octet. Set means the
    /// address was assigned locally (by an OS, a hypervisor or an admin) rather than
    /// burned in by a manufacturer, so no vendor registered it.
    /// </summary>
    public bool IsLocallyAdministered => (FirstOctet & 0x02) != 0;

    /// <summary>True for FF:FF:FF:FF:FF:FF.</summary>
    public bool IsBroadcast => _value == 0xFFFF_FFFF_FFFF;

    /// <summary>
    /// Parse a MAC address. Accepts <c>00:1A:2B:3C:4D:5E</c>, <c>00-1A-2B-3C-4D-5E</c>,
    /// <c>001A.2B3C.4D5E</c> and <c>001A2B3C4D5E</c>, in either case, with surrounding
    /// whitespace. Single-digit octets are refused: <c>0:1a:...</c> is ambiguous enough
    /// that guessing would hide a typo.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="address">The parsed address.</param>
    public static bool TryParse(string? text, [NotNullWhen(true)] out MacAddress? address)
    {
        address = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim();
        string? hex = value.Length switch
        {
            // 00:1A:2B:3C:4D:5E or 00-1A-2B-3C-4D-5E, one separator used throughout.
            17 when IsGrouped(value, 2, value[2]) && value[2] is ':' or '-' => value.Replace(value[2].ToString(), "", StringComparison.Ordinal),

            // 001A.2B3C.4D5E, the Cisco IOS form.
            14 when IsGrouped(value, 4, '.') => value.Replace(".", "", StringComparison.Ordinal),

            // 001A2B3C4D5E
            12 => value,

            _ => null,
        };

        if (hex is null
            || hex.Length != 12
            || !ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong parsed))
        {
            return false;
        }

        address = new MacAddress(parsed);
        return true;
    }

    /// <summary>
    /// The first <paramref name="bits"/> bits of the address as upper-case hex, which
    /// is how the IEEE keys its registries: 24 for MA-L, 28 for MA-M, 36 for MA-S.
    /// </summary>
    /// <param name="bits">24, 28 or 36.</param>
    public string Prefix(int bits)
    {
        if (bits is not (24 or 28 or 36))
        {
            throw new ArgumentOutOfRangeException(nameof(bits), bits, "A registry prefix is 24, 28 or 36 bits.");
        }

        return (_value >> (48 - bits)).ToString("X" + (bits / 4).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The same address with the I/G bit cleared. A group address is built from the
    /// OUI of whoever defined it, so this is what to look up in the registry.
    /// </summary>
    public MacAddress WithoutGroupBit() => new(_value & ~(1UL << 40));

    /// <summary>Six octets joined by <paramref name="separator"/>, upper-case.</summary>
    /// <param name="separator">':' or '-'.</param>
    public string ToOctets(char separator)
    {
        var builder = new StringBuilder(17);

        for (int shift = 40; shift >= 0; shift -= 8)
        {
            if (builder.Length > 0)
            {
                builder.Append(separator);
            }

            builder.Append(((byte)(_value >> shift)).ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>The Cisco dotted form, lower-case as IOS prints it: <c>001a.2b3c.4d5e</c>.</summary>
    public string ToCisco()
    {
        string hex = ToBare().ToLowerInvariant();
        return $"{hex[..4]}.{hex[4..8]}.{hex[8..]}";
    }

    /// <summary>Twelve hex digits with no separator, upper-case.</summary>
    public string ToBare() => _value.ToString("X12", CultureInfo.InvariantCulture);

    /// <summary>
    /// The IPv6 link-local address SLAAC derives from this MAC with modified EUI-64
    /// (RFC 4291 appendix A): FFFE in the middle and the U/L bit inverted. Hosts that
    /// use privacy or stable-opaque addresses will not use it, but routers, printers
    /// and many embedded devices still do.
    /// </summary>
    public string ToEui64LinkLocal()
    {
        ulong high = (_value >> 24) ^ 0x02_0000;
        ulong low = _value & 0xFF_FFFF;
        ulong interfaceId = (high << 40) | (0xFFFEUL << 24) | low;

        Span<byte> bytes = stackalloc byte[16];
        bytes[0] = 0xFE;
        bytes[1] = 0x80;

        for (int i = 0; i < 8; i++)
        {
            bytes[8 + i] = (byte)(interfaceId >> (56 - (8 * i)));
        }

        return new System.Net.IPAddress(bytes).ToString();
    }

    /// <inheritdoc />
    public override string ToString() => ToOctets(':');

    private static bool IsGrouped(string value, int groupLength, char separator)
    {
        // Every (groupLength + 1)th character is the separator and nothing else is.
        for (int i = 0; i < value.Length; i++)
        {
            bool shouldBeSeparator = (i + 1) % (groupLength + 1) == 0;

            if ((value[i] == separator) != shouldBeSeparator)
            {
                return false;
            }
        }

        return true;
    }
}
