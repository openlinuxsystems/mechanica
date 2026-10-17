using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Net;
using Mechanica.Core.Player;
using Mechanica.Game.Player;

namespace Mechanica.Game.Tools;

/// <summary>
/// Renders the player character in a running world to PNGs: standing, holding and swinging tools, looking up and down,
/// crouching, moving, and in first person with each tool. For checking how a character or skin looks in game; run
/// <c>tools/capture-characters.sh</c>, which uses a virtual display and the compatibility renderer. Given <c>lift</c> instead
/// of a character it puts the player's Lift down and captures its deploy, a raise and full extension (<c>tools/capture-lift.sh</c>).
/// Given <c>icons</c> it saves the item picture of every tool and a few parts, then the HUD with the hotbar and the Lift's
/// placement ghost (<c>tools/capture-icons.sh</c>).
/// </summary>
public partial class CaptureScene : Node
{
    private readonly Queue<(string Name, int Frames, Action Setup)> _shots = new();
    private Main _main = null!;
    private Camera3D _camera = null!;
    private string _out = "";
    private double _time;
    private (string Name, int Frames, Action Setup)? _shot;

    public override void _Ready()
    {
        // Software rendering runs at a few frames a second; slow the game so a quick swing spans several frames.
        Engine.TimeScale = 0.1;
        var args = OS.GetCmdlineUserArgs();
        _out = args.Length > 0 ? args[0] : OS.GetUserDataDir();
        var lift = args.Length > 1 && args[1] == "lift";
        var icons = args.Length > 1 && args[1] == "icons";
        Main.MenuCharacter = args.Length > 1 && !lift && !icons ? args[1] : "scrapper_male";
        _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
        _main.Options = LaunchOptions.Offline with { Seed = 7 };
        _main.ReadDeviceInput = icons;
        AddChild(_main);
        _camera = new Camera3D { Fov = 40 };
        AddChild(_camera);
        if (lift)
        {
            LiftShots();
            return;
        }
        if (icons)
        {
            IconShots();
            return;
        }
        Shot("front", 60, () => Look(new Vector3(0, 1.0f, -3.2f)));
        Shot("side", 10, () => Look(new Vector3(-3.2f, 1.0f, 0)));
        Shot("hammer", 40, () => { Player.HeldProp = CharacterProp.Hammer; Look(new Vector3(-1.6f, 1.2f, -2.2f)); });
        Shot("swing", 9, () => Player.Gesture(CharacterGesture.HammerSwing));
        Shot("sledge", 40, () => { Tool<Building.SledgeTool>(); Look(new Vector3(-1.6f, 1.2f, -2.2f)); });
        Shot("sledge_windup", 21, () => Player.Gesture(CharacterGesture.SledgeSwing));
        Shot("sledge_block", 30, () => Player.HeldGuard = true);
        Shot("weld", 40, () => { Player.HeldGuard = false; Tool<Building.WeldTool>(); });
        Shot("look_up", 40, () => { Tool<Building.HammerTool>(); Input(pitch: 0.9f); Look(new Vector3(-2.6f, 1.2f, -1.6f)); });
        Shot("look_down", 40, () => Input(pitch: -0.9f));
        Shot("crouch", 40, () => Input(crawl: true));
        Shot("walk", 23, () => { Input(z: 1); });
        Shot("run", 23, () => { Input(z: 1, sprint: true); });
        Shot("first_person", 40, () => { Input(); Player.MakeCameraCurrent(); });
        Shot("first_person_up", 30, () => Input(pitch: 0.6f));
        Shot("first_person_swing", 9, () => Player.Gesture(CharacterGesture.HammerSwing));
        Shot("first_person_walk", 30, () => Input(z: 1));
        Shot("first_person_sledge", 30, () => { Input(); Tool<Building.SledgeTool>(); });
        Shot("first_person_sledge_windup", 21, () => Player.Gesture(CharacterGesture.SledgeSwing));
        Shot("first_person_sledge_swing", 40, () => Player.Gesture(CharacterGesture.SledgeSwing));
        Shot("first_person_sledge_block", 30, () => Player.HeldGuard = true);
        Shot("first_person_weld", 30, () => { Player.HeldGuard = false; Tool<Building.WeldTool>(); });
        Shot("first_person_paint", 30, () => Tool<Building.PaintTool>());
        Shot("first_person_lift", 30, () => Tool<Building.LiftTool>());
        Shot("first_person_connect", 30, () => Tool<Building.ConnectTool>());
        Shot("first_person_crouch", 30, () => Input(crawl: true));
    }

    private PlayerController Player => _main.LocalPlayer!;

    private Vector3 _liftAt;

