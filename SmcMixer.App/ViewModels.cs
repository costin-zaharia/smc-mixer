using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using SmcMixer.Core;
using SmcMixer.Windows;

namespace SmcMixer.App;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name!);
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A button with an LED. Buttons without a controllable LED (arrows, « ») light only while held.</summary>
public sealed class ButtonVm : ViewModelBase
{
    private static readonly Color Panel = Color.FromRgb(0x26, 0x26, 0x2B);

    private readonly Brush _on;
    private readonly Brush _off;
    private readonly Effect _glow;
    private bool _isLit;

    public ButtonVm(ControlInfo info, string label)
    {
        Info = info;
        Label = label;

        var color = ToColor(info.LedColor);
        _on = Freeze(new SolidColorBrush(color));
        _off = Freeze(new SolidColorBrush(Blend(color, Panel, 0.14)));
        _glow = Freeze(new DropShadowEffect { Color = color, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 });
    }

    public ControlInfo Info { get; }
    public string Label { get; }

    public bool IsLit
    {
        get => _isLit;
        set
        {
            if (!Set(ref _isLit, value)) return;
            Raise(nameof(Fill));
            Raise(nameof(Glow));
            Raise(nameof(LabelBrush));
        }
    }

    public Brush Fill => IsLit ? _on : _off;
    public Effect? Glow => IsLit ? _glow : null;
    public Brush LabelBrush => IsLit ? Brushes.Black : Brushes.Gray;

    private static Color ToColor(LedColor? color) => color switch
    {
        LedColor.Red => Color.FromRgb(0xFF, 0x3B, 0x30),
        LedColor.Blue => Color.FromRgb(0x2F, 0x80, 0xFF),
        LedColor.Orange => Color.FromRgb(0xFF, 0x95, 0x00),
        LedColor.Green => Color.FromRgb(0x34, 0xC7, 0x59),
        _ => Color.FromRgb(0xF2, 0xF2, 0xF2),
    };

    private static Color Blend(Color a, Color b, double amount) => Color.FromRgb(
        (byte)(a.R * amount + b.R * (1 - amount)),
        (byte)(a.G * amount + b.G * (1 - amount)),
        (byte)(a.B * amount + b.B * (1 - amount)));

    private static T Freeze<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }
}

public sealed class StripVm : ViewModelBase
{
    private double _fader;
    private double _knobAngle;

    public StripVm(int index, IReadOnlyList<ButtonVm> buttons)
    {
        Index = index;
        Buttons = buttons;
    }

    public int Index { get; }

    /// <summary>Top to bottom, as on the device: Mute, Solo, Rec, Select.</summary>
    public IReadOnlyList<ButtonVm> Buttons { get; }

    /// <summary>0..1.</summary>
    public double Fader
    {
        get => _fader;
        set => Set(ref _fader, value);
    }

    /// <summary>Knobs are endless and relative, so this is just an accumulated rotation in degrees.</summary>
    public double KnobAngle
    {
        get => _knobAngle;
        set => Set(ref _knobAngle, value);
    }
}

public sealed class MixerVm : ViewModelBase, IDisposable
{
    private const double DegreesPerTick = 8;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _retry;
    private readonly Dictionary<int, ButtonVm> _byNote = new();
    private Mixer? _mixer;
    private string _status = "";

    public MixerVm(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        var info = new ControlMap().Buttons;
        ButtonVm Make(int note, string label)
        {
            var vm = new ButtonVm(info[note], label);
            _byNote[note] = vm;
            return vm;
        }

        Strips = Enumerable.Range(0, 8)
            .Select(i => new StripVm(i + 1, [Make(16 + i, "M"), Make(8 + i, "S"), Make(i, "R"), Make(24 + i, "□")]))
            .ToList();

        // Same order as the device's bottom row.
        Transport =
        [
            Make(94, "▶"), Make(93, "❚❚"), Make(95, "●"), Make(91, "◀◀"), Make(92, "▶▶"),
            Make(46, "«"), Make(47, "»"), Make(96, "△"), Make(97, "▽"), Make(98, "◁"), Make(99, "▷"),
        ];

        _retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _retry.Tick += (_, _) => TryConnect();
    }

    public IReadOnlyList<StripVm> Strips { get; }
    public IReadOnlyList<ButtonVm> Transport { get; }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public void Start() => TryConnect();

    private void TryConnect()
    {
        Mixer mixer;
        try
        {
            mixer = new Mixer(new WindowsMidiBackend());
        }
        catch (Exception ex)
        {
            Status = $"{ex.Message} Retrying...";
            _retry.Start();
            return;
        }

        _retry.Stop();
        _mixer = mixer;

        // The UI starts with every LED off, so make the device match.
        foreach (var button in _byNote.Values)
        {
            button.IsLit = false;
            mixer.SetLed(button.Info, false);
        }

        mixer.ControlChanged += (_, c) => _dispatcher.BeginInvoke(() => Handle(c));
        mixer.Start();
        Status = $"Connected: {mixer.InputName}" + (mixer.OutputName is null ? " (no output port, LEDs disabled)" : "");
    }

    private void Handle(ControlEvent c)
    {
        switch (c.Control.Kind)
        {
            case ControlKind.Fader:
                Strips[c.Control.Index - 1].Fader = c.Value;
                break;

            case ControlKind.Knob:
                Strips[c.Control.Index - 1].KnobAngle += c.Value * DegreesPerTick;
                break;

            case ControlKind.Button when c.Control.Note is { } note && _byNote.TryGetValue(note, out var button):
                if (!button.Info.HasLed)
                    button.IsLit = c.Value == 1; // the mixer lights these itself while held
                else if (c.Value == 1)
                {
                    button.IsLit = !button.IsLit; // LEDs latch: each press toggles
                    _mixer?.SetLed(button.Info, button.IsLit);
                }
                break;
        }
    }

    public void Dispose()
    {
        _retry.Stop();
        if (_mixer is null) return;

        foreach (var button in _byNote.Values)
            if (button.IsLit) _mixer.SetLed(button.Info, false);
        _mixer.Dispose();
        _mixer = null;
    }
}
