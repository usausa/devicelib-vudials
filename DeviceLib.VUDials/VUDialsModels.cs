namespace DeviceLib.VUDials;

using System.Globalization;

/// <summary>
/// 受信パケット 1 件を表す。
/// </summary>
public sealed record VUDialsResponse(byte Cmd, VUDialsDataType DataType, ushort DataLen, string HexPayload)
{
    /// <summary>HexPayload を byte 配列に変換する。</summary>
    public byte[] PayloadBytes => HexToBytes(HexPayload);

    /// <summary>HexPayload を ASCII 文字列としてデコード（印字可能な範囲のみ）。</summary>
    public string PayloadAsAscii
    {
        get
        {
            var bytes = PayloadBytes;
            var chars = new char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
                chars[i] = bytes[i] is >= 0x20 and < 0x7F ? (char)bytes[i] : '.';
            return new string(chars);
        }
    }

    /// <summary>DataType=StatusCode のとき、ステータスコードを取り出す。</summary>
    public bool TryGetStatus(out VUDialsStatus status)
    {
        status = VUDialsStatus.Ok;
        if (DataType != VUDialsDataType.StatusCode || string.IsNullOrEmpty(HexPayload))
            return false;
        var v = int.Parse(HexPayload, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        status = (VUDialsStatus)v;
        return true;
    }

    /// <summary>Hex 文字列を byte 配列に変換する。</summary>
    public static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return [];
        if (hex.Length % 2 != 0)
            throw new FormatException($"Hex string length must be even: '{hex}'");
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }
}

/// <summary>イージング設定（ダイヤル/バックライトのステップ・周期）。</summary>
public sealed record EasingConfig(uint DialStep, uint DialPeriod, uint BacklightStep, uint BacklightPeriod);

/// <summary>列挙されたダイヤル 1 基分の情報。</summary>
public sealed record DialInfo(byte Index, string UidHex);