    private void LiftShots()
    {
        Engine.TimeScale = 1.0;
        Shot("lift_00_wait", 60, () => { });
        Shot("lift_01_placed", 2, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 2, -2.5f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 10));
            _liftAt = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            _main.Actions.Request(Core.Building.ToolActions.PlaceLift, new Core.Building.PlaceLiftArgs(new Core.Data.Vec3(_liftAt.X, _liftAt.Y, _liftAt.Z)));
            LookAtLift(new Vector3(1.8f, 1.3f, -2.2f), 0.3f);
        });
        foreach (var frames in new[] { 24, 18, 18, 24, 30 })
            Shot($"lift_deploy_{_shots.Count:00}", frames, () => { });
        Shot("lift_raised_8", 200, () =>
        {
            _main.Actions.Request(Core.Building.ToolActions.SetLiftLevel, new Core.Building.SetLiftLevelArgs(8));
            LookAtLift(new Vector3(3.5f, 2.2f, -4.5f), 1.3f);
        });
        Shot("lift_top", 400, () =>
        {
            _main.Actions.Request(Core.Building.ToolActions.SetLiftLevel, new Core.Building.SetLiftLevelArgs(_main.Build.Settings!.Lift.MaxLevel));
            LookAtLift(new Vector3(6f, 3.5f, -8f), 2.8f);
        });
        Shot("lift_close", 2, () => LookAtLift(new Vector3(1.2f, 0.9f, -1.5f), 0.3f));
    }

    private static readonly string[] IconItems = [.. Core.Items.HotbarTools.All, "vanilla:gas_engine", "vanilla:wheel", "vanilla:chest"];

    // Each picture is saved as it becomes ready; the last shot is the HUD, with the hotbar showing the tools.
    private void IconShots()
    {
        Engine.TimeScale = 1.0;
        var saved = new HashSet<string>();
        Shot("icons_wait", 60, () => { });
        Shot("icons_hud", 240, () =>
        {
            foreach (var id in IconItems)
                _main.Icons!.For(id);
        });
        Shot("lift_ghost", 60, () =>
        {
            // The mouse would set the look every tick; stop it, so the player looks down at where the Lift would go.
            foreach (var reader in _main.FindChildren("*", owned: false).OfType<LocalPlayerInput>())
                reader.SetPhysicsProcess(false);
            Tool<Building.LiftTool>();
            Input(pitch: -0.5f);
            Player.MakeCameraCurrent();
        });
        _iconTick = () =>
        {
            if (_main.Icons is not { } iconsNode)
                return;
            foreach (var id in IconItems)
            {
                if (saved.Contains(id) || iconsNode.For(id) is not { } texture)
                    continue;
                saved.Add(id);
                var path = $"{_out}/icon_{id.Replace(':', '_')}.png";
                texture.GetImage().SavePng(path);
                GD.Print($"[capture] {path}");
            }
        };
    }

    private Action? _iconTick;

    private void LookAtLift(Vector3 offset, float height)
    {
        Player.ThirdPerson = true;
        _camera.MakeCurrent();
        _follow = null;
        _camera.GlobalPosition = _liftAt + offset;
        _camera.LookAt(_liftAt + Vector3.Up * height);
    }

    private void Tool<T>() where T : Building.HandTool => _main.Tools!.Use(_main.Tools.Tools.OfType<T>().First());

    private void Shot(string name, int frames, Action setup) => _shots.Enqueue((name, frames, setup));

    private void Input(float z = 0, float pitch = 0, bool crawl = false, bool sprint = false) =>
        Player.CurrentInput = new PlayerInput(0, z, Player.CurrentInput.Yaw, pitch, false, sprint, crawl);

    // Puts the capture camera at an offset from the player's feet, in the player's frame, looking at its chest.
    private void Look(Vector3 offset)
    {
        Player.ThirdPerson = true; // shows the player's own body
        _camera.MakeCurrent();
        _follow = offset;
    }

    private Vector3? _follow;

    public override void _Process(double delta)
    {
        if (_main.LocalPlayer is null || _main.Characters.LocalNeedsChoice || Player.Character is null)
            return;
        _iconTick?.Invoke();
        if (_follow is { } offset)
        {
            var basis = Player.GlobalTransform.Basis;
            _camera.GlobalPosition = Player.GlobalPosition + basis * offset;
            _camera.LookAt(Player.GlobalPosition + Vector3.Up * 1.0f);
        }
        if (_shot is null)
        {
            if (!_shots.TryDequeue(out var next))
            {
                GetTree().Quit();
                return;
            }
            _shot = next;
            _time = 0;
            next.Setup();
            if (next.Name.StartsWith("first_person"))
                _follow = null;
        }
        _time += delta;
        if (_time < _shot.Value.Frames / 60.0)
            return;
        if (Player.Character is CharacterModel body)
            GD.Print($"[capture] {_shot.Value.Name}: fps={Engine.GetFramesPerSecond()} delta={delta:F3} gesture={body.Animator.Gesture} upper={body.Animator.UpperWeight:F2} shown={Player.ShownGesture} prop={Player.ShownProp} mode={body.Animator.Mode}");
        var path = $"{_out}/{_shot.Value.Name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[capture] {path}");
        _shot = null;
    }
}
