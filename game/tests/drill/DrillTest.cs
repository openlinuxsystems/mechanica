using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Motion;
using Mechanica.Core.World;
using Mechanica.Game.Mods;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;
using NVector3 = System.Numerics.Vector3;

namespace Mechanica.Game.Tests;

/// <summary>
/// Drill spec, host plus a client, on flat ground: a drill lies on its side on a bearing on a pillar of a heavy slab, its
/// tip just short of a Stone Boulder (the loose block a large rock deposit block breaks into) sitting on the slab.
/// Turned slowly it does nothing; driven past drilling speed (as an engine would) its base ring glows on the client and
/// its tip bites the boulder, throwing chips on both machines, until the boulder breaks into Stone Chunks that both
/// machines see.
/// </summary>
public partial class DrillTest : StagedTest
{
    private const int StandId = 1, BoulderId = 2;
    private const string Boulder = "vanilla:stone_boulder", Chunk = "vanilla:stone_chunk";
    private static readonly ActionCaller Bot = new(Creation.HostPeer, ActionSource.Bot);

    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _bearing, _drill;

    protected override string Tag => "drill";

    protected override int StageTimeoutFrames => 900;

    public override void _Ready()
    {
        Add("a host and a client start on flat ground", frame =>
        {
            if (frame == 0)
            {
                _mods = ModLoader.Load([Path.Combine(ModPaths.ModsRoot, "vanilla")]);
                Check(!_mods.HasErrors, $"vanilla loads ({string.Join("; ", _mods.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))})");
                _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
                AddChild(_harness);
                _host = _harness.StartHost(mods: _mods);
                _client = _harness.StartClient("Client", mods: _mods);
            }
            return frame > 0 && _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null;
        });
        Add("spawn the drill on its stand facing a Stone Boulder", _ =>
        {
            // A 13 x 9 slab of metal blocks with a pillar three blocks high; a bearing on the pillar's +X face turns about
            // X, and the drill sits on the bearing with its tip pointing along +X, its body clear of the slab.
            var stand = new Creation(StandId, _mods);
            for (var x = -4; x <= 8; x++)
                for (var z = -4; z <= 4; z++)
                    Place(stand, "vanilla:metal_block", new GridCell(x, 0, z));
            for (var y = 1; y <= 3; y++)
                Place(stand, "vanilla:metal_block", new GridCell(0, y, 0));
            // The parts' up (their axis) along +X; their footprints run up and towards -Z from their cells.
            var sideways = GridRotation.FromAxes(new GridCell(0, 1, 0), new GridCell(1, 0, 0));
            _bearing = Place(stand, "vanilla:bearing", new GridCell(1, 3, 0), sideways).Id;
            var drill = Place(stand, "vanilla:drill", new GridCell(2, 2, 1), sideways);
            _drill = drill.Id;
            var size = stand.DefinitionOf(_drill).Size;
            Check(size == new GridSize(3, 3, 3), $"the drill is 3 x 3 x 3 blocks ({size})");
            var lift = new NVector3(0, 0.13f, 0);
            stand.SetAllBodyPoses(new Pose(lift, System.Numerics.Quaternion.Identity));
            _host.Main.Creations.Add(stand, _host.Main.Assets);
            // The boulder (1 m) sits on the slab on the drill's axis, 0.05 m past its tip.
            // The drill's body sits flush on the bearing's disc, off the grid (the rotor's inset).
            var centre = stand.BodyPose(stand.BodyIndexOf(_drill)).Apply(PartGeometry.CentreMetres(stand.DefinitionOf(_drill), drill.Cell, drill.Rotation).ToNumerics());
            var boulder = new Creation(BoulderId, _mods);
            Place(boulder, Boulder, GridCell.Zero);
            var middle = new NVector3(centre.X + 0.375f + 0.05f + 0.5f, 0.255f + 0.13f + 0.5f + 0.01f, centre.Z);
            boulder.SetAllBodyPoses(new Pose(middle - new NVector3(0.375f), System.Numerics.Quaternion.Identity));
            _host.Main.Creations.Add(boulder, _host.Main.Assets);
            return true;
        });
        Wait("everything settles", 90);

        Add("turned slower than drilling speed it does not drill", frame =>
        {
            if (frame == 0)
                Check(Act(MotionActions.SetBearingTarget, new SetBearingTargetArgs(StandId, _bearing, 5f, 2000f)).Ok, "the bearing turns the drill at 5 rad/s");
            if (frame < 90)
                return false;
            Check(BoulderDamage() == 0, $"the boulder is untouched ({BoulderDamage()})");
            Check(_host.Main.Streamer.Effects.Bites == 0, "no chips fly");
            return true;
        });
        Add("past drilling speed its base ring glows on the client and it bites the boulder, chips flying on both machines", frame =>
        {
            if (frame == 0)
                Check(Act(MotionActions.SetBearingTarget, new SetBearingTargetArgs(StandId, _bearing, 14f, 2000f)).Ok, "the bearing turns the drill at 14 rad/s");
            NudgeBoulder();
            if (BoulderDamage() <= 0 || _client.Main.Streamer.Effects.Bites == 0)
                return frame > 600 && Fail($"the drill never bit ({Diagnose()})");
            Check(BoulderDamage() % 25f == 0, $"each bite takes 25 ({BoulderDamage()})");
            Check(_host.Main.Streamer.Effects.Bites > 0, "the host shows the chips too");
            Check(_client.Main.Display.Readouts.Number(StandId, _drill, "spin") == 1f, "the client knows it runs at drilling speed");
            var glow = _client.Main.Creations.NodeOf(StandId)?.FindChildren("BitGlow*", nameof(Node3D), true, false).OfType<Node3D>().FirstOrDefault();
            Check(glow is not null && glow.Scale.X > 0.9f, $"the base ring glow is full on the client ({glow?.Scale})");
            return true;
        });
        Add("the boulder breaks into Stone Chunks on both machines", frame =>
        {
            NudgeBoulder();
            if (Count(_host, Boulder) > 0 || Count(_client, Boulder) > 0 || Count(_host, Chunk) < 2 || Count(_client, Chunk) < 2)
                return frame > 800 && Fail($"the boulder never broke ({Count(_host, Boulder)} boulders, {Count(_client, Chunk)} chunks on the client; {Diagnose()})");
            Check(Count(_host, Chunk) == 2, $"the boulder breaks into two Stone Chunks ({Count(_host, Chunk)})");
            return true;
        });
    }

