using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using Mechanica.Core.Ui;
using Mechanica.Game.Player;
using Mechanica.Game.Settings;
using Mechanica.Game.Ui;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The in-world HUD (UI spec PR 7), host plus a client. The client's crosshair is blue with a placement and white
/// with nothing to do, and pulses cyan over a part with logic in the Connect Tool's hands. Aiming at a part fades
/// its tooltip in after 0.25 s (name, description, what Use does) and drops it at once when the aim leaves; a switch's
/// tooltip shows the host's signal, its connections and its durability, which follow the host as the switch is
/// turned on and damaged. A menu over the world or Hide GUI hides the HUD. Holding Use dismisses a tip with a ring
/// that fills as it is held, and losing focus cancels the hold. The camera starts third-person, X and C step it in
/// to first-person and back out, and aim rays start level with the head so third-person reaches as far.
/// </summary>
public partial class HudTest : StagedTest
{
    private const string Block = "vanilla:aluminum_block";
    private const string Switch = "vanilla:switch";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private WorldHud _hud = null!;
    private HudRoot _root = null!;
    private TutorialHints _hints = null!;
    private bool _covered;
    private bool _holdUse;
    private int _block;
    private int _switch;
    private ulong _aimedAt;

    protected override string Tag => "hud";

    protected override int StageTimeoutFrames => 900;

    private Building.ToolBelt Tools => _client.Main.Tools!;

