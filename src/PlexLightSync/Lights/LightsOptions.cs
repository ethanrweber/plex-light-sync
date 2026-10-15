using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;

namespace PlexLightSync.Lights;

/// <summary>The mqtt broker. In the home assistant app, these will come from the supervisor.</summary>
public sealed class MqttOptions
{
    [ConfigurationKeyName("mqtt_host"), Display(Name = "mqtt_host"), Required]
    public string Host { get; set; } = "";

    [ConfigurationKeyName("mqtt_port"), Display(Name = "mqtt_port"), Range(1, 65535)]
    public int Port { get; set; } = 1883;

    [ConfigurationKeyName("mqtt_user"), Display(Name = "mqtt_user")]
    public string? User { get; set; }

    /// <summary>Never log it.</summary>
    [ConfigurationKeyName("mqtt_password"), Display(Name = "mqtt_password")]
    public string? Password { get; set; }
}

/// <summary>The lights, as zigbee2mqtt knows them.</summary>
public sealed class LightOptions
{
    /// <summary>The friendly name of the zigbee2mqtt group of the lights.</summary>
    [ConfigurationKeyName("zigbee2mqtt_group"), Display(Name = "zigbee2mqtt_group"), Required]
    public string Group { get; set; } = "";

    /// <summary>The base topic of zigbee2mqtt. Its default is "zigbee2mqtt".</summary>
    [ConfigurationKeyName("zigbee2mqtt_topic"), Display(Name = "zigbee2mqtt_topic"), Required]
    public string BaseTopic { get; set; } = "zigbee2mqtt";
}
