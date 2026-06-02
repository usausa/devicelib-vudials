namespace DeviceLib.VUDials;

using System.Globalization;
using System.IO.Ports;
using System.Text;

// ステータスコードレスポンス。
// ReSharper disable IdentifierTypo
#pragma warning disable CA1027
public enum VUDialsStatus
{
    Ok = 0x00,
    Fail = 0x01,
    Busy = 0x02,
    Timeout = 0x03,
    BadData = 0x04,
    ProtocolError = 0x05,
    NoMemory = 0x06,
    InvalidArgument = 0x07,
    BadAddress = 0x08,
    Forbidden = 0x09,
    AlreadyExists = 0x0B,
    Unsupported = 0x0C,
    NotImplemented = 0x0D,
    MalformedPackage = 0x0E,
    RecursiveCall = 0x10,
    DataMismatch = 0x11,
    DeviceOffline = 0x12,
    ModuleNotInit = 0x13,
    I2CError = 0x14,
    UsartError = 0x15,
    SpiError = 0x16
}
#pragma warning restore CA1027
// ReSharper restore IdentifierTypo

// イージング設定（ダイヤル/バックライトのステップ・周期）。
public sealed record EasingConfig(uint DialStep, uint DialPeriod, uint BacklightStep, uint BacklightPeriod);

// 列挙されたダイヤル 1 基分の情報。
public sealed record DialInfo(byte Index, string UidHex);

// VU1 GaugeHub シリアルプロトコル v1 クライアント。
// 物理層: 115200 8N1、行終端 "\r\n"、ASCII Hex。
// パケット: '>' CMD(2hex) TYPE(2hex) LEN(4hex) DATA(2hex × LEN) "\r\n"
// レスポンス: '<' で始まり以下同形式。
#pragma warning disable CA1003
public sealed class VUDialsClient : IDisposable
{
    // ====================================================================
    //  内部型定義
    // ====================================================================

    // VU1 シリアルプロトコルのコマンドID。
    // ReSharper disable IdentifierTypo
#pragma warning disable IDE0051
    private static class Commands
    {
        public const byte SetDialRawSingle = 0x01;
        public const byte SetDialRawMultiple = 0x02;
        public const byte SetDialPercSingle = 0x03;
        public const byte SetDialPercMultiple = 0x04;
        public const byte SetDialCalibrateMax = 0x05;
        public const byte SetDialCalibrateHalf = 0x06;
        public const byte GetDevicesMap = 0x07;
        public const byte ProvisionDevice = 0x08;
        public const byte ResetAllDevices = 0x09;
        public const byte DialPower = 0x0A;
        public const byte GetDeviceUid = 0x0B;
        public const byte RescanBus = 0x0C;
        public const byte DisplayClear = 0x0D;
        public const byte ResetCfg = 0x12;
        public const byte SetRgbBacklight = 0x13;
        public const byte SetDialEasingStep = 0x14;
        public const byte SetDialEasingPeriod = 0x15;
        public const byte SetBacklightEasingStep = 0x16;
        public const byte SetBacklightEasingPeriod = 0x17;
        public const byte GetEasingConfig = 0x18;
        public const byte GetBuildInfo = 0x19;
        public const byte GetFwInfo = 0x20;
        public const byte GetHwInfo = 0x21;
        public const byte GetProtocolInfo = 0x22;
    }
#pragma warning disable IDE0051
    // ReSharper restore IdentifierTypo

    // パケットの DataType フィールドの値。
    private enum DataType : byte
    {
        None = 0x01,
        SingleValue = 0x02,
        MultipleValue = 0x03,
        KeyValuePair = 0x04,
        StatusCode = 0x05
    }

    // 受信パケット 1 件を表す。
    private sealed record Response(DataType Type, string HexPayload)
    {
        public byte[] PayloadBytes => HexToBytes(HexPayload);

