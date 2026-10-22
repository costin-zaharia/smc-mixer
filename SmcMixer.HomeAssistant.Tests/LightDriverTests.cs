using Microsoft.Extensions.Logging.Abstractions;
using SmcMixer.HomeAssistant;

namespace SmcMixer.HomeAssistant.Tests;

public class LightDriverTests
{
    private const string Light = "light.test";

    private sealed class FakeHomeAssistant : IHomeAssistant
    {
        public List<(string Service, Dictionary<string, object> Data)> Calls { get; } = [];
        public LightInfo? Info { get; set; }
        public Exception? Failure { get; set; }

        public Task CallServiceAsync(string domain, string service, object data, CancellationToken ct)
        {
            lock (Calls)
            {
                if (Failure is not null) throw Failure;
                var dict = data is Dictionary<string, object> d
                    ? d
                    : data.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(data)!);
                Calls.Add(($"{domain}.{service}", dict));
            }
            return Task.CompletedTask;
        }

        public Task<LightInfo?> GetLightAsync(string entityId, CancellationToken ct) => Task.FromResult(Info);

        public List<(string Service, Dictionary<string, object> Data)> Snapshot()
        {
            lock (Calls) return [.. Calls];
        }
    }

    private static async Task<List<(string Service, Dictionary<string, object> Data)>> Run(
        FakeHomeAssistant ha, Action<LightDriver> act, int intervalMs = 20, int kelvinPerTick = 100, Func<LightDriver, Task>? init = null)
    {
        var driver = new LightDriver(Light, ha, intervalMs, kelvinPerTick, NullLogger.Instance);
        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);
        if (init is not null) await init(driver);

        act(driver);
        await Task.Delay(intervalMs * 6 + 100);

        cts.Cancel();
        await run;
        return ha.Snapshot();
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.019, 0)]
    [InlineData(0.02, 5)]
    [InlineData(0.5, 128)]
    [InlineData(1.0, 255)]
    public void FaderMapsToBrightness(double fader, int brightness) =>
        Assert.Equal(brightness, LightDriver.BrightnessFor(fader));

    [Fact]
    public async Task FaderMovesAreCoalescedToTheLatestValue()
    {
        var ha = new FakeHomeAssistant();
        var calls = await Run(ha, d =>
        {
            for (int i = 1; i <= 50; i++) d.SetBrightness(i / 100.0);
        });

        Assert.InRange(calls.Count, 1, 3);
        Assert.Equal("light.turn_on", calls[^1].Service);
        Assert.Equal(128, calls[^1].Data["brightness"]); // 0.50 * 255 rounded
        Assert.Equal(Light, calls[^1].Data["entity_id"]);
    }

    [Fact]
    public async Task BottomOfFaderTurnsTheLightOff()
    {
        var ha = new FakeHomeAssistant();
        var calls = await Run(ha, d => d.SetBrightness(0));

        var call = Assert.Single(calls);
        Assert.Equal("light.turn_off", call.Service);
    }

    [Fact]
    public async Task KnobStepsColorTemperatureFromDefault()
    {
        var ha = new FakeHomeAssistant();
        var calls = await Run(ha, d =>
        {
            d.StepColorTemperature(1);
            d.StepColorTemperature(1);
        });

        Assert.Equal(3200, calls[^1].Data["color_temp_kelvin"]); // 3000 default + 2 * 100
    }

    [Fact]
    public async Task KnobStartsFromTheLightsCurrentTemperature()
    {
        var ha = new FakeHomeAssistant { Info = new LightInfo(4000, 2202, 6535) };
        var calls = await Run(ha, d => d.StepColorTemperature(-3), init: d => d.InitAsync(CancellationToken.None));

        Assert.Equal(3700, calls[^1].Data["color_temp_kelvin"]);
    }

    [Fact]
    public async Task ColorTemperatureStaysWithinTheBulbsRange()
    {
        var ha = new FakeHomeAssistant { Info = new LightInfo(6500, 2202, 6535) };
        var up = await Run(ha, d => d.StepColorTemperature(50), init: d => d.InitAsync(CancellationToken.None));
        Assert.Equal(6535, up[^1].Data["color_temp_kelvin"]);

        var ha2 = new FakeHomeAssistant { Info = new LightInfo(2300, 2202, 6535) };
        var down = await Run(ha2, d => d.StepColorTemperature(-50), init: d => d.InitAsync(CancellationToken.None));
        Assert.Equal(2202, down[^1].Data["color_temp_kelvin"]);
    }

    [Fact]
    public async Task BrightnessAndTemperatureGoInOneCall()
    {
        var ha = new FakeHomeAssistant();
        var calls = await Run(ha, d =>
        {
            d.SetBrightness(1.0);
            d.StepColorTemperature(5);
        }, intervalMs: 200);

        var call = Assert.Single(calls);
        Assert.Equal(255, call.Data["brightness"]);
        Assert.Equal(3500, call.Data["color_temp_kelvin"]);
    }

    [Fact]
    public async Task ToggleCallsToggle()
    {
        var ha = new FakeHomeAssistant();
        var calls = await Run(ha, d => d.Toggle());

        var call = Assert.Single(calls);
        Assert.Equal("light.toggle", call.Service);
    }

    [Fact]
    public async Task FailedCallDoesNotStopTheDriver()
    {
        var ha = new FakeHomeAssistant { Failure = new HttpRequestException("unavailable") };
        var driver = new LightDriver(Light, ha, 20, 100, NullLogger.Instance);
        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);

        driver.Toggle();
        await Task.Delay(100);
        ha.Failure = null;
        driver.Toggle();
        await Task.Delay(150);

        cts.Cancel();
        await run;
        Assert.Single(ha.Snapshot());
    }

    [Fact]
    public async Task InitToleratesHomeAssistantBeingDown()
    {
        var ha = new FakeHomeAssistant { Info = null };
        var driver = new LightDriver(Light, ha, 20, 100, NullLogger.Instance);
        await driver.InitAsync(CancellationToken.None); // must not throw
    }
}
