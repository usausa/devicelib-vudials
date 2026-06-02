namespace DeviceLib.VUDials;

using System.Globalization;

// 受信パケット 1 件を表す。
public sealed record VUDialsResponse(byte Cmd, VUDialsDataType DataType, ushort DataLen, string HexPayload)
{
    // HexPayload を byte 配列に変換する。
    public byte[] PayloadBytes => HexToBytes(HexPayload);

    // DataType=StatusCode のとき、ステータスコードを取り出す。
    public bool TryGetStatus(out VUDialsStatus status)
    {
        status = VUDialsStatus.Ok;
        if (DataType != VUDialsDataType.StatusCode || string.IsNullOrEmpty(HexPayload))
        {
            return false;
        }
        var v = int.Parse(HexPayload, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        status = (VUDialsStatus)v;
        return true;
    }

    // Hex 文字列を byte 配列に変換する。
    public static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex))
        {
            return [];
        }
        if (hex.Length % 2 != 0)
        {
            throw new FormatException($"Hex string length must be even: '{hex}'");
        }
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        return bytes;
    }
}

// イージング設定（ダイヤル/バックライトのステップ・周期）。
public sealed record EasingConfig(uint DialStep, uint DialPeriod, uint BacklightStep, uint BacklightPeriod);

// 列挙されたダイヤル 1 基分の情報。
public sealed record DialInfo(byte Index, string UidHex);
