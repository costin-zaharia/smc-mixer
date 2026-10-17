using NAudio.Midi;

namespace SmcMixer.Core;

/// <summary>Switches the mixer's button LEDs on and off via Note On messages on its MIDI output port.</summary>
internal sealed class LedController : IDisposable
{
    private const int Channel = 1;
    private readonly MidiOut _out;

    public LedController(int outputIndex) => _out = new MidiOut(outputIndex);

    /// <summary>Velocity 0 is off; any of 1-127 is on. The color is fixed per button (see <see cref="ControlInfo.LedColor"/>).</summary>
    public void Set(ControlInfo control, bool on)
    {
        if (control.Note is { } note)
            _out.Send(MidiMessage.StartNote(note, on ? 127 : 0, Channel).RawData);
    }

    /// <summary>Note 115 lights (velocity 127) or clears (velocity 0) all eight M (Mute) LEDs at once.</summary>
    public void SetAllMute(bool on) =>
        _out.Send(MidiMessage.StartNote(115, on ? 127 : 0, Channel).RawData);

    public void Dispose() => _out.Dispose();
}
