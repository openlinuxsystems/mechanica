using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Player;
using Mechanica.Core.World;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Lift, Weld Tool, Paint Tool and Sledgehammer, host plus clients (chunk 15, D10). A client builds
/// a three-block bar and a lone block, paints the bar, picks it up with its Lift, raises and turns it,
/// lets it drop, welds the lone block onto it and smashes a block off it with the Sledgehammer. The
/// host's graphs, paint and Lifts must match on each client, a late joiner gets the Lifts, and a player
/// who leaves takes their Lift with them. Uses the building fixture mod in <c>tests/building/mods</c>.
/// </summary>
public partial class ToolsTest : StagedTest
{
    private const string Block = "build_test:block";
    private const string Red = "#d03030";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Instance _late = null!;
    private Vector3 _barAt;
    private int _bar;
    private int _lone;
    private float _ground;
    private int _sent;

    protected override string Tag => "tools";

    protected override int StageTimeoutFrames => 900;

    private ToolBelt Tools => _client.Main.Tools!;

    public override void _Ready()
    {
        _mods = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/building/mods"));
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost(mods: _mods);
        _client = _harness.StartClient("Client", mods: _mods);

        Add("the client joins with its tools", _ =>
        {
            if (_client.Main.PlayerSync.Players.Count != 2 || _client.Main.Tools is null || _host.Main.PlayerSync.Players.Count != 2)
                return false;
            Check(Tools.Tools.Select(t => t.ToolName).SequenceEqual(["Hammer", "Lift", "Weld Tool", "Paint Tool", "Sledgehammer", "Item"]),
                "the belt holds the Hammer, Lift, Weld Tool, Paint Tool, Sledgehammer and the item in hand");
            Check(Tools.Current == Tools.Hammer, "the Hammer starts in hand");
            Tools.Use(Tools.Lift);
            Check(Tools.Current == Tools.Lift && !Tools.Hammer.Active, "a tool can be put in hand");
            return true;
        });
        Add("the client builds a bar and a lone block", frame =>
        {
            if (frame == 0)
            {
                var me = _client.Main.LocalPlayer!.GlobalPosition;
                _ground = 0f;
                _barAt = new Vector3(Mathf.Snapped(me.X, 0.25f) - 0.25f, 0.125f, Mathf.Snapped(me.Z, 0.25f) - 2.5f);
                Request(BuildActions.PlacePart, new PlacePartArgs(Block, GridCell.Zero, Position: _barAt.ToNumerics().ToVec3()));
                Request(BuildActions.PlacePart, new PlacePartArgs(Block, GridCell.Zero, Position: (_barAt + new Vector3(1.75f, 0, 0)).ToNumerics().ToVec3()));
            }
            if (_results.Count < 2)
                return false;
            if (_results.Count == 2 && _sent == 0)
            {
                _sent = 2;
                Check(_results.All(r => r.Ok), $"both start ({string.Join("; ", _results)})");
                _bar = Value(_results[0], "creation");
                _lone = Value(_results[1], "creation");
                Request(BuildActions.PlacePart, new PlacePartArgs(Block, new GridCell(1, 0, 0), Creation: _bar));
                Request(BuildActions.PlacePart, new PlacePartArgs(Block, new GridCell(2, 0, 0), Creation: _bar));
            }
            if (_results.Count < 4 || !SameEverywhere())
                return false;
            Check(_results.All(r => r.Ok) && _host.Main.Creations.Find(_bar)!.Parts.Count == 3, "the bar has three blocks");
            return true;
        });
        Wait("the blocks settle", 20);
        AddPaint();
        AddLift();
        AddWeld();
        AddSledge();
        AddLateJoinAndLeave();
    }

