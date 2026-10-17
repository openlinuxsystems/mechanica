using System.Text;
using System.Text.Json;
using Mechanica.Core.Actions;

namespace Mechanica.Core.Net;

/// <summary>What a host on the LAN says about its session.</summary>
/// <param name="World">The world's name.</param>
/// <param name="Host">The host player's name.</param>
/// <param name="Port">The ENet port to join.</param>
/// <param name="Players">Players in the session, host included.</param>
/// <param name="MaxPlayers">Players allowed, host included.</param>
/// <param name="Mode">Game mode, <c>survival</c> or <c>creative</c>.</param>
/// <param name="Protocol">The host's <see cref="NetProtocol.Version"/>.</param>
/// <param name="Mods">The host's mods, <c>id version</c> each, in load order.</param>
public sealed record LanSession(string World, string Host, int Port, int Players, int MaxPlayers, string Mode, int Protocol, IReadOnlyList<string> Mods)
{
    /// <summary>True when this build can join it (same protocol version).</summary>
    public bool Compatible => Protocol == NetProtocol.Version;

    /// <summary>True when there is room for one more player.</summary>
    public bool HasRoom => Players < MaxPlayers;
}

/// <summary>A session found on the LAN, and where it answered from.</summary>
/// <param name="Address">The host's IP address.</param>
/// <param name="Session">What it said.</param>
/// <param name="SeenAt">When it last answered, in the browser's clock (seconds).</param>
public sealed record LanEntry(string Address, LanSession Session, double SeenAt)
{
    /// <summary>The address to join.</summary>
    public NetEndpoint Endpoint => new(Address, Session.Port);
}

/// <summary>
/// LAN discovery over UDP: a browser broadcasts a small query to <see cref="NetProtocol.LanPort"/>, and every
/// host listening there answers the sender directly with a <see cref="LanSession"/> as JSON. Hosts never send
/// unasked, and both packets are tiny, so nothing floods the network. This class is the wire format and the
/// browser's list; the game owns the sockets.
/// </summary>
public static class LanDiscovery
{
    /// <summary>Largest packet either side sends or accepts.</summary>
    public const int MaxPacketBytes = 2048;

    /// <summary>Seconds a browser keeps showing a host that stopped answering.</summary>
    public const double EntryLifetime = 4.0;

    /// <summary>Seconds between a browser's queries.</summary>
    public const double QueryInterval = 1.0;

    private static readonly byte[] QueryBytes = Encoding.UTF8.GetBytes($"{MechanicaInfo.GameName}?lan");

    /// <summary>The query a browser broadcasts.</summary>
    public static byte[] Query => (byte[])QueryBytes.Clone();

    /// <summary>True when <paramref name="packet"/> is a browser's query.</summary>
    public static bool IsQuery(ReadOnlySpan<byte> packet) => packet.SequenceEqual(QueryBytes);

    /// <summary>A host's answer.</summary>
    public static byte[] Encode(LanSession session) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Answer(MechanicaInfo.GameName, session), ActionJson.Options));

    /// <summary>Reads a host's answer; null when it is not one, is too large or makes no sense.</summary>
    public static LanSession? Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.Length is 0 or > MaxPacketBytes || packet[0] != (byte)'{')
            return null;
        try
        {
            var answer = JsonSerializer.Deserialize<Answer>(packet, ActionJson.Options);
            if (answer?.Game != MechanicaInfo.GameName || answer.Session is not { } s)
                return null;
            if (s.World is null || s.Host is null || s.Mode is null || s.Mods is null || s.Port is < 1 or > 65535
                || s.Players < 0 || s.MaxPlayers is < 1 or > NetProtocol.MaxPlayersLimit || s.Mods.Count > ContentManifest.MaxMods)
                return null;
            return s with { World = Clip(s.World), Host = Clip(s.Host), Mode = Clip(s.Mode) };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Clip(string text) => text.Length > 48 ? text[..48] : text;

    private sealed record Answer(string? Game, LanSession? Session);
}

/// <summary>The sessions a browser has heard from, dropping hosts that stop answering.</summary>
public sealed class LanBrowserList
{
    private readonly Dictionary<NetEndpoint, LanEntry> _entries = [];

    /// <summary>Sessions heard within <see cref="LanDiscovery.EntryLifetime"/>, by world then address.</summary>
    public IReadOnlyList<LanEntry> Entries => [.. _entries.Values.OrderBy(e => e.Session.World, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Address, StringComparer.Ordinal).ThenBy(e => e.Session.Port)];

    /// <summary>Records an answer from <paramref name="address"/> at <paramref name="now"/>. False when it is not one.</summary>
    public bool Heard(string address, ReadOnlySpan<byte> packet, double now)
    {
        if (LanDiscovery.Decode(packet) is not { } session)
            return false;
        // The same host can answer on loopback and its LAN address; keep one, the LAN one.
        var endpoint = new NetEndpoint(address, session.Port);
        if (LaunchOptions.IsLoopback(address) && _entries.Values.Any(e => !LaunchOptions.IsLoopback(e.Address) && Same(e.Session, session)))
            return true;
        foreach (var loop in _entries.Where(e => LaunchOptions.IsLoopback(e.Key.Address) && !LaunchOptions.IsLoopback(address) && Same(e.Value.Session, session)).Select(e => e.Key).ToList())
            _entries.Remove(loop);
        _entries[endpoint] = new LanEntry(address, session, now);
        return true;
    }

    private static bool Same(LanSession a, LanSession b) => a.World == b.World && a.Host == b.Host && a.Port == b.Port;

    /// <summary>Drops hosts not heard from since <paramref name="now"/> minus <see cref="LanDiscovery.EntryLifetime"/>.</summary>
    public void Expire(double now)
    {
        foreach (var stale in _entries.Where(e => now - e.Value.SeenAt > LanDiscovery.EntryLifetime).Select(e => e.Key).ToList())
            _entries.Remove(stale);
    }

    /// <summary>Forgets every host.</summary>
    public void Clear() => _entries.Clear();
}
