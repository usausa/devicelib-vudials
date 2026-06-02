# VU1 Dials library

| Package | Info |
|:-|:-|
| DeviceLib.VUDials | [![NuGet](https://img.shields.io/nuget/v/DeviceLib.VUDials.svg)](https://www.nuget.org/packages/DeviceLib.VUDials) |

## Usage

```csharp
using DeviceLib.VUDials;

using var client = new VUDialsClient("COM5");
client.Open();

// Provision and detect connected dials
var count = client.ProvisionAndRescan();
Console.WriteLine($"{count} dial(s) detected");

// List connected dials
foreach (var dial in client.ListDials())
{
    Console.WriteLine($"Dial #{dial.Index} UID={dial.UidHex}");
}

// Set dial position by percentage (0-100)
client.SetDialPercent(0, 75, out var status);

// Set RGBW backlight (values 0-100)
client.SetBacklight(0, r: 100, g: 0, b: 0, w: 0, out status);
```

## Global tool

### Install

```
> dotnet tool install -g DeviceTool.VUDials
```

### Commands

| Command | Description |
|:-|:-|
| `list-ports` | List available serial ports |
| `provision` | Provision and rescan all dials |
| `rescan` | Rescan I2C bus |
| `list-dials` | List connected dials |
| `set-percent` | Set dial position by percentage (0-100) |
| `set-raw` | Set dial position by raw value (0-65535) |
| `set-multiple` | Set multiple dials at once |
| `set-backlight` | Set RGBW backlight |
| `get-easing` | Get easing configuration |
| `set-dial-easing-step` | Set dial easing step |
| `set-dial-easing-period` | Set dial easing period (ms) |
| `set-backlight-easing-step` | Set backlight easing step |
| `set-backlight-easing-period` | Set backlight easing period (ms) |
| `device-info` | Get device information |
| `power` | Turn dial power on or off |
| `reset-devices` | Reset all devices |
| `reset-config` | Reset configuration to defaults |
| `calibrate` | Calibrate dial |
| `display-clear` | Clear the display of a dial |
| `sweep` | Run a 0→100→0 sweep test pattern |

### Usage

```
vud provision -p COM5
vud list-dials -p COM5
vud set-percent -p COM5 -d 0 -val 75
vud set-raw -p COM5 -d 0 -val 32768
vud set-multiple -p COM5 -val 0=50,1=75,2=20
vud set-backlight -p COM5 -d 0 --r 0 --g 100 --b 0 --w 0
vud power -p COM5 --on
vud sweep -p COM5 -d 0
```