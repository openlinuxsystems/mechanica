using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Mechanica.Core.Remote;

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
/// <c>--host [port]</c>, <c>--join address[:port]</c>, <c>--max-players n</c>, <c>--seed n</c>, <c>--creative</c> or <c>--survival</c> and <c>--easy</c>; on the host also the
/// external AI endpoint (<c>--ai [port]</c>, <c>--ai-bind address</c>, <c>--ai-token token</c>, <c>--ai-admin-token token</c>, <c>--ai-observe-token token</c>,
/// <c>--ai-allow-reload-with-clients</c>) and
/// record/replay of Action API calls (<c>--record file</c>, <c>--replay file</c>) and a saved world to load
/// (<c>--load file</c>); on any instance the player's name (<c>--name name</c>) and the world's password (<c>--password text</c>).
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

    /// <summary>Port of the external AI endpoint (WebSocket JSON-RPC), or null to leave it closed.</summary>
    public int? AiPort { get; init; }

    /// <summary>Address the AI endpoint listens on. Loopback unless the host opts in, since it can do anything the host can.</summary>
    public string AiBind { get; init; } = "127.0.0.1";

    /// <summary>
    /// Token agents must give before calling anything. Required when <see cref="AiBind"/> is not loopback;
    /// the game also reads it from the <c>MECHANICA_AI_TOKEN</c> environment variable.
    /// </summary>
    public string? AiToken { get; init; }

    /// <summary>
    /// Token that grants the AI endpoint's admin scope (debug actions, session control, mod tools). When it is
    /// set, <see cref="AiToken"/> grants only observe and play. Also read from <c>MECHANICA_AI_ADMIN_TOKEN</c>.
    /// </summary>
    public string? AiAdminToken { get; init; }

    /// <summary>Token that grants only the observe scope (queries and events). Also read from <c>MECHANICA_AI_OBSERVE_TOKEN</c>.</summary>
    public string? AiObserveToken { get; init; }

    /// <summary>True when any AI endpoint token is set here or in <paramref name="environment"/>.</summary>
    public bool HasAiToken(Func<string, string?> environment) => AiAccess(environment).RequiresToken;

    /// <summary>
    /// The AI endpoint's tokens and the scopes each grants, from the options and then the environment. With
    /// only <see cref="AiToken"/> it grants every scope (as before scopes existed); with an admin token too,
    /// it grants observe and play and the admin token grants everything.
    /// </summary>
    public EndpointAccess AiAccess(Func<string, string?> environment)
    {
        static string? Set(string? value) => string.IsNullOrEmpty(value) ? null : value;
        var play = Set(AiToken) ?? Set(environment("MECHANICA_AI_TOKEN"));
        var admin = Set(AiAdminToken) ?? Set(environment("MECHANICA_AI_ADMIN_TOKEN"));
        var observe = Set(AiObserveToken) ?? Set(environment("MECHANICA_AI_OBSERVE_TOKEN"));
        var access = new EndpointAccess();
        if (admin is null)
            access.AddToken(play, [.. EndpointAccess.All]);
        else
            access.AddToken(play, AgentScope.Observe, AgentScope.Play).AddToken(admin, [.. EndpointAccess.All]);
        access.AddToken(observe, AgentScope.Observe);
        return access;
    }

    /// <summary>
    /// Whether the AI endpoint's <c>mods.reload</c> may run while clients are joined (<c>--ai-allow-reload-with-clients</c>);
    /// clients whose mods then differ are dropped. Off by default, so a reload never desyncs a client.
    /// </summary>
    public bool AiAllowReloadWithClients { get; init; }

    /// <summary>File to write the host's Action API calls to when the game quits, or null.</summary>
    public string? RecordPath { get; init; }

    /// <summary>Recording to replay on the host once the session is up, or null.</summary>
    public string? ReplayPath { get; init; }

    /// <summary>How the world plays when this instance hosts it (<c>--creative</c>, <c>--survival</c>). A joining client plays the host's mode.</summary>
    public Survival.GameMode GameMode { get; init; } = Survival.GameMode.Survival;

    /// <summary>How hard a new Survival world is (<c>--easy</c>); a loaded world keeps its own.</summary>
    public Survival.Difficulty Difficulty { get; init; } = Survival.Difficulty.Normal;

    /// <summary>A save file to load on the host instead of a new world (<c>--load file</c>), or null.</summary>
    public string? LoadPath { get; init; }

    /// <summary>
    /// The world's password (<c>--password</c>): when hosting, what joining players must give (null uses the
    /// saved Server password); when joining, what this player sends.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>The player's display name for this run (<c>--name</c>), or null for the saved profile's.</summary>
    public string? PlayerName { get; init; }

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
                case "--ai":
                    var aiPort = NetProtocol.DefaultAiPort;
                    if (next is not null)
                    {
                        if (!NetEndpoint.TryParsePort(next, out aiPort))
                            return Fail($"'{next}' is not a valid AI endpoint port (1-65535).", out error);
                        i++;
                    }
                    result = result with { AiPort = aiPort };
                    break;
                case "--ai-bind":
                case "--ai-token":
                case "--ai-admin-token":
                case "--ai-observe-token":
                case "--record":
                case "--replay":
                case "--load":
                case "--name":
                case "--password":
                    if (next is null)
                        return Fail($"{args[i]} needs a value.", out error);
                    result = args[i] switch
                    {
                        "--ai-bind" => result with { AiBind = next },
                        "--ai-token" => result with { AiToken = next },
                        "--ai-admin-token" => result with { AiAdminToken = next },
                        "--ai-observe-token" => result with { AiObserveToken = next },
                        "--record" => result with { RecordPath = next },
                        "--load" => result with { LoadPath = next },
                        "--name" => result with { PlayerName = next },
                        "--password" => result with { Password = NetPassword.Clamp(next) },
                        _ => result with { ReplayPath = next },
                    };
                    i++;
                    break;
                case "--max-players":
                    if (next is null || !int.TryParse(next, NumberStyles.None, CultureInfo.InvariantCulture, out var max)
                        || max is < 1 or > NetProtocol.MaxPlayersLimit)
                        return Fail($"--max-players needs a number from 1 to {NetProtocol.MaxPlayersLimit}.", out error);
                    result = result with { MaxPlayers = max };
                    i++;
                    break;
                case "--ai-allow-reload-with-clients":
                    result = result with { AiAllowReloadWithClients = true };
                    break;
                case "--creative":
                    result = result with { GameMode = Survival.GameMode.Creative };
                    break;
                case "--survival":
                    result = result with { GameMode = Survival.GameMode.Survival };
                    break;
                case "--easy":
                    result = result with { Difficulty = Survival.Difficulty.Easy };
                    break;
                case "--seed":
                    if (next is null || !long.TryParse(next, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seed))
                        return Fail("--seed needs a whole number.", out error);
                    result = result with { Seed = seed };
                    i++;
                    break;
            }
        }

        if (result.Mode == SessionMode.Join && (result.AiPort is not null || result.RecordPath is not null || result.ReplayPath is not null || result.LoadPath is not null))
            return Fail("--ai, --record, --replay and --load run on the host, not on a joining client.", out error);

        options = result;
        return true;
    }

    /// <summary>True for addresses only this machine can reach.</summary>
    public static bool IsLoopback(string address) =>
        address is "localhost" or "::1" || address.StartsWith("127.", StringComparison.Ordinal);

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
