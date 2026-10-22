using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Commons.Music.Midi;
using SmcMixer.Core;

namespace SmcMixer.Linux;

/// <summary>
/// MIDI through ALSA's sequencer, via managed-midi. Port ids are ALSA ids ("client:port").
/// Bluetooth (BlueZ), USB and virtual ports all show up here as sequencer clients.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxMidiBackend : IMidiBackend
{
    private readonly IMidiAccess2 _access;

    static LinuxMidiBackend()
    {
        // alsa-sharp imports "asound", which resolves to libasound.so, a symlink that only the -dev package installs.
        // Fall back to the runtime library's versioned name so no dev packages are needed.
        NativeLibrary.SetDllImportResolver(typeof(AlsaSharp.AlsaSequencer).Assembly, ResolveAsound);
    }

    public LinuxMidiBackend() => _access = (IMidiAccess2)MidiAccessManager.Default;

    private static IntPtr ResolveAsound(string name, Assembly assembly, DllImportSearchPath? searchPath) =>
        name == "asound" && NativeLibrary.TryLoad("libasound.so.2", assembly, searchPath, out var handle) ? handle : IntPtr.Zero;

    public IReadOnlyList<MidiPortInfo> GetInputs() =>
        _access.Inputs.Select(p => new MidiPortInfo(p.Id, p.Name)).ToList();

    public IReadOnlyList<MidiPortInfo> GetOutputs() =>
        _access.Outputs.Select(p => new MidiPortInfo(p.Id, p.Name)).ToList();

    public SmcMixer.Core.IMidiInput OpenInput(MidiPortInfo port) =>
        new Input(_access.OpenInputAsync(port.Id).GetAwaiter().GetResult());

    public SmcMixer.Core.IMidiOutput OpenOutput(MidiPortInfo port) =>
        new Output(_access.OpenOutputAsync(port.Id).GetAwaiter().GetResult());

    private sealed class Input : SmcMixer.Core.IMidiInput
    {
        private readonly Commons.Music.Midi.IMidiInput _in;

        public Input(Commons.Music.Midi.IMidiInput input)
        {
            _in = input;
            _in.MessageReceived += OnMessage;
        }

        public event Action<byte[]>? MessageReceived;

        // ALSA delivers events as soon as the port is open, so there's nothing to start or stop.
        public void Start() { }

        public void Stop() { }

        // The last channel status byte seen, to complete running-status messages (see OnMessage).
        private byte _runningStatus;

        private void OnMessage(object? sender, MidiReceivedEventArgs e)
        {
            var data = e.Data.AsSpan(e.Start, e.Length);
            if (data.IsEmpty) return;

            if (data[0] >= 0x80)
            {
                // System messages (clock, sysex, ...) leave running status untouched here; Core ignores them anyway.
                if (data[0] < 0xF0) _runningStatus = data[0];
                MessageReceived?.Invoke(data.ToArray());
            }
            else if (_runningStatus != 0)
            {
                // Running status: consecutive messages of the same kind arrive without their status byte
                // (a stream of fader moves, say, is just data bytes after the first message).
                byte[] full = new byte[data.Length + 1];
                full[0] = _runningStatus;
                data.CopyTo(full.AsSpan(1));
                MessageReceived?.Invoke(full);
            }
        }

        public void Dispose()
        {
            _in.MessageReceived -= OnMessage;
            _in.CloseAsync().GetAwaiter().GetResult();
        }
    }

    private sealed class Output(Commons.Music.Midi.IMidiOutput output) : SmcMixer.Core.IMidiOutput
    {
        public void Send(byte[] message) => output.Send(message, 0, message.Length, 0);

        public void Dispose() => output.CloseAsync().GetAwaiter().GetResult();
    }
}
