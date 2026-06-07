namespace DeviceLib.VUDials;

using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
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

// https://docs.vudials.com/
public sealed class VUDialsClient : IDisposable
{
    //--------------------------------------------------------------------------------
    //  Command definitions
    //--------------------------------------------------------------------------------

    // VU1 CommandId。
    // ReSharper disable IdentifierTypo
    // ReSharper disable UnusedMember.Local
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
    // ReSharper restore UnusedMember.Local
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

    private const int DefaultWriteBufferSize = 64;
    private const int DefaultReadBufferSize = 512;
    private const int StackPayloadThreshold = 256;

    private readonly SerialPort port;

    private byte[] writeBuffer;
    private byte[] readBuffer;

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
        writeBuffer = ArrayPool<byte>.Shared.Rent(DefaultWriteBufferSize);
        readBuffer = ArrayPool<byte>.Shared.Rent(DefaultReadBufferSize);
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

        if (writeBuffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(writeBuffer);
            writeBuffer = [];
        }
        if (readBuffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
            readBuffer = [];
        }
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

    private bool TrySendCommand(byte command, DataType requestType, ReadOnlySpan<byte> requestData, out DataType responseType, out ReadOnlySpan<byte> responseData)
    {
        // Request: '>' CMD(2hex) TYPE(2hex) LEN(4hex) DATA(2hex × LEN) "\r\n"
        // Response: '<' CMD(2hex) TYPE(2hex) LEN(4hex) DATA(2hex × LEN) "\r\n"
        responseType = DataType.None;
        responseData = default;

        // '>' + CMD(2) + TYPE(2) + LEN(4) + DATA(2 × n) + CR LF
        var requestLength = 1 + 2 + 2 + 4 + (requestData.Length * 2) + 2;
        EnsureWriteCapacity(requestLength);

        var buffer = writeBuffer;
        var pos = 0;
        buffer[pos++] = (byte)'>';

        Span<byte> header = [command, (byte)requestType, (byte)(requestData.Length >> 8), (byte)requestData.Length];
        pos += HexHelper.WriteHex(header, buffer.AsSpan(pos));
        pos += HexHelper.WriteHex(requestData, buffer.AsSpan(pos));

        buffer[pos++] = (byte)'\r';
        buffer[pos++] = (byte)'\n';

        port.DiscardInBuffer();
        port.Write(buffer, 0, pos);

        // Monotonic deadline; Stopwatch is immune to wall-clock adjustments.
        var deadline = Stopwatch.GetTimestamp() + TimeoutToTicks(ResponseTimeout);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var length = ReadResponseLine(deadline);
            if (length < 0)
            {
                break;
            }
            if (length == 0)
            {
                continue;
            }

            var line = readBuffer.AsSpan(0, length);
            if (line[0] != (byte)'<')
            {
                continue;
            }
            if (length < 9)
            {
                break;
            }

            responseType = (DataType)HexHelper.ParseHexByte(line.Slice(3, 2));
            responseData = length > 9 ? readBuffer.AsSpan(9, length - 9) : default;
            return true;
        }

