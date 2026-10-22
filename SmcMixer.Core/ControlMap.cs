namespace SmcMixer.Core;

public enum ControlKind { Fader, Knob, Button }

public enum LedColor { Red, Blue, Orange, White, Green }

/// <summary>
/// Note is the note the button sends when pressed and, for buttons that have an LED, also the Note On that lights it (sent to the mixer's MIDI output).
/// LedColor is the fixed color of that LED; each LED is single-color, so it can only be switched on or off.
/// </summary>
public record ControlInfo(ControlKind Kind, int Index, string Name, int? Note = null, LedColor? LedColor = null, bool HasLed = true);

/// <summary>
/// A decoded mixer event. Value is 0..1 for faders, +n/-n ticks for knobs (they are relative),
/// and 1 (pressed) or 0 (released) for buttons.
/// </summary>
public record ControlEvent(ControlInfo Control, int RawValue, double Value);

/// <summary>
/// Decodes the mixer's messages in DAW mode (Mackie-style): faders are pitch bend on channels 1-8, knobs are relative
/// CC 16-23, and buttons are notes. Switch modes with SHIFT + a transport button; LEDs only respond in this mode.
/// </summary>
public sealed class ControlMap
{
    // The fader sends 7-bit values shifted into the 14-bit pitch bend range (steps of 128).
    private const double FaderMax = 127 * 128;

    private readonly Dictionary<int, ControlInfo> _buttons = new();

    public IReadOnlyDictionary<int, ControlInfo> Buttons => _buttons;

    public ControlMap()
    {
        for (int strip = 1; strip <= 8; strip++)
        {
            // Notes (confirmed by scan): R 0-7, S 8-15, M 16-23, square 24-31.
            Add(new(ControlKind.Button, strip, $"Rec {strip}", strip - 1, LedColor.Red));
            Add(new(ControlKind.Button, strip, $"Solo {strip}", 7 + strip, LedColor.Blue));
            Add(new(ControlKind.Button, strip, $"Mute {strip}", 15 + strip, LedColor.Orange));
            Add(new(ControlKind.Button, strip, $"Select {strip}", 23 + strip, LedColor.White)); // square button, name unconfirmed
        }

        // Transport row. Confirmed by scan: Rewind 91, Forward 92, Pause 93, Play 94, Record 95.
        Add(new(ControlKind.Button, 1, "Rewind", 91, LedColor.White));
        Add(new(ControlKind.Button, 2, "Forward", 92, LedColor.White));
        Add(new(ControlKind.Button, 3, "Pause", 93, LedColor.Red));
        Add(new(ControlKind.Button, 4, "Play", 94, LedColor.Green));
        Add(new(ControlKind.Button, 5, "Record", 95, LedColor.Red));

        // Input only: the << and >> buttons and the four arrows, assigned by press order. Their LEDs light while the
        // button is held, driven by the mixer itself, and don't respond to MIDI.
        Add(new(ControlKind.Button, 6, "Prev", 46, HasLed: false));
        Add(new(ControlKind.Button, 7, "Next", 47, HasLed: false));
        Add(new(ControlKind.Button, 8, "Up", 96, HasLed: false));
        Add(new(ControlKind.Button, 9, "Down", 97, HasLed: false));
        Add(new(ControlKind.Button, 10, "Left", 98, HasLed: false));
        Add(new(ControlKind.Button, 11, "Right", 99, HasLed: false));
    }

    private void Add(ControlInfo info) => _buttons[info.Note!.Value] = info;

    public ControlEvent? Decode(MidiMessage m)
    {
        switch (m.Command)
        {
            case MidiCommand.PitchBend when m.Channel <= 8:
                int pitch = m.PitchBend;
                return new(new(ControlKind.Fader, m.Channel, $"Fader {m.Channel}"), pitch, Math.Min(pitch / FaderMax, 1.0));

            case MidiCommand.ControlChange when m.Data1 is >= 16 and <= 23:
                // Relative: 1..63 is clockwise ticks, 65..127 is counter-clockwise (value - 64) ticks.
                int index = m.Data1 - 15;
                int ticks = m.Data2 < 64 ? m.Data2 : -(m.Data2 - 64);
                return new(new(ControlKind.Knob, index, $"Knob {index}"), m.Data2, ticks);

            case MidiCommand.NoteOn or MidiCommand.NoteOff when _buttons.TryGetValue(m.Data1, out var button):
                // Releases arrive as Note On with velocity 0, or as Note Off.
                bool pressed = m.Command == MidiCommand.NoteOn && m.Data2 > 0;
                return new(button, m.Data2, pressed ? 1 : 0);

            default:
                return null;
        }
    }
}
