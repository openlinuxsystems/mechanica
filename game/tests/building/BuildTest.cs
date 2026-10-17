using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Player;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Building by hand, host plus clients (chunk 7, D10). A client aims its Hammer at the ground and at a
/// Creation, places blocks on every face and a tipped, turned wedge, removes parts (splitting the
/// Creation), and gets clear refusals for bad requests. Every graph the host has must match on each
/// client, including a late joiner. Uses the fixture mod in <c>tests/building/mods</c>.
/// </summary>
public partial class BuildTest : StagedTest
{
    private const string Mod = "build_test";
    private const string Block = $"{Mod}:block";
    private const string Wedge = $"{Mod}:wedge";
    private static readonly GridCell Up = new(0, 1, 0);

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Instance _late = null!;
    private int _creation;
    private int _top;

    protected override string Tag => "build";

    public override void _Ready()
    {
        _mods = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/building/mods"));
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost(mods: _mods);
        _client = _harness.StartClient("Client", mods: _mods);

        Add("the client joins with a Hammer", _ =>
        {
            if (_client.Main.PlayerSync.Players.Count != 2 || _client.Main.Hammer is null || _host.Main.PlayerSync.Players.Count != 2)
                return false;
            Check(!_mods.HasErrors, $"fixture mod loads ({string.Join("; ", _mods.Diagnostics)})");
            var parts = _client.Main.Hammer.Parts;
            Check(parts.SequenceEqual([Block, $"{Mod}:plank", Wedge]), $"the Hammer offers the placeable parts only ({string.Join(", ", parts)})");
            Check(_client.Main.Hammer.Player == _client.Main.LocalPlayer, "the Hammer aims with the client's own player");
            return true;
        });
        Add("look down at the ground", frame => Look(_client, 0f, -0.6f, frame));
        Add("the client places a block on the ground with the Hammer", frame =>
        {
            var hammer = _client.Main.Hammer!;
            if (frame == 0)
            {
                hammer.UpdateAim();
                Check(hammer.Aim is { Creation: null, Position: not null }, $"aiming at the ground starts a new creation ({hammer.Aim})");
                Check(hammer.Place(_results.Add), "the Hammer sends PlacePart");
            }
            if (_results.Count == 0 || _client.Main.Creations.Count == 0)
                return false;
            Check(_results[0].Ok, $"the host accepts it ({_results[0]})");
            _creation = _results[0].Value!.Value.GetProperty("creation").GetInt32();
            var body = Body(_host, _creation);
            var origin = body.GlobalPosition;
            Check(Mathf.IsEqualApprox(origin.Y, 0.125f, 1e-3f), $"the block sits on the ground (y={origin.Y:F3})");
            Check(Mathf.IsEqualApprox(origin.X / 0.25f, Mathf.Round(origin.X / 0.25f), 1e-3f)
                && Mathf.IsEqualApprox(origin.Z / 0.25f, Mathf.Round(origin.Z / 0.25f), 1e-3f), $"and on the 0.25 m world grid ({origin})");
            Check(!body.Freeze && Body(_client, _creation).Freeze, "the host simulates it, the client mirrors it frozen");
            return true;
        });
        Wait("the block settles", 30);
        Add("blocks attach on every face", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                foreach (var face in GridCell.Directions.Where(d => d != -Up))
                    Place(_client, new PlacePartArgs(Block, face, Creation: _creation));
            }
            if (_results.Count < 5 || !SameEverywhere())
                return false;
            Check(_results.All(r => r.Ok), $"every face takes a block ({string.Join("; ", _results)})");
            var creation = _host.Main.Creations.Find(_creation)!;
            Check(creation.Parts.Count == 6 && creation.Bodies.Count == 1, $"they form one rigid body ({creation.Parts.Count} parts, {creation.Bodies.Count} bodies)");
            _top = creation.PartAt(Up)!.Id;
            return true;
        });
        Add("aim at the top block", frame =>
        {
            var target = Body(_client, _creation).GlobalTransform * new Vector3(0, 0.375f, 0);
            return LookAt(_client, target, frame);
        });
        Add("the client puts a tipped, turned wedge on top", frame =>
        {
            var hammer = _client.Main.Hammer!;
            if (frame == 0)
            {
                _results.Clear();
                Check(hammer.Select(Wedge), "the wedge can be selected");
                hammer.Tip();
                hammer.Turn();
                hammer.UpdateAim();
                var aim = hammer.Aim;
                Check(aim is { Creation: not null } && aim.Creation.Id == _creation && aim.HitPart == _top,
                    $"the Hammer sees the top block ({aim})");
                Check(aim!.Cell == Up + Up, $"the wedge goes above it ({aim.Cell})");
                Check(aim.Rotation.YAxis != Up, $"tipping turns its top away from the face ({aim.Rotation})");
                hammer.Place(_results.Add);
            }
            if (_results.Count == 0 || !SameEverywhere())
                return false;
            Check(_results[0].Ok, $"the wedge is placed ({_results[0]})");
            var part = _results[0].Value!.Value.GetProperty("part").GetInt32();
            var placed = _host.Main.Creations.Find(_creation)!.FindPart(part)!;
            Check(placed.Rotation == hammer.Aim!.Rotation && placed.Part == Wedge, $"with the Hammer's rotation ({placed.Rotation})");
            var shape = Body(_client, _creation).GetNode<CollisionShape3D>($"Shape{part}");
            Check(shape.Shape is ConvexPolygonShape3D && shape.Transform.Basis.IsEqualApprox(placed.Rotation.ToBasis()),
                "the client shows the wedge's collision turned the same way");
            return true;
        });
        AddRefusals();
        AddRemovals();
    }

    private void AddRefusals()
    {
        Add("bad requests are refused with reasons", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                var hostPlayer = _host.Main.LocalPlayer!.GlobalPosition + new Vector3(0, 0.9f, 0);
                var origin = Body(_host, _creation).GlobalPosition;
                Place(_client, new PlacePartArgs(Block, GridCell.Zero, Position: new Vec3(40, 0.125f, 0)));
                Place(_client, new PlacePartArgs(Block, Up, Creation: _creation));
                Place(_client, new PlacePartArgs($"{Mod}:ore", GridCell.Zero, Position: new Vec3(0, 0.125f, 2)));
                Place(_client, new PlacePartArgs($"{Mod}:missing", GridCell.Zero, Position: new Vec3(0, 0.125f, 2)));
                Place(_client, new PlacePartArgs(Block, GridCell.Zero, Position: hostPlayer.ToNumerics().ToVec3()));
                Place(_client, new PlacePartArgs(Block, GridCell.Zero, Position: origin.ToNumerics().ToVec3()));
                Place(_client, new PlacePartArgs(Block, new GridCell(5, 0, 0), Creation: _creation));
                _client.Main.Actions.RequestJson(BuildActions.RemovePart, """{"creation": 999, "part": 1}""", _results.Add);
            }
            if (_results.Count < 8)
                return false;
            Expect(0, "away; you can reach");
            Expect(1, "already taken");
            Expect(2, "cannot be placed");
            Expect(3, "Unknown part");
            Expect(4, "A player is in the way");
            Expect(5, $"Creation {_creation} is in the way");
            Expect(6, "does not attach");
            Expect(7, "no creation 999");
            Check(_host.Main.Creations.Count == 1 && _host.Main.Creations.Find(_creation)!.Parts.Count == 7, "nothing changed");
            return true;
        });
    }

    private void AddRemovals()
    {
        Add("removing the centre block splits the creation", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                var centre = _host.Main.Creations.Find(_creation)!.PartAt(GridCell.Zero)!.Id;
                _client.Main.Actions.Request(BuildActions.RemovePart, new RemovePartArgs(_creation, centre), _results.Add);
            }
            if (_results.Count == 0 || !SameEverywhere())
                return false;
            Check(_results[0].Ok, $"the host removes it ({_results[0]})");
            var split = _results[0].Value!.Value.GetProperty("split_off").GetArrayLength();
            Check(split == 4 && _host.Main.Creations.Count == 5, $"the four side blocks split off ({split} new, {_host.Main.Creations.Count} in all)");
            Check(_host.Main.Creations.Find(_creation)!.Parts.Count == 2, "the top block keeps the wedge");
            return true;
        });
        Wait("the pieces settle", 20);
        Add("aim at the nearest side block", frame =>
        {
            // Bodies share the grid frame they split from, so compare where their parts are.
            var eye = _client.Main.LocalPlayer!.Camera.GlobalPosition;
            var near = _host.Main.Creations.All.Where(c => c.Parts.Count == 1).MinBy(c => Centre(_client, c.Id).DistanceTo(eye))!;
            _creation = near.Id;
            return LookAt(_client, Centre(_client, near.Id) + new Vector3(0, 0.125f, 0), frame);
        });
        Add("the Hammer removes a whole one-block creation", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                var hammer = _client.Main.Hammer!;
                hammer.UpdateAim();
                Check(hammer.Aim?.Creation?.Id == _creation && hammer.Aim.HitPart is not null, $"the Hammer aims at the block ({hammer.Aim})");
                Check(hammer.Remove(_results.Add), "the Hammer sends RemovePart");
            }
            if (_results.Count == 0 || !SameEverywhere())
                return false;
            Check(_results[0].Ok && _results[0].Value!.Value.GetProperty("destroyed").GetBoolean(), $"the creation is destroyed ({_results[0]})");
            Check(_host.Main.Creations.Find(_creation) is null && _host.Main.Creations.Count == 4, "four creations are left");
            return true;
        });
        Add("a late joiner gets every creation", frame =>
        {
            if (frame == 0)
                _late = _harness.StartClient("Late", mods: _mods);
            if (frame < 20 || _late.Main.Creations.Count < 4)
                return false;
            Check(SameEverywhere(), "its creations match the host's");
            return true;
        });
    }

    private void Place(Instance who, PlacePartArgs args) => who.Main.Actions.Request(BuildActions.PlacePart, args, _results.Add);

    private void Expect(int index, string text) =>
        Check(_results[index] is { Ok: false, Code: ActionErrors.Rejected } r && r.Message!.Contains(text), $"refused: {_results[index]}");

    private static CreationBody Body(Instance who, int creation) => who.Main.Creations.NodeOf(creation)!.Bodies[0];

    /// <summary>World centre of a Creation's first part.</summary>
    private static Vector3 Centre(Instance who, int creation)
    {
        var graph = who.Main.Creations.Find(creation)!;
        var part = graph.Parts.First();
        return Body(who, creation).GlobalTransform * PartGeometry.CentreMetres(graph.DefinitionOf(part.Id), part.Cell, part.Rotation).ToGodot();
    }

    /// <summary>True once every client shows exactly the host's graphs, and their first bodies where the host's were sent.</summary>
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
                    || !copy.Parts.SequenceEqual(creation.Parts)
                    || !copy.Connections.SequenceEqual(creation.Connections))
                    return false;
            }
        }
        return true;
    }

    private bool Look(Instance who, float yaw, float pitch, int frame)
    {
        who.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Yaw = yaw, Pitch = pitch };
        return frame >= 10;
    }

    private bool LookAt(Instance who, Vector3 target, int frame)
    {
        var eye = who.Main.LocalPlayer!.Camera.GlobalPosition;
        var d = target - eye;
        var yaw = Mathf.Atan2(-d.X, -d.Z);
        var pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
        return Look(who, yaw, pitch, frame);
    }
}
