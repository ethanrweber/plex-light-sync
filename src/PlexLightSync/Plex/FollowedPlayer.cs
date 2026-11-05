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
/// <para>
/// Only the poll loop calls <see cref="Follow"/>, so the remembered session needs no lock.
/// </para>
/// </remarks>
public sealed class FollowedPlayer
{
    readonly string _id;
    readonly IPAddress? _address;

    // the app and the item of the session that the last poll followed
    (string PlayerId, PlayingItem Item)? _followed;

    public FollowedPlayer(PlexOptions options)
    {
        _id = options.PlayerId?.Trim() ?? ""; // a key with a null value binds null
        _address = ParseAddress(options.PlayerAddress);
    }

    public bool Matches(PlexSession session) =>
        (_id != "" && session.PlayerId == _id)
        || (_address is not null && _address.Equals(session.PlayerAddress));

    /// <summary>
    /// Takes the sessions of one poll, and gives the session to follow, or null for none.
    /// </summary>
    /// <remarks>
    /// Two apps on the device can both have a session: an app in the background keeps its paused
    /// session, and Plex keeps the session of an app that closed for some minutes. So:
    /// <list type="bullet">
    /// <item>while the followed session plays, it stays.</item>
    /// <item>another session on the device takes over only when it plays.</item>
    /// <item>a paused followed session stays, so that a pause is a pause.</item>
    /// <item>when the followed session ends, that is a stop, even if another app has a paused session.</item>
    /// </list>
    /// </remarks>
    public PlexSession? Follow(IEnumerable<PlexSession> sessions)
    {
        var candidates = sessions.Where(session => session.IsSyncable && Matches(session)).ToList();
        var followed = candidates
            .Where(session => _followed is { } last && session.PlayerId == last.PlayerId && session.Item == last.Item)
            .OrderBy(session => session.State == PlayerState.Playing ? 0 : 1)
            .FirstOrDefault();

        var next = followed is { State: PlayerState.Playing }
            ? followed
            : candidates.FirstOrDefault(session => session.State == PlayerState.Playing) ?? followed;

        _followed = next is null ? null : (next.PlayerId, next.Item);
        return next;
    }

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

    /// <summary>
    /// True for an address in its usual form. <see cref="IPAddress.TryParse(string?, out IPAddress?)"/>
    /// also takes the short and octal forms of inet_aton ("192.168.1" is 192.168.0.1, "010" is 8). In a
    /// setting, those are almost always a typo, so an IPv4 address must read back as the same text.
    /// </summary>
    public static bool IsAddress(string text)
    {
        text = text.Trim();
        return ParseAddress(text) is { } address && (text.Contains(':') || address.ToString() == text);
    }
}
