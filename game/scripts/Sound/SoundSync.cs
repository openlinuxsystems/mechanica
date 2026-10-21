using System.Text.Json;
using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Sound;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;

namespace Mechanica.Game.Sound;

/// <summary>
/// Sound over the network (chunk 16, D10). The host decides what is heard: <see cref="Emit"/> plays a
/// one-shot on the host and sends it to every client whose player is in earshot
/// (<see cref="SoundEvent.IsAudibleAt"/> plus <see cref="EarshotMargin"/>). Loops belong to the host too:
/// <see cref="StartLoop"/> anchors one to a world point or to a part of a Creation (it follows the part on
/// every peer), <see cref="SetLoopParameter"/> bends it and <see cref="StopLoop"/> ends it; clients get
/// every change, parameters batched at <see cref="ParameterHz"/>, and a late joiner gets every loop
/// running. A loop on a Creation stops when the Creation or the part is gone.
/// <para>
/// <see cref="PlayLocal"/> is for feedback only this player hears (a Sledgehammer swing in their own hands, a UI
/// click, a refusal) and never travels. These are the generic sound primitives the game, bots and the Lua
/// API share (D2).
/// </para>
/// </summary>
public partial class SoundSync : Node
{
    /// <summary>Metres past an event's <see cref="SoundEvent.MaxDistance"/> a client's player may be and still be sent it.</summary>
    public const float EarshotMargin = 5f;

    /// <summary>How often changed loop parameters are sent to clients.</summary>
    public const double ParameterHz = 20;

    private readonly Dictionary<int, Loop> _loops = [];
    private readonly HashSet<int> _dirty = [];
    private int _nextLoop = 1;
    private double _sinceFlush;
    private bool _wasConnected;

    /// <summary>A loop on this peer: what plays, where, and its voice.</summary>
    /// <param name="Handle">Host-wide id.</param>
    /// <param name="Event">The event.</param>
    /// <param name="Anchor">What it follows.</param>
    public sealed record Loop(int Handle, SoundEvent Event, SoundAnchor Anchor)
    {
        /// <summary>The voice playing it here; null when the limiter dropped it on this peer.</summary>
        public SoundPlayer.Voice? Voice { get; set; }

        /// <summary>Its parameter values, as the host last set them.</summary>
        public Dictionary<string, float> Parameters { get; init; } = [];
    }

    /// <summary>The session sounds travel over. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players, for earshot. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The Creations loops follow. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>This peer's playback.</summary>
    public SoundPlayer Player { get; private set; } = null!;

    /// <summary>The sound events; set before adding to the tree, swapped with <see cref="SetRegistry"/>.</summary>
    public SoundRegistry Registry { get; set; } = SoundRegistry.Empty;

    /// <summary>Loops running on this peer, by handle.</summary>
    public IReadOnlyDictionary<int, Loop> Loops => _loops;

    /// <summary>One-shots this host sent to clients, for tests.</summary>
    public int Sent { get; private set; }

    public override void _Ready()
    {
        Player = new SoundPlayer { Name = "Player", Registry = Registry };
        AddChild(Player);
        Session.PeerReady += SendLoopsTo;
        Session.StateChanged += OnStateChanged;
        Creations.Removed += OnCreationRemoved;
    }

    public override void _ExitTree()
    {
        Session.PeerReady -= SendLoopsTo;
        Session.StateChanged -= OnStateChanged;
        Creations.Removed -= OnCreationRemoved;
    }

    public override void _Process(double delta)
    {
        if (!Session.IsHost)
            return;
        // A Creation's part can go without the Creation going (removed, broken off): its loops end with it.
        foreach (var loop in _loops.Values.Where(l => l.Anchor.Creation is { } c && Creations.Find(c)?.FindPart(l.Anchor.Part) is null).ToList())
            StopLoop(loop.Handle);
        _sinceFlush += delta;
        if (_dirty.Count == 0 || _sinceFlush < 1 / ParameterHz)
            return;
        _sinceFlush = 0;
        foreach (var handle in _dirty)
        {
            if (!_loops.TryGetValue(handle, out var loop))
                continue;
            var json = JsonSerializer.Serialize(loop.Parameters);
            foreach (var peer in Session.ReadyPeers)
                RpcId(peer, MethodName.ClientSetLoopParameters, handle, json);
        }
        _dirty.Clear();
    }