        public bool TryGetStatus(out VUDialsStatus status)
        {
            status = VUDialsStatus.Ok;
            if (Type != DataType.StatusCode || string.IsNullOrEmpty(HexPayload))
            {
                return false;
            }
            var v = int.Parse(HexPayload, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            status = (VUDialsStatus)v;
            return true;
        }

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

    // ====================================================================
    //  フィールド・プロパティ
    // ====================================================================

    private readonly SerialPort port;
    private readonly Lock txLock = new();
    private bool disposed;

    // 応答待ちタイムアウト (ms)。
    public int ResponseTimeoutMs { get; set; } = 10_000;

    // 送信時に呼ばれるイベント（TX パケット文字列、CRLF 含まず）。
    public event Action<string>? OnTransmit;

    // 受信時に呼ばれるイベント（RX 1 行分、CRLF 含まず）。
    public event Action<string>? OnReceive;

    // 接続中のポート名。
    public string PortName => port.PortName;

    // ポートが開いているかどうか。
    public bool IsOpen => port.IsOpen;

    // ====================================================================
    //  ライフサイクル
    // ====================================================================

    public VUDialsClient(string portName)
    {
        port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 1_000,
            WriteTimeout = 1_000,
            NewLine = "\r\n",
            Encoding = Encoding.ASCII,
            DtrEnable = true,
            RtsEnable = true
        };
    }

    // ポートを開く。
    public void Open()
    {
        port.Open();
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
    }

