using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Actions;
using Mechanica.Core.Player;
using Mechanica.Core.Saves;

namespace Mechanica.Core.Remote;

/// <summary>
/// Limits for AI agents that join as players (engine settings, <c>game/data/agents.json</c>). The defaults
/// only fill fields the file leaves out.
/// </summary>
public sealed record AgentSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Agent players in the world at once.</summary>
    public int MaxAgents { get; init; } = 8;

    /// <summary>Most physics ticks one <c>SetPlayerInput</c> may hold its input for.</summary>
    public int MaxInputTicks { get; init; } = 600;

    /// <summary>Added to an agent's name so people can tell it is an AI.</summary>
    public string NameSuffix { get; init; } = " (AI)";

    /// <summary>Parses settings from JSON and validates them.</summary>
    /// <exception cref="FormatException">The JSON is malformed or a value is out of range.</exception>
    public static AgentSettings FromJson(string json)
    {
        AgentSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<AgentSettings>(json, ReadOptions);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid agent settings JSON: {e.Message}", e);
        }
        if (settings is null)
            throw new FormatException("Agent settings JSON is null.");
        if (settings.MaxAgents < 0)
            throw new FormatException("max_agents must not be negative.");
        if (settings.MaxInputTicks < 1)
            throw new FormatException("max_input_ticks must be at least 1.");
        return settings;
    }
}

/// <summary>One AI agent playing with its own body.</summary>
/// <param name="Peer">The player's peer id, from a range no network peer uses.</param>
/// <param name="Profile">Its profile: the shown name and a stable id derived from the agent's name, so its save follows it.</param>
/// <param name="ConnectionId">The endpoint connection that owns it.</param>
public sealed record AgentPlayer(int Peer, PlayerProfile Profile, string ConnectionId);

/// <summary>
/// Arguments of <c>SetPlayerInput</c>: what an agent's player does for the next <paramref name="Ticks"/>
/// physics ticks, as if its keys were held that long. A new call replaces the one still running.
/// </summary>
/// <param name="MoveX">Strafe, -1 (left) to 1 (right).</param>
/// <param name="MoveZ">Forward, -1 (back) to 1 (forward), relative to where the player looks.</param>
/// <param name="Yaw">Look yaw in radians (0 looks down -Z, positive turns left); left out keeps the current one.</param>
/// <param name="Pitch">Look pitch in radians (positive looks up); left out keeps the current one.</param>
/// <param name="Jump">Hold jump.</param>
/// <param name="Sprint">Hold sprint.</param>
/// <param name="Crawl">Hold crouch (dives in deep water).</param>
/// <param name="Ticks">How many physics ticks to hold this for, 1 to the host's <c>max_input_ticks</c>; then the player stands still.</param>
public sealed record SetPlayerInputArgs(
    float MoveX = 0,
    float MoveZ = 0,
    float? Yaw = null,
    float? Pitch = null,
    bool Jump = false,
    bool Sprint = false,
    bool Crawl = false,
    int Ticks = 1);

/// <summary>
/// What the game does when an endpoint connection joins as a player (<c>mechanica.join</c>) or leaves:
/// spawn or remove the agent's body. The host implements it; without one the endpoint refuses joins.
/// </summary>
public interface IAgentHost
{
    /// <summary>
    /// Spawns a player called <paramref name="name"/> for <paramref name="connectionId"/>. Returns null with
    /// a reason in <paramref name="error"/> when it can't.
    /// </summary>
    AgentPlayer? Join(string connectionId, string name, out string? error);

    /// <summary>The peer of the player <paramref name="connectionId"/> plays as, or null (never joined, left, or removed by the host).</summary>
    int? PeerOf(string connectionId);

    /// <summary>Removes the player <paramref name="connectionId"/> joined as, if any.</summary>
    void Leave(string connectionId);
}

/// <summary>
/// The AI agents that joined as players (Godot-free bookkeeping): their peer ids and profiles, and the
/// input each one asked to hold. The game spawns and steps their bodies.
/// </summary>
public sealed class AgentPlayers(AgentSettings settings)
{
    /// <summary>
    /// First peer id handed to agents. Godot gives network peers random ids up to 2^31 - 1; ids taken by a
    /// connected peer are skipped.
    /// </summary>
    public const int FirstPeer = 0x7F000000;

    private readonly Dictionary<int, AgentPlayer> _agents = [];
    /// <summary>Action name: moves an agent's own player and turns its view.</summary>
    public const string SetPlayerInput = "SetPlayerInput";

    private readonly record struct Held(PlayerInput Input, bool KeepYaw, bool KeepPitch, int Ticks);

    private readonly Dictionary<int, Held> _inputs = [];
    private int _nextPeer = FirstPeer;

    /// <summary>The limits in use.</summary>
    public AgentSettings Settings => settings;

    /// <summary>Agents playing now, by peer.</summary>
    public IReadOnlyDictionary<int, AgentPlayer> All => _agents;

    /// <summary>The agent <paramref name="connectionId"/> plays as, or null.</summary>
    public AgentPlayer? OfConnection(string connectionId) => _agents.Values.FirstOrDefault(a => a.ConnectionId == connectionId);

    /// <summary>Registers <c>SetPlayerInput</c>, which only an agent's own player may call.</summary>
    public void Register(ActionDispatcher dispatcher) =>
        dispatcher.Register<SetPlayerInputArgs>(SetPlayerInput, ValidateInput, ApplyInput);

