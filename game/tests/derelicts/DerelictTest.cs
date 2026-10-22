using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Derelict vehicles (POI <c>derelict</c> markers), host plus a client, in the vanilla Survival world. The host leaves a
/// derelict at every marker when the world starts and records each marker as used. The client goes to the nearest one:
/// both peers show it, it settles where it was left without sliding off or falling apart, and it is nobody's: the client
/// takes a part off it, which the host allows.
/// </summary>
public partial class DerelictTest : StagedTest
{
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private WorldPartSpot _spot = null!;
    private int _id;
    private Pose _start;
    private int _parts;
    private ActionResult? _removed;

    protected override string Tag => "derelicts";

    protected override int StageTimeoutFrames => 1800;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = Core.Survival.GameMode.Survival };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world", _ =>
            _client.Main.Survival.Own is not null && _client.Main.LocalPlayer is { } player && player.IsOnFloor());
        Add("the host left a derelict at every derelict marker and marked each used", _ =>
        {
            var spots = Derelicts.SpotsOf(_host.Main.Streamer.Generator);
            Check(spots.Count >= 40, $"the vanilla world has derelict markers ({spots.Count})");
            Check(spots.All(s => WorldParts.ObjectOf(s.Key) is { } id && _host.Main.Streamer.State.IsDestroyed(id)), "every marker is used");
            var all = _host.Main.Creations.All.ToList();
            Check(spots.All(s => all.Any(c => !c.IsWorldOwned && c.Parts.Count > 20
                && System.Numerics.Vector3.Distance(c.BodyPose(0).Position, s.Floor) < 8f)), "each one stands at its marker");
            var spawn = _client.Main.LocalPlayer!.GlobalPosition.ToNumerics();
            _spot = spots.MinBy(s => System.Numerics.Vector3.Distance(s.Floor, spawn))!;
            var creation = all.Where(c => !c.IsWorldOwned).MinBy(c => System.Numerics.Vector3.Distance(c.BodyPose(0).Position, _spot.Floor))!;
            _id = creation.Id;
            _start = creation.BodyPose(0);
            _parts = creation.Parts.Count;
            Check(creation.AuthorityPeer == Creation.HostPeer && !creation.IsAnchored(1), "simulated by the host, not anchored");
            GD.Print($"[derelicts] the nearest is {_spot.Part} at {_spot.Floor} ({_spot.Key}, {_parts} parts)");
            return true;
        });
        Add("the client goes to it and both peers show it settled where it was left", frame =>
        {
            if (frame == 0)
            {
                _host.Main.PlayerSync.HostTeleport(_client.Peer, _spot.Floor.ToGodot() + new Vector3(6, 1, 0));
                return false;
            }
            if (frame < 240 || _host.Main.Creations.NodeOf(_id) is null || _client.Main.Creations.NodeOf(_id) is null)
                return false;
            var now = _host.Main.Creations.Find(_id)!.BodyPose(0);
            var drop = _start.Position.Y - now.Position.Y;
            var slide = new Vector2(now.Position.X - _start.Position.X, now.Position.Z - _start.Position.Z).Length();
            Check(drop < 0.8f && drop > -0.2f, $"it settles onto the ground, not through it ({drop:0.00} m down)");
            Check(slide < 1f, $"it stays where it was left ({slide:0.00} m)");
            Check(_host.Main.Creations.Find(_id)!.Parts.Count == _parts, "nothing fell off");
            Check(_client.Main.Creations.Find(_id)?.Parts.Count == _parts, "the client has all of it");
            return true;
        });
        Add("the client takes a part off it", frame =>
        {
            if (frame == 0)
            {
                var creation = _host.Main.Creations.Find(_id)!;
                var wheel = creation.Parts.Where(p => p.Part.EndsWith("wheel", System.StringComparison.Ordinal)).Select(p => p.Id).FirstOrDefault();
                _client.Main.Actions.Request(BuildActions.RemovePart, new RemovePartArgs(_id, wheel), r => _removed = r);
                return false;
            }
            if (_removed is null)
                return false;
            Check(_removed.Ok, $"the host lets anyone strip it ({_removed})");
            return _host.Main.Creations.Find(_id)?.Parts.Count == _parts - 1 && _client.Main.Creations.Find(_id)?.Parts.Count == _parts - 1;
        });
    }
}
