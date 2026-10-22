using SmcMixer.Core;

/// <summary>Developer tool for exploring the mixer's LED protocol: sends Note On messages straight to its output port.</summary>
static class LedProbe
{
    // led <note> <velocity|sweep|scan[:velocity]|repl> [channel] [outputIndex]
    public static void Run(IMidiBackend backend, string[] a)
    {
        int note = int.Parse(a[0]);
        string what = a.Length > 1 ? a[1] : "127";
        int channel = a.Length > 2 ? int.Parse(a[2]) : 1;

        Console.WriteLine("MIDI output devices:");
        var outputs = backend.GetOutputs();
        for (int i = 0; i < outputs.Count; i++)
            Console.WriteLine($"  [{i}] {outputs[i].Name}");

        var port = a.Length > 3 ? outputs[int.Parse(a[3])] : Mixer.FindOutput(backend) ?? outputs[0];

        using var midiOut = backend.OpenOutput(port);
        Console.WriteLine($"Sending to {port.Name}, note {note}, channel {channel}");

        if (what == "sweep")
        {
            for (int v = 0; v <= 127; v++)
            {
                Console.WriteLine($"velocity {v}");
                midiOut.Send(MidiMessage.NoteOn(note, v, channel).ToBytes());
                Thread.Sleep(300);
            }
            midiOut.Send(MidiMessage.NoteOn(note, 0, channel).ToBytes());
        }
        else if (what.StartsWith("scan"))
        {
            // scan or scan:<velocity>: light each note from <note> to 127 for 700 ms, so you can see which LED it drives.
            int velocity = what.Contains(':') ? int.Parse(what.Split(':')[1]) : 127;
            for (int n = note; n <= 127; n++)
            {
                Console.WriteLine($"note {n} velocity {velocity}");
                midiOut.Send(MidiMessage.NoteOn(n, velocity, channel).ToBytes());
                Thread.Sleep(700);
                midiOut.Send(MidiMessage.NoteOn(n, 0, channel).ToBytes());
            }
            return;
        }
        else if (what == "repl")
        {
            // Type "<note> <velocity>" (or just "<velocity>" to reuse the last note); empty line quits.
            Console.WriteLine("Type '<note> <velocity>' or '<velocity>'. Empty line quits.");
            string? line;
            while (!string.IsNullOrWhiteSpace(line = Console.ReadLine()))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2) note = int.Parse(parts[0]);
                int velocity = int.Parse(parts[^1]);
                midiOut.Send(MidiMessage.NoteOn(note, velocity, channel).ToBytes());
                Console.WriteLine($"  note {note} velocity {velocity}");
            }
            return;
        }
        else
        {
            midiOut.Send(MidiMessage.NoteOn(note, int.Parse(what), channel).ToBytes());
        }

        // Closing the port resets it (which can switch the LED straight back off), so keep it open until told otherwise.
        Console.WriteLine("Sent. Check the mixer, then press Enter to close the port.");
        Console.ReadLine();
    }
}
