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
/// A Soil Bag makes a soil mound flat on the ground, like Scrap Mechanic's, host plus a client. Aimed at the ground the
/// build ghost is the mound, upright, the tip key leaves it so and the HUD offers no tip; with Force build held it is the
/// bag itself. Placed, the host has the mound upright and anchored, and the client sees it spill out of the bag and
/// starts it dry. A sideways mound sent straight to the host is refused, and aimed at the top of a block the ghost is
/// the bag, which sets down there.
/// </summary>
public partial class SoilTest : StagedTest
{
    private const string Soil = "vanilla:soil";
    private const string Bag = "vanilla:soil_bag";
    private const string Block = "vanilla:aluminum_block";
    private static readonly GridCell Up = new(0, 1, 0);

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _block;
    private int _mound;
    private bool _sent;

    protected override string Tag => "soil";

    private ToolBelt Tools => _client.Main.Tools!;

    private BuildTool Builder => Tools.Builder;

    private PlayerController Me => _client.Main.LocalPlayer!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins with a Soil Bag in hand", _ =>
        {
            if (_client.Main.PlayerSync.Players.Count != 2 || _client.Main.Tools is null || _host.Main.PlayerSync.Players.Count != 2)
                return false;
            Tools.Use(Builder);
            Check(!Builder.Parts.Contains(Soil), "the mound is not an item to pick");
            Check(Builder.Select(Bag), "the bag is placeable");
            return Check(Builder.Prop == Core.Characters.CharacterProp.SoilBag, $"carried in both hands ({Builder.Prop})");
        });
        Add("look at the ground ahead", frame => Look(0, -0.7f, frame));
        Add("the ghost stands upright on the ground and will not tip", frame =>
        {
            Builder.UpdateAim();
            if (Builder.Aim is not { } aim)
                return frame < 60 ? false : Check(false, "aiming at the ground");
            Check(Builder.Placing == Soil, $"the ghost is the mound ({Builder.Placing})");
            Check(aim.Rotation.YAxis == Up && Builder.PreviewValid, $"upright and blue ({aim.Rotation}, {Builder.PreviewProblem})");
            Check(!Builder.HudText().Contains(" tip"), $"the HUD offers no tip ({Builder.HudText()})");
            Builder.Tip();
            Builder.UpdateAim();
            Check(Builder.Aim!.Rotation.YAxis == Up && Builder.PreviewValid, $"the tip key leaves it upright ({Builder.Aim.Rotation})");
            Builder.Turn();
            Builder.UpdateAim();
            Check(Builder.Aim!.Rotation.YAxis == Up, $"turning keeps it flat ({Builder.Aim.Rotation})");
            Builder.ForceBuild = true;
            Builder.UpdateAim();
            Check(Builder.Placing == Bag, $"with Force build it is the bag ({Builder.Placing})");
            Builder.ForceBuild = false;
            Builder.UpdateAim();
            return true;
        });
        Add("a click places it flat on the host", frame =>
        {
            if (frame == 0)
            {
                Builder.UpdateAim();
                Builder.Place(_results.Add);
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
            Check(creation?.Parts.First().Part == Soil, $"it is a mound ({creation?.Parts.First().Part})");
            _mound = id;
            return true;
        });
        Add("the client sees it spill out of the bag, dry", frame =>
        {
            if (_client.Main.SoilLooks.LookOf(_mound, _client.Main.Creations.Find(_mound)?.Parts.First().Id ?? -1) is not { } look)
                return frame < 120 ? false : Check(false, "the client shows the mound");
            Check(_client.Main.SoilLooks.Spills == 1, $"spilled once ({_client.Main.SoilLooks.Spills})");
            Check(look.Wet == 0 && look.Drying == 0, $"dry ({look})");
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
        Add("on the block the ghost is the bag, and it sets down there", frame =>
        {
            if (!_sent)
            {
                Builder.UpdateAim();
                if (Builder.Aim is not { Creation: not null })
                    return frame < 60 ? false : Check(false, "aiming at the block");
                Check(Builder.Placing == Bag && Builder.PreviewValid, $"the bag, blue ({Builder.Placing}, {Builder.PreviewProblem})");
                _sent = Builder.Place(_results.Add);
                return !_sent && Check(false, "the bag is sent");
            }
            if (_results.Count == 0)
                return frame < 180 ? false : Check(false, "the host answers");
            Check(_results[0].Ok, $"placed ({_results[0].Message})");
            Check(_host.Main.Creations.Find(_block)?.Parts.Any(p => p.Part == Bag) == true, "the bag is on the block");
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
