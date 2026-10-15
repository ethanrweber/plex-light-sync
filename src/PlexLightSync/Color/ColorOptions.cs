using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;

namespace PlexLightSync.Color;

/// <summary>Settings for the color picker. Brightness and saturation values go from 0 to 1.</summary>
public sealed class ColorOptions
{
    /// <summary>A frame whose bright pixels stay below this level counts as black.</summary>
    [ConfigurationKeyName("color_black_level"), Display(Name = "color_black_level"), Range(0.0, 1.0)]
    public double BlackLevel { get; set; } = 0.06;

    /// <summary>Pixels with less saturation than this count as gray, and do not vote.</summary>
    [ConfigurationKeyName("color_min_saturation"), Display(Name = "color_min_saturation"), Range(0.0, 1.0)]
    public double MinSaturation { get; set; } = 0.2;

    /// <summary>Multiplies the saturation of the picked color. 1 keeps it as it is.</summary>
    [ConfigurationKeyName("color_saturation_boost"), Display(Name = "color_saturation_boost"), Range(1.0, 5.0)]
    public double SaturationBoost { get; set; } = 1.5;
}
