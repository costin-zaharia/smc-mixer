using SmcMixer.Core;
using SmcMixer.Linux;
using SmcMixer.Windows;

static class MidiBackends
{
    /// <summary>The MIDI backend for the operating system this is running on.</summary>
    public static IMidiBackend Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsMidiBackend();
        if (OperatingSystem.IsLinux()) return new LinuxMidiBackend();
        throw new PlatformNotSupportedException("Only Windows and Linux are supported.");
    }
}
