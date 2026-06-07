// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBeProtected.Global
// ReSharper disable MemberCanBePrivate.Global
namespace DeviceTool.VUDials;

using DeviceLib.VUDials;

using Smart.CommandLine.Hosting;

public static class CommandBuilderExtensions
{
    public static void AddCommands(this ICommandBuilder commands)
    {
        commands.AddCommand<DialsCommand>();
        commands.AddCommand<InfoCommand>();
        commands.AddCommand<PercentCommand>();
        commands.AddCommand<MultiCommand>();
        commands.AddCommand<BacklightCommand>();
        commands.AddCommand<CalibrateCommand>();
        commands.AddCommand<EasingCommand>();
        commands.AddCommand<ProvisionCommand>();
        commands.AddCommand<RescanCommand>();
        commands.AddCommand<PowerCommand>();
        commands.AddCommand<ResetCommand>();
        commands.AddCommand<ClearCommand>();
        commands.AddCommand<SweepCommand>();
    }
}

//--------------------------------------------------------------------------------
// Helper
//--------------------------------------------------------------------------------
internal static class PortHelper
{
    public static VUDialsClient Open(string port)
    {
        var client = new VUDialsClient(port);
        client.Open();
        return client;
    }
}

//--------------------------------------------------------------------------------
// Dials
//--------------------------------------------------------------------------------
[Command("dials", "List connected dials")]
public sealed class DialsCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port);

        var dials = client.ListDials();
        if (dials.Count == 0)
        {
            Console.WriteLine("No dials found.");
        }
        else
        {
            Console.WriteLine($"{"Index",-5} {"UID",-24}");
            Console.WriteLine(new string('-', 30));
            foreach (var d in dials)
            {
                Console.WriteLine($"{d.Index,-5} {d.UidHex,-24}");
            }
        }

        return ValueTask.CompletedTask;
    }
}

//--------------------------------------------------------------------------------
// Information
//--------------------------------------------------------------------------------
[Command("info", "Get device information for a dial")]
public sealed class InfoCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID", Required = true)]
    public byte DialId { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port);

        Console.WriteLine($"UID      : {client.GetDeviceUid(DialId) ?? "(none)"}");
        Console.WriteLine($"Build    : {client.GetBuildInfo(DialId) ?? "(none)"}");
        Console.WriteLine($"Firmware : {client.GetFirmwareVersion(DialId) ?? "(none)"}");
        Console.WriteLine($"Hardware : {client.GetHardwareVersion(DialId) ?? "(none)"}");
        Console.WriteLine($"Protocol : {client.GetProtocolVersion(DialId) ?? "(none)"}");

        return ValueTask.CompletedTask;
    }
}

//--------------------------------------------------------------------------------
// Set value
//--------------------------------------------------------------------------------
[Command("percent", "Set dial position by percentage (0-100)")]
public sealed class PercentCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID", Required = true)]
    public byte DialId { get; set; }

    [Option<byte>("--value", "-v", Description = "Percentage (0-100)", Required = true)]
    public byte Value { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port);
        var status = client.SetDialPercent(DialId, Value);
        Console.WriteLine(status == VUDialsStatus.Ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

//--------------------------------------------------------------------------------
// Set multiple values
//--------------------------------------------------------------------------------
[Command("multi", "Set multiple dials at once (e.g. 0=50,1=75,2=20)")]
public sealed class MultiCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<string>("--values", "-v", Description = "Dial=percent pairs (e.g. 0=50,1=75)", Required = true)]
    public string Values { get; set; } = default!;

    public async ValueTask ExecuteAsync(CommandContext context)
    {
        var pairs = new List<(byte, byte)>();
        foreach (var part in Values.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=');
            if ((kv.Length == 2) && Byte.TryParse(kv[0].Trim(), out var id) && Byte.TryParse(kv[1].Trim(), out var value))
            {
                pairs.Add((id, value));
            }
        }

        if (pairs.Count == 0)
        {
            await Console.Error.WriteLineAsync("Invalid values format. Expected: 0=50,1=75");
            return;
        }

        using var client = PortHelper.Open(Port);
        var status = client.SetMultipleDialsPercent(pairs);
        Console.WriteLine(status == VUDialsStatus.Ok ? "OK" : $"Failed: {status}");
    }
}