    private PlayerController Me => _client.Main.LocalPlayer!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins and gets a HUD", _ =>
        {
            if (_client.Main.PlayerSync.Players.Count != 2 || _client.Main.Tools is null || _host.Main.PlayerSync.Players.Count != 2)
                return false;
            _root = new HudRoot { Name = "HudRoot", ReadDeviceInput = false };
            AddChild(_root);
            _hud = new WorldHud
            {
                Name = "WorldHud", Players = _client.Main.PlayerSync, Actions = _client.Main.Actions, Build = _client.Main.Build,
                Tool = () => _client.Main.Tools?.Current, Covered = () => _covered,
                Interaction = () => Tools.Current == Tools.Connector ? null : "turn on the Switch",
            };
            // In the test's own window, so the tooltip is placed on a real-sized screen.
            AddChild(_hud);
            return true;
        });
        Add("the client places a block and a switch", frame =>
        {
            if (frame == 0)
            {
                var me = Me.GlobalPosition;
                var at = new Vector3(Mathf.Snapped(me.X, 0.25f) - 0.25f, 0.125f, Mathf.Snapped(me.Z, 0.25f) - 2.5f);
                Request(BuildActions.PlacePart, new PlacePartArgs(Block, GridCell.Zero, Position: at.ToNumerics().ToVec3()));
                Request(BuildActions.PlacePart, new PlacePartArgs(Switch, GridCell.Zero, Position: (at + new Vector3(1.5f, 0, 0)).ToNumerics().ToVec3()));
            }
            if (_results.Count < 2)
                return false;
            if (frame < 1000 && _block == 0)
            {
                Check(_results.All(r => r.Ok), $"both are placed ({string.Join("; ", _results)})");
                _block = Value(_results[0], "creation");
                _switch = Value(_results[1], "creation");
            }
            return _client.Main.Creations.NodeOf(_block) is not null && _client.Main.Creations.NodeOf(_switch) is not null;
        });
        Wait("they settle", 20);

        Add("look at the ground beside them", frame => Look(0, -0.9f, frame));
        Add("the crosshair is blue with a placement and white with nothing to do", _ =>
        {
            Tools.Use(Tools.Hammer);
            Tools.Hammer.UpdateAim();
            Check(Tools.Hammer.Aim is not null && _hud.State == CrosshairState.Valid, $"the Hammer's placement turns it blue ({_hud.State})");
            Check(_hud.CrosshairControl is CrosshairView { Colour: var c } && c.IsEqualApprox(UiTokens.Blue), "the cross is drawn blue");
            Tools.Use(Tools.Connector);
            return true;
        });
        Add("with the Connect Tool over the ground", frame =>
        {
            if (frame < 2)
                return false;
            Check(_hud.State == CrosshairState.Default, $"nothing to do is white ({_hud.State})");
            Check(((CrosshairView)_hud.CrosshairControl).Colour.IsEqualApprox(UiTokens.Text), "the cross is drawn white");
            Check(!_hud.TooltipShowing, "no tooltip over the ground");
            return true;
        });

        Add("aim at the block", frame => LookAt(PartCentre(_block, 1), frame));
        Add("its tooltip waits a quarter second, then fades in", frame =>
        {
            // The aim stage already held the block for ten frames, so look away and back to start the wait.
            if (frame == 0)
            {
                Look(Mathf.Pi, -0.9f, 0);
                return false;
            }
            if (frame == 2)
            {
                Check(!_hud.TooltipShowing, "aiming away drops it at once");
                LookAt(PartCentre(_block, 1), 0);
                _aimedAt = Time.GetTicksMsec();
                return false;
            }
            if (frame < 2 || !_hud.TooltipShowing)
                return false;
            var waited = Time.GetTicksMsec() - _aimedAt;
            Check(waited >= 200, $"it waited before showing ({waited} ms)");
            return true;
        });
        Wait("the fade ends", 10);
        Add("the block's tooltip", _ =>
        {
            Check(_hud.TooltipText.StartsWith("Aluminum Block\nLight metal for aircraft."), $"name and description ({_hud.TooltipText})");
            Check(!_hud.TooltipText.Contains("Signal") && !_hud.TooltipText.Contains("Durability"), "a plain block has no status line");
            Check(_hud.Tooltip.Modulate.A >= 0.99f, "fully in");
            var rect = _hud.Tooltip.GetGlobalRect();
            var screen = _hud.GetViewport().GetVisibleRect();
            Check(screen.Encloses(rect), $"on screen ({rect} in {screen})");
            return true;
        });

        Add("aim at the switch", frame => LookAt(PartCentre(_switch, 1), frame));
        Add("the switch pulses cyan and its tooltip reads the host", _ =>
        {
            if (!_hud.TooltipShowing || !_hud.TooltipText.Contains("Signal off"))
                return false;
            Check(_hud.State == CrosshairState.Connection, $"a logic part under the Connect Tool pulses cyan ({_hud.State})");
            return true;
        });
        Add("the switch's tooltip has its signal, connections and durability", _ =>
        {
            Check(_hud.TooltipText.StartsWith("Switch\n"), $"its name ({_hud.TooltipText})");
            Check(_hud.TooltipText.Contains("0 connections") && _hud.TooltipText.Contains("Durability 100%"), $"connections and durability ({_hud.TooltipText})");
            Tools.Use(Tools.Hammer);
            return true;
        });
        Add("with Use on offer the tooltip says what it does", frame =>
            frame > 1 && Check(_hud.TooltipText.Contains($"[{GameInput.Label(Core.Input.InputActions.Interact)}] turn on the Switch"), $"the key line ({_hud.TooltipText})")
            && Check(_hud.State == CrosshairState.Valid, $"and the crosshair is blue ({_hud.State})"));
        Add("the switch is turned on and damaged on the host", frame =>
        {
            if (frame == 0)
            {
                Request(LogicActions.ActivatePart, new LogicPartArgs(_switch, 1));
                var hosted = _host.Main.Creations.Find(_switch)!;
                hosted.SetPartDamage(1, _host.Main.Build.Settings!.Sledge.HealthOf(hosted.DefinitionOf(1).Durability)!.Value / 2);
            }
            return _hud.TooltipText.Contains("Signal on") && _hud.TooltipText.Contains("Durability 50%");
        });

        Add("a menu over the world hides the crosshair and tooltip", frame =>
        {
            _covered = true;
            if (frame < 2)
                return false;
            Check(!_hud.CrosshairControl.Visible && !_hud.TooltipShowing, "both hide");
            _covered = false;
            return true;
        });
        Add("Hide GUI hides the HUD and shows it again", frame =>
        {
            if (frame == 0)
            {
                _root.Toggle();
                return false;
            }
            if (frame == 1)
            {
                Check(HudRoot.Hidden && !_hud.Visible, "hidden");
                _root.Toggle();
                return false;
            }
            Check(!HudRoot.Hidden && _hud.Visible, "back");
            return true;
        });

        Add("a tip shows", frame =>
        {
            if (frame == 0)
            {
                GameSettings.Apply(GameSettings.Current with { Gameplay = GameSettings.Current.Gameplay with { ShowTutorialHints = true, SeenHints = [] } });
                _hints = new TutorialHints { Name = "Hints", UseHeld = () => _holdUse };
                AddChild(_hints);
            }
            return _hints.Showing is not null;
        });
        Add("holding Use fills the ring, and losing focus cancels it", frame =>
        {
            _holdUse = true;
            if (_hints.Ring.Value < 0.3f)
                return false;
            Check(_hints.Showing is not null, "the tip is still up partway");
            HudRoot.LoseFocus();
            return true;
        });
        Add("after the focus loss", frame =>
        {
            if (frame < 1)
                return false;
            Check(_hints.Ring.Value < 0.2f, $"the hold starts again ({_hints.Ring.Value})");
            return true;
        });
        Add("held through, the tip goes", frame =>
        {
            _holdUse = true;
            if (_hints.Showing is not null)
                return false;
            _holdUse = false;
            Check(_hints.Ring.Value == 0, "the ring empties");
            return true;
        });

        Add("the camera starts third-person and zooms in steps", frame =>
        {
            if (frame == 0)
            {
                _client.Main.AddChild(new LocalPlayerInput { Name = "TestInput", Target = Me });
                return false;
            }
            var input = _client.Main.GetNode<LocalPlayerInput>("TestInput");
            var settings = Me.Settings;
            Check(Me.ThirdPerson && Mathf.IsEqualApprox(Me.CameraDistance, settings.CameraDistance), $"third-person at {settings.CameraDistance} m ({Me.CameraDistance})");
            var distances = new List<float>();
            for (var i = 0; i < 4; i++)
            {
                input.Zoom(true);
                distances.Add(Me.CameraDistance);
            }
            Check(distances.Last() == 0 && !Me.ThirdPerson && !Me.BodyVisible, $"all the way in is first-person ({string.Join(", ", distances)})");
            input.Zoom(false);
            Check(Mathf.IsEqualApprox(Me.CameraDistance, settings.CameraMinDistance) && Me.BodyVisible, "zooming out comes back to the closest third-person distance");
            Me.CameraDistance = settings.CameraDistance;
            return true;
        });
        Add("third-person aim starts level with the head", frame =>
        {
            if (frame < 3)
                return false;
            var head = Me.Head.GlobalPosition;
            var forward = -Me.Camera.GlobalBasis.Z;
            var shoulder = new Vector2(Me.Settings.CameraShoulder, Me.Settings.CameraRaise).Length();
            Check(Me.Camera.GlobalPosition.DistanceTo(head) > 1f, "the camera is behind the player");
            Check(Mathf.Abs(forward.Dot(Me.AimOrigin - head)) < 0.05f && Me.AimOrigin.DistanceTo(head) <= shoulder + 0.05f,
                $"the aim starts level with the head, over the shoulder ({Me.AimOrigin} vs {head})");
            return true;
        });
    }

    private void Request<TArgs>(string action, TArgs args) => _client.Main.Actions.Request(action, args, _results.Add);

    private static int Value(ActionResult result, string name) => result.Value!.Value.GetProperty(name).GetInt32();

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

    private Vector3 PartCentre(int creation, int partId)
    {
        var graph = _client.Main.Creations.Find(creation)!;
        var part = graph.FindPart(partId)!;
        var body = _client.Main.Creations.NodeOf(creation)!.Bodies[graph.BodyIndexOf(partId)];
        return body.GlobalTransform * PartGeometry.CentreMetres(graph.DefinitionOf(partId), part.Cell, part.Rotation).ToGodot();
    }
}
