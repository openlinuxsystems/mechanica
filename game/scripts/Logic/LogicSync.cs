using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Logic;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Scripting;

namespace Mechanica.Game.Logic;

/// <summary>
/// The logic and power bus and seats over the network (chunk 10, D10). On the host it registers
/// <see cref="LogicActions"/> (the Connect Tool's links, part settings, using parts, seats and the Creation
/// Inspector's query) over the part scripts, hands seated players' input to their seats' scripts, and
/// stands up players whose seat went away. Who sits where is sent to every client, and every peer keeps its
/// seated players on their seats. Links and settings live in the Creation, so <see cref="CreationSync"/>
/// sends them with it.
/// </summary>
public partial class LogicSync : Node
{
    private int _sentSeats = -1;

    /// <summary>The session. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players, to seat them. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The Creations this peer shows. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Building, for reach and sounds. Set before adding to the tree.</summary>
    public BuildSync Build { get; init; } = null!;

    /// <summary>The host's part scripts. Set before adding to the tree.</summary>
    public ScriptSync Scripts { get; init; } = null!;

    /// <summary>True in Creative, where engines need no fuel.</summary>
    public bool Creative { get; init; }

    /// <summary>Who sits where: the host's own, or a client's copy of it.</summary>
    public SeatRegistry Seats { get; } = new();

    /// <summary>The logic actions, once registered.</summary>
    public LogicActions? Actions { get; private set; }

    /// <summary>Raised on every peer when the seats change.</summary>
    public event Action? SeatsChanged;

    /// <summary>Registers the logic actions (every peer registers; only the host runs them). Call after <see cref="Survival.SurvivalSync.RegisterActions"/> so Survival asks for a crafted Connect Tool.</summary>
    public void RegisterActions(ActionDispatcher dispatcher)
    {
        Build.Settings ??= BuildSync.LoadSettings();
        Actions = new LogicActions(Build.Settings, Build, Scripts.Runtime, Seats) { Costs = Build.Costs };
        Actions.Register(dispatcher);
        Actions.SeatChanged += (seat, entered) =>
        {
            if (!entered)
                Stood(seat);
        };
    }

    public override void _Ready()
    {
        // After the players and before the scripts, so a seat reads this step's input.
        ProcessPhysicsPriority = Scripts.ProcessPhysicsPriority - 1;
        var runtime = Scripts.Runtime;
        runtime.Seats = Seats;
        runtime.Creative = Creative;
        runtime.OccupantInput = peer => Players.Players.TryGetValue(peer, out var player) && !player.Dead ? player.LastInput : null;
        Session.PeerReady += SendSeatsTo;
        Session.StateChanged += OnStateChanged;
    }