    private void AddPaint()
    {
        Add("aim at the bar's middle", frame => LookAt(_client, PartCentre(_client, _bar, 2), frame));
        Add("the Paint Tool paints a block on every peer", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Tools.Use(Tools.Paint);
                while (Tools.Paint.Color != Red)
                    Tools.Paint.Cycle(1);
                Check(Tools.Paint.Paint(_results.Add), "the Paint Tool sends Paint");
            }
            if (_results.Count == 0 || !SameEverywhere())
                return false;
            Check(_results[0].Ok, $"the host paints it ({_results[0]})");
            Check(_host.Main.Creations.Find(_bar)!.FindPart(2)!.Color == Red, "the host's part is red");
            var mesh = Body(_client, _bar).GetNode<Node3D>("Part2").GetChild<MeshInstance3D>(0);
            var albedo = ((BaseMaterial3D)mesh.GetSurfaceOverrideMaterial(0)).AlbedoColor;
            Check(albedo.IsEqualApprox(new Color(Red)), $"the client shows it red ({albedo})");
            return true;
        });
        Add("the Paint Tool takes the paint off", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Tools.Paint.Erase(_results.Add);
            }
            if (_results.Count == 0 || !SameEverywhere())
                return false;
            Check(_results[0].Ok && _client.Main.Creations.Find(_bar)!.FindPart(2)!.Color is null, $"the paint is gone ({_results[0]})");
            return true;
        });
    }

    private void AddLift()
    {
        Add("the Lift picks the bar up", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Tools.Use(Tools.Lift);
                Tools.Lift.UpdateAim();
                Check(Tools.Lift.Aim is { Creation: { } c } && c == _bar, $"the Lift aims at the bar ({Tools.Lift.Aim})");
                Check(Tools.Lift.Place(_results.Add), "the Lift sends PlaceLift");
            }
            if (_results.Count == 0 || !SameEverywhere() || !_client.Main.Build.Lifts.ContainsKey(_client.Peer))
                return false;
            Check(_results[0].Ok, $"the host lifts it ({_results[0]})");
            var bar = _host.Main.Creations.Find(_bar)!;
            Check(bar.OnLift && Body(_host, _bar).Freeze, "the host holds the bar still");
            Check(Mathf.IsEqualApprox(Bottom(_host, _bar), _ground + 0.25f, 1e-3f), $"it rests on the platform (bottom {Bottom(_host, _bar):F3})");
            Check(_client.Main.Creations.Find(_bar)!.OnLift, "the client knows it is lifted");
            Check(Mathf.IsEqualApprox(Bottom(_client, _bar), Bottom(_host, _bar), 1e-3f), "and shows it at the same height");
            var lift = _client.Main.Build.Lifts[_client.Peer];
            Check(lift.State.Creation == _bar && lift.State.Level == 0, $"the client shows its Lift holding the bar ({lift.State})");
            return true;
        });
        Add("the Lift raises the bar two levels", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Check(Tools.Lift.Raise(2, _results.Add), "the Lift sends SetLiftLevel");
            }
            if (_results.Count == 0 || _client.Main.Build.Lifts[_client.Peer].State.Level != 2
                || !Mathf.IsEqualApprox(Bottom(_client, _bar), _ground + 0.75f, 1e-3f))
                return false;
            Check(_results[0].Ok, $"the host raises it ({_results[0]})");
            Check(Mathf.IsEqualApprox(Bottom(_host, _bar), _ground + 0.75f, 1e-3f), $"the bar is 0.5 m higher ({Bottom(_host, _bar):F3})");
            var top = _host.Main.Build.Lifts[_client.Peer].GetNode<CollisionShape3D>("Shape").Shape as BoxShape3D;
            Check(top is not null && Mathf.IsEqualApprox(top.Size.Y, 0.75f, 1e-3f), "the host's Lift column grew");
            return true;
        });
        Add("the Lift turns the bar", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Check(Tools.Lift.Turn(_results.Add), "the Lift sends TurnLift");
            }
            if (_results.Count == 0 || !SameEverywhere())
                return false;
            Check(_results[0].Ok, $"the host turns it ({_results[0]})");
            var box = Box(_host, _bar);
            Check(Mathf.IsEqualApprox(box.Max.Z - box.Min.Z, 0.75f, 1e-3f), $"the bar now runs along z ({box.Min} – {box.Max})");
            return true;
        });
        Add("taking the Lift away drops the bar", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Check(Tools.Lift.Remove(_results.Add), "the Lift sends RemoveLift");
            }
            if (_results.Count == 0 || _client.Main.Build.Lifts.Count != 0 || frame < 60)
                return false;
            Check(_results[0].Ok, $"the host takes it away ({_results[0]})");
            Check(!_host.Main.Creations.Find(_bar)!.OnLift && !Body(_host, _bar).Freeze, "the host simulates the bar again");
            Check(Bottom(_host, _bar) < _ground + 0.1f, $"it falls to the ground (bottom {Bottom(_host, _bar):F3})");
            return true;
        });
    }

    private void AddWeld()
    {
        Add("aim at the lone block", frame => LookAt(_client, PartCentre(_client, _lone, 1), frame));
        Add("the Weld Tool selects it", frame =>
        {
            Tools.Use(Tools.Weld);
            Check(Tools.Weld.Select() && Tools.Weld.Selected is { } s && s.Creation == _lone, $"the lone block is selected ({Tools.Weld.Selected})");
            return true;
        });
        Add("aim at the top of the bar", frame => LookAt(_client, PartCentre(_client, _bar, 1) + new Vector3(0, 0.125f, 0), frame));
        Add("the Weld Tool welds the block on top", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Tools.Weld.UpdateAim();
                Check(Tools.Weld.Aim is { } aim && aim.Creation.Id == _bar, $"it aims at the bar ({Tools.Weld.Aim})");
                Check(Tools.Weld.Weld(_results.Add), "the Weld Tool sends Weld");
            }
            if (_results.Count == 0 || !SameEverywhere())
                return false;
            Check(_results[0].Ok, $"the host welds it ({_results[0]})");
            var bar = _host.Main.Creations.Find(_bar)!;
            Check(_host.Main.Creations.Find(_lone) is null && _host.Main.Creations.Count == 1, "the lone block's creation is gone");
            Check(bar.Parts.Count == 4 && bar.Bodies.Count == 1, $"the bar has four blocks in one body ({bar.Parts.Count}, {bar.Bodies.Count})");
            var welded = bar.Parts.Last();
            Check(bar.PartAt(welded.Cell - new GridCell(0, 1, 0)) is not null, $"the block sits on top of the bar ({welded.Cell})");
            Check(Tools.Weld.Selected is null, "the selection clears");
            return true;
        });
        Wait("the bar settles", 30);
    }

    private void AddSledge()
    {
        Add("aim at the welded block", frame =>
        {
            var bar = _client.Main.Creations.Find(_bar)!;
            return LookAt(_client, PartCentre(_client, _bar, bar.Parts.Last().Id) + new Vector3(0, 0.1f, 0), frame);
        });
        Add("three Sledgehammer hits break it off", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                _sent = 0;
                Tools.Use(Tools.Sledge);
            }
            // Block durability 3 at 10 health each, 10 per hit; the tool waits out each swing.
            if (_results.Count < 3 && _results.Count == _sent)
            {
                if (Tools.Sledge.Swing(_results.Add))
                    _sent++;
            }
            if (_results.Count < 3 || !SameEverywhere())
                return false;
            Check(_results.All(r => r.Ok), $"every hit lands ({string.Join("; ", _results)})");
            Check(!_results[0].Value!.Value.GetProperty("broken").GetBoolean()
                && Mathf.IsEqualApprox(_results[1].Value!.Value.GetProperty("health").GetSingle(), 10f), "the first hits only damage it");
            Check(_results[2].Value!.Value.GetProperty("broken").GetBoolean(), "the third breaks it off");
            Check(_host.Main.Creations.Find(_bar)!.Parts.Count == 3, "the bar is back to three blocks");
            return true;
        });
    }

    private void AddLateJoinAndLeave()
    {
        Add("the client lifts the bar again", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Tools.Use(Tools.Lift);
                Request(ToolActions.PlaceLift, new PlaceLiftArgs(new Vec3(_barAt.X + 0.25f, _ground, _barAt.Z), _bar));
            }
            if (_results.Count == 0 || !_client.Main.Build.Lifts.ContainsKey(_client.Peer))
                return false;
            Check(_results[0].Ok, $"the bar is lifted ({_results[0]})");
            return true;
        });
        Add("a late joiner gets the Lift and the creations", frame =>
        {
            if (frame == 0)
                _late = _harness.StartClient("Late", mods: _mods);
            if (frame < 20 || _late.Main.Creations.Count < 1 || !_late.Main.Build.Lifts.ContainsKey(_client.Peer))
                return false;
            Check(SameEverywhere(), "its creations match the host's");
            Check(_late.Main.Build.Lifts[_client.Peer].State == _host.Main.Build.Tools!.LiftOf(_client.Peer), "its Lift matches the host's");
            return true;
        });
        Add("a player who leaves takes their Lift along", frame =>
        {
            if (frame == 0)
                _harness.Stop(_client);
            if (_host.Main.Build.Lifts.Count != 0 || _late.Main.Build.Lifts.Count != 0 || _late.Main.Creations.Find(_bar)?.OnLift != false)
                return false;
            Check(_host.Main.Build.Tools!.Lifts.Count == 0, "the host forgets the Lift");
            Check(!_host.Main.Creations.Find(_bar)!.OnLift && !_late.Main.Creations.Find(_bar)!.OnLift, "the bar drops");
            return true;
        });
    }

    private void Request<TArgs>(string action, TArgs args) => _client.Main.Actions.Request(action, args, _results.Add);

    private static int Value(ActionResult result, string name) => result.Value!.Value.GetProperty(name).GetInt32();

    private static CreationBody Body(Instance who, int creation) => who.Main.Creations.NodeOf(creation)!.Bodies[0];

    private static WorldBounds Box(Instance who, int creation)
    {
        var node = who.Main.Creations.NodeOf(creation)!;
        return CreationBounds.World(node.LocalBounds, [.. node.Bodies.Select(b => b.GlobalTransform.ToPose())]);
    }

    private static float Bottom(Instance who, int creation) => Box(who, creation).Min.Y;

    private static Vector3 PartCentre(Instance who, int creation, int partId)
    {
        var graph = who.Main.Creations.Find(creation)!;
        var part = graph.FindPart(partId)!;
        var body = who.Main.Creations.NodeOf(creation)!.Bodies[graph.BodyIndexOf(partId)];
        return body.GlobalTransform * PartGeometry.CentreMetres(graph.DefinitionOf(partId), part.Cell, part.Rotation).ToGodot();
    }

    /// <summary>True once every client shows exactly the host's graphs (paint and Lift state included).</summary>
    private bool SameEverywhere()
    {
        foreach (var client in _harness.Instances.Skip(1))
        {
            var mine = client.Main.Creations;
            if (mine.Count != _host.Main.Creations.Count)
                return false;
            foreach (var creation in _host.Main.Creations.All)
            {
                if (mine.Find(creation.Id) is not { } copy
                    || copy.OnLift != creation.OnLift
                    || !copy.Parts.SequenceEqual(creation.Parts)
                    || !copy.Connections.SequenceEqual(creation.Connections))
                    return false;
            }
        }
        return true;
    }

    private static bool Look(Instance who, float yaw, float pitch, int frame)
    {
        who.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Yaw = yaw, Pitch = pitch };
        return frame >= 10;
    }

    private static bool LookAt(Instance who, Vector3 target, int frame)
    {
        var eye = who.Main.LocalPlayer!.Camera.GlobalPosition;
        var d = target - eye;
        return Look(who, Mathf.Atan2(-d.X, -d.Z), Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), frame);
    }
}
