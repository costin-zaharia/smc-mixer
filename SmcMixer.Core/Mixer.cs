using NAudio.Midi;

namespace SmcMixer.Core;

/// <summary>
/// The M-VAVE SMC-Mixer (in DAW mode): raises events for faders, knobs and buttons, and switches button LEDs.
/// Events are raised on the MIDI driver's callback thread, so marshal to the UI thread before touching UI.
/// </summary>
public sealed class Mixer : IDisposable
{
    private readonly MidiIn _in;
    private readonly LedController? _leds;
    private readonly ControlMap _map = new();
    private bool _started;

    /// <summary>Raised for every mapped control: faders (0..1), knobs (+/- ticks) and buttons (1 pressed / 0 released).</summary>
    public event EventHandler<ControlEvent>? ControlChanged;

    /// <summary>Raised for messages the map doesn't recognize, e.g. to discover new controls.</summary>
    public event EventHandler<MidiEvent>? UnmappedMessage;

    /// <summary>All buttons, keyed by the note they send. Names: "Mute 1".."Mute 8", "Solo n", "Rec n", "Select n", "Play", ...</summary>
    public IReadOnlyDictionary<int, ControlInfo> Buttons => _map.Buttons;

    public string InputName { get; }
    public string? OutputName { get; }

    /// <summary>Opens the mixer, auto-detecting its MIDI ports unless indexes are given. Call <see cref="Start"/> to receive events.</summary>
    public Mixer(int? inputIndex = null, int? outputIndex = null)
    {
        int input = inputIndex ?? FindInput() ?? throw new InvalidOperationException("No SMC-Mixer MIDI input found. Is it connected?");
        _in = new MidiIn(input);
        InputName = MidiIn.DeviceInfo(input).ProductName;
        _in.MessageReceived += OnMessage;

        // LEDs are optional: input still works if the output port can't be opened.
        if ((outputIndex ?? FindOutput()) is { } output)
        {
            _leds = new LedController(output);
            OutputName = MidiOut.DeviceInfo(output).ProductName;
        }
    }

    public void Start()
    {
        if (_started) return;
        _in.Start();
        _started = true;
    }

    /// <summary>Lights or clears a button's LED (strip buttons and Play/Pause/Record/Rewind/Forward). Other buttons are ignored.</summary>
    public void SetLed(ControlInfo button, bool on)
    {
        if (button.HasLed) _leds?.Set(button, on);
    }

    /// <summary>Lights or clears all eight Mute LEDs at once.</summary>
    public void SetAllMuteLeds(bool on) => _leds?.SetAllMute(on);

    /// <summary>First MIDI input that looks like the mixer, or null.</summary>
    public static int? FindInput()
    {
        for (int i = 0; i < MidiIn.NumberOfDevices; i++)
            if (IsMixer(MidiIn.DeviceInfo(i).ProductName)) return i;
        return null;
    }

    /// <summary>
    /// The mixer exposes two output ports; the LEDs answer on the last matching one ("MIDIOUT2 (SMC-Mixer)"). Null if none.
    /// </summary>
    public static int? FindOutput()
    {
        int? found = null;
        for (int i = 0; i < MidiOut.NumberOfDevices; i++)
            if (IsMixer(MidiOut.DeviceInfo(i).ProductName)) found = i;
        return found;
    }

    private static bool IsMixer(string name) =>
        name.Contains("SMC", StringComparison.OrdinalIgnoreCase) || name.Contains("VAVE", StringComparison.OrdinalIgnoreCase);

    private void OnMessage(object? sender, MidiInMessageEventArgs e)
    {
        if (_map.Decode(e.MidiEvent) is { } control)
            ControlChanged?.Invoke(this, control);
        else
            UnmappedMessage?.Invoke(this, e.MidiEvent);
    }

    public void Dispose()
    {
        if (_started) _in.Stop();
        _in.MessageReceived -= OnMessage;
        _in.Dispose();
        _leds?.Dispose();
    }
}
