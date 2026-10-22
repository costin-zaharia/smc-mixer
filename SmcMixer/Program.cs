using SmcMixer.Core;

var backend = MidiBackends.Create();

if (args.Length >= 2 && args[0] == "led")
{
    LedProbe.Run(backend, args[1..]);
    return;
}

Console.WriteLine("MIDI inputs:");
var inputs = backend.GetInputs();
for (int i = 0; i < inputs.Count; i++)
    Console.WriteLine($"  [{i}] {inputs[i].Name}");

// Optional first argument: MIDI input index (from the list above). Otherwise the mixer is auto-detected.
MidiPortInfo? input = args.Length > 0 && int.TryParse(args[0], out var a) && a >= 0 && a < inputs.Count ? inputs[a] : null;

Mixer mixer;
try
{
    mixer = new Mixer(backend, input);
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
    mixer.UnmappedMessage += (_, m) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {m}  (unmapped)");

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
