using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;

namespace PlexLightSync.Plex;

/// <summary>
/// The Plex settings. The keys are flat snake_case. Configuration keys ignore case, so the same key
/// works in the home assistant app (<c>plex_url</c> in /data/options.json) and as an environment
/// variable (<c>PLEX_URL</c>). The display names put the key in validation errors.
/// </summary>
public sealed class PlexOptions : IValidatableObject
{
    /// <summary>The server address, for example https://192-168-1-2.0123abcd.plex.direct:32400.</summary>
    [ConfigurationKeyName("plex_url"), Display(Name = "plex_url"), Required, Url]
    public string Url { get; set; } = "";

    /// <summary>The token of the plex-light-sync device. Never log it.</summary>
    [ConfigurationKeyName("plex_token"), Display(Name = "plex_token"), Required]
    public string Token { get; set; } = "";

    /// <summary>The identifier that this service sends to Plex as its client id.</summary>
    [ConfigurationKeyName("plex_client_id"), Display(Name = "plex_client_id"), Required]
    public string ClientId { get; set; } = "";

    /// <summary>
    /// The IP address of the device to follow (the living room tv). Every Plex app on it counts. Set
    /// this, <see cref="PlayerId"/>, or both.
    /// </summary>
    [ConfigurationKeyName("plex_player_address"), Display(Name = "plex_player_address")]
    public string PlayerAddress { get; set; } = "";

    /// <summary>The machine identifier of one player app to follow. Set this, <see cref="PlayerAddress"/>, or both.</summary>
    [ConfigurationKeyName("plex_player_id"), Display(Name = "plex_player_id")]
    public string PlayerId { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(PlayerAddress) && string.IsNullOrWhiteSpace(PlayerId))
        {
            yield return new ValidationResult(
                "set plex_player_address (the IP address of the device to follow), plex_player_id, or both.",
                ["plex_player_address", "plex_player_id"]);
        }
        else if (!string.IsNullOrWhiteSpace(PlayerAddress) && FollowedPlayer.ParseAddress(PlayerAddress) is null)
        {
            yield return new ValidationResult(
                $"plex_player_address \"{PlayerAddress}\" is not an IP address.",
                ["plex_player_address"]);
        }
    }
}