    // ポートを閉じる。
    public void Close()
    {
        if (port.IsOpen)
        {
            port.Close();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Close();
        port.Dispose();
    }

    // ====================================================================
    //  低レベル: パケット送受信
    // ====================================================================

    private Response? SendCommand(byte cmd, DataType dataType, params byte[] data)
    {
        ThrowIfClosed();

        var sb = new StringBuilder(9 + (data.Length * 2));
        sb.Append('>');
        sb.Append(cmd.ToString("X2", CultureInfo.InvariantCulture));
        sb.Append(((byte)dataType).ToString("X2", CultureInfo.InvariantCulture));
        sb.Append(data.Length.ToString("X4", CultureInfo.InvariantCulture));
        foreach (var b in data)
        {
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        var line = sb.ToString();

        lock (txLock)
        {
            OnTransmit?.Invoke(line);
            port.DiscardInBuffer();
            port.Write(line + "\r\n");

            var deadline = DateTime.UtcNow.AddMilliseconds(ResponseTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                string rx;
                try
                {
                    rx = port.ReadLine();
                }
                catch (TimeoutException)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(rx))
                {
                    continue;
                }
                OnReceive?.Invoke(rx);

                if (rx.StartsWith('<'))
                {
                    return ParseResponse(rx);
                }
            }
            return null;
        }
    }

    private static Response? ParseResponse(string line)
    {
        if (string.IsNullOrEmpty(line) || line.Length < 9 || line[0] != '<')
        {
            return null;
        }
        var dataType = byte.Parse(line.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var payload = line.Length > 9 ? line[9..] : string.Empty;
        return new Response((DataType)dataType, payload);
    }

    private void ThrowIfClosed()
    {
        if (!port.IsOpen)
        {
            throw new InvalidOperationException("Serial port is not open. Call Open() first.");
        }
    }

    private static bool ExpectOk(Response? resp, out VUDialsStatus status)
    {
        if (resp is null)
        {
            status = VUDialsStatus.Timeout;
            return false;
        }
        if (!resp.TryGetStatus(out status))
        {
            status = VUDialsStatus.Ok;
            return true;
        }
        return status == VUDialsStatus.Ok;
    }

    // ====================================================================
    //  高レベル: 針（ダイヤル）制御
    // ====================================================================

    // 針の位置をパーセンテージ (0-100) で設定する。
    public bool SetDialPercent(byte dialId, byte percent, out VUDialsStatus status)
    {
        if (percent > 100)
        {
            percent = 100;
        }
        var r = SendCommand(Commands.SetDialPercSingle, DataType.KeyValuePair, dialId, percent);
        return ExpectOk(r, out status);
    }

    // 針の位置を raw 値 (0-65535) で設定する。
    public bool SetDialRaw(byte dialId, ushort raw, out VUDialsStatus status)
    {
        byte[] data = [dialId, (byte)(raw >> 8), (byte)(raw & 0xFF)];
        var r = SendCommand(Commands.SetDialRawSingle, DataType.KeyValuePair, data);
        return ExpectOk(r, out status);
    }

    // 複数のダイヤルにパーセンテージを一括設定する。
    public bool SetMultipleDialsPercent(IReadOnlyList<(byte dialId, byte percent)> values, out VUDialsStatus status)
    {
        var data = new byte[values.Count * 2];
        for (var i = 0; i < values.Count; i++)
        {
            data[i * 2] = values[i].dialId;
            data[(i * 2) + 1] = Math.Min(values[i].percent, (byte)100);
        }
        var r = SendCommand(Commands.SetDialPercMultiple, DataType.KeyValuePair, data);
        return ExpectOk(r, out status);
    }

    // RGBW バックライトを設定する。
    public bool SetBacklight(byte dialId, byte r, byte g, byte b, byte w, out VUDialsStatus status)
    {
        var resp = SendCommand(Commands.SetRgbBacklight, DataType.MultipleValue, dialId, r, g, b, w);
        return ExpectOk(resp, out status);
    }

    // ダイヤルの電源 ON/OFF を設定する。
    public bool SetDialPower(bool on, out VUDialsStatus status)
    {
        var resp = SendCommand(Commands.DialPower, DataType.SingleValue, (byte)(on ? 1 : 0));
        return ExpectOk(resp, out status);
    }

    // I2C バスを再スキャンする。
    public bool RescanBus(out VUDialsStatus status)
    {
        var resp = SendCommand(Commands.RescanBus, DataType.None);
        return ExpectOk(resp, out status);
    }

    // バス上に dial を 1 台登録する。
    public bool ProvisionDevice(out VUDialsStatus status)
    {
        var resp = SendCommand(Commands.ProvisionDevice, DataType.None);
        return ExpectOk(resp, out status);
    }

    // Provision ループ + RescanBus でチェーン上の全 dial を確実に認識させる。
    // maxRounds: provision ループの最大回数。
    // 戻り値: 最終的に認識されているオンライン dial 数。失敗時は -1。
    public int ProvisionAndRescan(int maxRounds = 16)
    {
        var prev = -1;
        for (var i = 0; i < maxRounds; i++)
        {
            var pResp = SendCommand(Commands.ProvisionDevice, DataType.None);
            if (pResp is null)
            {
                return -1;
            }
            var online = CountOnline();
            if (online < 0)
            {
                return -1;
            }

            if (online == prev)
            {
                break;
            }
            prev = online;
        }
        SendCommand(Commands.RescanBus, DataType.None);
        return CountOnline();
    }

    private int CountOnline()
    {
        var map = GetDevicesMap();
        if (map is null)
        {
            return -1;
        }
        var n = 0;
        foreach (var b in map)
        {
            if (b == 0x01)
            {
                n++;
            }
        }
        return n;
    }

    // 全デバイスをリセットする。
    public bool ResetAllDevices(out VUDialsStatus status)
    {
        var resp = SendCommand(Commands.ResetAllDevices, DataType.None);
        return ExpectOk(resp, out status);
    }

    // 設定をデフォルトに戻す。
    public bool ResetConfig(out VUDialsStatus status)
    {
        var resp = SendCommand(Commands.ResetCfg, DataType.None);
        return ExpectOk(resp, out status);
    }

    // キャリブレーションを実行する。fullScale=true → MAX、false → HALF。
    public bool CalibrateDial(byte dialId, uint value, bool fullScale, out VUDialsStatus status)
    {
        var cmd = fullScale ? Commands.SetDialCalibrateMax : Commands.SetDialCalibrateHalf;
        byte[] data =
        [
            dialId,
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF)
        ];
        var resp = SendCommand(cmd, DataType.KeyValuePair, data);
        return ExpectOk(resp, out status);
    }

    // ダイヤル動作のイージング・ステップを設定する。
    public bool SetDialEasingStep(byte dialId, uint step, out VUDialsStatus status) =>
        ExpectOk(SendUint32(Commands.SetDialEasingStep, dialId, step), out status);

    // ダイヤル動作のイージング・周期 (ms) を設定する。
    public bool SetDialEasingPeriod(byte dialId, uint periodMs, out VUDialsStatus status) =>
        ExpectOk(SendUint32(Commands.SetDialEasingPeriod, dialId, periodMs), out status);

    // バックライト動作のイージング・ステップを設定する。
    public bool SetBacklightEasingStep(byte dialId, uint step, out VUDialsStatus status) =>
        ExpectOk(SendUint32(Commands.SetBacklightEasingStep, dialId, step), out status);

    // バックライト動作のイージング・周期 (ms) を設定する。
    public bool SetBacklightEasingPeriod(byte dialId, uint periodMs, out VUDialsStatus status) =>
        ExpectOk(SendUint32(Commands.SetBacklightEasingPeriod, dialId, periodMs), out status);

    private Response? SendUint32(byte cmd, byte dialId, uint value)
    {
        byte[] data =
        [
            dialId,
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF)
        ];
        return SendCommand(cmd, DataType.SingleValue, data);
    }

