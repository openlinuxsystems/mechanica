using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using Mechanica.Core.World;
using Mechanica.Game.Building;
using Mechanica.Game.Logic;
using Mechanica.Game.Mods;
using Mechanica.Game.Player;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Connect Tool's nodes, drag and lines (UI spec PR 10), host plus a client on vanilla parts. With the tool out
/// every part with logic shows a node of its kind. Dragging from the switch to the engine links them, showing the
/// line before the host answers; while dragging, the other Creation's switch fades. A drag from the engine over one
/// bearing to another links both. The line between switch and engine shows "Switch → Gas Engine" when hovered;
/// grabbing it and letting go in the air disconnects it. A link the host refuses fades out red. Right click on a
/// bearing reverses it on the host. With the tool put away, a link flashes when its source's signal changes.
/// </summary>
public partial class ConnectTest : StagedTest
{
    private const int Board = 1, Other = 2;

    private static readonly Vector3 BoardAt = new(0, 0.15f, -1.6f);

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _switch, _engine, _wheel, _wheel2, _gate;

    protected override string Tag => "connect";

    protected override int StageTimeoutFrames => 900;

    private ToolBelt Tools => _client.Main.Tools!;

    private ConnectTool Connector => Tools.Connector;

    private LogicOverlay Overlay => _client.Main.LogicOverlay;

    private PlayerController Me => _client.Main.LocalPlayer!;

    private Creation HostBoard => _host.Main.Creations.Find(Board)!;

