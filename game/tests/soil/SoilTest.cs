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
/// Soil goes only flat on the ground, like Scrap Mechanic's, host plus a client. The Hammer's ghost stands upright on
/// the ground, the tip key leaves it so and the HUD offers no tip; placed, the host has it upright and anchored. A
/// sideways request sent straight to the host is refused, and aimed at the top of a block the ghost is red and a click
/// sends nothing.
/// </summary>
public partial class SoilTest : StagedTest
{
    private const string Soil = "vanilla:soil";
    private const string Block = "vanilla:aluminum_block";
    private static readonly GridCell Up = new(0, 1, 0);

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _block;

    protected override string Tag => "soil";

    private ToolBelt Tools => _client.Main.Tools!;

    private HammerTool Hammer => Tools.Hammer;

    private PlayerController Me => _client.Main.LocalPlayer!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins with the Hammer and soil", _ =>
        {
            if (_client.Main.PlayerSync.Players.Count != 2 || _client.Main.Tools is null || _host.Main.PlayerSync.Players.Count != 2)
                return false;
            Tools.Use(Hammer);
            return Check(Hammer.Select(Soil), "soil is placeable");
        });
        Add("look at the ground ahead", frame => Look(0, -0.7f, frame));
        Add("the ghost stands upright on the ground and will not tip", frame =>
        {
            Hammer.UpdateAim();
            if (Hammer.Aim is not { } aim)
                return frame < 60 ? false : Check(false, "aiming at the ground");
            Check(aim.Rotation.YAxis == Up && Hammer.PreviewValid, $"upright and blue ({aim.Rotation}, {Hammer.PreviewProblem})");
            Check(!Hammer.HudText().Contains(" tip"), $"the HUD offers no tip ({Hammer.HudText()})");
            Hammer.Tip();
            Hammer.UpdateAim();
            Check(Hammer.Aim!.Rotation.YAxis == Up && Hammer.PreviewValid, $"the tip key leaves it upright ({Hammer.Aim.Rotation})");
            Hammer.Turn();
            Hammer.UpdateAim();
            Check(Hammer.Aim!.Rotation.YAxis == Up, $"turning keeps it flat ({Hammer.Aim.Rotation})");
            return true;
        });
        Add("a click places it flat on the host", frame =>
        {
            if (frame == 0)
            {
                Hammer.UpdateAim();
                Hammer.Place(_results.Add);
                return false;
            }
            if (_results.Count == 0)
                return false;
            Check(_results[0].Ok, $"the host takes it ({_results[0].Message})");
            var id = _results[0].Value!.Value.GetProperty("creation").GetInt32();
            _results.Clear();
            var creation = _host.Main.Creations.Find(id);
            Check(creation?.Parts.Count == 1 && creation.Parts.First().Rotation.YAxis == Up, "the host has it upright");
            Check(creation?.IsGrounded == true, "anchored to the ground");
            return true;
        });
        Add("the host refuses soil on its side", frame =>
        {
            if (frame == 0)
            {
                var at = Me.GlobalPosition + new Vector3(1.5f, 0.125f - Me.GlobalPosition.Y, -1.5f);
                var sideways = GridRotation.FromAxes(new GridCell(1, 0, 0), new GridCell(0, 0, 1));
                _client.Main.Actions.Request(BuildActions.PlacePart, new PlacePartArgs(Soil, GridCell.Zero, sideways, Position: at.ToNumerics().ToVec3()), _results.Add);
                return false;
            }
            if (_results.Count == 0)
                return false;
            Check(!_results[0].Ok && _results[0].Message?.Contains("flat on the ground") == true, $"refused ({_results[0].Message})");
            _results.Clear();
            return true;
        });
        Add("a block stands to one side", frame =>
        {
            if (frame == 0)
            {
                var at = Me.GlobalPosition + new Vector3(-1.5f, 0.125f - Me.GlobalPosition.Y, -1.5f);
                _client.Main.Actions.Request(BuildActions.PlacePart, new PlacePartArgs(Block, GridCell.Zero, Position: at.ToNumerics().ToVec3()), _results.Add);
                return false;
            }
            if (_results.Count == 0)
                return false;
            Check(_results[0].Ok, $"placed ({_results[0].Message})");
            _block = _results[0].Value!.Value.GetProperty("creation").GetInt32();
            _results.Clear();
            return true;
        });
        Wait("it arrives", 20);
        Add("look at the block's top", frame =>
        {
            var body = _client.Main.Creations.NodeOf(_block)?.Bodies[0];
            return body is not null && LookAt(body.GlobalPosition + new Vector3(0, 0.12f, 0), frame);
        });
        Add("on the block the ghost is red and a click sends nothing", frame =>
        {
            Hammer.UpdateAim();
            if (Hammer.Aim is not { Creation: not null })
                return frame < 60 ? false : Check(false, "aiming at the block");
            Check(!Hammer.PreviewValid && Hammer.PreviewProblem?.Contains("flat on the ground") == true, $"red ({Hammer.PreviewProblem})");
            Hammer.PressPlace();
            Hammer.ReleasePlace(_results.Add);
            Check(_results.Count == 0 && _host.Main.Creations.Find(_block)?.Parts.Count == 1, "nothing is sent");
            return true;
        });
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
