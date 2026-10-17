using NAudio.Midi;
using SmcMixer.Core;

if (args.Length >= 2 && args[0] == "led")
{
    LedProbe.Run(args[1..]);
    return;
}

// Optional first argument: MIDI input index. Otherwise the mixer is auto-detected.
int? inputIndex = args.Length > 0 && int.TryParse(args[0], out var a) ? a : null;

Mixer mixer;
try
{
    mixer = new Mixer(inputIndex);
}
catch (InvalidOperationException ex)
{
    Console.WriteLine(ex.Message);
    return;
}

using (mixer)
{
    Console.WriteLine($"Input:  {mixer.InputName}");
    Console.WriteLine($"Output: {mixer.OutputName ?? "none (LEDs disabled)"}");
    Console.WriteLine("Press Enter to quit.\n");

    // Demo: every button with an LED toggles it on each press.
    var lit = new HashSet<ControlInfo>();

    mixer.ControlChanged += (_, c) =>
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {Describe(c)}");

        if (c.Control is { Kind: ControlKind.Button } button && c.Value == 1)
        {
            bool on = lit.Add(button);
            if (!on) lit.Remove(button);
            mixer.SetLed(button, on);
        }
    };
    mixer.UnmappedMessage += (_, ev) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {DescribeRaw(ev)}  (unmapped)");

    mixer.Start();
    Console.ReadLine();

    foreach (var button in lit) mixer.SetLed(button, false);
}

static string Describe(ControlEvent c) => c.Control.Kind switch
{
    ControlKind.Button => $"{c.Control.Name,-10} {(c.Value > 0 ? "pressed" : "released")}",
    ControlKind.Knob => $"{c.Control.Name,-10} {c.Value:+0;-0}",
    _ => $"{c.Control.Name,-10} {c.Value,5:P0}  (raw {c.RawValue})",
};

static string DescribeRaw(MidiEvent ev) => ev switch
{
    ControlChangeEvent cc => $"CC    ch{cc.Channel,-2} controller={(int)cc.Controller,-3} value={cc.ControllerValue}",
    NoteOnEvent n => $"NoteOn  ch{n.Channel,-2} note={n.NoteNumber,-3} velocity={n.Velocity}",
    NoteEvent n => $"{n.CommandCode,-6} ch{n.Channel,-2} note={n.NoteNumber,-3} velocity={n.Velocity}",
    PitchWheelChangeEvent p => $"Pitch ch{p.Channel,-2} value={p.Pitch}",
    PatchChangeEvent p => $"Prog  ch{p.Channel,-2} patch={p.Patch}",
    _ => $"{ev.CommandCode} ch{ev.Channel} raw=0x{ev.GetAsShortMessage():X8}",
};
