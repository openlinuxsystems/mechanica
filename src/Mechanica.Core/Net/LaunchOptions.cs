using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Mechanica.Core.Net;

/// <summary>How a game instance starts its session.</summary>
public enum SessionMode
{
    /// <summary>Single player: this instance is the host with no socket open (D10).</summary>
    Offline,

    /// <summary>Host a session that clients can join.</summary>
    Host,

    /// <summary>Join a session at <see cref="LaunchOptions.Endpoint"/>.</summary>
    Join,
}

/// <summary>
/// Session settings from the command line (the user arguments after <c>--</c>):
/// <c>--host [port]</c>, <c>--join address[:port]</c>, <c>--max-players n</c> and <c>--seed n</c>.
/// </summary>
public sealed record LaunchOptions
{
    /// <summary>Single player with defaults.</summary>
    public static LaunchOptions Offline { get; } = new();

    /// <summary>How to start.</summary>
    public SessionMode Mode { get; init; } = SessionMode.Offline;

    /// <summary>Port to listen on when hosting.</summary>
    public int Port { get; init; } = NetProtocol.DefaultPort;

    /// <summary>Where to connect when joining.</summary>
    public NetEndpoint? Endpoint { get; init; }

    /// <summary>Players allowed when hosting, host included.</summary>
    public int MaxPlayers { get; init; } = NetProtocol.DefaultMaxPlayers;

    /// <summary>World seed when this instance generates the world (offline or hosting). Null picks a random one; a joining client always uses the host's.</summary>
    public long? Seed { get; init; }

    /// <summary>Options for hosting on <paramref name="port"/>.</summary>
    public static LaunchOptions ForHost(int port, int maxPlayers = NetProtocol.DefaultMaxPlayers) =>
        new() { Mode = SessionMode.Host, Port = port, MaxPlayers = maxPlayers };

    /// <summary>Options for joining <paramref name="endpoint"/>.</summary>
    public static LaunchOptions ForJoin(NetEndpoint endpoint) => new() { Mode = SessionMode.Join, Endpoint = endpoint };

    /// <summary>Parses user command-line arguments. Unrelated arguments are ignored.</summary>
    public static bool TryParse(IReadOnlyList<string> args, [NotNullWhen(true)] out LaunchOptions? options, out string error)
    {
        options = null;
        error = "";
        var result = Offline;
        for (var i = 0; i < args.Count; i++)
        {
            var next = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
            switch (args[i])
            {
                case "--host":
                    if (result.Mode == SessionMode.Join)
                        return Fail("--host and --join can't be combined.", out error);
                    var port = NetProtocol.DefaultPort;
                    if (next is not null)
                    {
                        if (!NetEndpoint.TryParsePort(next, out port))
                            return Fail($"'{next}' is not a valid port (1-65535).", out error);
                        i++;
                    }
                    result = result with { Mode = SessionMode.Host, Port = port };
                    break;
                case "--join":
                    if (result.Mode == SessionMode.Host)
                        return Fail("--host and --join can't be combined.", out error);
                    if (next is null)
                        return Fail("--join needs an address, for example --join 192.168.1.20:27460.", out error);
                    if (!NetEndpoint.TryParse(next, NetProtocol.DefaultPort, out var endpoint, out error))
                        return false;
                    result = result with { Mode = SessionMode.Join, Endpoint = endpoint };
                    i++;
                    break;
                case "--max-players":
                    if (next is null || !int.TryParse(next, NumberStyles.None, CultureInfo.InvariantCulture, out var max)
                        || max is < 1 or > NetProtocol.MaxPlayersLimit)
                        return Fail($"--max-players needs a number from 1 to {NetProtocol.MaxPlayersLimit}.", out error);
                    result = result with { MaxPlayers = max };
                    i++;
                    break;
                case "--seed":
                    if (next is null || !long.TryParse(next, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seed))
                        return Fail("--seed needs a whole number.", out error);
                    result = result with { Seed = seed };
                    i++;
                    break;
            }
        }

        options = result;
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
