using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Game.Building;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The hydraulic lift model, host plus a client with the vanilla mods. A client puts its Lift down and both peers load
/// the model and play its deploy: the outriggers swing out until the feet stand on the ground, the column rises from its
/// transport height to the resting 2 units, the lights flash and hold. Raising eases the column up level by level the same
/// way on both peers, the deck and its collision follow the extend clip exactly (to 22 units at the top), and coming to
/// rest at the top or bottom plays the end-stop clunk.
/// </summary>
public partial class LiftTest : StagedTest
{
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private readonly List<ActionResult> _results = [];
    private Vector3 _at;
    private int _deployFrames;
    private float _lastTop;
    private bool _rose;
    private int _endStops;
    private float _worstRide;
    private int _offDeck;
    private int _heldTo;
    private int _heldStops;
    private bool _heldMoved;
    private float _slowest;
    private int _corrections;

    protected override string Tag => "lift";

    protected override int StageTimeoutFrames => 900;

    private LiftSettings Settings => _host.Main.Build.Settings!.Lift;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("host and client are in", _ => _client.Main.PlayerSync.Players.Count == 2 && _host.Main.PlayerSync.Players.Count == 2
            && _client.Main.LocalPlayer is not null && _client.Main.Tools is not null);
        Add("the Lift's ghost and the tool icons are the real models", _ =>
        {
            var ghost = _client.Main.Tools!.Lift.Ghost;
            var meshes = ghost.GetChildren().OfType<MeshInstance3D>().ToList();
            Check(meshes.Count > 10 && meshes.All(m => m.MaterialOverride == LiftTool.GhostMaterial), $"the ghost is the lift's meshes in the ghost look ({meshes.Count})");
            var deployed = Footprint(meshes.Select(m => m.Transform * m.GetAabb()));
            Check(deployed > Settings.PlatformSize * 1.3f, $"the ghost stands with its outriggers out ({deployed:F2} m across)");
            var models = new Ui.ToolIconModels(_client.Main.Characters.Assets, _client.Main.Assets, Settings);
            Check(models.For(Core.Items.HotbarTools.Hammer) is null, "a tool with no model draws as its initials");
            foreach (var tool in new[] { Core.Items.HotbarTools.Sledge, Core.Items.HotbarTools.Lift })
            {
                var model = models.For(tool);
                Check(model is not null && model.FindChildren("*", nameof(MeshInstance3D), owned: false).Count > 0, $"the {tool} icon is drawn from its model");
                if (tool == Core.Items.HotbarTools.Lift && model is not null)
                {
                    AddChild(model);
                    var folded = Footprint(model.FindChildren("*", nameof(MeshInstance3D), owned: false).OfType<MeshInstance3D>().Select(m => m.GlobalTransform * m.GetAabb()));
                    Check(folded < deployed, $"the Lift's icon is folded, outriggers in ({folded:F2} m across)");
                }
                model?.QueueFree();
            }
            return true;
        });
        Add("the client puts its Lift down", frame =>
        {
            if (frame == 0)
            {
                var me = _client.Main.LocalPlayer!.GlobalPosition;
                var from = me + new Vector3(0, 2, -2.5f);
                var hit = _client.Main.LocalPlayer.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 10));
                _at = hit.Count > 0 ? hit["position"].AsVector3() : me + new Vector3(0, 0, -2.5f);
                _client.Main.Sounds.Player.VoiceStarted += voice =>
                {
                    if (voice.Event.QualifiedId == "vanilla:" + Settings.EndStopSound)
                        _endStops++;
                };
                Request(ToolActions.PlaceLift, new PlaceLiftArgs(new Core.Data.Vec3(_at.X, _at.Y, _at.Z)));
            }
            if (_results.Count == 0)
                return false;
            Check(_results[0].Ok, $"the host places it ({_results[0]})");
            if (!Both(out var host, out var client))
                return false;
            foreach (var (lift, who) in new[] { (host, "host"), (client, "client") })
            {
                Check(lift.Model is not null, $"the {who} loads the lift model");
                Check(!lift.Deployed && lift.ShownTop < Settings.TopAt(0), $"the {who} starts the deploy below the resting height ({lift.ShownTop:F3})");
                Check(lift.Model!.FindChild("Stage19", true, false) is not null && lift.Model.FindChild("Platform", true, false) is not null, $"the {who}'s model has every column stage and the platform");
            }
            return true;
        });
        Add("the Lift deploys on both peers", frame =>
        {
            if (!Both(out var host, out var client))
                return false;
            _deployFrames = frame;
            if (!host.Deployed || !client.Deployed)
                return false;
            Check(_deployFrames is > 60 and < 150, $"the deploy takes about two seconds ({_deployFrames} frames)");
            foreach (var (lift, who) in new[] { (host, "host"), (client, "client") })
            {
                Check(Mathf.IsEqualApprox(lift.ShownTop, Settings.TopAt(0), 1e-4f), $"the {who}'s column comes up to 2 units ({lift.ShownTop:F3})");
                Check(Mathf.IsEqualApprox(Deck(lift), Settings.TopAt(0), 2e-3f), $"the {who}'s deck is drawn at 0.5 m ({Deck(lift):F4})");
                Check(lift.LightLevel > 0.5f, $"the {who}'s lights hold on ({lift.LightLevel:F2})");
                foreach (var corner in new[] { "BR", "FR", "FL", "BL" })
                {
                    var foot = (Node3D)lift.Model!.FindChild("OutriggerFoot" + corner, true, false);
                    var local = foot.GlobalPosition - lift.GlobalPosition;
                    Check(Mathf.Abs(local.Y - 0.064f) < 0.01f, $"the {who}'s {corner} foot stands on the ground ({local.Y:F3})");
                    Check(new Vector2(local.X, local.Z).Length() > 0.85f, $"the {who}'s {corner} outrigger is out ({local})");
                }
            }
            return true;
        });
        Add("raising eases the column up eight levels on both peers", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Request(ToolActions.SetLiftLevel, new SetLiftLevelArgs(8));
                _lastTop = 0;
            }
            if (!Both(out var host, out var client) || client.State.Level != 8)
                return false;
            if (client.Moving)
            {
                Check(client.ShownTop >= _lastTop - 1e-5f, "the column never drops while rising");
                _rose |= client.ShownTop > Settings.TopAt(0) + 0.05f && client.ShownTop < Settings.TopAt(8) - 0.05f;
                _lastTop = client.ShownTop;
            }
            if (host.Moving || client.Moving || !Mathf.IsEqualApprox(client.ShownTop, Settings.TopAt(8), 1e-4f))
                return false;
            Check(_rose, "the client shows the column part way up on the way");
            Check(frame > 60, $"two metres takes a few seconds, not one frame ({frame} frames)");
            foreach (var (lift, who) in new[] { (host, "host"), (client, "client") })
            {
                Check(Mathf.IsEqualApprox(Deck(lift), Settings.TopAt(8), 2e-3f), $"the {who}'s deck is drawn at 2.5 m ({Deck(lift):F4})");
                var shape = lift.GetNode<CollisionShape3D>("Shape");
                Check(Mathf.IsEqualApprox(shape.Position.Y + ((BoxShape3D)shape.Shape).Size.Y / 2, Settings.TopAt(8), 1e-3f), $"the {who}'s platform collision is at the deck");
            }
            Check(_endStops == 0, "stopping between the ends makes no end-stop clunk");
            return true;
        });
        Add("at the top it stands 22 units tall and clunks", frame =>
        {
            if (frame == 0)
                Request(ToolActions.SetLiftLevel, new SetLiftLevelArgs(Settings.MaxLevel));
            if (!Both(out var host, out var client) || client.State.Level != Settings.MaxLevel || host.Moving || client.Moving
                || !Mathf.IsEqualApprox(client.ShownTop, Settings.TopAt(Settings.MaxLevel), 1e-4f))
                return false;
            Check(Mathf.IsEqualApprox(Settings.TopAt(Settings.MaxLevel), 22 * 0.25f), "the top level is 22 units");
            Check(Mathf.IsEqualApprox(Deck(client), 5.5f, 3e-3f), $"the model's deck reaches 5.5 m ({Deck(client):F4})");
            Check(_endStops == 1, $"the end stop clunks once ({_endStops})");
            return true;
        });
        Add("and back down to rest", frame =>
        {
            if (frame == 0)
                Request(ToolActions.SetLiftLevel, new SetLiftLevelArgs(0));
            if (!Both(out var host, out var client) || client.State.Level != 0 || host.Moving || client.Moving
                || !Mathf.IsEqualApprox(client.ShownTop, Settings.TopAt(0), 1e-4f))
                return false;
            Check(Mathf.IsEqualApprox(Deck(client), Settings.TopAt(0), 2e-3f), $"the deck is back at 0.5 m ({Deck(client):F4})");
            Check(_endStops == 2, $"and clunks at the bottom ({_endStops})");
            return true;
        });
        Add("the player steps onto the deck", frame =>
        {
            if (frame == 0)
                _host.Main.PlayerSync.HostTeleport(_client.Peer, _at + new Vector3(0, Settings.TopAt(0) + 0.05f, 0));
            return frame > 60 && OnDeck(_client.Main.LocalPlayer!, out _) && OnDeck(HostPlayer, out _);
        });
        foreach (var (level, way) in new[] { (8, "up"), (0, "down") })
        {
            Add($"standing on the deck the player rides it {way}", frame =>
            {
                if (frame == 0)
                {
                    Request(ToolActions.SetLiftLevel, new SetLiftLevelArgs(level));
                    _worstRide = 0;
                    _offDeck = 0;
                }
                foreach (var player in new[] { _client.Main.LocalPlayer!, HostPlayer })
                {
                    OnDeck(player, out var gap);
                    if (!Corrected(player))
                        _worstRide = Math.Max(_worstRide, Math.Abs(gap));
                    if (!player.IsOnFloor())
                        _offDeck++;
                }
                if (!Both(out var host, out var client) || client.State.Level != level || host.Moving || client.Moving || frame < 30)
                    return false;
                Check(_worstRide < 0.01f, $"the player's feet stay on the deck ({_worstRide:F3} m at worst)");
                Check(_offDeck < 6, $"and the player never leaves the floor ({_offDeck} frames off it)");
                return true;
            });
        }
        foreach (var (direction, stopAt, way) in new[] { (1, 6, "up"), (-1, 2, "down") })
        {
            Add($"with the Hammer in hand, holding the arrow key on the deck drives the Lift {way} smoothly", frame =>
            {
                var tool = _client.Main.Tools!.Lift;
                var lift = _client.Main.Build.Lifts[_client.Peer];
                if (frame == 0)
                {
                    tool.Active = false;
                    Check(tool.StandsOnOwnLift, "the player stands on their Lift");
                    Check(tool.PressRaise(direction) && tool.RaiseHeld, "the arrow key drives it from the deck");
                    _heldStops = 0;
                    _heldMoved = false;
                    _worstRide = 0;
                    _slowest = float.MaxValue;
                    return false;
                }
                OnDeck(_client.Main.LocalPlayer!, out var gap);
                if (!Corrected(_client.Main.LocalPlayer!))
                    _worstRide = Math.Max(_worstRide, Math.Abs(gap));
                if (tool.RaiseHeld)
                {
                    if (lift.Moving)
                        _heldMoved = true;
                    else if (_heldMoved)
                        _heldStops++;
                    // Once up to speed it holds that speed: no slowing at each level.
                    if (_slowest < float.MaxValue || Math.Abs(lift.Speed) >= Settings.RaiseSpeed * 0.99f)
                        _slowest = Math.Min(_slowest, Math.Abs(lift.Speed));
                    if (direction > 0 ? lift.State.Level < stopAt : lift.State.Level > stopAt)
                        return false;
                    tool.ReleaseRaise();
                    _heldTo = lift.State.Level;
                    return false;
                }
                if (lift.Moving || _host.Main.Build.Lifts[_client.Peer].Moving)
                    return false;
                Check(_heldStops == 0, $"held, the column runs on without stopping at each level ({_heldStops} stops)");
                Check(_slowest >= Settings.RaiseSpeed * 0.99f && _slowest < float.MaxValue, $"at an even speed ({_slowest:F3} m/s at slowest)");
                Check(Math.Abs(_heldTo - stopAt) <= 1 && lift.State.Level == _heldTo, $"let go, it stops at the next level ({_heldTo})");
                Check(_worstRide < 0.01f, $"and the player rides it the whole way ({_worstRide:F3} m at worst)");
                return true;
            });
        }
    }

    private void Request<TArgs>(string action, TArgs args) => _client.Main.Actions.Request(action, args, _results.Add);

    private bool Both(out LiftBody host, out LiftBody client)
    {
        _host.Main.Build.Lifts.TryGetValue(_client.Peer, out host!);
        _client.Main.Build.Lifts.TryGetValue(_client.Peer, out client!);
        return host is not null && client is not null;
    }

    // True on the tick the client's prediction was corrected: the replay starts from where the host had the player a
    // few ticks back, under the deck that has risen since, and the Lift puts them back on it before the frame is drawn.
    private bool Corrected(Game.Player.PlayerController player)
    {
        if (player == HostPlayer)
            return false;
        var corrections = _client.Main.PlayerSync.Corrections;
        var changed = corrections != _corrections;
        _corrections = corrections;
        return changed;
    }

    private Game.Player.PlayerController HostPlayer => _host.Main.PlayerSync.Players[_client.Peer];

    // True when the player's feet are over the deck; gap is how far above the deck's top they are.
    private bool OnDeck(Game.Player.PlayerController player, out float gap)
    {
        var body = (player == HostPlayer ? _host : _client).Main.Build.Lifts[_client.Peer];
        gap = player.GlobalPosition.Y - (body.GlobalPosition.Y + body.ShownTop);
        var flat = new Vector2(player.GlobalPosition.X - body.GlobalPosition.X, player.GlobalPosition.Z - body.GlobalPosition.Z);
        return Math.Abs(flat.X) < 0.5f && Math.Abs(flat.Y) < 0.5f && Math.Abs(gap) < 0.06f;
    }

    private static float Deck(LiftBody lift) => ((Node3D)lift.Model!.FindChild("Deck", true, false)).GlobalPosition.Y - lift.GlobalPosition.Y;

    // Width across of the boxes on the ground plane.
    private static float Footprint(IEnumerable<Aabb> boxes)
    {
        var all = boxes.Aggregate((a, b) => a.Merge(b));
        return Math.Max(all.Size.X, all.Size.Z);
    }
}
