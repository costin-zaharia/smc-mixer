using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SmcMixer.HomeAssistant;

/// <summary>
/// Drives one light. Mixer events arrive faster than Home Assistant (and a bulb) can follow, so they only record the
/// wanted state; <see cref="RunAsync"/> sends the latest wanted state, at most once per interval.
/// </summary>
public sealed class LightDriver
{
    // The bottom of a fader's travel is "off". The mixer's faders step by under 1%, so leave a little room.
    private const double OffBelow = 0.02;

    private const int DefaultMinKelvin = 2202;
    private const int DefaultMaxKelvin = 6535;
    private const int DefaultKelvin = 3000;

    private readonly string _entity;
    private readonly IHomeAssistant _ha;
    private readonly TimeSpan _interval;
    private readonly int _kelvinPerTick;
    private readonly ILogger _log;
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly object _lock = new();

    private int _minKelvin = DefaultMinKelvin;
    private int _maxKelvin = DefaultMaxKelvin;
    private int _kelvin = DefaultKelvin;

    private int? _wantBrightness;   // 0 = off, 1..255 = on at that brightness
    private bool _wantKelvin;
    private bool _wantToggle;

    public LightDriver(string entity, IHomeAssistant ha, int minCallIntervalMs, int kelvinPerTick, ILogger log)
    {
        _entity = entity;
        _ha = ha;
        _interval = TimeSpan.FromMilliseconds(minCallIntervalMs);
        _kelvinPerTick = kelvinPerTick;
        _log = log;
    }

    public string Entity => _entity;

    /// <summary>Fader position 0..1. The bottom switches the light off; above it, brightness is 1..255.</summary>
    public static int BrightnessFor(double fader) =>
        fader < OffBelow ? 0 : Math.Clamp((int)Math.Round(fader * 255), 1, 255);

    public void SetBrightness(double fader)
    {
        lock (_lock) _wantBrightness = BrightnessFor(fader);
        _wake.Writer.TryWrite(true);
    }

    public void StepColorTemperature(int ticks)
    {
        lock (_lock)
        {
            _kelvin = Math.Clamp(_kelvin + ticks * _kelvinPerTick, _minKelvin, _maxKelvin);
            _wantKelvin = true;
        }
        _wake.Writer.TryWrite(true);
    }

    public void Toggle()
    {
        lock (_lock) _wantToggle = true;
        _wake.Writer.TryWrite(true);
    }

    /// <summary>Starts the knob from the light's current color temperature, if Home Assistant can tell us. Failure is fine.</summary>
    public async Task InitAsync(CancellationToken ct)
    {
        try
        {
            if (await _ha.GetLightAsync(_entity, ct) is not { } info) return;
            lock (_lock)
            {
                _minKelvin = info.MinKelvin ?? _minKelvin;
                _maxKelvin = info.MaxKelvin ?? _maxKelvin;
                _kelvin = Math.Clamp(info.ColorTempKelvin ?? _kelvin, _minKelvin, _maxKelvin);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("Could not read {Entity} from Home Assistant: {Message}", _entity, ex.Message);
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in _wake.Reader.ReadAllAsync(ct))
            {
                if (await SendPendingAsync(ct))
                    await Task.Delay(_interval, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Sends whatever is wanted now. Returns false if there was nothing to send.</summary>
    private async Task<bool> SendPendingAsync(CancellationToken ct)
    {
        int? brightness;
        int? kelvin;
        bool toggle;
        lock (_lock)
        {
            brightness = _wantBrightness;
            kelvin = _wantKelvin ? _kelvin : null;
            toggle = _wantToggle;
            _wantBrightness = null;
            _wantKelvin = false;
            _wantToggle = false;
        }

        try
        {
            if (toggle)
                await _ha.CallServiceAsync("light", "toggle", new { entity_id = _entity }, ct);
            else if (brightness == 0)
                await _ha.CallServiceAsync("light", "turn_off", new { entity_id = _entity }, ct);
            else if (brightness is not null || kelvin is not null)
                await _ha.CallServiceAsync("light", "turn_on", TurnOnData(brightness, kelvin), ct);
            else
                return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unavailable bulb or a Home Assistant restart must not take the worker down.
            _log.LogWarning("Home Assistant call for {Entity} failed: {Message}", _entity, ex.Message);
        }

        return true;
    }

    private Dictionary<string, object> TurnOnData(int? brightness, int? kelvin)
    {
        var data = new Dictionary<string, object> { ["entity_id"] = _entity };
        if (brightness is { } b) data["brightness"] = b;
        if (kelvin is { } k) data["color_temp_kelvin"] = k;
        return data;
    }
}
