namespace SmcMixer.Core;

/// <summary>A MIDI port. <see cref="Id"/> is whatever the backend needs to reopen it and is only meaningful to that backend.</summary>
public sealed record MidiPortInfo(string Id, string Name);

/// <summary>
/// The operating system's MIDI API. Core only talks to this, so supporting a platform means writing one backend.
/// Implementations: SmcMixer.Windows (winmm via NAudio) and SmcMixer.Linux (ALSA).
/// </summary>
public interface IMidiBackend
{
    /// <summary>Lists the ports as they are now. Call again to see devices that connected later.</summary>
    IReadOnlyList<MidiPortInfo> GetInputs();

    IReadOnlyList<MidiPortInfo> GetOutputs();

    IMidiInput OpenInput(MidiPortInfo port);

    IMidiOutput OpenOutput(MidiPortInfo port);
}

public interface IMidiInput : IDisposable
{
    /// <summary>One complete MIDI message per call, raised on a backend thread. System messages may be passed through; Core ignores them.</summary>
    event Action<byte[]>? MessageReceived;

    void Start();

    void Stop();
}

public interface IMidiOutput : IDisposable
{
    void Send(byte[] message);
}
