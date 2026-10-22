using System.Runtime.Versioning;
using SmcMixer.Core;
using MidiIn = NAudio.Midi.MidiIn;
using MidiInMessageEventArgs = NAudio.Midi.MidiInMessageEventArgs;
using MidiOut = NAudio.Midi.MidiOut;

namespace SmcMixer.Windows;

/// <summary>MIDI through the Windows multimedia API (winmm), via NAudio. Port ids are winmm device indexes.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMidiBackend : IMidiBackend
{
    public IReadOnlyList<MidiPortInfo> GetInputs() =>
        Enumerable.Range(0, MidiIn.NumberOfDevices)
            .Select(i => new MidiPortInfo(i.ToString(), MidiIn.DeviceInfo(i).ProductName))
            .ToList();

    public IReadOnlyList<MidiPortInfo> GetOutputs() =>
        Enumerable.Range(0, MidiOut.NumberOfDevices)
            .Select(i => new MidiPortInfo(i.ToString(), MidiOut.DeviceInfo(i).ProductName))
            .ToList();

    public IMidiInput OpenInput(MidiPortInfo port) => new Input(new MidiIn(int.Parse(port.Id)));

    public IMidiOutput OpenOutput(MidiPortInfo port) => new Output(new MidiOut(int.Parse(port.Id)));

    private sealed class Input : IMidiInput
    {
        private readonly MidiIn _in;

        public Input(MidiIn input)
        {
            _in = input;
            _in.MessageReceived += OnMessage;
        }

        public event Action<byte[]>? MessageReceived;

        public void Start() => _in.Start();

        public void Stop() => _in.Stop();

        private void OnMessage(object? sender, MidiInMessageEventArgs e)
        {
            // winmm packs a short message into an int: status, data1, data2 from the low byte up.
            int raw = e.RawMessage;
            byte status = (byte)raw;
            byte[] data = status is >= 0xC0 and < 0xE0
                ? [status, (byte)(raw >> 8)]
                : [status, (byte)(raw >> 8), (byte)(raw >> 16)];
            MessageReceived?.Invoke(data);
        }

        public void Dispose()
        {
            _in.MessageReceived -= OnMessage;
            _in.Dispose();
        }
    }

    private sealed class Output(MidiOut output) : IMidiOutput
    {
        public void Send(byte[] message)
        {
            int raw = message[0] | (message.Length > 1 ? message[1] << 8 : 0) | (message.Length > 2 ? message[2] << 16 : 0);
            output.Send(raw);
        }

        public void Dispose() => output.Dispose();
    }
}
