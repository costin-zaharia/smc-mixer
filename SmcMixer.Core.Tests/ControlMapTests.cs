using SmcMixer.Core;

namespace SmcMixer.Core.Tests;

public class ControlMapTests
{
    private static ControlEvent? Decode(params byte[] bytes) =>
        MidiMessage.TryParse(bytes, out var m) ? new ControlMap().Decode(m) : null;

    [Fact]
    public void FaderIsPitchBendOnItsChannel()
    {
        // Pitch bend on channel 3 (status 0xE2), full scale 16256 = 127 << 7: LSB 0, MSB 127.
        var e = Decode(0xE2, 0, 127)!;
        Assert.Equal(ControlKind.Fader, e.Control.Kind);
        Assert.Equal(3, e.Control.Index);
        Assert.Equal("Fader 3", e.Control.Name);
        Assert.Equal(16256, e.RawValue);
        Assert.Equal(1.0, e.Value);
    }

    [Fact]
    public void FaderBottomIsZeroAndMiddleIsHalf()
    {
        Assert.Equal(0.0, Decode(0xE0, 0, 0)!.Value);
        Assert.Equal(64 * 128 / 16256.0, Decode(0xE0, 0, 64)!.Value, 6);
    }

    [Fact]
    public void FaderValueNeverExceedsOne()
    {
        Assert.Equal(1.0, Decode(0xE0, 127, 127)!.Value); // 16383, above the fader's real maximum
    }

    [Fact]
    public void PitchBendOnChannelNineIsNotAFader()
    {
        Assert.Null(Decode(0xE8, 0, 64));
    }

    [Theory]
    [InlineData(16, 1)]
    [InlineData(23, 8)]
    public void KnobIndexComesFromControllerNumber(byte controller, int index)
    {
        var e = Decode(0xB0, controller, 1)!;
        Assert.Equal(ControlKind.Knob, e.Control.Kind);
        Assert.Equal(index, e.Control.Index);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(63, 63)]
    [InlineData(65, -1)]
    [InlineData(127, -63)]
    public void KnobIsRelativeTicks(byte value, int ticks)
    {
        Assert.Equal(ticks, Decode(0xB0, 16, value)!.Value);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(24)]
    public void ControllersOutsideTheKnobRangeAreUnmapped(byte controller)
    {
        Assert.Null(Decode(0xB0, controller, 5));
    }

    [Theory]
    [InlineData(0, "Rec 1")]
    [InlineData(10, "Solo 3")]
    [InlineData(16, "Mute 1")]
    [InlineData(31, "Select 8")]
    [InlineData(94, "Play")]
    [InlineData(91, "Rewind")]
    [InlineData(99, "Right")]
    public void ButtonNotesMapToNames(byte note, string name)
    {
        var e = Decode(0x90, note, 127)!;
        Assert.Equal(ControlKind.Button, e.Control.Kind);
        Assert.Equal(name, e.Control.Name);
        Assert.Equal(1, e.Value);
    }

    [Fact]
    public void ButtonReleaseIsNoteOnWithVelocityZero()
    {
        Assert.Equal(0, Decode(0x90, 16, 0)!.Value);
    }

    [Fact]
    public void ButtonReleaseCanBeNoteOff()
    {
        Assert.Equal(0, Decode(0x80, 16, 64)!.Value);
    }

    [Fact]
    public void UnknownNoteIsUnmapped()
    {
        Assert.Null(Decode(0x90, 60, 127));
    }

    [Fact]
    public void EveryButtonNoteIsUnique()
    {
        var buttons = new ControlMap().Buttons;
        Assert.Equal(32 + 11, buttons.Count);
        Assert.All(buttons, kv => Assert.Equal(kv.Key, kv.Value.Note));
    }

    [Fact]
    public void LedlessButtonsAreTheArrowsAndShiftedTransport()
    {
        var noLed = new ControlMap().Buttons.Values.Where(b => !b.HasLed).Select(b => b.Name).Order();
        Assert.Equal(new[] { "Down", "Left", "Next", "Prev", "Right", "Up" }, noLed);
    }
}

public class MidiMessageTests
{
    [Fact]
    public void ParsesThreeByteMessage()
    {
        Assert.True(MidiMessage.TryParse(new byte[] { 0x93, 60, 100 }, out var m));
        Assert.Equal(MidiCommand.NoteOn, m.Command);
        Assert.Equal(4, m.Channel);
        Assert.Equal(60, m.Data1);
        Assert.Equal(100, m.Data2);
    }

    [Fact]
    public void ProgramChangeHasOneDataByte()
    {
        Assert.True(MidiMessage.TryParse(new byte[] { 0xC0, 5 }, out var m));
        Assert.Equal(5, m.Data1);
        Assert.Equal(new byte[] { 0xC0, 5 }, m.ToBytes());
    }

    [Theory]
    [InlineData(0xF8)]            // timing clock
    [InlineData(0xFE)]            // active sensing
    [InlineData(0xF0, 1, 2, 3)]   // sysex start
    [InlineData(0x40, 1, 2)]      // data byte where a status is expected
    public void IgnoresSystemAndMalformedMessages(params int[] data)
    {
        Assert.False(MidiMessage.TryParse(data.Select(b => (byte)b).ToArray(), out _));
    }

    [Fact]
    public void ThreeByteMessageNeedsThreeBytes()
    {
        Assert.False(MidiMessage.TryParse(new byte[] { 0x90, 60 }, out _));
    }

    [Fact]
    public void PitchBendIs14Bit()
    {
        MidiMessage.TryParse(new byte[] { 0xE0, 0x7F, 0x7F }, out var m);
        Assert.Equal(16383, m.PitchBend);
    }

    [Fact]
    public void NoteOnBuildsWireBytes()
    {
        Assert.Equal(new byte[] { 0x90, 115, 127 }, MidiMessage.NoteOn(115, 127, 1).ToBytes());
        Assert.Equal(new byte[] { 0x91, 16, 0 }, MidiMessage.NoteOn(16, 0, 2).ToBytes());
    }
}
