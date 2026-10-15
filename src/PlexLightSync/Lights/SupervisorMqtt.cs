using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace PlexLightSync.Lights;

/// <summary>
/// In the home assistant app, the mqtt login comes from the supervisor. The app's config.yaml declares
/// "services: mqtt:need", and the supervisor gives the app a token in SUPERVISOR_TOKEN.
/// </summary>
public static class SupervisorMqtt
{
    const string Url = "http://supervisor/services/mqtt";
    const int Attempts = 5;

    /// <summary>
    /// Gets the login as the service's own mqtt settings. It tries again a few times, because right
    /// after a boot the supervisor can be slow to answer.
    /// </summary>
    public static async Task<Dictionary<string, string?>> LoadAsync(string token, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return Parse(await http.GetStringAsync(Url, cancellationToken));
            }
            catch (HttpRequestException) when (attempt < Attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }

    /// <summary>Turns the supervisor's reply into the keys mqtt_host, mqtt_port, mqtt_user, and mqtt_password.</summary>
    internal static Dictionary<string, string?> Parse(string json)
    {
        var data = JsonNode.Parse(json)?["data"]
            ?? throw new InvalidOperationException("the supervisor's reply has no mqtt data");
        if (data["ssl"]?.GetValue<bool>() == true)
            throw new InvalidOperationException("the mqtt broker requires ssl, which this service does not support");

        return new Dictionary<string, string?>
        {
            ["mqtt_host"] = data["host"]?.GetValue<string>(),
            ["mqtt_port"] = data["port"]?.GetValue<int>().ToString(CultureInfo.InvariantCulture),
            ["mqtt_user"] = data["username"]?.GetValue<string>(),
            ["mqtt_password"] = data["password"]?.GetValue<string>(),
        };
    }
}