    public override void _Ready()
    {
        Add("a host and a client start on vanilla in Creative", frame =>
        {
            if (frame == 0)
            {
                _mods = ModLoader.Load([Path.Combine(ModPaths.ModsRoot, "vanilla")]);
                Check(!_mods.HasErrors, "vanilla loads");
                _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
                AddChild(_harness);
                _host = _harness.StartHost(mods: _mods);
                _client = _harness.StartClient("Client", mods: _mods);
            }
            return frame > 0 && _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null && _client.Main.Tools is not null;
        });
        Add("the host spawns a board of logic parts and a lone switch, and the client stands by them", frame =>
        {
            if (frame == 0)
            {
                SpawnBoard();
                _host.Main.PlayerSync.HostTeleport(_client.Peer, new Vector3(0, 0.1f, 0.4f));
            }
            return frame >= 30 && _client.Main.Creations.NodeOf(Board) is not null && _client.Main.Creations.NodeOf(Other) is not null;
        });
        Wait("the board settles", 60);
        Add("nodes show only with the Connect Tool out, one per part with logic, coloured by kind", frame =>
        {
            if (frame == 0)
            {
                Tools.Use(Tools.Builder);
                return false;
            }
            if (frame == 2)
            {
                Check(Overlay.DrawnNodes.Count == 0, "no nodes with a part in hand");
                Tools.Use(Connector);
                return false;
            }
            // The overlay draws on a render frame, and a slow machine can run several physics frames per render frame.
            if (frame < 4 || (Overlay.DrawnNodes.Count(n => n.Creation == Board) < 5 && frame < 120))
                return false;
            var mine = Overlay.DrawnNodes.Where(n => n.Creation == Board).Select(n => n.Part).Order().ToList();
            Check(mine.SequenceEqual(new[] { _switch, _engine, _wheel, _wheel2, _gate }.Order()), $"the board's five logic parts have nodes ({string.Join(", ", mine)})");
            Check(Overlay.DrawnNodes.Any(n => n.Creation == Other), "and so does the lone switch");
            Check(Connector.Nodes.First(n => n.Part == _engine && n.Creation == Board).Kind == PortKind.Power, "the engine's node is power");
            Check(Connector.Nodes.First(n => n.Part == _wheel && n.Creation == Board) is { Kind: PortKind.Bearing, Reversible: true }, "a bearing's is a reversible bearing node");
            return true;
        });
        Add("look at the switch", frame => LookAt(Centre(_switch), frame));
        Add("the switch's node is hovered", _ =>
        {
            Connector.UpdateHover();
            Check(Connector.HoveredNode is { Creation: Board } n && n.Part == _switch, $"the switch is hovered ({Connector.HoveredNode?.Part})");
            Check(Connector.Crosshair == Core.Ui.CrosshairState.Connection, "the crosshair pulses cyan");
            Check(Connector.HoverTip is null, "a node shows its part's own tooltip");
            Check(Connector.PressDrag(), "pressing starts a drag");
            Check(Connector.Dragging && Connector.DragFrom?.Part == _switch, "from the switch");
            return true;
        });
        Add("while dragging, the other Creation's node fades and the drag snaps to the engine", frame =>
        {
            if (!LookAt(Centre(_engine), frame))
                return false;
            Connector.UpdateHover();
            Overlay._Process(0);
            Check(Alpha(Other, Overlay.DrawnNodes.First(n => n.Creation == Other).Part) < 0.5f, "the lone switch fades to 30%");
            Check(Alpha(Board, _engine) == 1f, "the engine stays full");
            Check(Connector.HoveredNode?.Part == _engine, $"the drag snaps to the engine ({Connector.HoveredNode?.Part})");
            Check(Connector.ReleaseDrag(_results.Add), "letting go links");
            Check(Connector.Pending.Any(p => p.Adding && p.Link == new LogicLink(_switch, _engine)), "the line shows before the host answers");
            return true;
        });
        Add("the host links the switch to the engine", _ => Answered(1) && Check(HostBoard.Links.Contains(new LogicLink(_switch, _engine)), "switch → engine"));
        Add("look at the engine", frame => LookAt(Centre(_engine), frame));
        Add("press on the engine's node", _ => Check(Connector.PressDrag() && Connector.DragFrom?.Part == _engine, "the drag starts at the engine"));
        Add("drag over the first bearing", frame =>
        {
            if (!LookAt(Centre(_wheel), frame))
                return false;
            Connector.UpdateHover();
            return Check(Connector.Swept.Any(n => n.Part == _wheel), "the bearing joins the drag");
        });
        Add("let go on the second bearing", frame =>
        {
            if (!LookAt(Centre(_wheel2), frame))
                return false;
            Connector.UpdateHover();
            return Check(Connector.ReleaseDrag(_results.Add), "letting go links");
        });
        Add("the host links the engine to both bearings", _ => Answered(2)
            && Check(HostBoard.Links.Contains(new LogicLink(_engine, _wheel)) && HostBoard.Links.Contains(new LogicLink(_engine, _wheel2)), "engine → both bearings"));
        Add("look between the switch and the engine", frame => LookAt((Centre(_switch) + Centre(_engine)) / 2, frame));
        Add("the line between them is hovered, with its tooltip", frame =>
        {
            if (_client.Main.Creations.Find(Board)?.Links.Count != 3)
                return false;
            Connector.UpdateHover();
            if (Connector.HoveredLink is null && frame < 30)
                return false;
            Check(Connector.HoveredLink == (Board, new LogicLink(_switch, _engine)), $"the switch's line is hovered ({Connector.HoveredLink})");
            Check(Connector.HoverTip is { Title: "Switch → Gas Engine" } tip && tip.Body.StartsWith("Signal:"), $"the tooltip names both ends and the signal ({Connector.HoverTip?.Title} | {Connector.HoverTip?.Body})");
            Check(Overlay.Drawn.Count(d => d.Creation == Board) == 3, "three lines are drawn");
            Check(Connector.PressDrag() && Connector.Grabbed is not null, "pressing on the line grabs its end");
            return true;
        });
        Add("let go in the air", frame =>
        {
            if (!Look(0, 0.6f, frame))
                return false;
            Connector.UpdateHover();
            return Check(Connector.HoveredNode is null && Connector.ReleaseDrag(_results.Add), "letting go off any node disconnects");
        });
        Add("the host takes the link away", _ => Answered(1) && Check(!HostBoard.Links.Contains(new LogicLink(_switch, _engine)), "switch → engine is gone"));
        Add("look at the switch again", frame => LookAt(Centre(_switch), frame));
        Add("press on the switch", _ => Check(Connector.PressDrag(), "the drag starts"));
        Add("look at the logic gate", frame => LookAt(Centre(_gate), frame));
        Add("a link the host refuses fades out red", frame =>
        {
            if (frame == 0)
            {
                Connector.UpdateHover();
                Check(Connector.HoveredNode?.Part == _gate, "the drag snaps to the gate");
                // The host takes the gate away just as the client links to it.
                _host.Main.Actions.Request(BuildActions.RemovePart, new RemovePartArgs(Board, _gate), _ => { });
                Connector.ReleaseDrag(_results.Add);
                return false;
            }
            if (_results.Count == 0)
                return false;
            Check(!_results[0].Ok, "the host refuses it");
            Check(Connector.Pending.Any(p => p.Refused is not null), "the line rolls back");
            _results.Clear();
            return true;
        });
        Add("the red line is gone after the fade", frame => Connector.Pending.Count == 0 || (frame > 120 && Check(false, "gone")));
        Add("look at the first bearing", frame => LookAt(Centre(_wheel), frame));
        Add("right click reverses the bearing", frame =>
        {
            if (frame == 0)
            {
                Check(Connector.Reverse(_results.Add), "the bearing is hovered");
                Check(Connector.ShownReversed(Connector.HoveredNode!.Value), "the arrow flips at once");
                return false;
            }
            if (!Answered(1))
                return false;
            Check(PartSettings.Reversed(HostBoard.DefinitionOf(_wheel), HostBoard.FindPart(_wheel)!.Settings), "the host reverses it");
            return true;
        });
        Add("the client's bearing shows reversed", frame =>
        {
            Connector.UpdateHover();
            return Connector.Nodes.Any(n => n.Part == _wheel && n.Creation == Board && n.Reversed) || (frame > 60 && Check(false, "reversed on the client"));
        });
        Add("with the tool put away, a link flashes when its signal changes", frame =>
        {
            if (frame == 0)
            {
                Tools.Use(Tools.Builder);
                Request(LogicActions.ConnectParts, new LinkPartsArgs(Board, _switch, _engine));
                return false;
            }
            if (frame == 30)
                Request(LogicActions.ActivatePart, new LogicPartArgs(Board, _switch));
            if (frame < 30)
                return false;
            if (Overlay.Flashing.Contains((Board, new LogicLink(_switch, _engine))))
            {
                Check(Overlay.Drawn.Any(d => d.Link == new LogicLink(_switch, _engine)), "the flashing line is drawn");
                Check(!Overlay.Drawn.Any(d => d.Link.From == _wheel), "lines whose signal did not change stay hidden");
                return true;
            }
            return frame > 300 && Check(false, $"the switch's link flashed ({string.Join("; ", _results)})");
        });
        Add("the flash ends", frame => !Overlay.Drawn.Any(d => d.Link.From == _switch) || (frame > 120 && Check(false, "the line hides again")));
    }