    // ====================================================================
    //  高レベル: 取得系
    // ====================================================================

    // バス上のオンライン ダイヤルマップを取得する（生 payload の byte 配列）。
    public byte[]? GetDevicesMap()
    {
        var r = SendCommand(Commands.GetDevicesMap, DataType.None);
        return r?.PayloadBytes;
    }

    // 指定ダイヤルの UID を取得する（生 HEX 文字列）。
    public string? GetDeviceUid(byte dialId)
    {
        var r = SendCommand(Commands.GetDeviceUid, DataType.SingleValue, dialId);
        return r?.HexPayload;
    }

    // 指定ダイヤルのファームウェアバージョンを取得する（HEX 文字列）。
    public string? GetFirmwareVersion(byte dialId)
    {
        var r = SendCommand(Commands.GetFwInfo, DataType.SingleValue, dialId);
        return r?.HexPayload;
    }

    // 指定ダイヤルのハードウェアバージョンを取得する（HEX 文字列）。
    public string? GetHardwareVersion(byte dialId)
    {
        var r = SendCommand(Commands.GetHwInfo, DataType.SingleValue, dialId);
        return r?.HexPayload;
    }

    // 指定ダイヤルのプロトコルバージョンを取得する（HEX 文字列）。
    public string? GetProtocolVersion(byte dialId)
    {
        var r = SendCommand(Commands.GetProtocolInfo, DataType.SingleValue, dialId);
        return r?.HexPayload;
    }

    // 指定ダイヤルのビルドハッシュを取得する（HEX 文字列）。
    public string? GetBuildInfo(byte dialId)
    {
        var r = SendCommand(Commands.GetBuildInfo, DataType.SingleValue, dialId);
        return r?.HexPayload;
    }

    // イージング設定を取得する（uint32 × 4 個の BE 配列）。
    public EasingConfig? GetEasingConfig(byte dialId)
    {
        var r = SendCommand(Commands.GetEasingConfig, DataType.SingleValue, dialId);
        if (r is null)
        {
            return null;
        }
        var b = r.PayloadBytes;
        if (b.Length < 16)
        {
            return null;
        }
        return new EasingConfig(
            ReadUInt32Be(b, 0),
            ReadUInt32Be(b, 4),
            ReadUInt32Be(b, 8),
            ReadUInt32Be(b, 12));
    }

    private static uint ReadUInt32Be(byte[] b, int offset) =>
        ((uint)b[offset] << 24) | ((uint)b[offset + 1] << 16) |
        ((uint)b[offset + 2] << 8) | b[offset + 3];

    // 接続中のダイヤル一覧を取得する（devices_map + uid）。
    // dial ID は 0 始まり。
    public IReadOnlyList<DialInfo> ListDials()
    {
        var list = new List<DialInfo>();
        foreach (var id in GetOnlineDialIds())
        {
            var uid = GetDeviceUid(id) ?? "(unknown)";
            list.Add(new DialInfo(id, uid));
        }
        return list;
    }

    // 接続中の dial ID（0 始まり）のみを取得する。
    public IReadOnlyList<byte> GetOnlineDialIds()
    {
        var ids = new List<byte>();
        var map = GetDevicesMap();
        if (map is null)
        {
            return ids;
        }
        for (var i = 0; i < map.Length; i++)
        {
            if (map[i] == 0x01)
            {
                ids.Add((byte)i);
            }
        }
        return ids;
    }

    // ディスプレイをクリアする。
    public bool DisplayClear(byte dialId, bool whiteBackground, out VUDialsStatus status)
    {
        var resp = SendCommand(
            Commands.DisplayClear,
            DataType.KeyValuePair,
            dialId,
            (byte)(whiteBackground ? 1 : 0));
        return ExpectOk(resp, out status);
    }
}
#pragma warning restore CA1003
