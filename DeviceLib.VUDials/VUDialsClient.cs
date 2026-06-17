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

    private bool RequestResponse(byte command, DataType requestType, ReadOnlySpan<byte> requestData, out DataType responseType, out ReadOnlySpan<byte> responseData)
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

        // Write
        port.DiscardInBuffer();
        port.Write(buffer, 0, pos);

        // Read
        var deadline = Stopwatch.GetTimestamp() + (ResponseTimeout * Stopwatch.Frequency / 1000); // ms -> Stopwatch ticks
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
                break;
            }

            if (length < 9)
            {
                continue;
            }

            if (HexHelper.ParseHexByte(line.Slice(1, 2)) != command)
            {
                break;
            }

            responseType = (DataType)HexHelper.ParseHexByte(line.Slice(3, 2));
            responseData = length > 9 ? readBuffer.AsSpan(9, length - 9) : default;
            return true;
        }

        return false;
    }

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
    // Send command
    //--------------------------------------------------------------------------------

    private VUDialsStatus SendCommand(byte cmd, DataType dataType, ReadOnlySpan<byte> data)
    {
        if (!RequestResponse(cmd, dataType, data, out var responseType, out var hexPayload))
        {
            return VUDialsStatus.Timeout;
        }

        if (responseType != DataType.StatusCode || hexPayload.IsEmpty)
        {
            return VUDialsStatus.Ok;
        }

        var rawLength = hexPayload.Length / 2;
        var rentedRaw = default(byte[]?);
        var raw = rawLength <= StackPayloadThreshold
            ? stackalloc byte[rawLength]
            : (rentedRaw = ArrayPool<byte>.Shared.Rent(rawLength)).AsSpan(0, rawLength);
        try
        {
            var count = HexHelper.ReadHex(hexPayload, raw);
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                value = (value << 8) | raw[i];
            }
            return (VUDialsStatus)value;
        }
        finally
        {
            if (rentedRaw is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedRaw);
            }
        }
    }

    private VUDialsStatus SendUInt32(byte cmd, DataType dataType, byte dialId, uint value)
    {
        Span<byte> data = stackalloc byte[5];
        data[0] = dialId;
        BinaryPrimitives.WriteUInt32BigEndian(data[1..], value);
        return SendCommand(cmd, dataType, data);
    }

    private string? SendQuery(byte cmd, byte dialId)
    {
        Span<byte> data = [dialId];
        if (!RequestResponse(cmd, DataType.SingleValue, data, out _, out var hexPayload))
        {
            return null;
        }
        if (hexPayload.IsEmpty)
        {
            return string.Empty;
        }

        var rawLength = hexPayload.Length / 2;
        var rentedRaw = default(byte[]?);
        var raw = rawLength <= StackPayloadThreshold
            ? stackalloc byte[rawLength]
            : (rentedRaw = ArrayPool<byte>.Shared.Rent(rawLength)).AsSpan(0, rawLength);
        try
        {
            var count = HexHelper.ReadHex(hexPayload, raw);
            return Encoding.ASCII.GetString(raw[..count]);
        }
        finally
        {
            if (rentedRaw is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedRaw);
            }
        }
    }

    //--------------------------------------------------------------------------------
    // List Dials
    //--------------------------------------------------------------------------------

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

    public byte[]? GetDevicesMap()
    {
        if (!RequestResponse(Commands.GetDevicesMap, DataType.None, default, out _, out var hexPayload))
        {
            return null;
        }

        var result = new byte[hexPayload.Length / 2];
        HexHelper.ReadHex(hexPayload, result);
        return result;
    }

    //--------------------------------------------------------------------------------
    // Dial Information
    //--------------------------------------------------------------------------------

    public string? GetDeviceUid(byte dialId) => SendQuery(Commands.GetDeviceUid, dialId);

    public string? GetFirmwareVersion(byte dialId) => SendQuery(Commands.GetFwInfo, dialId);

    public string? GetHardwareVersion(byte dialId) => SendQuery(Commands.GetHwInfo, dialId);

    public string? GetProtocolVersion(byte dialId) => SendQuery(Commands.GetProtocolInfo, dialId);

    public string? GetBuildInfo(byte dialId) => SendQuery(Commands.GetBuildInfo, dialId);

    //--------------------------------------------------------------------------------
    // Set Value
    //--------------------------------------------------------------------------------

    public VUDialsStatus SetDialPercent(byte dialId, byte value)
    {
        if (value > 100)
        {
            value = 100;
        }

        Span<byte> request = [dialId, value];
        return SendCommand(Commands.SetDialPercSingle, DataType.KeyValuePair, request);
    }

    public VUDialsStatus SetMultipleDialsPercent(IReadOnlyList<(byte DialId, byte Value)> values)
    {
        var count = values.Count;
        var length = count * 2;
        var rented = default(byte[]?);

        var request = length <= StackPayloadThreshold
            ? stackalloc byte[length]
            : (rented = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);
        try
        {
            for (var i = 0; i < count; i++)
            {
                request[i * 2] = values[i].DialId;
                request[(i * 2) + 1] = Math.Min(values[i].Value, (byte)100);
            }

            return SendCommand(Commands.SetDialPercMultiple, DataType.KeyValuePair, request);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    //--------------------------------------------------------------------------------
    // Raw value : Bypasses calibration and easing; range 0-2048.
    //--------------------------------------------------------------------------------

    public VUDialsStatus SetDialRaw(byte dialId, ushort value)
    {
        Span<byte> request = [dialId, (byte)(value >> 8), (byte)(value & 0xFF)];
        return SendCommand(Commands.SetDialRawSingle, DataType.KeyValuePair, request);
    }

    //--------------------------------------------------------------------------------
    // Set Backlight : red/green/blue (0-100)
    //--------------------------------------------------------------------------------

    public VUDialsStatus SetBacklight(byte dialId, byte red, byte green, byte blue, byte white)
    {
        Span<byte> request = [dialId, Math.Min(red, (byte)100), Math.Min(green, (byte)100), Math.Min(blue, (byte)100), Math.Min(white, (byte)100)];
        return SendCommand(Commands.SetRgbBacklight, DataType.MultipleValue, request);
    }

    //--------------------------------------------------------------------------------
    // Calibrate
    //--------------------------------------------------------------------------------

    public VUDialsStatus CalibrateDial(byte dialId, uint value, bool fullScale)
    {
        var command = fullScale ? Commands.SetDialCalibrateMax : Commands.SetDialCalibrateHalf;
        return SendUInt32(command, DataType.KeyValuePair, dialId, value);
    }

    //--------------------------------------------------------------------------------
    // Set Dial Easing
    //--------------------------------------------------------------------------------

    public VUDialsStatus SetDialEasingStep(byte dialId, uint step) =>
        SendUInt32(Commands.SetDialEasingStep, DataType.SingleValue, dialId, step);

    public VUDialsStatus SetDialEasingPeriod(byte dialId, uint period) =>
        SendUInt32(Commands.SetDialEasingPeriod, DataType.SingleValue, dialId, period);

    //--------------------------------------------------------------------------------
    // Set Backlight Easing
    //--------------------------------------------------------------------------------

    public VUDialsStatus SetBacklightEasingStep(byte dialId, uint step) =>
        SendUInt32(Commands.SetBacklightEasingStep, DataType.SingleValue, dialId, step);

    public VUDialsStatus SetBacklightEasingPeriod(byte dialId, uint period) =>
        SendUInt32(Commands.SetBacklightEasingPeriod, DataType.SingleValue, dialId, period);

    //--------------------------------------------------------------------------------
    // Get Easing Config
    //--------------------------------------------------------------------------------

    public EasingConfig? GetEasingConfig(byte dialId)
    {
        Span<byte> request = [dialId];
        if (!RequestResponse(Commands.GetEasingConfig, DataType.SingleValue, request, out _, out var hexPayload))
        {
            return null;
        }

        // 4 x uint32 big-endian => 16 bytes => 32 hex chars.
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

    //--------------------------------------------------------------------------------
    // Admin API
    //--------------------------------------------------------------------------------

    public VUDialsStatus ProvisionDevice() =>
        SendCommand(Commands.ProvisionDevice, DataType.None, default);

    public int ProvisionAndRescan(int maxRounds = 16)
    {
        var prev = -1;
        for (var i = 0; i < maxRounds; i++)
        {
            if (!RequestResponse(Commands.ProvisionDevice, DataType.None, default, out _, out _))
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

        RequestResponse(Commands.RescanBus, DataType.None, default, out _, out _);

        return CountOnline();

        int CountOnline()
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
    }

    public VUDialsStatus RescanBus() =>
        SendCommand(Commands.RescanBus, DataType.None, default);

    //--------------------------------------------------------------------------------
    // Client specific
    //--------------------------------------------------------------------------------

    public VUDialsStatus SetDialPower(bool on)
    {
        Span<byte> request = [(byte)(on ? 1 : 0)];
        return SendCommand(Commands.DialPower, DataType.SingleValue, request);
    }

    public VUDialsStatus ResetAllDevices() =>
        SendCommand(Commands.ResetAllDevices, DataType.None, default);

    public VUDialsStatus ResetConfig() =>
        SendCommand(Commands.ResetCfg, DataType.None, default);

    public VUDialsStatus DisplayClear(byte dialId, bool whiteBackground)
    {
        // VU-Server dial_display_clear: SingleValue type; flag 0 = white background, 1 = black.
        Span<byte> request = [dialId, (byte)(whiteBackground ? 0 : 1)];
        return SendCommand(Commands.DisplayClear, DataType.SingleValue, request);
    }
}