        return false;
    }

    // Reads one CRLF-terminated line into readBuffer (terminator stripped) and
    // returns its length, or -1 on timeout. Bytes are accumulated across per-byte
    // read timeouts until the shared deadline expires.
    private int ReadResponseLine(long deadline)
    {
        var offset = 0;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            int value;
            try
            {
                value = port.ReadByte();
            }
            catch (TimeoutException)
            {
                continue;
            }

            if (value < 0)
            {
                continue;
            }
            if (value == '\n')
            {
                if (offset > 0 && readBuffer[offset - 1] == (byte)'\r')
                {
                    offset--;
                }
                return offset;
            }

            EnsureReadCapacity(offset + 1);
            readBuffer[offset++] = (byte)value;
        }
        return -1;
    }

    // Converts a millisecond timeout into monotonic Stopwatch timestamp ticks.
    private static long TimeoutToTicks(int milliseconds) =>
        milliseconds * Stopwatch.Frequency / 1000;

    // Sends a command and interprets the response as a status code.
    // Value responses (no status code) and successful transfers map to Ok.
    private VUDialsStatus SendCommand(byte cmd, DataType dataType, ReadOnlySpan<byte> data)
    {
        if (!TrySendCommand(cmd, dataType, data, out var responseType, out var hexPayload))
        {
            return VUDialsStatus.Timeout;
        }

        if (responseType != DataType.StatusCode || hexPayload.IsEmpty)
        {
            return VUDialsStatus.Ok;
        }

        return ParseStatus(hexPayload);
    }

    private VUDialsStatus SendUint32(byte cmd, DataType dataType, byte dialId, uint value)
    {
        Span<byte> data = stackalloc byte[5];
        data[0] = dialId;
        BinaryPrimitives.WriteUInt32BigEndian(data[1..], value);
        return SendCommand(cmd, dataType, data);
    }

    private string? GetHexInfo(byte cmd, byte dialId)
    {
        Span<byte> data = [dialId];
        if (!TrySendCommand(cmd, DataType.SingleValue, data, out _, out var hexPayload))
        {
            return null;
        }
        return hexPayload.IsEmpty ? string.Empty : Encoding.ASCII.GetString(hexPayload);
    }

    private static VUDialsStatus ParseStatus(ReadOnlySpan<byte> hexPayload)
    {
        Span<byte> raw = stackalloc byte[hexPayload.Length / 2];
        var count = HexHelper.ReadHex(hexPayload, raw);
        var value = 0;
        for (var i = 0; i < count; i++)
        {
            value = (value << 8) | raw[i];
        }
        return (VUDialsStatus)value;
    }

    //--------------------------------------------------------------------------------
    // Buffer management
    //--------------------------------------------------------------------------------

    private void EnsureWriteCapacity(int required)
    {
        if (writeBuffer.Length >= required)
        {
            return;
        }

        if (writeBuffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(writeBuffer);
        }

        writeBuffer = ArrayPool<byte>.Shared.Rent(required);
    }

    private void EnsureReadCapacity(int required)
    {
        if (readBuffer.Length >= required)
        {
            return;
        }

        var bigger = ArrayPool<byte>.Shared.Rent(required);
        readBuffer.AsSpan().CopyTo(bigger);
        ArrayPool<byte>.Shared.Return(readBuffer);
        readBuffer = bigger;
    }

    //--------------------------------------------------------------------------------
    // High-level: Dial Control
    //--------------------------------------------------------------------------------

    public VUDialsStatus SetDialPercent(byte dialId, byte percent)
    {
        if (percent > 100)
        {
            percent = 100;
        }
        Span<byte> data = [dialId, percent];
        return SendCommand(Commands.SetDialPercSingle, DataType.KeyValuePair, data);
    }

    public VUDialsStatus SetDialRaw(byte dialId, ushort raw)
    {
        Span<byte> data = [dialId, (byte)(raw >> 8), (byte)(raw & 0xFF)];
        return SendCommand(Commands.SetDialRawSingle, DataType.KeyValuePair, data);
    }

    public VUDialsStatus SetMultipleDialsPercent(IReadOnlyList<(byte DialId, byte Percent)> values)
    {
        var count = values.Count;
        var length = count * 2;
        byte[]? rented = null;
        var data = length <= StackPayloadThreshold
            ? stackalloc byte[length]
            : (rented = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);
        try
        {
            for (var i = 0; i < count; i++)
            {
                data[i * 2] = values[i].DialId;
                data[(i * 2) + 1] = Math.Min(values[i].Percent, (byte)100);
            }

            return SendCommand(Commands.SetDialPercMultiple, DataType.KeyValuePair, data);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public VUDialsStatus SetBacklight(byte dialId, byte r, byte g, byte b, byte w)
    {
        Span<byte> data = [dialId, r, g, b, w];
        return SendCommand(Commands.SetRgbBacklight, DataType.MultipleValue, data);
    }

    public VUDialsStatus SetDialPower(bool on)
    {
        Span<byte> data = [(byte)(on ? 1 : 0)];
        return SendCommand(Commands.DialPower, DataType.SingleValue, data);
    }

    public VUDialsStatus RescanBus() =>
        SendCommand(Commands.RescanBus, DataType.None, default);

    public VUDialsStatus ProvisionDevice() =>
        SendCommand(Commands.ProvisionDevice, DataType.None, default);

    public int ProvisionAndRescan(int maxRounds = 16)
    {
        var prev = -1;
        for (var i = 0; i < maxRounds; i++)
        {
            if (!TrySendCommand(Commands.ProvisionDevice, DataType.None, default, out _, out _))
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
        TrySendCommand(Commands.RescanBus, DataType.None, default, out _, out _);
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

    public VUDialsStatus ResetAllDevices() =>
        SendCommand(Commands.ResetAllDevices, DataType.None, default);

    public VUDialsStatus ResetConfig() =>
        SendCommand(Commands.ResetCfg, DataType.None, default);

    public VUDialsStatus CalibrateDial(byte dialId, uint value, bool fullScale)
    {
        var cmd = fullScale ? Commands.SetDialCalibrateMax : Commands.SetDialCalibrateHalf;
        return SendUint32(cmd, DataType.KeyValuePair, dialId, value);
    }

    public VUDialsStatus SetDialEasingStep(byte dialId, uint step) =>
        SendUint32(Commands.SetDialEasingStep, DataType.SingleValue, dialId, step);

    public VUDialsStatus SetDialEasingPeriod(byte dialId, uint periodMs) =>
        SendUint32(Commands.SetDialEasingPeriod, DataType.SingleValue, dialId, periodMs);

    public VUDialsStatus SetBacklightEasingStep(byte dialId, uint step) =>
        SendUint32(Commands.SetBacklightEasingStep, DataType.SingleValue, dialId, step);

    public VUDialsStatus SetBacklightEasingPeriod(byte dialId, uint periodMs) =>
        SendUint32(Commands.SetBacklightEasingPeriod, DataType.SingleValue, dialId, periodMs);

    //--------------------------------------------------------------------------------
    // High-level: Device Info
    //--------------------------------------------------------------------------------

    public byte[]? GetDevicesMap()
    {
        if (!TrySendCommand(Commands.GetDevicesMap, DataType.None, default, out _, out var hexPayload))
        {
            return null;
        }

        var result = new byte[hexPayload.Length / 2];
        HexHelper.ReadHex(hexPayload, result);
        return result;
    }

    public string? GetDeviceUid(byte dialId) => GetHexInfo(Commands.GetDeviceUid, dialId);

    public string? GetFirmwareVersion(byte dialId) => GetHexInfo(Commands.GetFwInfo, dialId);

    public string? GetHardwareVersion(byte dialId) => GetHexInfo(Commands.GetHwInfo, dialId);

    public string? GetProtocolVersion(byte dialId) => GetHexInfo(Commands.GetProtocolInfo, dialId);

    public string? GetBuildInfo(byte dialId) => GetHexInfo(Commands.GetBuildInfo, dialId);

    public EasingConfig? GetEasingConfig(byte dialId)
    {
        Span<byte> request = [dialId];
        if (!TrySendCommand(Commands.GetEasingConfig, DataType.SingleValue, request, out _, out var hexPayload))
        {
            return null;
        }

        // 4 × uint32 big-endian => 16 bytes => 32 hex chars.
        if (hexPayload.Length < 32)
        {
            return null;
        }

        Span<byte> b = stackalloc byte[16];
        if (HexHelper.ReadHex(hexPayload[..32], b) < 16)
        {
            return null;
        }

        return new EasingConfig(
            BinaryPrimitives.ReadUInt32BigEndian(b),
            BinaryPrimitives.ReadUInt32BigEndian(b[4..]),
            BinaryPrimitives.ReadUInt32BigEndian(b[8..]),
            BinaryPrimitives.ReadUInt32BigEndian(b[12..]));
    }

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

    public VUDialsStatus DisplayClear(byte dialId, bool whiteBackground)
    {
        Span<byte> data = [dialId, (byte)(whiteBackground ? 1 : 0)];
        return SendCommand(Commands.DisplayClear, DataType.KeyValuePair, data);
    }
}
