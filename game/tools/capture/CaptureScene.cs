using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Net;
using Mechanica.Core.Player;
using Mechanica.Core.Settings;
using Mechanica.Core.World;
using Mechanica.Game.Player;
using Mechanica.Game.Settings;
using Mechanica.Game.World;

namespace Mechanica.Game.Tools;

/// <summary>
/// Renders the player character in a running world to PNGs: standing, holding and swinging tools, looking up and down,
/// crouching, moving, refining (the Refine Tool drawn, worked and put away), the Connect Tool drawn, raised, clicked and put away, and in first person with each tool. For checking how a character or skin looks in game; run
/// <c>tools/capture-characters.sh</c>, which uses a virtual display and the compatibility renderer. Given <c>lift</c> instead
/// of a character it puts the player's Lift down and captures its deploy, a raise and full extension (<c>tools/capture-lift.sh</c>).
/// Given <c>flora</c> it takes the player to a meadow and captures the grass, ground cover and bushes: wide and close, at every
/// Foliage density level, a player in a bush, and every flora model in a row (<c>tools/capture-flora.sh</c>).
/// Given <c>icons</c> it saves the item picture of every tool and a few parts, then the HUD with the hotbar and the Lift's
/// placement ghost (<c>tools/capture-icons.sh</c>). Given <c>blocks</c> it builds a wall of every block material with its wedge,
/// the Ramp and Stairs, and see-through blocks in front of solid ones (<c>tools/capture-blocks.sh</c>).
/// Given <c>water</c> it finds a shore and captures the sea: wide and close, at every Water quality level, a run of frames for
/// the motion and the view from under the surface (<c>tools/capture-water.sh</c>).
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
        var flora = args.Length > 1 && args[1] == "flora";
        var icons = args.Length > 1 && args[1] == "icons";
        var water = args.Length > 1 && args[1] == "water";
        var blocks = args.Length > 1 && args[1] == "blocks";
        Main.MenuCharacter = args.Length > 1 && !lift && !flora && !icons && !water && !blocks ? args[1] : "scrapper_male";
        _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
        // Building the block wall needs Creative: free parts and no build reach.
        _main.Options = LaunchOptions.Offline with { Seed = 7, GameMode = blocks ? Core.Survival.GameMode.Creative : LaunchOptions.Offline.GameMode };
        _main.ReadDeviceInput = icons;
        AddChild(_main);
        _camera = new Camera3D { Fov = 40 };
        AddChild(_camera);
        if (lift)
        {
            LiftShots();
            return;
        }
        if (flora)
        {
            FloraShots();
            return;
        }
        if (icons)
        {
            IconShots();
            return;
        }
        if (water)
        {
            WaterShots(quick: args.Length > 2 && args[2] == "quick");
            return;
        }
        if (blocks)
        {
            BlockShots();
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
        Shot("refine_draw", 8, () => { Player.ActionProp = CharacterProp.RefineTool; Look(new Vector3(-2.2f, 1.2f, -1.8f)); });
        Shot("refine", 40, () => { });
        Shot("refine_bite", 9, () => { });
        Shot("refine_stow", 7, () => Player.ActionProp = null);
        Shot("bucket_empty", 40, () => { Player.HeldGuard = false; Hold(CharacterProp.WaterBucket); Look(new Vector3(-1.6f, 1.2f, -2.2f)); });
        Shot("bucket_fill", 22, () => Player.Gesture(CharacterGesture.BucketFill));
        Shot("bucket_full", 80, () => Hold(CharacterProp.WaterBucketFull));
        Shot("bucket_throw_windup", 14, () => Player.Gesture(CharacterGesture.BucketThrow));
        Shot("bucket_throw", 30, () => Player.Gesture(CharacterGesture.BucketThrow));
        Shot("connect_draw", 8, () => { Tool<Building.ConnectTool>(); Look(new Vector3(-0.85f, 1.45f, -1.25f), 1.15f); });
        Shot("connect", 40, () => { });
        Shot("connect_aim", 30, () => Connector.KeepRaised = true);
        Shot("connect_click", 3, () => Player.Gesture(CharacterGesture.ConnectClick));
        Shot("connect_reverse", 5, () => Player.Gesture(CharacterGesture.ConnectReverse));
        Shot("connect_side", 20, () => Look(new Vector3(-1.4f, 1.3f, -0.15f), 1.15f));
        Shot("connect_stow", 7, () => { Connector.KeepRaised = false; Tool<Building.HammerTool>(); });
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
        Shot("first_person_bucket_empty", 30, () => { Player.HeldGuard = false; Hold(CharacterProp.WaterBucket); Input(); Player.MakeCameraCurrent(); });
        Shot("first_person_bucket_fill", 22, () => Player.Gesture(CharacterGesture.BucketFill));
        Shot("first_person_bucket_full", 80, () => Hold(CharacterProp.WaterBucketFull));
        Shot("first_person_bucket_throw", 30, () => Player.Gesture(CharacterGesture.BucketThrow));
        Shot("first_person_paint", 30, () => Tool<Building.PaintTool>());
        Shot("first_person_lift", 30, () => Tool<Building.LiftTool>());
        Shot("first_person_connect_draw", 8, () => { Input(); Player.MakeCameraCurrent(); Tool<Building.ConnectTool>(); });
        Shot("first_person_connect", 30, () => { });
        Shot("first_person_connect_aim", 30, () => Connector.KeepRaised = true);
        Shot("first_person_connect_click", 3, () => Player.Gesture(CharacterGesture.ConnectClick));
        Shot("first_person_connect_reverse", 5, () => Player.Gesture(CharacterGesture.ConnectReverse));
        Shot("first_person_connect_stow", 7, () => { Connector.KeepRaised = false; Tool<Building.HammerTool>(); });
        Shot("first_person_refine_draw", 8, () => Player.ActionProp = CharacterProp.RefineTool);
        Shot("first_person_refine", 40, () => { });
        Shot("first_person_refine_bite", 9, () => { });
        Shot("first_person_refine_stow", 7, () => Player.ActionProp = null);
        Shot("first_person_crouch", 30, () => Input(crawl: true));
    }

    private PlayerController Player => _main.LocalPlayer!;

    private Building.ConnectTool Connector => _main.Tools!.Connector;

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

    private Vector3 _meadow;

    private void FloraShots()
    {
        Engine.TimeScale = 1.0;
        Shot("flora_00_wait", 60, () => { });
        Shot("flora_00_go", 240, () =>
        {
            var gen = _main.Streamer.Generator;
            for (var r = 100f; r < 2000f && _meadow == default; r += 50f)
            {
                for (var a = 0f; a < Mathf.Tau && _meadow == default; a += 0.3f)
                {
                    var (x, z) = (r * Mathf.Cos(a), r * Mathf.Sin(a));
                    if (new[] { (0f, 0f), (40f, 0f), (-40f, 0f), (0f, 40f), (0f, -40f) }.All(d => gen.BiomeAt(x + d.Item1, z + d.Item2).Id == "meadow")
                        && gen.SlopeAt(x, z) < 0.15f && !gen.PoisAt(x, z).Any() && !gen.ObjectsIn(x - 5, z - 5, x + 5, z + 5).Any())
                        _meadow = new Vector3(x, gen.HeightAt(x, z), z);
                }
            }
            GD.Print($"[capture] meadow at {_meadow}");
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + Vector3.Up * 0.3f, 0f);
            Player.ThirdPerson = true;
            _camera.MakeCurrent();
            _camera.GlobalPosition = _meadow + new Vector3(0, 30, 0);
        });
        Shot("flora_01_meadow", 30, () => { _main.Flora.BuildAll(); View(new Vector3(-3f, 1.8f, 3f), new Vector3(8f, 0.4f, -14f)); });
        Shot("flora_02_overview", 30, () => View(new Vector3(-12f, 9f, 12f), new Vector3(6f, 0f, -6f)));
        Shot("flora_03_grass_close", 30, () => View(new Vector3(1.2f, 0.9f, 1.6f), new Vector3(2.4f, 0.1f, -0.6f)));
        Shot("flora_04_grass_low_angle", 30, () => View(new Vector3(0.5f, 0.35f, 2.5f), new Vector3(3f, 0.25f, -4f)));
        Shot("flora_05_bush", 30, () =>
        {
            var bush = NearestFlora(FloraKind.Bush);
            ViewAt(bush, new Vector3(2.2f, 1.3f, 2.2f), 0.5f);
        });
        Shot("flora_06_ground_cover", 30, () =>
        {
            var cover = NearestFlora(FloraKind.GroundCover);
            ViewAt(cover, new Vector3(0.9f, 0.7f, 0.9f), 0.1f);
        });
        Shot("flora_07_seed_bush", 30, () =>
        {
            var gen = _main.Streamer.Generator;
            var bush = gen.ObjectsIn(_meadow.X - 120, _meadow.Z - 120, _meadow.X + 120, _meadow.Z + 120).Where(o => o.Definition.Id == "bush")
                .OrderBy(o => o.Position.ToGodot().DistanceTo(_meadow)).First();
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, bush.Position.ToGodot() + new Vector3(-1.6f, 0.3f, 1.2f), 0f);
            _main.Flora.BuildAll();
            ViewAt(bush.Position.ToGodot(), new Vector3(2f, 1.4f, 2.4f), 0.5f);
        });
        Shot("flora_08_player_in_bush", 60, () =>
        {
            var bush = NearestFlora(FloraKind.Bush);
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, bush + Vector3.Up * 0.3f, 0f);
            ViewAt(bush, new Vector3(2.5f, 1.8f, 3.2f), 0.8f);
        });
        foreach (var level in new[] { FoliageLevel.Off, FoliageLevel.Low, FoliageLevel.Medium, FoliageLevel.High, FoliageLevel.VeryHigh })
        {
            Shot($"flora_10_level_{(int)level}_{level}", 30, () =>
            {
                _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + Vector3.Up * 0.3f, 0f);
                GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Grass = level, GroundCover = level, Bushes = level } });
                _main.Flora.BuildAll();
                View(new Vector3(-4f, 3.2f, 4f), new Vector3(10f, 0f, -22f));
            });
        }
        Shot("flora_20_models", 30, () =>
        {
            GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Grass = FoliageLevel.Off, GroundCover = FoliageLevel.Off, Bushes = FoliageLevel.Off } });
            _main.Flora.BuildAll();
            var row = new Node3D { Name = "Models" };
            AddChild(row);
            var gen = _main.Streamer.Generator;
            var models = FloraModels.All;
            var tints = new Dictionary<FloraKind, Color> { [FloraKind.Grass] = new(0.49f, 0.78f, 0.23f), [FloraKind.GroundCover] = new(0.47f, 0.76f, 0.24f), [FloraKind.Bush] = new(0.4f, 0.7f, 0.24f) };
            var counts = new Dictionary<FloraKind, int>();
            // Foliage is off, which fades the shared materials out; the row gets copies that draw at any distance.
            var lineup = new[] { FloraKind.Grass, FloraKind.GroundCover, FloraKind.Bush }.ToDictionary(k => k, k =>
            {
                var material = (ShaderMaterial)FloraMaterials.For(k).Duplicate();
                FloraMaterials.Configure(material, 0.3f, 1f, 1000f, 2000f);
                return material;
            });
            foreach (var model in models)
            {
                var index = counts[model.Kind] = counts.GetValueOrDefault(model.Kind) + 1;
                var (spacing, depth) = model.Kind switch { FloraKind.Bush => (1.6f, -2.2f), FloraKind.Grass => (0.75f, 0f), _ => (0.6f, 1.1f) };
                var x = _meadow.X + (index - 1) * spacing - spacing * 2.5f;
                var z = _meadow.Z + depth;
                var at = new Vector3(x, gen.HeightAt(x, z), z);
                var multi = FloraField.New(FloraMaterials.Meshes(model).Full, 1);
                multi.SetInstanceTransform(0, new Transform3D(Basis.Identity, at));
                multi.SetInstanceColor(0, Colors.White);
                multi.SetInstanceCustomData(0, tints[model.Kind].SrgbToLinear() with { A = 0.3f });
                row.AddChild(new MultiMeshInstance3D { Name = model.Id, Multimesh = multi, MaterialOverride = lineup[model.Kind] });
            }
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + new Vector3(-6f, 0.3f, 3f), 0f);
            View(new Vector3(0.3f, 2.1f, 4.6f), new Vector3(0.3f, 0.2f, -0.6f));
        });
        Shot("ground_00_reset", 10, () =>
        {
            GetNodeOrNull("Models")?.QueueFree();
            GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Grass = FoliageLevel.High, GroundCover = FoliageLevel.High, Bushes = FoliageLevel.High } });
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + Vector3.Up * 0.3f, 0f);
        });
        GroundShots();
    }

    // Ground review shots: the textured ground in each biome, the meadow to the horizon and the crash site.
    private void GroundShots()
    {
        WorldGenerator gen() => _main.Streamer.Generator;
        Shot("ground_30_meadow_eye", 30, () => { _main.Flora.BuildAll(); View(new Vector3(-2.5f, 1.7f, 2.5f), new Vector3(30f, 0.5f, -60f)); });
        Shot("ground_31_meadow_high", 30, () => View(new Vector3(-20f, 14f, 20f), new Vector3(20f, 0f, -30f)));
        foreach (var biome in new[] { "forest", "ruin_field", "warehouse_district", "coastal" })
        {
            Shot($"ground_00_go_{biome}", 300, () =>
            {
                var g = gen();
                var spot = Find(p => new[] { (0f, 0f), (25f, 0f), (-25f, 0f), (0f, 25f), (0f, -25f) }.All(d => g.BiomeAt(p.X + d.Item1, p.Y + d.Item2).Id == biome)
                    && g.SlopeAt(p.X, p.Y) < 0.2f && !g.PoisAt(p.X, p.Y).Any());
                Go(spot);
            });
            Shot($"ground_32_{biome}", 30, () => { _main.Flora.BuildAll(); View(new Vector3(-4f, 2.6f, 4f), new Vector3(10f, 0f, -18f)); });
        }
        Shot("ground_00_go_crash", 300, () => Go(gen().Pois.First(p => p.Poi.Prefab.Id == "crash_site").ToWorld(new System.Numerics.Vector3(0f, 0f, 4f)).ToGodot()));
        Shot("ground_40_crash_spawn", 30, () => { _main.Flora.BuildAll(); View(new Vector3(-2f, 1.8f, -6f), new Vector3(0f, 1f, 18f)); });
        Shot("ground_41_crash_high", 30, () => View(new Vector3(-30f, 26f, -24f), new Vector3(0f, 0f, 18f)));
        Shot("ground_42_crash_edge", 30, () => View(new Vector3(-26f, 2.5f, 2f), new Vector3(0f, 0.5f, 20f)));
        Shot("ground_43_crash_from_meadow", 30, () => View(new Vector3(-50f, 4f, -30f), new Vector3(0f, 0f, 18f)));
    }

    // Water review shots: a shore with the sun across the water, every Water quality level, a run of frames for the
    // motion and the view from under the surface.
    private void WaterShots(bool quick)
    {
        // Towards the sun, so its glints show on the water as they do in the reference.
        var toSun = new Vector3(0.5f, 0f, 0.866f);
        Vector3 shore = default;
        float sea() => _main.Streamer.Generator.SeaLevel;
        void view(Vector3 from, Vector3 to)
        {
            _follow = null;
            _camera.MakeCurrent();
            var side = toSun.Cross(Vector3.Up);
            _camera.GlobalPosition = new Vector3(shore.X, sea(), shore.Z) + toSun * from.Z + side * from.X + Vector3.Up * from.Y;
            _camera.LookAt(new Vector3(shore.X, sea(), shore.Z) + toSun * to.Z + side * to.X + Vector3.Up * to.Y);
        }
        void quality(Quality q) => GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Preset = Quality.Custom, Water = q } });
        Shot("water_00_wait", 60, () => { });
        Shot("water_00_go", 300, () =>
        {
            var g = _main.Streamer.Generator;
            var level = g.SeaLevel;
            // Walk out towards the sun until the sea; the shore is the last dry ground before 45 m of open water.
            for (var t = 0f; t < 3000f; t += 2f)
            {
                var p = toSun * t;
                if (g.HeightAt(p.X, p.Z) > level + 0.2f)
                    shore = new Vector3(p.X, g.HeightAt(p.X, p.Z), p.Z);
                else if (shore != default && Enumerable.Range(1, 9).All(i => g.HeightAt(p.X + toSun.X * i * 5f, p.Z + toSun.Z * i * 5f) < level - 1f))
                    break;
            }
            Go(shore);
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, shore - toSun * 6f + Vector3.Up * 0.3f, 0f);
        });
        Shot("water_01_shore", 40, () => { quality(Quality.High); _main.Flora.BuildAll(); view(new Vector3(-2f, 5f, -4f), new Vector3(0f, 0f, 14f)); });
        Shot("water_03_wide", 30, () => view(new Vector3(-18f, 22f, -16f), new Vector3(0f, 0f, 22f)));
        Shot("water_04_waterline", 30, () => view(new Vector3(-1.5f, 1.2f, 3f), new Vector3(1.5f, 0f, 8f)));
        Shot("water_02_eye_level", 30, () => view(new Vector3(0f, 1.7f, -1f), new Vector3(0f, 0.2f, 30f)));
        if (quick)
            return;
        Shot("water_05_across", 30, () => view(new Vector3(6f, 3f, 30f), new Vector3(0f, 0f, 0f)));
        foreach (var q in new[] { Quality.Low, Quality.Medium, Quality.High })
            Shot($"water_10_level_{(int)q}_{q}", 30, () => { quality(q); view(new Vector3(-2f, 5f, -4f), new Vector3(0f, 0f, 14f)); });
        for (var i = 0; i < 16; i++)
            Shot($"water_20_motion_{i:00}", 3, () => view(new Vector3(-1f, 3.5f, -1f), new Vector3(0f, 0f, 10f)));
        Shot("water_30_underwater", 30, () => view(new Vector3(0f, -1.2f, 18f), new Vector3(0f, -0.8f, 40f)));
        Shot("water_31_up_from_under", 30, () => view(new Vector3(0f, -1.5f, 18f), new Vector3(0f, 2f, 24f)));
    }

    private Vector3 Find(Func<Vector2, bool> ok)
    {
        var gen = _main.Streamer.Generator;
        for (var r = 100f; r < 2000f; r += 40f)
        {
            for (var a = 0f; a < Mathf.Tau; a += 0.25f)
            {
                var (x, z) = (r * Mathf.Cos(a), r * Mathf.Sin(a));
                if (ok(new Vector2(x, z)) && !gen.ObjectsIn(x - 4, z - 4, x + 4, z + 4).Any())
                    return new Vector3(x, gen.HeightAt(x, z), z);
            }
        }
        return _meadow;
    }

    private void Go(Vector3 spot)
    {
        GD.Print($"[capture] go {spot}");
        _meadow = spot;
        _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, spot + Vector3.Up * 0.3f, 0f);
        _camera.MakeCurrent();
        _camera.GlobalPosition = spot + new Vector3(0, 30, 0);
    }

    // The plant of a kind nearest the meadow spot, from the flora drawn there.
    private Vector3 NearestFlora(FloraKind kind)
    {
        var world = _main.Streamer;
        var settings = _main.Flora.Settings!;
        return world.Chunks.Values
            .SelectMany(c => FloraScatter.Grow(world.Generator, c.Data, kind, settings.MaxDensity))
            .Where(p => p.Rank < settings.Keep(FoliageLevel.High))
            .Select(p => p.Position.ToGodot())
            .OrderBy(p => p.DistanceTo(_meadow + new Vector3(3f, 0f, -3f)))
            .First();
    }

    // Camera at an offset from the meadow spot, looking at another offset from it.
    private void View(Vector3 from, Vector3 to)
    {
        _follow = null;
        _camera.MakeCurrent();
        _camera.GlobalPosition = Ground(_meadow + from) + Vector3.Up * from.Y;
        _camera.LookAt(Ground(_meadow + to) + Vector3.Up * to.Y);
    }

    private void ViewAt(Vector3 target, Vector3 offset, float height)
    {
        _follow = null;
        _camera.MakeCurrent();
        _camera.GlobalPosition = Ground(target + offset) + Vector3.Up * offset.Y;
        _camera.LookAt(target + Vector3.Up * height);
    }

    private Vector3 Ground(Vector3 at) => new(at.X, _main.Streamer.Generator.HeightAt(at.X, at.Z), at.Z);

    private static readonly string[] BlockMaterials =
    [
        "wood", "scrap_wood", "cardboard", "brick", "concrete", "plaster", "tile", "carpet", "insulation", "barrier",
        "metal", "scrap_metal", "rusted_metal", "aluminum", "diamond_plate", "hull_plate", "glass", "armored_glass", "net", "path_light",
    ];

    // A 3 by 3 panel of each block material with wedges in front, in two rows of ten, built through the Action API.
    private void BlockShots()
    {
        Engine.TimeScale = 1.0;
        Shot("blocks_00_wait", 60, () => { });
        Shot("blocks_00_go", 240, () =>
        {
            var g = _main.Streamer.Generator;
            Go(Find(p => new[] { (0f, 0f), (12f, 0f), (-12f, 0f), (0f, 12f), (0f, -12f) }.All(d => g.BiomeAt(p.X + d.Item1, p.Y + d.Item2).Id == "meadow")
                && g.SlopeAt(p.X, p.Y) < 0.08f && !g.PoisAt(p.X, p.Y).Any()));
            GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Grass = FoliageLevel.Off, GroundCover = FoliageLevel.Off, Bushes = FoliageLevel.Off } });
            _main.Flora.BuildAll();
        });
        Shot("blocks_00_build", 120, () =>
        {
            var panel = new List<Core.Creations.GridCell>();
            for (var x = 0; x < 3; x++)
                for (var y = 0; y < 3; y++)
                    panel.Add(new(x, y, 0));
            for (var i = 0; i < BlockMaterials.Length; i++)
            {
                var at = Ground(_meadow + new Vector3(-9.5f + (i % 10) * 2f, 0, i < 10 ? -3f : 0f)) + Vector3.Up * 0.16f;
                // One path light, as on a path: a panel of them lights itself up.
                Place($"vanilla:{BlockMaterials[i]}_block", BlockMaterials[i] == "path_light" ? [new(1, 0, 0)] : panel, at);
                Place($"vanilla:{BlockMaterials[i]}_wedge", [new(0, 0, 0)], at + new Vector3(0f, 0f, 0.6f));
                Place($"vanilla:{BlockMaterials[i]}_wedge", [new(0, 0, 0), new(1, 0, 0)], at + new Vector3(0.375f, 0f, 0.6f));
            }
            var decor = Ground(_meadow + new Vector3(-2f, 0, 4f)) + Vector3.Up * 0.55f;
            Place("vanilla:ramp_block", [new(0, 0, 0)], decor);
            Place("vanilla:stairs_block", [new(0, 0, 0)], decor + new Vector3(2f, 0, 0));
            // See-through blocks in front of brick, so the brick shows through.
            var back = Ground(_meadow + new Vector3(6f, 0, 4f)) + Vector3.Up * 0.16f;
            var wall = new List<Core.Creations.GridCell>();
            for (var x = 0; x < 9; x++)
                for (var y = 0; y < 4; y++)
                    wall.Add(new(x, y, 0));
            Place("vanilla:brick_block", wall, back);
            foreach (var (material, dx) in new[] { ("glass", 0f), ("armored_glass", 0.75f), ("net", 1.5f) })
                Place($"vanilla:{material}_block", panel, back + new Vector3(dx, 0, 0.75f));
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, Ground(_meadow + new Vector3(-14f, 0, 10f)) + Vector3.Up * 0.3f, 0f);
        });
        Shot("blocks_01_row_back", 30, () => View(new Vector3(-6.5f, 1.4f, 0.6f), new Vector3(-6.5f, 0.4f, -3f)));
        Shot("blocks_02_row_back_right", 30, () => View(new Vector3(3.5f, 1.4f, 0.6f), new Vector3(3.5f, 0.4f, -3f)));
        Shot("blocks_03_row_front", 30, () => View(new Vector3(-6.5f, 1.4f, 3.6f), new Vector3(-6.5f, 0.4f, 0f)));
        Shot("blocks_04_row_front_right", 30, () => View(new Vector3(3.5f, 1.4f, 3.6f), new Vector3(3.5f, 0.4f, 0f)));
        Shot("blocks_05_overview", 30, () => View(new Vector3(-1f, 6f, 9f), new Vector3(-1f, 0f, -1.5f)));
        Shot("blocks_06_ramp_stairs", 30, () => View(new Vector3(-0.5f, 2.6f, 9.5f), new Vector3(-0.5f, 0.4f, 4.3f)));
        Shot("blocks_07_see_through", 30, () => View(new Vector3(7.2f, 1.2f, 7.2f), new Vector3(7.2f, 0.4f, 4.5f)));
        Shot("blocks_08_close", 30, () => View(new Vector3(-8.6f, 0.9f, 1.6f), new Vector3(-8.6f, 0.4f, 0f)));
        Shot("blocks_09_close_metals", 30, () => View(new Vector3(-4.6f, 1.3f, 2.4f), new Vector3(-4.6f, 0.4f, 0f)));
        Shot("blocks_10_close_path_light", 30, () => View(new Vector3(9.4f, 0.9f, 1.8f), new Vector3(8.9f, 0.4f, 0f)));
    }

    // Build reach holds in Creative too, so the player steps up to each spot first.
    private void Place(string part, IReadOnlyList<Core.Creations.GridCell> cells, Vector3 at)
    {
        _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, at + new Vector3(0.4f, 0.3f, 2f), 0f);
        _main.Actions.Request(Core.Building.BuildActions.PlaceParts, new Core.Building.PlacePartsArgs(part, cells, default, null, new Core.Data.Vec3(at.X, at.Y, at.Z)),
            r => GD.Print($"[capture] place {part}: {r}"));
    }

    private static readonly string[] IconItems = [.. Core.Items.HotbarTools.All, "vanilla:gas_engine", "vanilla:wheel", "vanilla:chest", "vanilla:water_bucket", "vanilla:water_bucket_full"];

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

    private void Tool<T>() where T : Building.HandTool
    {
        _main.Tools!.SetProcess(true);
        _main.Tools.Use(_main.Tools.Tools.OfType<T>().First());
    }

    // Puts a prop in the player's hand without an item behind it: the tool belt, which shows its own tool, stops for it.
    private void Hold(CharacterProp prop)
    {
        _main.Tools!.SetProcess(false);
        Player.HeldProp = prop;
    }

    // CAPTURE_ONLY=text keeps the shots whose names contain it (and the setup shots, named *_00_*).
    private void Shot(string name, int frames, Action setup)
    {
        if (OS.GetEnvironment("CAPTURE_ONLY") is { Length: > 0 } only && !name.Contains(only, StringComparison.Ordinal) && !name.Contains("_00_", StringComparison.Ordinal))
            return;
        _shots.Enqueue((name, frames, setup));
    }

    private void Input(float z = 0, float pitch = 0, bool crawl = false, bool sprint = false) =>
        Player.CurrentInput = new PlayerInput(0, z, Player.CurrentInput.Yaw, pitch, false, sprint, crawl);

    // Puts the capture camera at an offset from the player's feet, in the player's frame, looking at its chest.
    private void Look(Vector3 offset, float height = 1.0f)
    {
        Player.ThirdPerson = true; // shows the player's own body
        _camera.MakeCurrent();
        _follow = offset;
        _followHeight = height;
    }

    private float _followHeight = 1.0f;

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
            _camera.LookAt(Player.GlobalPosition + Vector3.Up * _followHeight);
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