    private string? ValidateInput(ActionCaller caller, SetPlayerInputArgs args)
    {
        if (caller.Source != ActionSource.Player || !IsAgent(caller.Peer))
            return "Only an agent that joined with mechanica.join steers a player this way.";
        if (!float.IsFinite(args.MoveX) || !float.IsFinite(args.MoveZ)
            || args.Yaw is { } yaw && !float.IsFinite(yaw) || args.Pitch is { } pitch && !float.IsFinite(pitch))
            return "Movement and look values must be finite numbers.";
        if (args.Ticks < 1 || args.Ticks > settings.MaxInputTicks)
            return $"ticks must be 1 to {settings.MaxInputTicks}.";
        return null;
    }

    private JsonElement? ApplyInput(ActionCaller caller, SetPlayerInputArgs args)
    {
        var input = new PlayerInput(args.MoveX, args.MoveZ, args.Yaw ?? 0, args.Pitch ?? 0, args.Jump, args.Sprint, args.Crawl);
        _inputs[caller.Peer] = new Held(input, args.Yaw is null, args.Pitch is null, args.Ticks);
        return null;
    }

    /// <summary>True when <paramref name="peer"/> is an agent's player.</summary>
    public bool IsAgent(int peer) => _agents.ContainsKey(peer);

    /// <summary>The stable profile id (a valid <see cref="PlayerProfile.Id"/>) for an agent called <paramref name="name"/> (case-insensitive).</summary>
    public static string ProfileId(string name) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("agent:" + PlayerProfile.CleanName(name).ToLowerInvariant())))[..32];

    /// <summary>
    /// Adds an agent called <paramref name="name"/> for <paramref name="connectionId"/>. Returns null with a
    /// reason in <paramref name="error"/> when the name is empty or taken, the connection already plays, or
    /// the agent limit is reached. <paramref name="peerTaken"/> says whether a network peer uses an id.
    /// </summary>
    public AgentPlayer? Join(string connectionId, string name, Func<int, bool> peerTaken, out string? error)
    {
        var clean = PlayerProfile.CleanName(name ?? "");
        if (string.IsNullOrWhiteSpace(name) || clean.Length == 0)
        {
            error = "Give the agent a name.";
            return null;
        }
        if (_agents.Values.Any(a => a.ConnectionId == connectionId))
        {
            error = "This connection already plays; call mechanica.leave first.";
            return null;
        }
        var id = ProfileId(clean);
        if (_agents.Values.Any(a => a.Profile.Id == id))
        {
            error = $"An agent called '{clean}' is already playing.";
            return null;
        }
        if (_agents.Count >= settings.MaxAgents)
        {
            error = $"At most {settings.MaxAgents} agents can play at once.";
            return null;
        }
        while (_agents.ContainsKey(_nextPeer) || peerTaken(_nextPeer))
            _nextPeer = _nextPeer == int.MaxValue ? FirstPeer : _nextPeer + 1;
        var shown = PlayerProfile.CleanName(clean.Length + settings.NameSuffix.Length <= PlayerProfile.MaxNameLength
            ? clean + settings.NameSuffix
            : clean[..Math.Max(1, PlayerProfile.MaxNameLength - settings.NameSuffix.Length)] + settings.NameSuffix);
        var agent = new AgentPlayer(_nextPeer, new PlayerProfile(id, shown), connectionId);
        _nextPeer = _nextPeer == int.MaxValue ? FirstPeer : _nextPeer + 1;
        _agents[agent.Peer] = agent;
        error = null;
        return agent;
    }

    /// <summary>Removes an agent. Returns false when it wasn't playing.</summary>
    public bool Leave(int peer)
    {
        _inputs.Remove(peer);
        return _agents.Remove(peer);
    }

    /// <summary>Holds <paramref name="input"/> for an agent for <paramref name="ticks"/> physics ticks.</summary>
    public void SetInput(int peer, PlayerInput input, int ticks)
    {
        if (_agents.ContainsKey(peer))
            _inputs[peer] = new Held(input, false, false, Math.Clamp(ticks, 1, settings.MaxInputTicks));
    }

    /// <summary>Physics ticks left on an agent's current input (0 when it stands still).</summary>
    public int TicksLeft(int peer) => _inputs.TryGetValue(peer, out var held) ? held.Ticks : 0;

    /// <summary>
    /// The input to step an agent with this tick, given the one it was stepped with last. Once its held
    /// input runs out the agent stops moving and keeps looking where it last looked. What it holds and its
    /// gestures always carry over from <paramref name="last"/>.
    /// </summary>
    public PlayerInput NextInput(int peer, PlayerInput last)
    {
        var carried = PlayerInput.None with
        {
            Yaw = last.Yaw,
            Pitch = last.Pitch,
            Prop = last.Prop,
            Gesture = last.Gesture,
            GestureCount = last.GestureCount,
            Guard = last.Guard,
        };
        if (!_inputs.TryGetValue(peer, out var held))
            return carried;
        if (held.Ticks <= 1)
            _inputs.Remove(peer);
        else
            _inputs[peer] = held with { Ticks = held.Ticks - 1 };
        var input = held.Input;
        return carried with
        {
            MoveX = input.MoveX,
            MoveZ = input.MoveZ,
            Yaw = held.KeepYaw ? last.Yaw : input.Yaw,
            Pitch = held.KeepPitch ? last.Pitch : input.Pitch,
            Jump = input.Jump,
            Sprint = input.Sprint,
            Crawl = input.Crawl,
        };
    }
}
