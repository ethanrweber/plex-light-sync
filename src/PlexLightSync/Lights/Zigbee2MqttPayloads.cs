using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlexLightSync.Color;

namespace PlexLightSync.Lights;

/// <summary>The state of one bulb, as zigbee2mqtt reports it. Null values are unknown.</summary>
/// <param name="ColorMode">"color_temp" (white), "xy", or "hs".</param>
/// <param name="ColorTemp">The white color temperature, in mireds.</param>
public sealed record BulbState(
    bool IsOn,
    string? ColorMode = null,
    double? X = null,
    double? Y = null,
    double? Hue = null,
    double? Saturation = null,
    int? ColorTemp = null);

/// <summary>Reads and writes zigbee2mqtt's JSON messages.</summary>
internal static class Zigbee2MqttPayloads
{
    /// <summary>Maps each device's ieee address to its friendly name, from the bridge/devices message.</summary>
    public static Dictionary<string, string> ParseDeviceNames(string devicesJson)
    {
        var names = new Dictionary<string, string>();
        foreach (var device in JsonNode.Parse(devicesJson)?.AsArray() ?? [])
        {
            if (device?["ieee_address"]?.GetValue<string>() is { } ieee && device["friendly_name"]?.GetValue<string>() is { } name)
                names[ieee] = name;
        }
        return names;
    }

    /// <summary>
    /// The member addresses of a group, from the bridge/groups message. Null if the group does not exist.
    /// </summary>
    public static IReadOnlyList<string>? ParseGroupMembers(string groupsJson, string groupName)
    {
        var group = (JsonNode.Parse(groupsJson)?.AsArray() ?? [])
            .FirstOrDefault(group => group?["friendly_name"]?.GetValue<string>() == groupName);
        return group is null
            ? null
            : [.. (group["members"]?.AsArray() ?? []).Select(member => member?["ieee_address"]?.GetValue<string>()).OfType<string>()];
    }

    /// <summary>Reads a bulb's state message. A value that the message leaves out keeps its previous value.</summary>
    public static BulbState ParseBulbState(string json, BulbState? previous)
    {
        var state = JsonNode.Parse(json);
        var color = state?["color"];
        var last = previous ?? new BulbState(IsOn: false);
        return new BulbState(
            IsOn: state?["state"]?.GetValue<string>() is { } onOff ? onOff == "ON" : last.IsOn,
            ColorMode: state?["color_mode"]?.GetValue<string>() ?? last.ColorMode,
            X: Number(color?["x"]) ?? last.X,
            Y: Number(color?["y"]) ?? last.Y,
            Hue: Number(color?["hue"]) ?? last.Hue,
            Saturation: Number(color?["saturation"]) ?? last.Saturation,
            ColorTemp: Number(state?["color_temp"]) is { } mireds ? (int)mireds : last.ColorTemp);
    }

    /// <summary>The payload that asks zigbee2mqtt to read a bulb's state from the bulb.</summary>
    public const string StateRequest = """{"state":"","color":"","color_temp":"","color_mode":""}""";

    /// <summary>Sets a color and fades to it. Zigbee2mqtt takes saturation from 0 to 100.</summary>
    public static string Color(HsColor color, TimeSpan fade) => new JsonObject
    {
        ["color"] = new JsonObject
        {
            ["hue"] = Math.Round(color.Hue, 1),
            ["saturation"] = Math.Round(color.Saturation * 100, 1),
        },
        ["transition"] = Seconds(fade),
    }.ToJsonString();

    /// <summary>
    /// Puts a saved color back, in the saved color mode. Null if the saved state has no usable color.
    /// The payload has no "state", so it never turns a bulb on or off.
    /// </summary>
    public static string? Restore(BulbState saved, TimeSpan fade)
    {
        JsonObject? payload = saved switch
        {
            { ColorMode: "color_temp", ColorTemp: { } mireds } => new() { ["color_temp"] = mireds },
            { ColorMode: "xy", X: { } x, Y: { } y } => new() { ["color"] = new JsonObject { ["x"] = x, ["y"] = y } },
            { ColorMode: "hs", Hue: { } hue, Saturation: { } saturation } =>
                new() { ["color"] = new JsonObject { ["hue"] = hue, ["saturation"] = saturation } },
            _ => null,
        };
        if (payload is null)
            return null;
        payload["transition"] = Seconds(fade);
        return payload.ToJsonString();
    }

    static double Seconds(TimeSpan time) => Math.Round(time.TotalSeconds, 1);

    static double? Number(JsonNode? node) =>
        node?.GetValueKind() switch
        {
            JsonValueKind.Number => node.GetValue<double>(),
            JsonValueKind.String => double.TryParse(node.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null,
            _ => null,
        };
}
