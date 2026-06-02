// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBeProtected.Global
// ReSharper disable MemberCanBePrivate.Global
namespace DeviceTool.VUDials;

using System.IO.Ports;

using DeviceLib.VUDials;

using Smart.CommandLine.Hosting;

public static class CommandBuilderExtensions
{
    public static void AddCommands(this ICommandBuilder commands)
    {
        // Port listing
        commands.AddCommand<ListPortsCommand>();
        // Discovery
        commands.AddCommand<ProvisionCommand>();
        commands.AddCommand<RescanCommand>();
        commands.AddCommand<ListDialsCommand>();
        // Dial control
        commands.AddCommand<SetPercentCommand>();
        commands.AddCommand<SetRawCommand>();
        commands.AddCommand<SetMultipleCommand>();
        // Backlight
        commands.AddCommand<SetBacklightCommand>();
        // Easing
        commands.AddCommand<GetEasingCommand>();
        commands.AddCommand<SetDialEasingStepCommand>();
        commands.AddCommand<SetDialEasingPeriodCommand>();
        commands.AddCommand<SetBacklightEasingStepCommand>();
        commands.AddCommand<SetBacklightEasingPeriodCommand>();
        // Device info
        commands.AddCommand<DeviceInfoCommand>();
        // System
        commands.AddCommand<PowerCommand>();
        commands.AddCommand<ResetDevicesCommand>();
        commands.AddCommand<ResetConfigCommand>();
        commands.AddCommand<CalibrateCommand>();
        commands.AddCommand<DisplayClearCommand>();
        // Test patterns
        commands.AddCommand<SweepCommand>();
    }
}

// ============================================================
//  Shared options helper
// ============================================================

internal static class PortHelper
{
    public static VUDialsClient Open(string port, bool verbose = false)
    {
        var client = new VUDialsClient(port);
        if (verbose)
        {
            client.OnTransmit += s => Console.WriteLine($"TX: {s}");
            client.OnReceive  += s => Console.WriteLine($"RX: {s}");
            client.OnLog      += s => Console.WriteLine($"-- {s}");
        }
        client.Open();
        return client;
    }
}

// ============================================================
//  list-ports
// ============================================================

