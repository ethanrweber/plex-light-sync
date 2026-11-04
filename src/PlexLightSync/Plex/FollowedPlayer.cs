using System.Net;

namespace PlexLightSync.Plex;

/// <summary>
/// The player to follow: every app on one device, by the device's IP address, or one app, by its
/// machine id. A session that matches either setting counts.
/// </summary>
/// <remarks>
/// The address follows the device: each Plex app on it (the official app, Plezy, ...) has its own
/// machine id, and a reinstall can give an app a new one. The address stays the same if the router
/// gives the device a fixed address.
/// </remarks>
public sealed class FollowedPlayer
{
    readonly string _id;
    readonly IPAddress? _address;

    public FollowedPlayer(PlexOptions options)
    {
        _id = options.PlayerId?.Trim() ?? ""; // a key with a null value binds null
        _address = ParseAddress(options.PlayerAddress);
    }

    public bool Matches(PlexSession session) =>
        (_id != "" && session.PlayerId == _id)
        || (_address is not null && _address.Equals(session.PlayerAddress));

    /// <summary>
    /// The session to follow, or null. Two apps on the device can both have a session, for example
    /// when Plex still keeps the session of an app that closed: the one that plays wins.
    /// </summary>
    public PlexSession? Pick(IEnumerable<PlexSession> sessions) =>
        sessions
            .Where(session => session.IsSyncable && Matches(session))
            .OrderBy(session => session.State == PlayerState.Playing ? 0 : 1)
            .FirstOrDefault();

    public override string ToString() => (_address, _id) switch
    {
        ({ } address, "") => $"at {address}",
        (null, var id) => id,
        ({ } address, var id) => $"at {address} or {id}",
    };

    /// <summary>
    /// Reads an IP address. Plex can give an IPv4 address in its IPv6 form (::ffff:192.168.1.5); this
    /// gives the IPv4 form, so that the two compare as equal. Null if the text is not an address.
    /// </summary>
    public static IPAddress? ParseAddress(string? text) =>
        IPAddress.TryParse(text?.Trim(), out var address)
            ? address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address
            : null;
}