    /// <summary>Uses new sound events from now on, as the Test Sandbox does after the editor exports.</summary>
    public void SetRegistry(SoundRegistry registry)
    {
        Registry = registry;
        Player.Registry = registry;
        Player.ClearCache();
    }

    /// <summary>Raised when a world one-shot plays here (<see cref="Emit"/> on the host, its copies on clients), for effects that go with it.</summary>
    public event Action<SoundEvent, Vector3>? Played;

    /// <summary>
    /// Plays a one-shot for everyone in earshot. On the host it plays here and is sent to clients near
    /// <paramref name="position"/>; on a client it only plays here. False when the event is unknown.
    /// </summary>
    public bool Emit(string sound, string ownerModId, Vector3 position)
    {
        if (Registry.Find(sound, ownerModId) is not { } e)
            return false;
        Player.Play(e, position, once: true);
        Played?.Invoke(e, position);
        if (!Session.IsHost)
            return true;
        foreach (var peer in Session.ReadyPeers)
        {
            if (e.Spatial && (!Players.Players.TryGetValue(peer, out var listener)
                    || !e.IsAudibleAt(listener.GlobalPosition.DistanceTo(position) - EarshotMargin)))
                continue;
            RpcId(peer, MethodName.ClientPlay, e.QualifiedId, position);
            Sent++;
        }
        return true;
    }

    /// <summary>Plays a sound only this player hears (feedback for their own action). Null when unknown or dropped.</summary>
    public SoundPlayer.Voice? PlayLocal(string sound, string ownerModId, Vector3 position) => Player.Play(sound, ownerModId, position);

    /// <summary>Plays a vanilla UI sound here (<c>ui_click</c>, <c>ui_error</c>).</summary>
    public SoundPlayer.Voice? PlayUi(string sound) => Player.Play(sound, Identifiers.VanillaModId, Vector3.Zero);

    /// <summary>
    /// Host: starts a loop on every peer, following <paramref name="anchor"/>. Returns its handle, or 0 when
    /// the event is unknown, is not a loop, or this peer is not the host.
    /// </summary>
    public int StartLoop(string sound, string ownerModId, SoundAnchor anchor, IReadOnlyDictionary<string, float>? parameters = null)
    {
        if (!Session.IsHost || Registry.Find(sound, ownerModId) is not { Loop: true } e)
            return 0;
        var loop = new Loop(_nextLoop++, e, anchor) { Parameters = e.DefaultParameters() };
        foreach (var (name, value) in parameters ?? new Dictionary<string, float>())
        {
            if (e.FindParameter(name) is not null && float.IsFinite(value))
                loop.Parameters[name] = value;
        }
        Begin(loop);
        foreach (var peer in Session.ReadyPeers)
            SendLoop(peer, loop);
        return loop.Handle;
    }

    /// <summary>Host: sets a running loop's parameter. False for an unknown loop or parameter.</summary>
    public bool SetLoopParameter(int handle, string name, float value)
    {
        if (!Session.IsHost || !_loops.TryGetValue(handle, out var loop) || loop.Event.FindParameter(name) is null || !float.IsFinite(value))
            return false;
        if (loop.Parameters.TryGetValue(name, out var old) && old == value)
            return true;
        loop.Parameters[name] = value;
        loop.Voice?.SetParameter(name, value);
        _dirty.Add(handle);
        return true;
    }

    /// <summary>Host: stops a loop on every peer. False when it is not running.</summary>
    public bool StopLoop(int handle)
    {
        if (!Session.IsHost || !End(handle))
            return false;
        foreach (var peer in Session.ReadyPeers)
            RpcId(peer, MethodName.ClientStopLoop, handle);
        return true;
    }

    /// <summary>Where a loop's anchor is now on this peer; null when its Creation or part is not shown here.</summary>
    public Vector3? AnchorPosition(SoundAnchor anchor)
    {
        if (anchor.Creation is not { } id)
            return anchor.Position;
        if (Creations.Find(id) is not { } creation || creation.FindPart(anchor.Part) is not { } part
            || Creations.NodeOf(id)?.Bodies.FirstOrDefault(b => b.PartIds.Contains(anchor.Part)) is not { } body)
            return null;
        var centre = PartGeometry.CentreMetres(creation.DefinitionOf(part.Id), part.Cell, part.Rotation).ToGodot();
        return body.GlobalTransform * centre;
    }

