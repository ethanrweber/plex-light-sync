using System.Net.Http.Json;
using System.Text.Json;

namespace PlexLightSync.Plex;

/// <summary>Reads playback sessions and library items from the Plex server.</summary>
public sealed class PlexClient(HttpClient http)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Sets the server address and the Plex headers. The token goes in a header, not in the URL, so a
    /// logged URL never contains it.
    /// </summary>
    public static void Configure(HttpClient http, PlexOptions options)
    {
        http.BaseAddress = new Uri(options.Url);
        http.Timeout = TimeSpan.FromSeconds(5);
        http.DefaultRequestHeaders.Add("Accept", "application/json");
        http.DefaultRequestHeaders.Add("X-Plex-Product", "plex-light-sync");
        http.DefaultRequestHeaders.Add("X-Plex-Client-Identifier", options.ClientId);
        http.DefaultRequestHeaders.Add("X-Plex-Token", options.Token);
    }

    /// <summary>All playback sessions on the server, for every player.</summary>
    public async Task<IReadOnlyList<PlexSession>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        var response = await http.GetFromJsonAsync<PlexResponse>("/status/sessions", Json, cancellationToken);
        return [.. (response?.MediaContainer.Metadata ?? []).Select(PlexSession.From)];
    }

    /// <summary>
    /// The file of one part of a library item. The file comes from the library, not from the session:
    /// in a transcoded session, the session describes the transcode.
    /// </summary>
    public async Task<MediaFile> GetMediaFileAsync(PlayingItem item, CancellationToken cancellationToken)
    {
        var path = "/library/metadata/" + Uri.EscapeDataString(item.RatingKey);
        var response = await http.GetFromJsonAsync<PlexResponse>(path, Json, cancellationToken);
        var part = (response?.MediaContainer.Metadata ?? [])
            .SelectMany(metadata => metadata.Media ?? [])
            .SelectMany(media => media.Part ?? [])
            .FirstOrDefault(part => part.Id == item.PartId);

        if (part?.Key is null)
            throw new InvalidOperationException($"Plex item {item.RatingKey} has no part {item.PartId}.");

        var video = part.Stream?.FirstOrDefault(stream => stream.StreamType == PlexStream.Video);
        // smpte2084 is hdr10 (and the base layer of dolby vision); arib-std-b67 is hlg
        return new MediaFile(part.Key, IsHdr: video?.ColorTrc is "smpte2084" or "arib-std-b67");
    }
}
