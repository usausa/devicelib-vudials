using System.Globalization;

using DeviceLib.VUDials;

if (args.Length < 1)
{
    Console.WriteLine("Usage: Example <PORT> [dialId=0] [percent=50]");
    return 1;
}

var portName = args[0];
var dialId = args.Length > 1 ? byte.Parse(args[1], CultureInfo.InvariantCulture) : (byte)0;
var percent = args.Length > 2 ? byte.Parse(args[2], CultureInfo.InvariantCulture) : (byte)50;

using var client = new VUDialsClient(portName);
client.Open();

Console.WriteLine("==== 1) Provision + Rescan (detect all dials) ====");
var n = client.ProvisionAndRescan();
Console.WriteLine($"   -> {n} dial(s) detected");

Console.WriteLine("==== 2) Dial list ====");
foreach (var d in client.ListDials())
{
    Console.WriteLine($"  Dial #{d.Index}  UID={d.UidHex}");
}

Console.WriteLine($"==== 3) Set dial {dialId} to {percent}% ====");
var st = client.SetDialPercent(dialId, percent);
if (st != VUDialsStatus.Ok)
{
    Console.WriteLine($"FAIL: status={st}");
    return 2;
}
Console.WriteLine("   -> OK");

Console.WriteLine("==== 4) Get device info ====");
Console.WriteLine($"  UID      : {client.GetDeviceUid(dialId) ?? "(none)"}");
Console.WriteLine($"  Firmware : {client.GetFirmwareVersion(dialId) ?? "(none)"}");
Console.WriteLine($"  Hardware : {client.GetHardwareVersion(dialId) ?? "(none)"}");
Console.WriteLine($"  Protocol : {client.GetProtocolVersion(dialId) ?? "(none)"}");

Console.WriteLine("==== 5) Get easing config ====");
var easing = client.GetEasingConfig(dialId);
if (easing is not null)
{
    Console.WriteLine($"  DialStep        : {easing.DialStep}");
    Console.WriteLine($"  DialPeriod      : {easing.DialPeriod}");
    Console.WriteLine($"  BacklightStep   : {easing.BacklightStep}");
    Console.WriteLine($"  BacklightPeriod : {easing.BacklightPeriod}");
}

Console.WriteLine("==== 6) Sweep (0->100->0) ====");
for (var v = 0; v <= 100; v += 10)
{
    client.SetDialPercent(dialId, (byte)v);
    Thread.Sleep(150);
}
for (var v = 100; v >= 0; v -= 10)
{
    client.SetDialPercent(dialId, (byte)v);
    Thread.Sleep(150);
}

Console.WriteLine("==== 7) Backlight: red -> off ====");
client.SetBacklight(dialId, 100, 0, 0, 0);
Thread.Sleep(1000);
client.SetBacklight(dialId, 0, 0, 0, 0);

Console.WriteLine("[Done]");
return 0;