    private void SpawnBoard()
    {
        var board = new Creation(Board, _mods);
        // The switch sits well away from the engine, so the line between them is long even on a small headless screen.
        for (var x = -9; x <= 5; x++)
            Place(board, "vanilla:metal_block", new GridCell(x, 0, 0));
        _switch = Place(board, "vanilla:switch", new GridCell(-9, 1, 0));
        _engine = Place(board, "vanilla:gas_engine", new GridCell(-2, 1, -1));
        var up = GridRotation.FromAxes(new GridCell(1, 0, 0), new GridCell(0, 1, 0));
        _wheel = Place(board, "vanilla:bearing", new GridCell(1, 1, 0), up);
        _wheel2 = Place(board, "vanilla:bearing", new GridCell(3, 1, 0), up);
        _gate = Place(board, "vanilla:logic_gate", new GridCell(5, 1, 0));
        board.SetAllBodyPoses(new Pose(BoardAt.ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Creations.Add(board, _host.Main.Assets);
        var other = new Creation(Other, _mods);
        Place(other, "vanilla:metal_block", GridCell.Zero);
        Place(other, "vanilla:switch", new GridCell(0, 1, 0));
        other.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(1.6f, 0.15f, -1.2f), System.Numerics.Quaternion.Identity));
        _host.Main.Creations.Add(other, _host.Main.Assets);
    }

    private static int Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        if (!creation.TryPlacePart(part, cell, rotation, out var placed, out var error))
            throw new InvalidOperationException(error);
        return placed.Id;
    }

    // The client's view of a board part's centre.
    private Vector3 Centre(int part) => ConnectionNodes.Centre(_client.Main.Creations.NodeOf(Board)!, part)!.Value;

    private float Alpha(int creation, int part) => Overlay.DrawnNodes.FirstOrDefault(n => n.Creation == creation && n.Part == part).Alpha;

    // True once count answers are in, all taken.
    private bool Answered(int count)
    {
        if (_results.Count < count)
            return false;
        Check(_results.All(r => r.Ok), $"the host takes them ({string.Join("; ", _results.Select(r => r.Message))})");
        _results.Clear();
        return true;
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

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
