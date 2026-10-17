using NAudio.Midi;
using SmcMixer.Core;

/// <summary>Developer tool for exploring the mixer's LED protocol: sends Note On messages straight to its output port.</summary>
static class LedProbe
{
    // led <note> <velocity|sweep|scan[:velocity]|repl> [channel] [outputIndex]
    public static void Run(string[] a)
    {
        int note = int.Parse(a[0]);
        string what = a.Length > 1 ? a[1] : "127";
        int channel = a.Length > 2 ? int.Parse(a[2]) : 1;

        Console.WriteLine("MIDI output devices:");
        for (int i = 0; i < MidiOut.NumberOfDevices; i++)
            Console.WriteLine($"  [{i}] {MidiOut.DeviceInfo(i).ProductName}");

        int index = a.Length > 3 ? int.Parse(a[3]) : Mixer.FindOutput() ?? 0;

        using var midiOut = new MidiOut(index);
        Console.WriteLine($"Sending to [{index}] {MidiOut.DeviceInfo(index).ProductName}, note {note}, channel {channel}");

        if (what == "sweep")
        {
            for (int v = 0; v <= 127; v++)
            {
                Console.WriteLine($"velocity {v}");
                midiOut.Send(MidiMessage.StartNote(note, v, channel).RawData);
                Thread.Sleep(300);
            }
            midiOut.Send(MidiMessage.StartNote(note, 0, channel).RawData);
        }
        else if (what.StartsWith("scan"))
        {
            // scan or scan:<velocity>: light each note from <note> to 127 for 700 ms, so you can see which LED it drives.
            int velocity = what.Contains(':') ? int.Parse(what.Split(':')[1]) : 127;
            for (int n = note; n <= 127; n++)
            {
                Console.WriteLine($"note {n} velocity {velocity}");
                midiOut.Send(MidiMessage.StartNote(n, velocity, channel).RawData);
                Thread.Sleep(700);
                midiOut.Send(MidiMessage.StartNote(n, 0, channel).RawData);
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
                midiOut.Send(MidiMessage.StartNote(note, velocity, channel).RawData);
                Console.WriteLine($"  note {note} velocity {velocity}");
            }
            return;
        }
        else
        {
            midiOut.Send(MidiMessage.StartNote(note, int.Parse(what), channel).RawData);
        }

        // Closing the port resets it (which can switch the LED straight back off), so keep it open until told otherwise.
        Console.WriteLine("Sent. Check the mixer, then press Enter to close the port.");
        Console.ReadLine();
    }
}
