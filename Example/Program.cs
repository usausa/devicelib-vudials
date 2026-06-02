using System.Globalization;
using System.IO.Ports;
using System.Text;

using DeviceLib.VUDials;

// VUDials 動作確認サンプル。
// Usage: Example <COMポート> [dialId] [percent]
// 注意: dial-id は 0 始まり。
Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 1)
{
    Console.WriteLine("Usage: Example <PORT> [dialId=0] [percent=50]");
    Console.WriteLine("例: Example COM5 0 75\n");
    Console.WriteLine("利用可能な COM ポート:");
    foreach (var p in SerialPort.GetPortNames())
    {
        Console.WriteLine("  " + p);
    }
    return 1;
}

var portName = args[0];
var dialId = args.Length > 1 ? byte.Parse(args[1], CultureInfo.InvariantCulture) : (byte)0;
var percent = args.Length > 2 ? byte.Parse(args[2], CultureInfo.InvariantCulture) : (byte)50;

using var client = new VUDialsClient(portName);
client.OnTransmit += s => Console.WriteLine($"TX: {s}");
client.OnReceive += s => Console.WriteLine($"RX: {s}");

client.Open();

Console.WriteLine("\n=== 1) Provision + Rescan (全 dial を検出) ===");
var n = client.ProvisionAndRescan();
Console.WriteLine($"   → {n} 台検出");

Console.WriteLine("\n=== 2) ダイヤル一覧 ===");
foreach (var d in client.ListDials())
{
    Console.WriteLine($"  Dial #{d.Index}  UID={d.UidHex}");
}

Console.WriteLine($"\n=== 3) ダイヤル {dialId} を {percent}% に設定 ===");
if (!client.SetDialPercent(dialId, percent, out var st))
{
    Console.WriteLine($"FAIL: status={st}");
    return 2;
}
Console.WriteLine("   → OK");

Console.WriteLine("\n=== 4) デバイス情報取得 ===");
Console.WriteLine($"  UID      : {client.GetDeviceUid(dialId) ?? "(none)"}");
Console.WriteLine($"  Firmware : {client.GetFirmwareVersion(dialId) ?? "(none)"}");
Console.WriteLine($"  Hardware : {client.GetHardwareVersion(dialId) ?? "(none)"}");
Console.WriteLine($"  Protocol : {client.GetProtocolVersion(dialId) ?? "(none)"}");

Console.WriteLine("\n=== 5) イージング設定取得 ===");
var easing = client.GetEasingConfig(dialId);
if (easing is not null)
{
    Console.WriteLine($"  DialStep        : {easing.DialStep}");
    Console.WriteLine($"  DialPeriod      : {easing.DialPeriod}");
    Console.WriteLine($"  BacklightStep   : {easing.BacklightStep}");
    Console.WriteLine($"  BacklightPeriod : {easing.BacklightPeriod}");
}

Console.WriteLine("\n=== 6) スイープ (0→100→0) ===");
for (var v = 0; v <= 100; v += 10)
{
    client.SetDialPercent(dialId, (byte)v, out _);
    Thread.Sleep(150);
}
for (var v = 100; v >= 0; v -= 10)
{
    client.SetDialPercent(dialId, (byte)v, out _);
    Thread.Sleep(150);
}

Console.WriteLine("\n=== 7) バックライト: レッド → オフ ===");
client.SetBacklight(dialId, 100, 0, 0, 0, out _);
Thread.Sleep(1000);
client.SetBacklight(dialId, 0, 0, 0, 0, out _);

Console.WriteLine("\n[Done]");
return 0;
