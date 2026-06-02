namespace DeviceLib.VUDials;

using System.Globalization;
using System.IO.Ports;
using System.Text;

// VU1 GaugeHub シリアルプロトコル v1 クライアント。
// 物理層: 115200 8N1、行終端 "\r\n"、ASCII Hex。
// パケット: '>' CMD(2hex) TYPE(2hex) LEN(4hex) DATA(2hex × LEN) "\r\n"
// レスポンス: '<' で始まり以下同形式。
public sealed class VUDialsClient : IDisposable
{
    private readonly SerialPort port;
    private readonly Lock txLock = new();
    private bool disposed;

    // 応答待ちタイムアウト (ms)。
    public int ResponseTimeoutMs { get; set; } = 10_000;

    // 送信時に呼ばれるイベント（TX パケット文字列、CRLF 含まず）。
    public event Action<string>? OnTransmit;

    // 受信時に呼ばれるイベント（RX 1 行分、CRLF 含まず）。
    public event Action<string>? OnReceive;

    // 一般ログイベント。
    public event Action<string>? OnLog;

    // 接続中のポート名。
    public string PortName => port.PortName;

    // ポートが開いているかどうか。
    public bool IsOpen => port.IsOpen;

    public VUDialsClient(string portName)
    {
        port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 1_000,
            WriteTimeout = 1_000,
            NewLine = "\r\n",
            Encoding = Encoding.ASCII,
            DtrEnable = true,
            RtsEnable = true,
        };
    }

    // ポートを開く。
    public void Open()
    {
        port.Open();
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
        OnLog?.Invoke($"Opened {port.PortName} @115200 8N1");
    }

    // ポートを閉じる。
    public void Close()
    {
        if (port.IsOpen)
        {
            port.Close();
            OnLog?.Invoke($"Closed {port.PortName}");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            Close();
        }
        catch
        {
            // ignore
        }
        port.Dispose();
    }

    // ====================================================================
    //  低レベル: パケット送受信
    // ====================================================================

    // 任意のコマンドを送信し、'<' で始まる最初のレスポンス行を取得する。
    // タイムアウト時は null を返す。
    public VUDialsResponse? SendCommand(byte cmd, VUDialsDataType dataType, params byte[] data)
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

    // '<CCTTLLLLDD...' 形式の文字列をパースする。
    public static VUDialsResponse? ParseResponse(string line)
    {
        if (string.IsNullOrEmpty(line) || line.Length < 9 || line[0] != '<')
        {
            return null;
        }
        var cmd = byte.Parse(line.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var dataType = byte.Parse(line.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var dataLen = ushort.Parse(line.AsSpan(5, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var payload = line.Length > 9 ? line[9..] : string.Empty;
        return new VUDialsResponse(cmd, (VUDialsDataType)dataType, dataLen, payload);
    }

    private void ThrowIfClosed()
    {
        if (!port.IsOpen)
        {
            throw new InvalidOperationException("Serial port is not open. Call Open() first.");
        }
    }

    private bool ExpectOk(VUDialsResponse? resp, out VUDialsStatus status)
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
        var r = SendCommand(VUDialsCommands.SetDialPercSingle, VUDialsDataType.KeyValuePair, dialId, percent);
        return ExpectOk(r, out status);
    }

    // 針の位置を raw 値 (0-65535) で設定する。
    public bool SetDialRaw(byte dialId, ushort raw, out VUDialsStatus status)
    {
        byte[] data = [dialId, (byte)(raw >> 8), (byte)(raw & 0xFF)];
        var r = SendCommand(VUDialsCommands.SetDialRawSingle, VUDialsDataType.KeyValuePair, data);
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
        var r = SendCommand(VUDialsCommands.SetDialPercMultiple, VUDialsDataType.KeyValuePair, data);
        return ExpectOk(r, out status);
    }

    // RGBW バックライトを設定する。
    public bool SetBacklight(byte dialId, byte r, byte g, byte b, byte w, out VUDialsStatus status)
    {
        var resp = SendCommand(VUDialsCommands.SetRgbBacklight, VUDialsDataType.MultipleValue, dialId, r, g, b, w);
        return ExpectOk(resp, out status);
    }

    // ダイヤルの電源 ON/OFF を設定する。
    public bool SetDialPower(bool on, out VUDialsStatus status)
    {
        var resp = SendCommand(VUDialsCommands.DialPower, VUDialsDataType.SingleValue, (byte)(on ? 1 : 0));
        return ExpectOk(resp, out status);
    }

    // I2C バスを再スキャンする。
    public bool RescanBus(out VUDialsStatus status)
    {
        var resp = SendCommand(VUDialsCommands.RescanBus, VUDialsDataType.None);
        return ExpectOk(resp, out status);
    }

    // バス上に dial を 1 台登録する。
    public bool ProvisionDevice(out VUDialsStatus status)
    {
        var resp = SendCommand(VUDialsCommands.ProvisionDevice, VUDialsDataType.None);
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
            var pResp = SendCommand(VUDialsCommands.ProvisionDevice, VUDialsDataType.None);
            if (pResp is null)
            {
                return -1;
            }
            var online = CountOnline();
            if (online < 0)
            {
                return -1;
            }
            OnLog?.Invoke($"provision round {i + 1}: online={online}");
            if (online == prev)
            {
                break;
            }
            prev = online;
        }
        SendCommand(VUDialsCommands.RescanBus, VUDialsDataType.None);
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
        var resp = SendCommand(VUDialsCommands.ResetAllDevices, VUDialsDataType.None);
        return ExpectOk(resp, out status);
    }

    // 設定をデフォルトに戻す。
    public bool ResetConfig(out VUDialsStatus status)
    {
        var resp = SendCommand(VUDialsCommands.ResetCfg, VUDialsDataType.None);
        return ExpectOk(resp, out status);
    }

    // キャリブレーションを実行する。fullScale=true → MAX、false → HALF。
    public bool CalibrateDial(byte dialId, uint value, bool fullScale, out VUDialsStatus status)
    {
        var cmd = fullScale ? VUDialsCommands.SetDialCalibrateMax : VUDialsCommands.SetDialCalibrateHalf;
        byte[] data =
        [
            dialId,
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF),
        ];
        var resp = SendCommand(cmd, VUDialsDataType.KeyValuePair, data);
        return ExpectOk(resp, out status);
    }

    // ダイヤル動作のイージング・ステップを設定する。
    public bool SetDialEasingStep(byte dialId, uint step, out VUDialsStatus status) =>
        ExpectOk(SendUint32(VUDialsCommands.SetDialEasingStep, dialId, step), out status);

    // ダイヤル動作のイージング・周期 (ms) を設定する。
    public bool SetDialEasingPeriod(byte dialId, uint periodMs, out VUDialsStatus status) =>
        ExpectOk(SendUint32(VUDialsCommands.SetDialEasingPeriod, dialId, periodMs), out status);

    // バックライト動作のイージング・ステップを設定する。
    public bool SetBacklightEasingStep(byte dialId, uint step, out VUDialsStatus status) =>
        ExpectOk(SendUint32(VUDialsCommands.SetBacklightEasingStep, dialId, step), out status);

    // バックライト動作のイージング・周期 (ms) を設定する。
    public bool SetBacklightEasingPeriod(byte dialId, uint periodMs, out VUDialsStatus status) =>
        ExpectOk(SendUint32(VUDialsCommands.SetBacklightEasingPeriod, dialId, periodMs), out status);

    private VUDialsResponse? SendUint32(byte cmd, byte dialId, uint value)
    {
        byte[] data =
        [
            dialId,
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF),
        ];
        return SendCommand(cmd, VUDialsDataType.SingleValue, data);
    }

    // ====================================================================
    //  高レベル: 取得系
    // ====================================================================

    // バス上のオンライン ダイヤルマップを取得する（生 payload の byte 配列）。
    public byte[]? GetDevicesMap()
    {
        var r = SendCommand(VUDialsCommands.GetDevicesMap, VUDialsDataType.None);
        return r?.PayloadBytes;
    }

    // 指定ダイヤルの UID を取得する（生 HEX 文字列）。
    public string? GetDeviceUid(byte dialId)
    {
        var r = SendCommand(VUDialsCommands.GetDeviceUid, VUDialsDataType.SingleValue, dialId);
        return r?.HexPayload;
    }

    // 指定ダイヤルのファームウェアバージョンを取得する（HEX 文字列）。
    public string? GetFirmwareVersion(byte dialId)
    {
        var r = SendCommand(VUDialsCommands.GetFwInfo, VUDialsDataType.SingleValue, dialId);
        return r?.HexPayload;
    }

    // 指定ダイヤルのハードウェアバージョンを取得する（HEX 文字列）。
    public string? GetHardwareVersion(byte dialId)
    {
        var r = SendCommand(VUDialsCommands.GetHwInfo, VUDialsDataType.SingleValue, dialId);
        return r?.HexPayload;
    }

    // 指定ダイヤルのプロトコルバージョンを取得する（HEX 文字列）。
    public string? GetProtocolVersion(byte dialId)
    {
        var r = SendCommand(VUDialsCommands.GetProtocolInfo, VUDialsDataType.SingleValue, dialId);
        return r?.HexPayload;
    }

    // 指定ダイヤルのビルドハッシュを取得する（HEX 文字列）。
    public string? GetBuildInfo(byte dialId)
    {
        var r = SendCommand(VUDialsCommands.GetBuildInfo, VUDialsDataType.SingleValue, dialId);
        return r?.HexPayload;
    }

    // イージング設定を取得する（uint32 × 4 個の BE 配列）。
    public EasingConfig? GetEasingConfig(byte dialId)
    {
        var r = SendCommand(VUDialsCommands.GetEasingConfig, VUDialsDataType.SingleValue, dialId);
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
    public List<DialInfo> ListDials()
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
    public List<byte> GetOnlineDialIds()
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
            VUDialsCommands.DisplayClear,
            VUDialsDataType.KeyValuePair,
            dialId,
            (byte)(whiteBackground ? 1 : 0));
        return ExpectOk(resp, out status);
    }
}
