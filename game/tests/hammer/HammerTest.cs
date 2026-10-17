using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Player;
using Mechanica.Game.Building;
using Mechanica.Game.Player;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Hammer's gestures (UI spec PR 9), host plus a client. The ghost is blue where a block fits and red where it
/// would not, and a click on a red ghost sends nothing. A click places on release and leaves a ghost until the host
/// answers. Holding still enters alignment mode with arrows; dragging along the ground lays a row on the host in one
/// request, and Q steps the drag's plane. A placement the host refuses fades out red. Holding right click fills the
/// ring and removes the block only once it is full, letting go early cancels, and a right-click drag along the row
/// removes the blocks it marks.
/// </summary>
public partial class HammerTest : StagedTest
{
    private const string Block = "vanilla:aluminum_block";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _creation;
    private int _parts;
    private float _yaw;
    private PendingGhost? _ghost;
    private bool _sawRing;
    private Vector3 _from;
    private int _row;
    private Vector3 _to;

    protected override string Tag => "hammer";

    protected override int StageTimeoutFrames => 900;

    private ToolBelt Tools => _client.Main.Tools!;

    private HammerTool Hammer => Tools.Hammer;

    private PlayerController Me => _client.Main.LocalPlayer!;

    private int HostParts => _host.Main.Creations.Find(_creation)?.Parts.Count ?? 0;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins with the Hammer", _ =>
        {
            if (_client.Main.PlayerSync.Players.Count != 2 || _client.Main.Tools is null || _host.Main.PlayerSync.Players.Count != 2)
                return false;
            Tools.Use(Hammer);
            return Check(Hammer.Select(Block), "the block is placeable");
        });
        Add("look at the ground ahead", frame => Look(0, -0.7f, frame));
        Add("the ghost is blue where the block fits, red where it would not", frame =>
        {
            if (frame == 0)
            {
                Check(Hammer.Aim is not null && Hammer.PreviewValid, $"it fits ({Hammer.PreviewProblem})");
                Check(Hammer.Crosshair == Core.Ui.CrosshairState.Valid, "the crosshair is blue");
                Hammer.CanAfford = _ => false;
                return false;
            }
            if (frame < 2)
                return false;
            Check(!Hammer.PreviewValid && Hammer.PreviewProblem is not null, "with none left it is red");
            Check(Hammer.Crosshair == Core.Ui.CrosshairState.Default, "and the crosshair white");
            Hammer.PressPlace();
            Check(Hammer.ReleasePlace(_results.Add), "the click is taken");
            Check(_results.Count == 0 && Hammer.LastResult is null && !PendingGhosts().Any(), "but nothing is sent");
            Hammer.CanAfford = null;
            return true;
        });
        Add("a click places on release and leaves a ghost until the host answers", frame =>
        {
            if (frame == 0)
            {
                Hammer.UpdateAim();
                Hammer.PressPlace();
                Check(Hammer.PlacePhase == GesturePhase.Pressed && _results.Count == 0, "pressing alone sends nothing");
                Hammer.ReleasePlace(_results.Add);
                Check(PendingGhosts().Any(), "a ghost stays where the block goes");
                return false;
            }
            if (_results.Count == 0)
                return false;
            Check(_results[0].Ok, $"the host takes it ({_results[0].Message})");
            _creation = _results[0].Value!.Value.GetProperty("creation").GetInt32();
            _results.Clear();
            return HostParts == 1 || Check(false, "the host has the block");
        });
        Add("the ghost goes once the host answers", frame => !PendingGhosts().Any(g => !g.RollingBack) || (frame > 60 && Check(false, "the ghost went")));
        Add("look a little to the side of the block", frame => Look(_yaw = 0.25f, -0.7f, frame));
        Add("holding still aligns, with arrows", frame =>
        {
            if (frame == 0)
            {
                Hammer.PressPlace();
                return false;
            }
            Hammer.UpdateAim();
            if (Hammer.PlacePhase != GesturePhase.Holding)
                return frame < 120 ? false : Check(false, $"alignment mode ({Hammer.PlacePhase})");
            Check(Hammer.Arrows.Visible && Hammer.Arrows.GetChildCount() == 4, "four arrows show the ways a drag can grow");
            Check(Hammer.Plane == 1, $"on the ground the drag spreads over the floor plane ({Hammer.Plane})");
            Hammer.CyclePlane();
            Check(Hammer.Plane == 2, "Q steps the plane");
            Hammer.CyclePlane();
            Hammer.CyclePlane();
            return true;
        });
        Add("dragging along the ground grows a row", frame =>
        {
            // Turn slowly, so the aim walks along the ground.
            Look(_yaw -= 0.02f, -0.7f, 0);
            if (Hammer.PlacePhase != GesturePhase.Dragging || Hammer.DragCells.Count < 4)
                return frame < 300 ? false : Check(false, $"a drag ({Hammer.PlacePhase}, {Hammer.DragCells.Count})");
            Check(Hammer.DragCells.Select(c => c.Y).Distinct().Count() == 1, "all in one plane");
            return true;
        });
        Add("letting go places the row in one request", frame =>
        {
            if (frame == 0)
            {
                _parts = Hammer.DragCells.Count;
                Check(Hammer.ReleasePlace(_results.Add), "the drag ends");
                Check(PendingGhosts().Count() >= _parts, "every block has a ghost until the host answers");
                return false;
            }
            if (_results.Count == 0)
                return false;
            Check(_results[0].Ok, $"the host takes it ({_results[0].Message})");
            _results.Clear();
            return true;
        });
        Add("the host has the row", frame =>
        {
            var row = _host.Main.Creations.All.Sum(c => c.Parts.Count);
            if (row < 1 + _parts / 2)
                return frame < 120 ? false : Check(false, $"the row is placed ({row} parts for {_parts})");
            Check(true, $"{row} parts on the host after a drag of {_parts}");
            return true;
        });
        Add("look at the first block", frame => LookAt(PartCentre(_creation, 1), frame));
        Add("a refused placement fades out red", frame =>
        {
            if (frame == 0)
            {
                Hammer.UpdateAim();
                // The host takes the block away just as the client places against it.
                if (!Check(Hammer.Aim is { Creation: not null, HitPart: not null }, "aiming at a block"))
                    return true;
                var (target, part) = (Hammer.Aim!.Creation!.Id, Hammer.Aim.HitPart!.Value);
                Hammer.Place(_results.Add);
                _ghost = PendingGhosts().LastOrDefault();
                _host.Main.Actions.Request(BuildActions.RemovePart, new RemovePartArgs(target, part), _ => { });
                return false;
            }
            if (_results.Count == 0)
                return false;
            if (_ghost is null)
                return Check(false, "a ghost was left");
            if (_results[0].Ok)
                return Check(false, "the host refuses it");
            Check(!IsInstanceValid(_ghost) || _ghost.RollingBack, "the ghost rolls back");
            Check(!IsInstanceValid(_ghost) || _ghost.Colour.R > _ghost.Colour.B, $"in red ({(IsInstanceValid(_ghost) ? _ghost.Colour : default)})");
            _results.Clear();
            return true;
        });
        Add("the red ghost is gone after the fade", frame => !IsInstanceValid(_ghost) || (frame > 60 && Check(false, "gone")));
        Add("look at a block of the row", frame => LookAt(AnyPartCentre(out _), frame));
        Add("letting go of right click early cancels", frame =>
        {
            if (frame == 0)
            {
                _parts = _host.Main.Creations.All.Sum(c => c.Parts.Count);
                Hammer.PressRemove();
                Check(Hammer.RemovePhase == GesturePhase.Pressed, "pressed on a block");
                return false;
            }
            if (Hammer.Progress is not { } progress || progress <= 0)
                return frame < 30 ? false : Check(false, "the ring starts filling");
            Check(progress < 1, $"not full yet ({progress})");
            Hammer.ReleaseRemove(_results.Add);
            Check(Hammer.Progress is null, "the ring goes");
            return true;
        });
        Wait("nothing comes off", 30);
        Add("holding right click until the ring is full removes the block", frame =>
        {
            if (frame == 0)
            {
                Check(_host.Main.Creations.All.Sum(c => c.Parts.Count) == _parts && _results.Count == 0, "the early release removed nothing");
                Hammer.PressRemove();
                return false;
            }
            _sawRing |= Hammer.Progress is > 0.5f and < 1f;
            if (_host.Main.Creations.All.Sum(c => c.Parts.Count) != _parts - 1)
                return frame < 240 ? false : Check(false, "the block comes off");
            Check(_sawRing, "the ring filled on the way");
            Hammer.ReleaseRemove();
            return true;
        });
        Add("a straight row stands ahead", frame =>
        {
            if (frame == 0)
            {
                var at = Me.GlobalPosition + new Vector3(-0.5f, 0.125f - Me.GlobalPosition.Y, -2.5f);
                var cells = Enumerable.Range(0, 5).Select(x => new Core.Creations.GridCell(x, 0, 0)).ToList();
                _client.Main.Actions.Request(BuildActions.PlaceParts, new PlacePartsArgs(Block, cells, Position: at.ToNumerics().ToVec3()), _results.Add);
                return false;
            }
            if (_results.Count == 0)
                return false;
            Check(_results[0].Ok, $"placed ({_results[0].Message})");
            _row = _results[0].Value!.Value.GetProperty("creation").GetInt32();
            _results.Clear();
            return true;
        });
        Wait("it arrives", 20);
        Add("look at the row's first block", frame =>
        {
            (_from, _to) = (PartCentre(_row, 1), PartCentre(_row, 5));
            return LookAt(_from, frame);
        });
        Add("a right-click drag marks blocks along the row", frame =>
        {
            if (frame == 0)
            {
                _parts = _host.Main.Creations.All.Sum(c => c.Parts.Count);
                Hammer.PressRemove();
                Check(Hammer.RemovePhase == GesturePhase.Pressed, "pressed on the row's end");
                return false;
            }
            // Sweep the aim along the row to its other end.
            LookAt(_from.Lerp(_to, Mathf.Min(1, frame / 40f)), 0);
            if (Hammer.RemovePhase != GesturePhase.Dragging || Hammer.DragParts.Count < 3)
                return frame < 240 ? false : Check(false, $"a removal drag ({Hammer.RemovePhase}, {Hammer.DragParts.Count})");
            Check(Hammer.Progress is null, "a drag has no ring");
            return true;
        });
        Add("letting go removes the marked blocks", frame =>
        {
            if (frame == 0)
            {
                _parts -= Hammer.DragParts.Count;
                Hammer.ReleaseRemove(_results.Add);
                return false;
            }
            if (_results.Count == 0)
                return false;
            Check(_results[0].Ok, $"the host takes it ({_results[0].Message})");
            var left = _host.Main.Creations.All.Sum(c => c.Parts.Count);
            return left == _parts || (frame > 120 && Check(false, $"{_parts} left ({left})"));
        });
    }

    private IEnumerable<PendingGhost> PendingGhosts() => Hammer.GetChildren().OfType<PendingGhost>().Where(g => !g.IsQueuedForDeletion());

    private Vector3 PartCentre(int creation, int part) =>
        _client.Main.Creations.NodeOf(creation)!.Bodies[0].GlobalTransform * BuildActionsCentre(creation, part);

    private Vector3 BuildActionsCentre(int creation, int part)
    {
        var c = _client.Main.Creations.Find(creation)!;
        var p = c.FindPart(part)!;
        return Core.Creations.PartGeometry.CentreMetres(c.DefinitionOf(part), p.Cell, p.Rotation).ToGodot();
    }

    // The centre of some part still on the client, nearest the player.
    private Vector3 AnyPartCentre(out int creation)
    {
        var best = (Creation: 0, Part: 0, Distance: float.MaxValue);
        foreach (var c in _client.Main.Creations.All)
        {
            foreach (var p in c.Parts)
            {
                var d = PartCentre(c.Id, p.Id).DistanceTo(Me.GlobalPosition);
                if (d < best.Distance)
                    best = (c.Id, p.Id, d);
            }
        }
        creation = best.Creation;
        return PartCentre(best.Creation, best.Part);
    }

    // The centres of the two parts furthest apart in the Creation with the most parts.
    private (Vector3 From, Vector3 To) RowEnds()
    {
        var row = _client.Main.Creations.All.MaxBy(c => c.Parts.Count)!;
        var centres = row.Parts.Select(p => PartCentre(row.Id, p.Id)).ToList();
        var pair = centres.SelectMany(a => centres.Select(b => (a, b))).MaxBy(p => p.a.DistanceTo(p.b));
        return pair;
    }

    private bool Look(float yaw, float pitch, int frame)
    {
        Me.CurrentInput = PlayerInput.None with { Yaw = yaw, Pitch = pitch };
        return frame >= 10;
    }

    private bool LookAt(Vector3 target, int frame)
    {
        var d = target - Me.Camera.GlobalPosition;
        return Look(Mathf.Atan2(-d.X, -d.Z), Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), frame);
    }
}
