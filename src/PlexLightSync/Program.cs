using Microsoft.Extensions.Options;
using PlexLightSync;
using PlexLightSync.Color;
using PlexLightSync.Lights;
using PlexLightSync.Plex;
using PlexLightSync.Sync;
using PlexLightSync.Video;

// "probe <ratingKey> ..." runs the developer tool instead of the service
var probe = args is ["probe", ..];

var builder = Host.CreateApplicationBuilder(probe ? [] : args);

// in the home assistant app, the settings form arrives as /data/options.json. environment variables
// come after it, so they override it.
builder.Configuration
    .AddJsonFile("/data/options.json", optional: true)
    .AddEnvironmentVariables();

builder.Logging.AddSimpleConsole(console =>
{
    console.SingleLine = true;
    console.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOptions<PlexOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<ColorOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<PlexClient>((services, http) =>
    PlexClient.Configure(http, services.GetRequiredService<IOptions<PlexOptions>>().Value));

builder.Services.AddOptions<MqttOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<LightOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SyncOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddTransient<IMediaFileLookup>(services => services.GetRequiredService<PlexClient>());
builder.Services.AddSingleton(services => new PlaybackTracker(
    services.GetRequiredService<TimeProvider>(), PlaybackMonitor.SeekThreshold, reportDelay: PlaybackMonitor.PollInterval / 2));
builder.Services.AddSingleton<FrameSource>();
builder.Services.AddSingleton<IFrameSource>(services => services.GetRequiredService<FrameSource>());
builder.Services.AddSingleton<ColorPicker>();
builder.Services.AddSingleton<MqttBus>();
builder.Services.AddSingleton<IMqttBus>(services => services.GetRequiredService<MqttBus>());
builder.Services.AddSingleton<HomeAssistantEntities>();
builder.Services.AddSingleton<Zigbee2MqttLights>();
builder.Services.AddSingleton<ILights>(services => services.GetRequiredService<Zigbee2MqttLights>());
builder.Services.AddSingleton<SavedColors>();
builder.Services.AddSingleton<SyncInbox>();

if (probe)
{
    builder.Services.AddTransient<Probe>();
}
else
{
    // hosted services start in this order. the mqtt parts register their subscriptions before the
    // bus connects, so the first connect subscribes to all of them.
    builder.Services.AddHostedService(services => services.GetRequiredService<HomeAssistantEntities>());
    builder.Services.AddHostedService(services => services.GetRequiredService<Zigbee2MqttLights>());
    builder.Services.AddHostedService<SyncEngine>();
    builder.Services.AddHostedService(services => services.GetRequiredService<MqttBus>());
    builder.Services.AddHostedService<PlaybackMonitor>();
}

try
{
    using var host = builder.Build();
    if (probe)
        return await host.Services.GetRequiredService<Probe>().RunAsync(args[1..], CancellationToken.None);

    await host.RunAsync();
    return 0;
}
catch (OptionsValidationException ex)
{
    // a missing or wrong setting: say which one, and exit without a crash
    Console.Error.WriteLine("settings error: " + string.Join(" ", ex.Failures));
    return 1;
}