    public override void _ExitTree()
    {
        Session.PeerReady -= SendSeatsTo;
        Session.StateChanged -= OnStateChanged;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Session.IsHost && Actions is { } actions)
        {
            foreach (var seat in actions.Vacate(peer => Players.Players.TryGetValue(peer, out var player) && !player.Dead))
                StandUp(seat);
            // Buttons let go for players who stood up, died or left while holding them.
            actions.ReleaseHolds(peer => Players.Players.TryGetValue(peer, out var player) && !player.Dead);
            if (Seats.Revision != _sentSeats)
            {
                _sentSeats = Seats.Revision;
                var json = JsonSerializer.Serialize(Seats.All, ActionJson.Options);
                foreach (var peer in Session.ReadyPeers)
                    RpcId(peer, MethodName.ClientSeats, json);
                SeatsChanged?.Invoke();
            }
        }
        Attach();
    }

    // Visual only: keeps seated players on their seats between physics steps, where Creations interpolate.
    public override void _Process(double delta)
    {
        foreach (var player in Players.Players.Values)
        {
            if (player.Seated && !player.Simulated)
                player.SitStill();
        }
    }

    /// <summary>Where a seat puts its player's feet now, and how fast the seat moves; null when this peer does not show it.</summary>
    public (Vector3 Feet, Vector3 Velocity)? SeatPose(int creationId, int partId) =>
        SeatTransform(creationId, partId) is { } seat ? (seat.Feet.Origin, seat.Velocity) : null;

    /// <summary>
    /// The seat's frame now, at the sitting player's feet (the player faces its -Z), and how fast it moves; null when
    /// this peer does not show it.
    /// </summary>
    public (Transform3D Feet, Vector3 Velocity)? SeatTransform(int creationId, int partId)
    {
        if (Creations.NodeOf(creationId) is not { } node || node.Creation.FindPart(partId) is not { } part)
            return null;
        var creation = node.Creation;
        var index = creation.BodyIndexOf(partId);
        if (index >= node.Bodies.Count)
            return null;
        var definition = creation.DefinitionOf(partId);
        var local = PartGeometry.CentreMetres(definition, part.Cell, part.Rotation) + part.Rotation.Apply(SeatStyle.Anchor(definition));
        var body = node.Bodies[index];
        var frame = body.GlobalTransform * new Transform3D(part.Rotation.ToBasis(), local.ToNumerics().ToGodot());
        return (frame.Orthonormalized(), body.LinearVelocity);
    }

    /// <summary>
    /// Where a player standing up from a seat lands: beside it (left, then right, front, back, then above), the first
    /// spot with room for them (UI spec §10).
    /// </summary>
    public Vector3 BesideSeat(int creationId, int partId, PlayerController player)
    {
        if (SeatTransform(creationId, partId) is not { } seat)
            return player.GlobalPosition + Vector3.Up * 0.5f;
        var basis = seat.Feet.Basis;
        var feet = seat.Feet.Origin;
        // The seat's own up may be tipped; stand upright in the world.
        Vector3 Flat(Vector3 v) => new Vector3(v.X, 0, v.Z) is var f && f.LengthSquared() > 1e-4f ? f.Normalized() : Vector3.Zero;
        var side = Flat(basis.X);
        var front = Flat(-basis.Z);
        var reach = player.Settings.Radius + 0.45f;
        Vector3[] spots = [feet - side * reach, feet + side * reach, feet + front * reach, feet - front * reach];
        foreach (var spot in spots)
        {
            if (side == Vector3.Zero && front == Vector3.Zero)
                break;
            var at = spot + Vector3.Up * 0.05f;
            if (player.RoomAt(at))
                return at;
        }
        return feet + Vector3.Up * 0.75f;
    }

    // The yaw of a seat's front (-Z), flattened onto the ground; null when it points straight up or down.
    private static float? Facing(Basis basis)
    {
        var front = -basis.Z;
        return new Vector2(front.X, front.Z).LengthSquared() > 1e-4f ? Mathf.Atan2(-front.X, -front.Z) : null;
    }

    // Gives each seated player its seat, and takes it away from players who stood up.
    private void Attach()
    {
        foreach (var (peer, player) in Players.Players)
        {
            if (Seats.SeatOf(peer) is { } seat)
            {
                if (!player.Seated)
                {
                    player.SeatStyle = Creations.NodeOf(seat.Creation)?.Creation is { } creation && creation.FindPart(seat.Part) is not null
                        ? SeatStyle.For(creation.DefinitionOf(seat.Part)) : SeatStyle.Default;
                    player.SeatFacing = () => SeatTransform(seat.Creation, seat.Part) is { } at ? Facing(at.Feet.Basis) : null;
                    player.Seat = () => SeatPose(seat.Creation, seat.Part);
                }
                if (!player.Simulated)
                    player.SitStill();
            }
            else if (player.Seated)
            {
                player.Seat = null;
            }
        }
    }

    // A player whose seat is gone lands next to where it was.
    private void StandUp(SeatAssignment seat)
    {
        if (Players.Players.TryGetValue(seat.Peer, out var player))
        {
            player.Seat = null;
            Players.HostTeleport(seat.Peer, player.GlobalPosition + Vector3.Up * 0.5f);
        }
    }

    // Host side: a player stood up with LeaveSeat; they land beside the seat.
    private void Stood(SeatAssignment seat)
    {
        if (!Players.Players.TryGetValue(seat.Peer, out var player))
            return;
        var at = BesideSeat(seat.Creation, seat.Part, player);
        player.Seat = null;
        Players.HostTeleport(seat.Peer, at);
    }

    private void SendSeatsTo(int peer) =>
        RpcId(peer, MethodName.ClientSeats, JsonSerializer.Serialize(Seats.All, ActionJson.Options));

    /// <summary>Host → client: who sits where.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientSeats(string json)
    {
        if (Session.IsHost)
            return;
        try
        {
            Seats.Load(JsonSerializer.Deserialize<List<SeatAssignment>>(json, ActionJson.Options) ?? []);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"[logic] bad seats from the host: {e.Message}");
            return;
        }
        SeatsChanged?.Invoke();
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        // A client's copy belongs to the session it leaves; a host starts with everyone standing.
        Seats.Load([]);
        _sentSeats = -1;
    }
}
