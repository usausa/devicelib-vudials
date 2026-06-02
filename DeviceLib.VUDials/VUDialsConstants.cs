namespace DeviceLib.VUDials;

/// <summary>
/// VU1 シリアルプロトコルのコマンドID。
/// </summary>
public static class VUDialsCommands
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

/// <summary>
/// パケットの DataType フィールドの値。
/// </summary>
public enum VUDialsDataType : byte
{
    None = 0x01,
    SingleValue = 0x02,
    MultipleValue = 0x03,
    KeyValuePair = 0x04,
    StatusCode = 0x05,
}

/// <summary>
/// ステータスコードレスポンス。
/// </summary>
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
    SpiError = 0x16,
}