//--------------------------------------------------------------------------------
// Backlight
//--------------------------------------------------------------------------------
[Command("backlight", "Set RGBW backlight (values 0-100)")]
public sealed class BacklightCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID", Required = true)]
    public byte DialId { get; set; }

    [Option<byte>("--red", "-r", Description = "Red (0-100)")]
    public byte Red { get; set; }

    [Option<byte>("--green", "-g", Description = "Green (0-100)")]
    public byte Green { get; set; }

    [Option<byte>("--blue", "-b", Description = "Blue (0-100)")]
    public byte Blue { get; set; }

    [Option<byte>("--white", "-w", Description = "White (0-100)")]
    public byte White { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port);

        var status = client.SetBacklight(DialId, Red, Green, Blue, White);
        Console.WriteLine(status == VUDialsStatus.Ok ? "OK" : $"Failed: {status}");

        return ValueTask.CompletedTask;
    }
}

//--------------------------------------------------------------------------------
// Calibrate
//--------------------------------------------------------------------------------
[Command("calibrate", "Calibrate dial (full-scale or half-scale)")]
public sealed class CalibrateCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID", Required = true)]
    public byte DialId { get; set; }

    [Option<uint>("--value", "-v", Description = "Calibration value", Required = true)]
    public uint Value { get; set; }

    [Option<bool>("--full", Description = "Full-scale calibration (default: half-scale)")]
    public bool Full { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port);
        var status = client.CalibrateDial(DialId, Value, Full);
        Console.WriteLine(status == VUDialsStatus.Ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

//--------------------------------------------------------------------------------
// Easing
//--------------------------------------------------------------------------------
[Command("easing", "Show easing config, or set only the options you pass")]
public sealed class EasingCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID", Required = true)]
    public byte DialId { get; set; }

    [Option<uint?>("--dial-step", "-ds", Description = "Dial easing step (set if provided)")]
    public uint? DialStep { get; set; }

    [Option<uint?>("--dial-period", "-dp", Description = "Dial easing period in ms (set if provided)")]
    public uint? DialPeriod { get; set; }

    [Option<uint?>("--backlight-step", "-bs", Description = "Backlight easing step (set if provided)")]
    public uint? BacklightStep { get; set; }

    [Option<uint?>("--backlight-period", "-bp", Description = "Backlight easing period in ms (set if provided)")]
    public uint? BacklightPeriod { get; set; }

    public async ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port);

        if (DialStep is null && DialPeriod is null && BacklightStep is null && BacklightPeriod is null)
        {
            var config = client.GetEasingConfig(DialId);
            if (config is null)
            {
                await Console.Error.WriteLineAsync("Failed to get easing config.");
                return;
            }

            Console.WriteLine($"DialStep        : {config.DialStep}");
            Console.WriteLine($"DialPeriod      : {config.DialPeriod}");
            Console.WriteLine($"BacklightStep   : {config.BacklightStep}");
            Console.WriteLine($"BacklightPeriod : {config.BacklightPeriod}");
            return;
        }

        if (DialStep is not null)
        {
            Report("dial-step", client.SetDialEasingStep(DialId, DialStep.Value));
        }
        if (DialPeriod is not null)
        {
            Report("dial-period", client.SetDialEasingPeriod(DialId, DialPeriod.Value));
        }
        if (BacklightStep is not null)
        {
            Report("backlight-step", client.SetBacklightEasingStep(DialId, BacklightStep.Value));
        }
        if (BacklightPeriod is not null)
        {
            Report("backlight-period", client.SetBacklightEasingPeriod(DialId, BacklightPeriod.Value));
        }

        static void Report(string name, VUDialsStatus status) =>
            Console.WriteLine(status == VUDialsStatus.Ok ? $"{name}: OK" : $"{name}: Failed: {status}");
    }
}

