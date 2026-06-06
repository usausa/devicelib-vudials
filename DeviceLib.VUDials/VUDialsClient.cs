namespace DeviceLib.VUDials;

using System.Globalization;
using System.IO.Ports;
using System.Text;

//--------------------------------------------------------------------------------
// Status
//--------------------------------------------------------------------------------

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

//--------------------------------------------------------------------------------
// Data
//--------------------------------------------------------------------------------

public sealed record EasingConfig(
    uint DialStep,
    uint DialPeriod,
    uint BacklightStep,
    uint BacklightPeriod);

public sealed record DialInfo(
    byte Index,
    string UidHex);

//--------------------------------------------------------------------------------
// Client
//--------------------------------------------------------------------------------

// Request: '>' CMD(2hex) TYPE(2hex) LEN(4hex) DATA(2hex × LEN) "\r\n"
// Response: '<'
public sealed class VUDialsClient : IDisposable
{
    //--------------------------------------------------------------------------------
    //  内部型定義
    //--------------------------------------------------------------------------------

    // VU1 CommandId。
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
#pragma warning restore IDE0051
    // ReSharper restore IdentifierTypo

    private enum DataType : byte
    {
        None = 0x01,
        SingleValue = 0x02,
        MultipleValue = 0x03,
        KeyValuePair = 0x04,
        StatusCode = 0x05
    }

    //--------------------------------------------------------------------------------
    // Fields
    //--------------------------------------------------------------------------------

    private readonly SerialPort port;
    private bool disposed;

    //--------------------------------------------------------------------------------
    // Property
    //--------------------------------------------------------------------------------

    public int ResponseTimeout { get; set; } = 10_000;

    public string PortName => port.PortName;

    public bool IsOpen => port.IsOpen;

    //--------------------------------------------------------------------------------
    // Constructor
    //--------------------------------------------------------------------------------

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

    //--------------------------------------------------------------------------------
    // Open / Close
    //--------------------------------------------------------------------------------

    public void Open()
    {
        port.Open();
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
    }

    public void Close()
    {
        if (port.IsOpen)
        {
            port.Close();
        }
    }

    //--------------------------------------------------------------------------------
    // Low-level: Packet I/O
    //--------------------------------------------------------------------------------