    /// <summary>Host → client: play a one-shot.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ClientPlay(string sound, Vector3 position)
    {
        if (Session.State == NetSession.SessionState.Connected && Registry.Find(sound) is { } e)
        {
            Player.Play(e, position, once: true);
            Played?.Invoke(e, position);
        }
    }

    /// <summary>Host → client: a loop started (or was running when this client joined).</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientStartLoop(int handle, string sound, int creation, int part, Vector3 position, string parameters)
    {
        if (Session.State != NetSession.SessionState.Connected || Registry.Find(sound) is not { Loop: true } e)
            return;
        End(handle);
        var anchor = new SoundAnchor(position, creation > 0 ? creation : null, part);
        var loop = new Loop(handle, e, anchor) { Parameters = ReadParameters(parameters) ?? e.DefaultParameters() };
        Begin(loop);
    }

    /// <summary>Host → client: a loop's parameters, all of them.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientSetLoopParameters(int handle, string parameters)
    {
        if (Session.State != NetSession.SessionState.Connected || !_loops.TryGetValue(handle, out var loop)
            || ReadParameters(parameters) is not { } values)
            return;
        foreach (var (name, value) in values)
        {
            loop.Parameters[name] = value;
            loop.Voice?.SetParameter(name, value);
        }
    }

    /// <summary>Host → client: a loop stopped.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientStopLoop(int handle)
    {
        if (Session.State == NetSession.SessionState.Connected)
            End(handle);
    }

    private void Begin(Loop loop)
    {
        _loops[loop.Handle] = loop;
        var start = AnchorPosition(loop.Anchor) ?? loop.Anchor.Position;
        loop.Voice = Player.Play(loop.Event, start, () => AnchorPosition(loop.Anchor), loop.Parameters);
    }

    private bool End(int handle)
    {
        if (!_loops.Remove(handle, out var loop))
            return false;
        _dirty.Remove(handle);
        if (loop.Voice is { } voice)
            Player.Stop(voice);
        return true;
    }

    private void SendLoop(int peer, Loop loop)
    {
        // The anchor's position now, so a client that doesn't show the Creation yet still hears it in the right place.
        var at = AnchorPosition(loop.Anchor) ?? loop.Anchor.Position;
        RpcId(peer, MethodName.ClientStartLoop, loop.Handle, loop.Event.QualifiedId, loop.Anchor.Creation ?? 0, loop.Anchor.Part, at,
            JsonSerializer.Serialize(loop.Parameters));
    }

    private void SendLoopsTo(int peer)
    {
        foreach (var loop in _loops.Values)
            SendLoop(peer, loop);
    }

    private void OnCreationRemoved(int id)
    {
        if (!Session.IsHost)
            return;
        foreach (var loop in _loops.Values.Where(l => l.Anchor.Creation == id).ToList())
            StopLoop(loop.Handle);
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A client hears the host's loops only while connected to it; the host keeps its own.
        if (state == NetSession.SessionState.Connecting || (state == NetSession.SessionState.Offline && _wasConnected))
        {
            foreach (var handle in _loops.Keys.ToList())
                End(handle);
        }
        _wasConnected = state == NetSession.SessionState.Connected;
    }

    private static Dictionary<string, float>? ReadParameters(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, float>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// What a loop follows: a part of a Creation (<see cref="Creation"/> and <see cref="Part"/>), or a fixed
/// world point (<see cref="Position"/>, also where a part-anchored loop plays until its Creation shows).
/// </summary>
/// <param name="Position">World point, metres.</param>
/// <param name="Creation">Creation id, for a loop on a part.</param>
/// <param name="Part">Part id within it.</param>
public readonly record struct SoundAnchor(Vector3 Position, int? Creation = null, int Part = 0)
{
    /// <summary>A loop on a part of a Creation.</summary>
    public static SoundAnchor OnPart(int creation, int part) => new(Vector3.Zero, creation, part);
}
