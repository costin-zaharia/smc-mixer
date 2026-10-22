using SmcMixer.Core;
using SmcMixer.HomeAssistant;
using SmcMixer.Linux;

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("This worker only runs on Linux.");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<HomeAssistantOptions>(builder.Configuration.GetSection("HomeAssistant"));
builder.Services.AddSingleton<IMidiBackend, LinuxMidiBackend>();
builder.Services.AddSingleton<IHomeAssistant, HomeAssistantClient>();
builder.Services.AddHostedService<MixerWorker>();

builder.Build().Run();
return 0;
