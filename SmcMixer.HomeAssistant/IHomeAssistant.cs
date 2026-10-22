namespace SmcMixer.HomeAssistant;

/// <summary>What the worker needs to know about a light up front. Any value can be missing, e.g. while the bulb is unavailable.</summary>
public sealed record LightInfo(int? ColorTempKelvin, int? MinKelvin, int? MaxKelvin);

public interface IHomeAssistant
{
    Task CallServiceAsync(string domain, string service, object data, CancellationToken ct);

    Task<LightInfo?> GetLightAsync(string entityId, CancellationToken ct);
}
