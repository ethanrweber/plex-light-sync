using Microsoft.Extensions.Options;
using PlexLightSync.Plex;

var builder = Host.CreateApplicationBuilder(args);

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

builder.Services.AddHttpClient<PlexClient>((services, http) =>
    PlexClient.Configure(http, services.GetRequiredService<IOptions<PlexOptions>>().Value));

builder.Services.AddHostedService<PlaybackMonitor>();

try
{
    builder.Build().Run();
    return 0;
}
catch (OptionsValidationException ex)
{
    // a missing or wrong setting: say which one, and exit without a crash
    Console.Error.WriteLine("settings error: " + string.Join(" ", ex.Failures));
    return 1;
}
