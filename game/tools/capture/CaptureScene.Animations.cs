using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Player;
using Mechanica.Core.Settings;
using Mechanica.Game.Player;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Tools;

// Given `animations`, the capture records the player character's moves as runs of frames (OUT/<move>/0000.png ...), for
// turning into clips (tools/capture-animations.sh): standing, walking, jogging and running from the side and the front,
// starting and stopping, turning on the spot, strafing, walking with a tool, jumping, crouching, the Sledgehammer swings,
// the bucket throw, being hit, knocked down and killed, eating a tomato, and the tomato plant growing, wilting and harvested. Run it with --fixed-fps so every frame is the same step of
// game time, whatever the renderer manages.
public partial class CaptureScene
{
    private readonly Dictionary<string, Action<int>> _recording = new(StringComparer.Ordinal);
    private int _frame;
    private Vector3? _track;
    private Vector3 _trackLook = Vector3.Up;
    private float _yaw;

    private void AnimationShots()
    {
        Engine.TimeScale = 1.0;
        Shot("moves_00_wait", 60, () => { });
        Shot("moves_00_go", 120, () =>
        {
            var g = _main.Streamer.Generator;
            GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Grass = FoliageLevel.Off, GroundCover = FoliageLevel.Off } });
            Go(Find(p => new[] { (0f, 0f), (20f, 0f), (-20f, 0f), (0f, 20f), (0f, -20f) }.All(d => g.BiomeAt(p.X + d.Item1, p.Y + d.Item2).Id == "meadow")
                && g.SlopeAt(p.X, p.Y) < 0.05f && !g.PoisAt(p.X, p.Y).Any()));
            _main.Flora.BuildAll();
            Player.ThirdPerson = true;
        });
        var side = new Vector3(3.6f, 1.0f, 0.4f);
        var front = new Vector3(1.9f, 1.1f, -3.0f);
        Move("idle", 150, front, _ => Drive());
        Move("walk_side", 60, side, _ => Drive(z: 0.5f));
        Move("walk_front", 60, front, _ => Drive(z: 0.5f));
        Move("jog_side", 45, side, _ => Drive(z: 1f));
        Move("run_side", 45, side, _ => Drive(z: 1f, sprint: true));
        Move("run_front", 45, front, _ => Drive(z: 1f, sprint: true));
        Move("start_stop", 120, side, f => Drive(z: f is > 15 and < 75 ? 1f : 0f, sprint: f is > 15 and < 75));
        Move("turn_on_spot", 150, front, f => { _yaw += f < 75 ? 0.035f : -0.035f; Drive(); }, still: true);
        Move("strafe", 90, front, f => Drive(x: f < 45 ? 0.5f : -0.5f));
        Move("zigzag", 120, new Vector3(4.5f, 2.2f, 2.5f), f => { _yaw += 0.03f * Mathf.Sin(f * 0.08f); Drive(z: 1f); });
        Move("walk_tool", 60, front, f => { if (f == 0) Tool<Building.ConnectTool>(); Drive(z: 0.5f); });
        Move("run_sledge", 45, side, f => { if (f == 0) Tool<Building.SledgeTool>(); Drive(z: 1f); });
        Move("jump", 50, side, f => Drive(jump: f == 3));
        Move("run_jump", 60, side, f => Drive(z: 1f, sprint: true, jump: f == 10));
        Move("crouch_walk", 70, side, f => Drive(z: f > 10 ? 0.5f : 0f, crawl: true));
        Move("sledge_swings", 120, front, f =>
        {
            Drive();
            if (f % 30 == 4)
                Player.Gesture(CharacterGesture.SledgeSwing);
        });
        Move("bucket_throw", 60, new Vector3(3.2f, 1.2f, -1.4f), f =>
        {
            if (f == 0)
                Hold(CharacterProp.WaterBucketFull);
            Drive();
            if (f == 8)
                Player.Gesture(CharacterGesture.BucketThrow);
        });
        // The throw as it plays, the water flying out as the vanilla bucket throws it (watering.lua), splashing far ahead.
        Move("bucket_splash", 100, new Vector3(5.5f, 2.6f, 4.5f), f =>
        {
            if (f == 0)
                Hold(CharacterProp.WaterBucketFull);
            Drive();
            if (f == 8)
                Player.Gesture(CharacterGesture.BucketThrow);
            if (f == 18)
                Throw(12f, 2.5f);
        }, look: new Vector3(0, 0.8f, -6f));
        Move("hurt", 45, front, f =>
        {
            if (f == 0)
                Hold(CharacterProp.None);
            Drive();
            if (f == 5 && Player.Character is CharacterModel body)
                body.Animator.Play(CharacterGesture.Hurt);
        });
        // Blows from in front, the left, behind, a heavy one, a knockdown and a death.
        foreach (var (name, reaction, from) in new[]
        {
            ("hit_front", HitReaction.Light, 0f), ("hit_left", HitReaction.Light, 1.6f), ("hit_back", HitReaction.Light, 3.1f),
            ("hit_heavy", HitReaction.Heavy, 0f),
        })
            Move(name, 40, front, f => { Drive(); if (f == 5) React(reaction, from); });
        Move("knockdown", 110, new Vector3(3.2f, 1.4f, -1.6f), f => { Drive(); if (f == 5) React(HitReaction.Knockdown, 0f); });
        Move("death", 90, new Vector3(3.2f, 1.4f, -1.6f), f =>
        {
            Drive();
            if (f == 5)
                Player.Dead = true;
            if (f == 89)
                Player.Dead = false;
        });
        // Food (the food design): a tomato on the palm, eaten in two bites, seen from the front and from the eyes.
        void Eat(int f)
        {
            if (f == 0)
            {
                Hold(CharacterProp.Food);
                Player.HeldItem = _main.HeldItems.IndexOf("vanilla:tomato");
            }
            Drive();
            if (f == 30)
                Player.Gesture(CharacterGesture.Eat);
        }
        Move("eat_tomato", 90, new Vector3(1.0f, 1.55f, -1.5f), Eat, look: new Vector3(0, 1.35f, 0));
        Move("eat_tomato_side", 90, new Vector3(1.7f, 1.45f, 0.1f), Eat, look: new Vector3(0, 1.35f, 0));
        Move("eat_tomato_fp", 90, new Vector3(1.7f, 1.45f, 0.1f), f =>
        {
            if (f == 0)
            {
                _track = null;
                Player.ThirdPerson = false;
                Player.MakeCameraCurrent();
            }
            Eat(f);
        });
        // The tomato plant from seed to ripe, wilting on dry soil and picking up again, then harvested.
        Survival.CropNode? crop = null;
        void ShowCrop(int stage, Core.Survival.SoilWetness wetness) => crop!.Show(new Core.Survival.SoilView(0, 0, "vanilla:tomato_seed",
            "vanilla:tomato", stage, Wetness: wetness), _main.Survival.Catalog, _main.Survival.Assets);
        Move("tomato_grow", 420, new Vector3(1.1f, 0.85f, -0.3f), f =>
        {
            if (f == 0)
            {
                Hold(CharacterProp.None);
                Player.ThirdPerson = true;
                crop = new Survival.CropNode { Name = "CaptureCrop" };
                AddChild(crop);
                crop.GlobalPosition = Player.GlobalPosition + new Vector3(0, 0, -1.3f);
                ShowCrop(0, Core.Survival.SoilWetness.Wet);
            }
            Drive();
            if (f % 75 == 30 && f / 75 < 4)
                ShowCrop(f / 75 + 1, Core.Survival.SoilWetness.Wet);
        }, still: true, look: new Vector3(0, 0.35f, -1.3f));
        Move("tomato_wilt", 150, new Vector3(1.1f, 0.85f, -0.3f), f =>
        {
            Drive();
            if (f == 10)
                ShowCrop(4, Core.Survival.SoilWetness.Dry);
            if (f == 90)
                ShowCrop(4, Core.Survival.SoilWetness.Wet);
        }, still: true, look: new Vector3(0, 0.35f, -1.3f));
        Move("tomato_harvest", 60, new Vector3(1.1f, 0.85f, -0.3f), f =>
        {
            Drive();
            if (f == 10)
                crop!.Remove(harvested: true);
        }, still: true, look: new Vector3(0, 0.35f, -1.3f));
    }

    // A run of frames named `name`, the camera at `offset` from the player in world axes (tracking, not turning with it),
    // looking at the chest or `look` from the feet. A still camera stays where it started.
    private void Move(string name, int frames, Vector3 offset, Action<int> tick, bool still = false, Vector3? look = null)
    {
        Shot(name, frames, () =>
        {
            _follow = null;
            _camera.MakeCurrent();
            _track = offset;
            _trackLook = look ?? Vector3.Up * 1.0f;
            _yaw = 0f;
            _frame = 0;
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + Vector3.Up * 0.1f, 0f);
            Drive();
            Place();
            _stillCamera = still;
            DirAccess.MakeDirRecursiveAbsolute($"{_out}/{name}");
        });
        _recording[name] = tick;
    }

    private bool _stillCamera;

    private void Place()
    {
        if (_track is not { } offset)
            return;
        var at = Player.GlobalPosition;
        _camera.GlobalPosition = at + offset;
        _camera.LookAt(at + _trackLook);
    }

    // A blow on the player as every peer shows one: the body (and the first-person arms) react, the camera jolts.
    private void React(HitReaction reaction, float from)
    {
        foreach (var model in new[] { Player.Character as CharacterModel, Player.Arms as CharacterModel })
            model?.Animator.React(reaction, from);
    }

    // Throws water `distance` metres ahead along the look as watering.lua does, drawn `size` metres across with the game's
    // own water look. It flies here frame by frame rather than through the survival sync, which flies projectiles by the
    // wall clock and so would race ahead of a capture's fixed frames.
    private void Throw(float distance, float size)
    {
        var forward = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
        var from = Player.GlobalPosition + Vector3.Up * 1.3f + forward * 0.4f;
        var gravity = new Vector3(0, -9.8f, 0);
        const float seconds = 1.1f;
        var velocity = (Player.GlobalPosition + forward * distance - from) / seconds - gravity * (0.5f * seconds);
        var water = Survival.WaterEffects.Blob(size);
        AddChild(water);
        water.GlobalPosition = from;
        var ground = Player.GlobalPosition.Y;
        var t = 0f;
        Action? fly = null;
        fly = () =>
        {
            t += 1f / 30f;
            var at = from + velocity * t + gravity * (0.5f * t * t);
            if (at.Y > ground || t < 0.2f)
            {
                water.GlobalPosition = at;
                return;
            }
            GetTree().ProcessFrame -= fly;
            Survival.WaterEffects.Splash(this, new Vector3(at.X, ground, at.Z), size);
            water.QueueFree();
        };
        GetTree().ProcessFrame += fly;
    }

    private void Drive(float x = 0, float z = 0, bool sprint = false, bool crawl = false, bool jump = false) =>
        Player.CurrentInput = new PlayerInput(x, z, _yaw, 0f, jump, sprint, crawl);

    private void Record((string Name, int Frames, Action Setup) shot, Action<int> tick)
    {
        // The first frames after a teleport settle the body before anything is saved.
        if (_time < 0.5)
        {
            _time += 1.0 / 30.0;
            return;
        }
        tick(_frame);
        if (!_stillCamera)
            Place();
        GetViewport().GetTexture().GetImage().SavePng($"{_out}/{shot.Name}/{_frame:0000}.png");
        if (++_frame >= shot.Frames)
        {
            GD.Print($"[capture] {_out}/{shot.Name} ({shot.Frames} frames)");
            _track = null;
            _shot = null;
        }
    }
}
