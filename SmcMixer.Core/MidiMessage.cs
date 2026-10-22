namespace SmcMixer.Core;

public enum MidiCommand : byte
{
    NoteOff = 0x80,
    NoteOn = 0x90,
    KeyPressure = 0xA0,
    ControlChange = 0xB0,
    ProgramChange = 0xC0,
    ChannelPressure = 0xD0,
    PitchBend = 0xE0,
}

/// <summary>
/// A MIDI channel message (Note, Control Change, Pitch Bend, ...). System messages such as clock, active sensing and SysEx
/// are not represented. Program Change and Channel Pressure carry one data byte, so <see cref="Data2"/> is 0 for them.
/// </summary>
public readonly record struct MidiMessage(byte Status, byte Data1, byte Data2)
{
    public MidiCommand Command => (MidiCommand)(Status & 0xF0);

    /// <summary>MIDI channel, 1-16.</summary>
    public int Channel => (Status & 0x0F) + 1;

    /// <summary>The 14-bit value of a Pitch Bend message, 0..16383.</summary>
    public int PitchBend => Data1 | (Data2 << 7);

    public static MidiMessage NoteOn(int note, int velocity, int channel) =>
        new((byte)(0x90 | (channel - 1)), (byte)note, (byte)velocity);

    /// <summary>Parses one message as delivered by a backend. Returns false for system messages and anything malformed.</summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out MidiMessage message)
    {
        message = default;
        if (data.Length < 2 || data[0] is < 0x80 or >= 0xF0) return false;

        var command = (MidiCommand)(data[0] & 0xF0);
        bool oneDataByte = command is MidiCommand.ProgramChange or MidiCommand.ChannelPressure;
        if (!oneDataByte && data.Length < 3) return false;

        message = new(data[0], data[1], oneDataByte ? (byte)0 : data[2]);
        return true;
    }

    public byte[] ToBytes() =>
        Command is MidiCommand.ProgramChange or MidiCommand.ChannelPressure ? [Status, Data1] : [Status, Data1, Data2];

    public override string ToString() => Command switch
    {
        MidiCommand.ControlChange => $"CC     ch{Channel,-2} controller={Data1,-3} value={Data2}",
        MidiCommand.NoteOn => $"NoteOn ch{Channel,-2} note={Data1,-3} velocity={Data2}",
        MidiCommand.NoteOff => $"NoteOff ch{Channel,-2} note={Data1,-3} velocity={Data2}",
        MidiCommand.PitchBend => $"Pitch  ch{Channel,-2} value={PitchBend}",
        MidiCommand.ProgramChange => $"Prog   ch{Channel,-2} patch={Data1}",
        _ => $"{Command} ch{Channel} data={Data1},{Data2}",
    };
}