[Command("list-ports", "List available serial ports")]
public sealed class ListPortsCommand : ICommandHandler
{
    public ValueTask ExecuteAsync(CommandContext context)
    {
        var ports = SerialPort.GetPortNames();
        if (ports.Length == 0)
            Console.WriteLine("No serial ports found.");
        else
            foreach (var p in ports) Console.WriteLine(p);
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  provision
// ============================================================

[Command("provision", "Provision and rescan all dials")]
public sealed class ProvisionCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port (e.g. COM5)", Required = true)]
    public string Port { get; set; } = default!;

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var n = client.ProvisionAndRescan();
        Console.WriteLine(n >= 0 ? $"Detected {n} dial(s)." : "Failed.");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  rescan
// ============================================================

[Command("rescan", "Rescan I2C bus")]
public sealed class RescanCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.RescanBus(out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  list-dials
// ============================================================

[Command("list-dials", "List connected dials")]
public sealed class ListDialsCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var dials = client.ListDials();
        if (dials.Count == 0)
        {
            Console.WriteLine("No dials found.");
        }
        else
        {
            Console.WriteLine($"{"Index",-8} {"UID",-32}");
            Console.WriteLine(new string('-', 42));
            foreach (var d in dials)
                Console.WriteLine($"{d.Index,-8} {d.UidHex,-32}");
        }
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  set-percent
// ============================================================

[Command("set-percent", "Set dial position by percentage (0-100)")]
public sealed class SetPercentCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID (0-based)", Required = true)]
    public byte DialId { get; set; }

    [Option<byte>("--value", "-val", Description = "Percentage (0-100)", Required = true)]
    public byte Value { get; set; }

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.SetDialPercent(DialId, Value, out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  set-raw
// ============================================================

[Command("set-raw", "Set dial position by raw value (0-65535)")]
public sealed class SetRawCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID (0-based)", Required = true)]
    public byte DialId { get; set; }

    [Option<ushort>("--value", "-val", Description = "Raw value (0-65535)", Required = true)]
    public ushort Value { get; set; }

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.SetDialRaw(DialId, Value, out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  set-multiple
// ============================================================

[Command("set-multiple", "Set multiple dials at once (e.g. 0=50,1=75,2=20)")]
public sealed class SetMultipleCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<string>("--values", "-val", Description = "Dial=percent pairs (e.g. 0=50,1=75)", Required = true)]
    public string Values { get; set; } = default!;

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        var pairs = new List<(byte, byte)>();
        foreach (var part in Values.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && byte.TryParse(kv[0].Trim(), out var id) && byte.TryParse(kv[1].Trim(), out var pct))
                pairs.Add((id, pct));
        }
        if (pairs.Count == 0)
        {
            Console.Error.WriteLine("Invalid values format. Expected: 0=50,1=75");
            return ValueTask.CompletedTask;
        }
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.SetMultipleDialsPercent(pairs, out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  set-backlight
// ============================================================

[Command("set-backlight", "Set RGBW backlight (values 0-100)")]
public sealed class SetBacklightCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID (0-based)", Required = true)]
    public byte DialId { get; set; }

    [Option<byte>("--r", Description = "Red (0-100)")]
    public byte R { get; set; }

    [Option<byte>("--g", Description = "Green (0-100)")]
    public byte G { get; set; }

    [Option<byte>("--b", Description = "Blue (0-100)")]
    public byte B { get; set; }

    [Option<byte>("--w", Description = "White (0-100)")]
    public byte W { get; set; }

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.SetBacklight(DialId, R, G, B, W, out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  get-easing
// ============================================================

[Command("get-easing", "Get easing configuration for a dial")]
public sealed class GetEasingCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID (0-based)", Required = true)]
    public byte DialId { get; set; }

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var cfg = client.GetEasingConfig(DialId);
        if (cfg is null)
        {
            Console.Error.WriteLine("Failed to get easing config.");
        }
        else
        {
            Console.WriteLine($"DialStep        : {cfg.DialStep}");
            Console.WriteLine($"DialPeriod      : {cfg.DialPeriod}");
            Console.WriteLine($"BacklightStep   : {cfg.BacklightStep}");
            Console.WriteLine($"BacklightPeriod : {cfg.BacklightPeriod}");
        }
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  set-dial-easing-step
// ============================================================

[Command("set-dial-easing-step", "Set dial easing step")]
public sealed class SetDialEasingStepCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID (0-based)", Required = true)]
    public byte DialId { get; set; }

    [Option<uint>("--value", "-val", Description = "Step value", Required = true)]
    public uint Value { get; set; }

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.SetDialEasingStep(DialId, Value, out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  set-dial-easing-period
// ============================================================

[Command("set-dial-easing-period", "Set dial easing period (ms)")]
public sealed class SetDialEasingPeriodCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID (0-based)", Required = true)]
    public byte DialId { get; set; }

    [Option<uint>("--value", "-val", Description = "Period in ms", Required = true)]
    public uint Value { get; set; }

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.SetDialEasingPeriod(DialId, Value, out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  set-backlight-easing-step
// ============================================================

[Command("set-backlight-easing-step", "Set backlight easing step")]
public sealed class SetBacklightEasingStepCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID (0-based)", Required = true)]
    public byte DialId { get; set; }

    [Option<uint>("--value", "-val", Description = "Step value", Required = true)]
    public uint Value { get; set; }

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.SetBacklightEasingStep(DialId, Value, out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  set-backlight-easing-period
// ============================================================

[Command("set-backlight-easing-period", "Set backlight easing period (ms)")]
public sealed class SetBacklightEasingPeriodCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID (0-based)", Required = true)]
    public byte DialId { get; set; }

    [Option<uint>("--value", "-val", Description = "Period in ms", Required = true)]
    public uint Value { get; set; }

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.SetBacklightEasingPeriod(DialId, Value, out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  device-info
// ============================================================

[Command("device-info", "Get device information for a dial")]
public sealed class DeviceInfoCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID (0-based)", Required = true)]
    public byte DialId { get; set; }

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        Console.WriteLine($"UID      : {client.GetDeviceUid(DialId) ?? "(none)"}");
        Console.WriteLine($"Build    : {client.GetBuildInfo(DialId) ?? "(none)"}");
        Console.WriteLine($"Firmware : {client.GetFirmwareVersion(DialId) ?? "(none)"}");
        Console.WriteLine($"Hardware : {client.GetHardwareVersion(DialId) ?? "(none)"}");
        Console.WriteLine($"Protocol : {client.GetProtocolVersion(DialId) ?? "(none)"}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  power
// ============================================================

[Command("power", "Turn dial power on or off")]
public sealed class PowerCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<bool>("--on", Description = "Power on (default: off)")]
    public bool On { get; set; }

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.SetDialPower(On, out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  reset-devices
// ============================================================

[Command("reset-devices", "Reset all devices")]
public sealed class ResetDevicesCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.ResetAllDevices(out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  reset-config
// ============================================================

[Command("reset-config", "Reset configuration to defaults")]
public sealed class ResetConfigCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.ResetConfig(out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  calibrate
// ============================================================

[Command("calibrate", "Calibrate dial (full-scale or half-scale)")]
public sealed class CalibrateCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID (0-based)", Required = true)]
    public byte DialId { get; set; }

    [Option<uint>("--value", "-val", Description = "Calibration value", Required = true)]
    public uint Value { get; set; }

    [Option<bool>("--full", Description = "Full-scale calibration (default: half-scale)")]
    public bool Full { get; set; }

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.CalibrateDial(DialId, Value, Full, out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  display-clear
// ============================================================

[Command("display-clear", "Clear the display of a dial")]
public sealed class DisplayClearCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID (0-based)", Required = true)]
    public byte DialId { get; set; }

    [Option<bool>("--white", Description = "Clear with white background")]
    public bool White { get; set; }

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        var ok = client.DisplayClear(DialId, White, out var status);
        Console.WriteLine(ok ? "OK" : $"Failed: {status}");
        return ValueTask.CompletedTask;
    }
}

// ============================================================
//  sweep
// ============================================================

[Command("sweep", "Run a 0→100→0 sweep test pattern")]
public sealed class SweepCommand : ICommandHandler
{
    [Option<string>("--port", "-p", Description = "Serial port", Required = true)]
    public string Port { get; set; } = default!;

    [Option<byte>("--dial", "-d", Description = "Dial ID (0-based)", Required = true)]
    public byte DialId { get; set; }

    [Option<int>("--loops", "-l", Description = "Number of loops (default: 1)")]
    public int Loops { get; set; } = 1;

    [Option<int>("--delay", Description = "Delay between steps in ms (default: 80)")]
    public int DelayMs { get; set; } = 80;

    [Option<bool>("--verbose", "-v", Description = "Show TX/RX")]
    public bool Verbose { get; set; }

    public ValueTask ExecuteAsync(CommandContext context)
    {
        using var client = PortHelper.Open(Port, Verbose);
        for (var loop = 0; loop < Loops; loop++)
        {
            for (var v = 0; v <= 100; v += 5)
            {
                client.SetDialPercent(DialId, (byte)v, out _);
                Console.Write($"\r{v,3}%");
                Thread.Sleep(DelayMs);
            }
            for (var v = 100; v >= 0; v -= 5)
            {
                client.SetDialPercent(DialId, (byte)v, out _);
                Console.Write($"\r{v,3}%");
                Thread.Sleep(DelayMs);
            }
        }
        Console.WriteLine("\nDone.");
        return ValueTask.CompletedTask;
    }
}
