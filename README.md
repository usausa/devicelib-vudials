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

// Set dial position by percentage (0-100). Returns VUDialsStatus (Ok == success).
var status = client.SetDialPercent(0, 75);
if (status != VUDialsStatus.Ok)
{
    Console.WriteLine($"Failed: {status}");
}

// Set RGBW backlight (values 0-100)
client.SetBacklight(0, red: 100, green: 0, blue: 0, white: 0);
```

## Global tool

### Install

```
> dotnet tool install -g DeviceTool.VUDials
```

### Commands

| Command | Description |
|:-|:-|
| `port` | List available serial ports |
| `dials` | List connected dials |
| `info` | Get device information for a dial |
| `percent` | Set dial position by percentage (0-100) |
| `multi` | Set multiple dials at once |
| `raw` | Set dial position by raw value (0-65535) |
| `backlight` | Set RGBW backlight |
| `calibrate` | Calibrate dial |
| `easing` | Show easing config, or set only the options you pass |
| `provision` | Provision and rescan all dials |
| `rescan` | Rescan I2C bus |
| `power` | Turn dial power on or off |
| `reset` | Reset devices and/or configuration |
| `clear` | Clear the display of a dial |
| `sweep` | Run a 0->100->0 sweep test pattern |

### Usage

```
vud provision -p COM5
vud dials -p COM5
vud info -p COM5 -d 0
vud percent -p COM5 -d 0 -val 75
vud raw -p COM5 -d 0 -val 32768
vud multi -p COM5 -val 0=50,1=75,2=20
vud backlight -p COM5 -d 0 --r 0 --g 100 --b 0 --w 0
vud easing -p COM5 -d 0
vud easing -p COM5 -d 0 --dial-step 5 --backlight-period 40
vud reset -p COM5 --config
vud power -p COM5 --on
vud sweep -p COM5 -d 0
```