    private (DataType Type, string Payload)? SendCommand(byte cmd, DataType dataType, params byte[] data)
    {
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

        port.DiscardInBuffer();
        port.Write(line + "\r\n");

        var deadline = DateTime.UtcNow.AddMilliseconds(ResponseTimeout);
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

            if (rx.StartsWith('<'))
            {
                return ParseResponse(rx);
            }
        }
        return null;
    }

    private static (DataType Type, string Payload)? ParseResponse(string line)
    {
        if (string.IsNullOrEmpty(line) || line.Length < 9 || line[0] != '<')
        {
            return null;
        }
        var dataType = byte.Parse(line.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var payload = line.Length > 9 ? line[9..] : string.Empty;
        return ((DataType)dataType, payload);
    }

    private static byte[] HexToBytes(string hex)
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

    private static bool ExpectOk((DataType Type, string Payload)? resp, out VUDialsStatus status)
    {
        if (resp is null)
        {
            status = VUDialsStatus.Timeout;
            return false;
        }
        if (resp.Value.Type != DataType.StatusCode || string.IsNullOrEmpty(resp.Value.Payload))
        {
            status = VUDialsStatus.Ok;
            return true;
        }
        var v = int.Parse(resp.Value.Payload, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        status = (VUDialsStatus)v;
        return status == VUDialsStatus.Ok;
    }

    private (DataType Type, string Payload)? SendUint32(byte cmd, byte dialId, uint value)
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

    //--------------------------------------------------------------------------------
    // High-level: Dial Control
    //--------------------------------------------------------------------------------

    public bool SetDialPercent(byte dialId, byte percent, out VUDialsStatus status)
    {
        if (percent > 100)
        {
            percent = 100;
        }
        var r = SendCommand(Commands.SetDialPercSingle, DataType.KeyValuePair, dialId, percent);
        return ExpectOk(r, out status);
    }

    public bool SetDialRaw(byte dialId, ushort raw, out VUDialsStatus status)
    {
        byte[] data = [dialId, (byte)(raw >> 8), (byte)(raw & 0xFF)];
        var r = SendCommand(Commands.SetDialRawSingle, DataType.KeyValuePair, data);
        return ExpectOk(r, out status);
    }

    public bool SetMultipleDialsPercent(IReadOnlyList<(byte DialId, byte Percent)> values, out VUDialsStatus status)
    {
        var data = new byte[values.Count * 2];
        for (var i = 0; i < values.Count; i++)
        {
            data[i * 2] = values[i].DialId;
            data[(i * 2) + 1] = Math.Min(values[i].Percent, (byte)100);
        }

        var r = SendCommand(Commands.SetDialPercMultiple, DataType.KeyValuePair, data);
        return ExpectOk(r, out status);
    }

    public bool SetBacklight(byte dialId, byte r, byte g, byte b, byte w, out VUDialsStatus status)
    {
        var resp = SendCommand(Commands.SetRgbBacklight, DataType.MultipleValue, dialId, r, g, b, w);
        return ExpectOk(resp, out status);
    }

    public bool SetDialPower(bool on, out VUDialsStatus status)
    {
        var resp = SendCommand(Commands.DialPower, DataType.SingleValue, (byte)(on ? 1 : 0));
        return ExpectOk(resp, out status);
    }

    public bool RescanBus(out VUDialsStatus status)
    {
        var resp = SendCommand(Commands.RescanBus, DataType.None);
        return ExpectOk(resp, out status);
    }

    public bool ProvisionDevice(out VUDialsStatus status)
    {
        var resp = SendCommand(Commands.ProvisionDevice, DataType.None);
        return ExpectOk(resp, out status);
    }

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

    public bool ResetAllDevices(out VUDialsStatus status)
    {
        var resp = SendCommand(Commands.ResetAllDevices, DataType.None);
        return ExpectOk(resp, out status);
    }

    public bool ResetConfig(out VUDialsStatus status)
    {
        var resp = SendCommand(Commands.ResetCfg, DataType.None);
        return ExpectOk(resp, out status);
    }

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

    public bool SetDialEasingStep(byte dialId, uint step, out VUDialsStatus status) =>
        ExpectOk(SendUint32(Commands.SetDialEasingStep, dialId, step), out status);

    public bool SetDialEasingPeriod(byte dialId, uint periodMs, out VUDialsStatus status) =>
        ExpectOk(SendUint32(Commands.SetDialEasingPeriod, dialId, periodMs), out status);

    public bool SetBacklightEasingStep(byte dialId, uint step, out VUDialsStatus status) =>
        ExpectOk(SendUint32(Commands.SetBacklightEasingStep, dialId, step), out status);

    public bool SetBacklightEasingPeriod(byte dialId, uint periodMs, out VUDialsStatus status) =>
        ExpectOk(SendUint32(Commands.SetBacklightEasingPeriod, dialId, periodMs), out status);

    //--------------------------------------------------------------------------------
    // High-level: Device Info
    //--------------------------------------------------------------------------------

    public byte[]? GetDevicesMap()
    {
        var r = SendCommand(Commands.GetDevicesMap, DataType.None);
        return r.HasValue ? HexToBytes(r.Value.Payload) : null;
    }

    public string? GetDeviceUid(byte dialId)
    {
        var r = SendCommand(Commands.GetDeviceUid, DataType.SingleValue, dialId);
        return r?.Payload;
    }

    public string? GetFirmwareVersion(byte dialId)
    {
        var r = SendCommand(Commands.GetFwInfo, DataType.SingleValue, dialId);
        return r?.Payload;
    }

    public string? GetHardwareVersion(byte dialId)
    {
        var r = SendCommand(Commands.GetHwInfo, DataType.SingleValue, dialId);
        return r?.Payload;
    }

    public string? GetProtocolVersion(byte dialId)
    {
        var r = SendCommand(Commands.GetProtocolInfo, DataType.SingleValue, dialId);
        return r?.Payload;
    }

    public string? GetBuildInfo(byte dialId)
    {
        var r = SendCommand(Commands.GetBuildInfo, DataType.SingleValue, dialId);
        return r?.Payload;
    }

    public EasingConfig? GetEasingConfig(byte dialId)
    {
        var r = SendCommand(Commands.GetEasingConfig, DataType.SingleValue, dialId);
        if (!r.HasValue)
        {
            return null;
        }
        var b = HexToBytes(r.Value.Payload);
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

    // TODO Use BinaryPrimitives once available.
    private static uint ReadUInt32Be(byte[] b, int offset) =>
        ((uint)b[offset] << 24) | ((uint)b[offset + 1] << 16) |
        ((uint)b[offset + 2] << 8) | b[offset + 3];

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
