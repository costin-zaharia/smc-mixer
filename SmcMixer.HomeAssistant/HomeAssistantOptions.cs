namespace SmcMixer.HomeAssistant;

public enum MappingAction
{
    /// <summary>A fader sets the light's brightness; the bottom of its travel switches the light off.</summary>
    Brightness,

    /// <summary>A knob steps the light's color temperature, <see cref="HomeAssistantOptions.KelvinPerTick"/> per tick.</summary>
    ColorTemperature,

    /// <summary>A button press toggles the light on and off.</summary>
    Toggle,
}

/// <summary>Ties one mixer control, by the name <c>Mixer</c> gives it ("Fader 1", "Knob 2", "Mute 1", "Play", ...), to a light.</summary>
public sealed class MappingOptions
{
    public string Control { get; set; } = "";
    public MappingAction Action { get; set; }
    public string Entity { get; set; } = "";
}

public sealed class HomeAssistantOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8123";

    /// <summary>File holding a long-lived access token. A leading "~" is the home directory.</summary>
    public string TokenFile { get; set; } = "~/.config/smc-ha/token";

    /// <summary>Faders and knobs send many messages a second; each light gets at most one Home Assistant call per this interval.</summary>
    public int MinCallIntervalMs { get; set; } = 100;

    public int KelvinPerTick { get; set; } = 100;

    public List<MappingOptions> Mappings { get; set; } = [];
}
