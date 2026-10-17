# M-VAVE SMC-Mixer

A C# library and desktop app for the M-VAVE SMC-Mixer MIDI controller. It reads the faders,
knobs and buttons, and lights the button LEDs from your code.

![The desktop app mirroring the mixer](docs/screenshot.png)

The window above is a live mirror of the device: faders and knobs follow the hardware, and the LEDs light up in the
same colors as on the mixer.

## Requirements

- Windows 10 or 11 (the library uses the Windows MIDI API through [NAudio](https://github.com/naudio/NAudio))
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- The mixer connected by USB and in **DAW mode**. To switch to it, hold SHIFT and press the `«` button. LEDs only
  respond in this mode.

## Projects

| Project | What it is |
|---|---|
| `SmcMixer.Core` | The library: the `Mixer` class with events for faders, knobs and buttons, and LED control. |
| `SmcMixer.App` | WPF desktop app that mirrors the mixer. Pressing a button toggles its LED. |
| `SmcMixer` | Console app: prints every control change, and an `led` command for probing the LED protocol. |

## Run

```
dotnet run --project SmcMixer.App     # desktop mirror
dotnet run --project SmcMixer         # console monitor, buttons toggle their LEDs
```

Only one program can use the mixer's MIDI ports at a time, so close one before starting the other.

Console LED probe, for exploring the protocol (see the [MIDI notes](docs/midi-notes.md)):

```
dotnet run --project SmcMixer -- led <note> <velocity|sweep|scan|repl> [channel] [outputIndex]
```

## Using the library

```csharp
using SmcMixer.Core;

using var mixer = new Mixer();                    // auto-detects the MIDI ports
mixer.ControlChanged += (_, c) =>
{
    // c.Control.Name: "Fader 3", "Knob 1", "Mute 2", "Play", ...
    // c.Value: faders 0..1, knobs +/- ticks (relative), buttons 1 pressed / 0 released
    Console.WriteLine($"{c.Control.Name} {c.Value}");
};
mixer.Start();

mixer.SetLed(mixer.Buttons[16], true);            // note 16 = "Mute 1"
mixer.SetAllMuteLeds(false);
```

Events are raised on the MIDI driver's thread, so marshal to the UI thread before touching UI.

## Controls and LEDs

- 8 faders (pitch bend), 8 endless knobs (relative ticks), 32 strip buttons and 11 transport buttons.
- Strip buttons have fixed-color LEDs: **M** orange, **S** blue, **R** red and the square button white.
- Of the transport buttons, Play is green, Pause and Record are red, and Rewind and Forward are white.
  `«`, `»` and the four arrows light only while held, and can't be controlled over MIDI.
- LEDs are single-color and stay on until switched off.

## Limitations

- DAW mode only. The mixer's other mode (CC mode) has overlapping CC numbers and no working LED output.
- The app does not notice the mixer being unplugged while it runs.
- The square button is named `Select` in the code, which is a guess.

## More

- [docs/midi-notes.md](docs/midi-notes.md): everything known about the mixer's MIDI behavior in both modes, what is
  confirmed on the device, and what comes from third-party sources.
