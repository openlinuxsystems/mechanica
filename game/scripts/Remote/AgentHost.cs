using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Remote;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Remote;

/// <summary>
/// Lets AI endpoint connections join the host's world as players of their own (<c>mechanica.join</c>):
/// keeps <see cref="AgentPlayers"/>, gives each agent a profile in the <see cref="NetSession"/> so its save
/// follows its name, and spawns or removes its body through <see cref="PlayerSync"/>. Registers
/// <c>SetPlayerInput</c>, which steers an agent's own player.
/// </summary>
public sealed class AgentHost : IAgentHost
{
    /// <summary>The engine settings file for agents.</summary>
    public const string DefaultSettingsPath = "res://data/agents.json";

    private readonly NetSession _session;
    private readonly PlayerSync _players;

    /// <summary>Wires agents into <paramref name="players"/> and registers their action on <paramref name="dispatcher"/>.</summary>
    public AgentHost(NetSession session, PlayerSync players, ActionDispatcher dispatcher, AgentSettings? settings = null)
    {
        _session = session;
        _players = players;
        Agents = new AgentPlayers(settings ?? LoadDefaultSettings());
        Agents.Register(dispatcher);
        players.Agents = Agents;
        // A player can also go without a leave (the host joins another world): forget the agent then.
        players.PlayerDespawning += OnDespawning;
    }

    /// <summary>The agents playing now.</summary>
    public AgentPlayers Agents { get; }

    /// <summary>Raised after an agent's player spawned.</summary>
    public event Action<AgentPlayer>? Joined;

    /// <summary>Raised after an agent's player was removed.</summary>
    public event Action<AgentPlayer>? Left;

    /// <summary>Reads the limits from <see cref="DefaultSettingsPath"/>.</summary>
    public static AgentSettings LoadDefaultSettings()
    {
        using var file = Godot.FileAccess.Open(DefaultSettingsPath, Godot.FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {DefaultSettingsPath}: {Godot.FileAccess.GetOpenError()}");
        return AgentSettings.FromJson(file.GetAsText());
    }

    /// <inheritdoc/>
    public AgentPlayer? Join(string connectionId, string name, out string? error)
    {
        if (!_session.IsHost)
        {
            error = "Only the host takes agent players.";
            return null;
        }
        if (Agents.Join(connectionId, name, peer => _session.IsReady(peer) || _players.Players.ContainsKey(peer), out error) is not { } agent)
            return null;
        _session.SetAgentProfile(agent.Peer, agent.Profile);
        if (_players.HostAddAgent(agent.Peer) is { } refused)
        {
            _session.SetAgentProfile(agent.Peer, null);
            Agents.Leave(agent.Peer);
            error = refused;
            return null;
        }
        GD.Print($"[ai] {agent.Profile.Name} joined as player {agent.Peer}");
        Joined?.Invoke(agent);
        return agent;
    }

    /// <inheritdoc/>
    public int? PeerOf(string connectionId) => Agents.OfConnection(connectionId)?.Peer;

    /// <inheritdoc/>
    public void Leave(string connectionId)
    {
        if (Agents.OfConnection(connectionId) is { } agent)
            Remove(agent, despawn: true);
    }

    private void OnDespawning(PlayerController player)
    {
        if (Agents.All.TryGetValue(player.PeerId, out var agent))
            Remove(agent, despawn: false);
    }

    private void Remove(AgentPlayer agent, bool despawn)
    {
        // Leave the bookkeeping first so the despawn below doesn't come back here.
        Agents.Leave(agent.Peer);
        if (despawn)
            _players.HostRemoveAgent(agent.Peer); // saved while its profile is still known
        _session.SetAgentProfile(agent.Peer, null);
        GD.Print($"[ai] {agent.Profile.Name} left");
        Left?.Invoke(agent);
    }
}
