using Microsoft.Extensions.Options;
using SmcMixer.Core;

namespace SmcMixer.HomeAssistant;

/// <summary>Connects to the mixer (and reconnects when it goes away and comes back) and routes its controls to lights.</summary>
public sealed class MixerWorker(IMidiBackend backend, IHomeAssistant ha, IOptions<HomeAssistantOptions> options, ILogger<MixerWorker> log)
    : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PresenceCheckInterval = TimeSpan.FromSeconds(2);

    // So the log says "waiting" once per absence instead of every retry.
    private bool _waitingLogged;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (o.Mappings.Count == 0)
        {
            log.LogError("No mappings configured (HomeAssistant:Mappings).");
            return;
        }

        var drivers = o.Mappings
            .Select(m => m.Entity)
            .Distinct()
            .ToDictionary(e => e, e => new LightDriver(e, ha, o.MinCallIntervalMs, o.KelvinPerTick, log));
        var byControl = o.Mappings.ToLookup(m => m.Control, StringComparer.OrdinalIgnoreCase);

        var senders = drivers.Values.Select(d => d.RunAsync(ct)).ToList();
        await Task.WhenAll(drivers.Values.Select(d => d.InitAsync(ct)));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunMixerAsync(drivers, byControl, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await Task.WhenAll(senders);
    }

    /// <summary>Waits for the mixer, handles its events until it disappears, then returns so the caller can wait for it again.</summary>
    private async Task RunMixerAsync(Dictionary<string, LightDriver> drivers, ILookup<string, MappingOptions> byControl, CancellationToken ct)
    {
        Mixer mixer;
        try
        {
            mixer = new Mixer(backend);
        }
        catch (InvalidOperationException)
        {
            if (!_waitingLogged)
                log.LogInformation("Waiting for the SMC-Mixer to connect");
            _waitingLogged = true;
            await Task.Delay(RetryDelay, ct);
            return;
        }

        try
        {
            _waitingLogged = false;
            log.LogInformation("Connected to {Input}", mixer.InputName);
            mixer.ControlChanged += (_, c) =>
            {
                foreach (var m in byControl[c.Control.Name])
                    Apply(drivers[m.Entity], m.Action, c);
            };
            mixer.Start();

            // The mixer raises nothing when it goes away (e.g. Bluetooth drops), so notice by its port disappearing.
            while (Mixer.FindInput(backend) is not null)
                await Task.Delay(PresenceCheckInterval, ct);

            log.LogWarning("Mixer disconnected, waiting for it to come back");
        }
        finally
        {
            try
            {
                mixer.Dispose();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogDebug("Closing the mixer port failed: {Message}", ex.Message);
            }
        }
    }

    private static void Apply(LightDriver light, MappingAction action, ControlEvent c)
    {
        switch (action)
        {
            case MappingAction.Brightness when c.Control.Kind == ControlKind.Fader:
                light.SetBrightness(c.Value);
                break;
            case MappingAction.ColorTemperature when c.Control.Kind == ControlKind.Knob:
                light.StepColorTemperature((int)c.Value);
                break;
            case MappingAction.Toggle when c.Control.Kind == ControlKind.Button && c.Value == 1:
                light.Toggle();
                break;
        }
    }
}