    // Keeps the boulder pressed against the tip: a small push towards the drill every frame it has drifted off.
    private void NudgeBoulder()
    {
        if (_host.Main.Creations.NodeOf(BoulderId) is not { } node || _host.Main.Creations.NodeOf(StandId) is not { } stand)
            return;
        var body = node.Bodies[0];
        var tip = stand.Bodies.Last().GlobalPosition.X + 0.375f;
        if (body.GlobalPosition.X - 0.5f > tip + 0.02f)
            body.ApplyCentralForce(new Vector3(-400f, 0, 0));
    }

    private bool Fail(string message)
    {
        Check(false, message);
        return true;
    }

    // Where the drill and the boulder are and how the drill runs, for a failure's message.
    private string Diagnose()
    {
        var stand = _host.Main.Creations.NodeOf(StandId);
        var boulder = _host.Main.Creations.NodeOf(BoulderId);
        var fault = _host.Main.Scripts.Runtime.Instances.FirstOrDefault(i => i.Part == _drill)?.Fault;
        return $"spin {_host.Main.Display.Readouts.Number(StandId, _drill, "spin")}, drill body {stand?.Bodies.Last().GlobalPosition} "
            + $"turning {stand?.Bodies.Last().AngularVelocity}, boulder {boulder?.Bodies[0].GlobalPosition}, "
            + $"host bites {_host.Main.Streamer.Effects.Bites}, damage {BoulderDamage()}, fault {fault}";
    }

    private float BoulderDamage() =>
        _host.Main.Creations.Find(BoulderId) is { } boulder && boulder.Parts.Count == 1 ? boulder.PartDamage(boulder.Parts.First().Id) : -1;

    private static int Count(Instance instance, string part) =>
        instance.Main.Creations.All.Count(c => c.Parts.Count == 1 && Identifiers.Qualify(c.Parts.First().Part) == part);

    private ActionResult Act<TArgs>(string action, TArgs args) => _host.Main.Actions.Dispatcher.Dispatch(Bot, action, args);

    private static PartInstance Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        if (!creation.TryPlacePart(part, cell, rotation, out var placed, out var error))
            throw new InvalidOperationException($"{part} at {cell}: {error}");
        return placed;
    }
}
