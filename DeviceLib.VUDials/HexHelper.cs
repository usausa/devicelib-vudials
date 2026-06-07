namespace DeviceLib.VUDials;

internal static class HexHelper
{
    private static ReadOnlySpan<byte> HexDigits => "0123456789ABCDEF"u8;

    public static int WriteHex(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var pos = 0;
        foreach (var b in source)
        {
            destination[pos] = HexDigits[b >> 4];
            destination[pos + 1] = HexDigits[b & 0x0F];
            pos += 2;
        }
        return pos;
    }

    public static int ReadHex(ReadOnlySpan<byte> hex, Span<byte> destination)
    {
        var count = hex.Length / 2;
        for (var i = 0; i < count; i++)
        {
            destination[i] = ParseHexByte(hex.Slice(i * 2, 2));
        }
        return count;
    }

    public static byte ParseHexByte(ReadOnlySpan<byte> hex) =>
        (byte)((FromHexNibble(hex[0]) << 4) | FromHexNibble(hex[1]));

    private static int FromHexNibble(byte c)
    {
        if (c is >= (byte)'0' and <= (byte)'9')
        {
            return c - '0';
        }
        if (c is >= (byte)'A' and <= (byte)'F')
        {
            return c - 'A' + 10;
        }
        if (c is >= (byte)'a' and <= (byte)'f')
        {
            return c - 'a' + 10;
        }
        return 0;
    }
}