//--------------------------------------------------------------------------------
// Provision
//--------------------------------------------------------------------------------
[Command("provision", "Provision and rescan all dials")]
public sealed class ProvisionCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port (e.g. COM5)", Required = true)]
    public string Port { get; set; } = default!;

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port);

        var n = client.ProvisionAndRescan();
        Console.WriteLine(n >= 0 ? $"Detected {n} dial(s)." : "Failed.");

        return ValueTask.CompletedTask;
    }
}

//--------------------------------------------------------------------------------
// Rescan
//--------------------------------------------------------------------------------
[Command("rescan", "Rescan I2C bus")]
public sealed class RescanCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port);

        var status = client.RescanBus();
        Console.WriteLine(status == VUDialsStatus.Ok ? "OK" : $"Failed: {status}");

        return ValueTask.CompletedTask;
    }
}

//--------------------------------------------------------------------------------
// Power
//--------------------------------------------------------------------------------
[Command("power", "Turn dial power on or off")]
public sealed class PowerCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<bool>("--on", Description = "Power on (default: off)")]
    public bool On { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port);

        var status = client.SetDialPower(On);
        Console.WriteLine(status == VUDialsStatus.Ok ? "OK" : $"Failed: {status}");

        return ValueTask.CompletedTask;
    }
}

//--------------------------------------------------------------------------------
// Reset
//--------------------------------------------------------------------------------
[Command("reset", "Reset devices (--devices) and/or configuration (--config)")]
public sealed class ResetCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<bool>("--devices", Description = "Reset all devices")]
    public bool Devices { get; set; }

    [Option<bool>("--config", Description = "Reset configuration to defaults")]
    public bool Config { get; set; }

    public async ValueTask ExecuteAsync(CommandContext context)
    {
        if (!Devices && !Config)
        {
            await Console.Error.WriteLineAsync("Specify --devices and/or --config.");
            return;
        }

        using var client = PortHelper.Open(Port);

        if (Devices)
        {
            Console.WriteLine($"devices: {Describe(client.ResetAllDevices())}");
        }
        if (Config)
        {
            Console.WriteLine($"config: {Describe(client.ResetConfig())}");
        }

        static string Describe(VUDialsStatus status) =>
            status == VUDialsStatus.Ok ? "OK" : $"Failed: {status}";
    }
}

//--------------------------------------------------------------------------------
// Clear
//--------------------------------------------------------------------------------
[Command("clear", "Clear the display of a dial")]
public sealed class ClearCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID", Required = true)]
    public byte DialId { get; set; }

    [Option<bool>("--white", "-w", Description = "Clear with white background")]
    public bool White { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port);

        var status = client.DisplayClear(DialId, White);
        Console.WriteLine(status == VUDialsStatus.Ok ? "OK" : $"Failed: {status}");

        return ValueTask.CompletedTask;
    }
}

//--------------------------------------------------------------------------------
// Sweep
//--------------------------------------------------------------------------------
[Command("sweep", "Run a 0->100->0 sweep test pattern")]
public sealed class SweepCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID", Required = true)]
    public byte DialId { get; set; }

    [Option<int>("--loops", "-l", Description = "Number of loops (default: 1)", DefaultValue = 1)]
    public int Loops { get; set; }

    [Option<int>("--delay", Description = "Delay between steps in ms (default: 80)", DefaultValue = 80)]
    public int Delay { get; set; }

    public async ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port);

        for (var loop = 0; loop < Loops; loop++)
        {
            for (var v = 0; v <= 100; v += 5)
            {
                client.SetDialPercent(DialId, (byte)v);
                Console.Write($"\r{v,3}%");
                await Task.Delay(Delay);
            }
            for (var v = 100; v >= 0; v -= 5)
            {
                client.SetDialPercent(DialId, (byte)v);
                Console.Write($"\r{v,3}%");
                await Task.Delay(Delay);
            }
        }

        Console.WriteLine("\nDone.");
    }
}
