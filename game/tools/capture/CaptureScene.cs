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
/// crouching, moving, refining (the Refine Tool drawn, worked and put away), the Connect Tool drawn, raised, clicked and put away, the Weld Tool drawn, raised, welding (its arms clamped, sparks) and put away, the Paint Tool drawn, spraying, taking paint off, reloaded and put away, and in first person with each tool. For checking how a character or skin looks in game; run
/// <c>tools/capture-characters.sh</c>, which uses a virtual display and the compatibility renderer. Given <c>lift</c> instead
/// of a character it puts the player's Lift down and captures its deploy, a raise and full extension (<c>tools/capture-lift.sh</c>).
/// Given <c>flora</c> it takes the player to a meadow and captures the grass, ground cover and bushes: wide and close, at every
/// Foliage density level, a player in a bush, and every flora model in a row (<c>tools/capture-flora.sh</c>).
/// Given <c>icons</c> it saves the item picture of every tool and a few parts, then the HUD with the hotbar and the Lift's
/// placement ghost (<c>tools/capture-icons.sh</c>). Given <c>blocks</c> it builds a wall of every block material with its wedge,
/// the Ramp and Stairs, and see-through blocks in front of solid ones (<c>tools/capture-blocks.sh</c>).
/// Given <c>water</c> it finds a shore and captures the sea: wide and close, at every Water quality level, a run of frames for
/// the motion and the view from under the surface (<c>tools/capture-water.sh</c>).
/// Given <c>rocks</c> it finds stone and metal-rich rock deposits and captures them, then drills a large block into
/// boulders, the boulders into chunks and the chunks into rods (<c>tools/capture-rocks.sh</c>).
/// Given <c>soil</c> it carries a Soil Bag, tips it out, spills a mound and shows a row of mounds dry, lightly watered,
/// fully wet and drying (<c>tools/capture-soil.sh</c>).
/// Given <c>knockout_bag</c> it knocks the player out and captures the knockout bag their items drop in from the reference
/// sheet's views, with its label and next to the respawned player (<c>tools/capture-knockout-bag.sh</c>).
/// Given <c>scrap_seat</c> it puts down a Scrap Driver's Seat on a slab with a gas engine, a switch and a button linked to
/// it, sits the player in it (the enter clip, then seated), runs the engine (the rattle), shows the seated driver from
/// the sides, saves the seat's item picture and stands up again (<c>tools/capture-scrap-seat.sh</c>).
/// Given <c>portable_fabricator</c> it puts down a Portable Fabricator, captures it from the reference sheet's views and idle,
/// then crafts a Connect Tool on it (the start, craft and complete clips) and saves its item picture
/// (<c>tools/capture-portable-fabricator.sh</c>).
/// Given <c>trees</c> it finds a clear example of every tree type and captures it with the player beside it and close up,
/// then a forest, an autumn and a burned forest wide, and a felled tree's logs (<c>tools/capture-trees.sh</c>).
/// Given <c>piston</c> it puts down Pistons at levels 1, 3 and 5 under blocks, linked to a switch, and captures them put
/// down, pushing out, fully out and pulling back (<c>tools/capture-piston.sh</c>).
/// Given <c>wheels</c> it sets three little cars down in a meadow, one on each wheel (Scrap Wheel, Wheel, Off Road
/// Wheel), captures them wide and close, and shows the three wheels on the hotbar (<c>tools/capture-wheels.sh</c>).
/// Given <c>switch</c> it puts down switches off, on and on a wall, shows each state close up and toggles one
/// (<c>tools/capture-switch.sh</c>).
/// Given <c>fabricator</c> it puts down a level 1 and a level 5 Fabricator, shows them from every side (the input port on
/// the left, the output on the right), then has each craft: unfolding, welding and drilling with sparks, and folding back
/// (<c>tools/capture-fabricator.sh</c>).
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
        var rocks = args.Length > 1 && args[1] == "rocks";
        var soil = args.Length > 1 && args[1] == "soil";
        var bag = args.Length > 1 && args[1] == "knockout_bag";
        var seat = args.Length > 1 && args[1] == "scrap_seat";
        var trees = args.Length > 1 && args[1] == "trees";
        var seats = args.Length > 1 && args[1] == "seats";
        var piston = args.Length > 1 && args[1] == "piston";
        var portable = args.Length > 1 && args[1] == "portable_fabricator";
        var wheels = args.Length > 1 && args[1] == "wheels";
        var debugCar = args.Length > 1 && args[1] == "debug_car";
        var button = args.Length > 1 && args[1] == "button";
        var toggle = args.Length > 1 && args[1] == "switch";
        var fabricator = args.Length > 1 && args[1] == "fabricator";
        var oil = args.Length > 1 && args[1] == "oil";
        var small = args.Length > 1 && args[1] == "small_pois";
        var finds = args.Length > 1 && args[1] == "finds";
        var borders = args.Length > 1 && args[1] == "biomes";
        var moves = args.Length > 1 && args[1] == "animations";
        Main.MenuCharacter = args.Length > 1 && !lift && !flora && !icons && !water && !blocks && !rocks && !soil && !bag && !seat && !trees && !seats && !piston && !wheels && !button && !toggle && !portable && !fabricator && !oil && !small && !finds && !borders && !moves ? args[1] : moves && args.Length > 2 ? args[2] : "scrapper_male";
        if (debugCar)
            Main.MenuCharacter = "scrapper_male";
        _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
        // Building the block wall needs Creative: free parts and no build reach.
        _main.Options = LaunchOptions.Offline with { Seed = 7, GameMode = blocks || soil || wheels || portable ? Core.Survival.GameMode.Creative : LaunchOptions.Offline.GameMode };
        // The button shots show the HUD (the creation hotbar), which only a main reading the devices builds.
        _main.ReadDeviceInput = icons || wheels || button;
        _main.DebugCar = debugCar;
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
        if (rocks)
        {
            RockShots();
            return;
        }
        if (soil)
        {
            SoilShots();
            return;
        }
        if (bag)
        {
            KnockoutBagShots();
            return;
        }
        if (seat)
        {
            ScrapSeatShots();
            return;
        }
        if (trees)
        {
            TreeShots();
            return;
        }
        if (seats)
        {
            SeatsShots();
            return;
        }
        if (piston)
        {
            PistonShots();
            return;
        }
        if (button)
        {
            ButtonShots();
            return;
        }
        if (wheels)
        {
            WheelShots();
            return;
        }
        if (debugCar)
        {
            DebugCarShots();
            return;
        }
        if (toggle)
        {
            SwitchShots();
            return;
        }
        if (fabricator)
        {
            FabricatorShots();
            return;
        }
        if (portable)
        {
            PortableFabricatorShots();
            return;
        }
        if (oil)
        {
            OilShots();
            return;
        }
        if (small)
        {
            SmallPoiShots();
            return;
        }
        if (borders)
        {
            BiomeBorderShots();
            return;
        }
        if (finds)
        {
            FindShots();
            return;
        }
        if (moves)
        {
            AnimationShots();
            return;
        }
        Shot("front", 60, () => Look(new Vector3(0, 1.0f, -3.2f)));
        Shot("side", 10, () => Look(new Vector3(-3.2f, 1.0f, 0)));
        Shot("sledge", 40, () => { Tool<Building.SledgeTool>(); Look(new Vector3(-1.6f, 1.2f, -2.2f)); });
        Shot("sledge_windup", 21, () => Player.Gesture(CharacterGesture.SledgeSwing));
        Shot("sledge_block", 30, () => Player.HeldGuard = true);
        Shot("weld_draw", 8, () => { Player.HeldGuard = false; Tool<Building.WeldTool>(); Look(new Vector3(-0.85f, 1.45f, -1.25f), 1.15f); });
        Shot("weld", 40, () => { });
        Shot("weld_aim", 30, () => Welder.KeepRaised = true);
        Shot("weld_push", 2, () => Player.Gesture(CharacterGesture.Weld));
        Shot("weld_clamp", 2, () => { });
        Shot("weld_sparks", 4, () => _main.WeldEffects.Burst(Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(-0.15f, 1.3f, -0.75f)));
        Shot("weld_side", 20, () => Look(new Vector3(-1.4f, 1.3f, -0.15f), 1.15f));
        Shot("weld_stow", 7, () => { Welder.KeepRaised = false; Tool<Building.SledgeTool>(); });
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
        Shot("connect_stow", 7, () => { Connector.KeepRaised = false; Tool<Building.WeldTool>(); });
        Shot("paint_draw", 9, () => { GivePaintTool(); Tool<Building.PaintTool>(); Look(new Vector3(-0.85f, 1.45f, -1.25f), 1.15f); });
        Shot("paint", 40, () => { });
        Shot("paint_spray", 24, () => { Painter.Choose(13); Painter.KeepSpraying = true; });
        Shot("paint_erase", 24, () => Painter.KeepErasing = true);
        Shot("paint_side", 20, () => { Painter.KeepSpraying = Painter.KeepErasing = false; Painter.Choose(25); Look(new Vector3(-1.4f, 1.3f, -0.15f), 1.15f); });
        Shot("paint_reload_off", 32, () => Painter.Reload());
        Shot("paint_reload_on", 30, () => { });
        Shot("paint_stow", 8, () => Tool<Building.WeldTool>());
        Shot("look_up", 40, () => { Tool<Building.WeldTool>(); Input(pitch: 0.9f); Look(new Vector3(-2.6f, 1.2f, -1.6f)); });
        Shot("look_down", 40, () => Input(pitch: -0.9f));
        Shot("crouch", 40, () => Input(crawl: true));
        Shot("walk", 23, () => { Input(z: 1); });
        Shot("run", 23, () => { Input(z: 1, sprint: true); });
        Shot("first_person", 40, () => { Input(); Player.MakeCameraCurrent(); });
        Shot("first_person_up", 30, () => Input(pitch: 0.6f));
        Shot("first_person_walk", 30, () => Input(z: 1));
        Shot("first_person_sledge", 30, () => { Input(); Tool<Building.SledgeTool>(); });
        Shot("first_person_sledge_windup", 21, () => Player.Gesture(CharacterGesture.SledgeSwing));
        Shot("first_person_sledge_swing", 40, () => Player.Gesture(CharacterGesture.SledgeSwing));
        Shot("first_person_sledge_block", 30, () => Player.HeldGuard = true);
        Shot("first_person_weld_draw", 8, () => { Player.HeldGuard = false; Input(); Player.MakeCameraCurrent(); Tool<Building.WeldTool>(); });
        Shot("first_person_weld", 30, () => { });
        Shot("first_person_weld_aim", 30, () => Welder.KeepRaised = true);
        Shot("first_person_weld_push", 2, () => Player.Gesture(CharacterGesture.Weld));
        Shot("first_person_weld_clamp", 2, () => { });
        Shot("first_person_weld_stow", 7, () => { Welder.KeepRaised = false; Tool<Building.SledgeTool>(); });
        Shot("first_person_bucket_empty", 30, () => { Player.HeldGuard = false; Hold(CharacterProp.WaterBucket); Input(); Player.MakeCameraCurrent(); });
        Shot("first_person_bucket_fill", 22, () => Player.Gesture(CharacterGesture.BucketFill));
        Shot("first_person_bucket_full", 80, () => Hold(CharacterProp.WaterBucketFull));
        Shot("first_person_bucket_throw", 30, () => Player.Gesture(CharacterGesture.BucketThrow));
        Shot("first_person_paint_draw", 9, () => { GivePaintTool(); Input(); Player.MakeCameraCurrent(); Tool<Building.PaintTool>(); });
        Shot("first_person_paint", 30, () => { });
        Shot("first_person_paint_spray", 24, () => { Painter.Choose(1); Painter.KeepSpraying = true; });
        Shot("first_person_paint_erase", 24, () => Painter.KeepErasing = true);
        Shot("first_person_paint_reload", 32, () => { Painter.KeepSpraying = Painter.KeepErasing = false; Painter.Choose(25); Painter.Reload(); });
        Shot("first_person_paint_stow", 30, () => Tool<Building.LiftTool>());
        Shot("first_person_lift", 30, () => Tool<Building.LiftTool>());
        Shot("first_person_connect_draw", 8, () => { Input(); Player.MakeCameraCurrent(); Tool<Building.ConnectTool>(); });
        Shot("first_person_connect", 30, () => { });
        Shot("first_person_connect_aim", 30, () => Connector.KeepRaised = true);
        Shot("first_person_connect_click", 3, () => Player.Gesture(CharacterGesture.ConnectClick));
        Shot("first_person_connect_reverse", 5, () => Player.Gesture(CharacterGesture.ConnectReverse));
        Shot("first_person_connect_stow", 7, () => { Connector.KeepRaised = false; Tool<Building.WeldTool>(); });
        Shot("first_person_refine_draw", 8, () => Player.ActionProp = CharacterProp.RefineTool);
        Shot("first_person_refine", 40, () => { });
        Shot("first_person_refine_bite", 9, () => { });
        Shot("first_person_refine_stow", 7, () => Player.ActionProp = null);
        Shot("first_person_crouch", 30, () => Input(crawl: true));
    }

    private PlayerController Player => _main.LocalPlayer!;

    private Building.ConnectTool Connector => _main.Tools!.Connector;

    private Building.WeldTool Welder => _main.Tools!.Weld;

    private Building.PaintTool Painter => _main.Tools!.Paint;

    // In Survival the Paint Tool is crafted before it is held; the captures hand one over.
    private void GivePaintTool()
    {
        if (!Painter.Owns)
            _main.Inventory.Items!.PlayerInventory(_main.Session.LocalPeer).Add(Core.Items.HotbarTools.Paint, 1);
    }

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
    private void RockShots()
    {
        Engine.TimeScale = 1.0;
        var gen = _main.Streamer.Generator;
        // Deposits scattered by the rule whose object is `id`, biggest first among the first few.
        List<List<WorldObjectPlacement>> Deposits(string id) => gen.ObjectsIn(-1500, -1500, 1500, 1500)
            .Where(o => !o.Id.IsPoi && gen.Data.ScatterRules[o.Id.Rule].Object.Id == id)
            .GroupBy(o => o.Id with { Piece = 0 }).Take(24).OrderByDescending(g => g.Count()).Select(g => g.ToList()).ToList();
        Vector3 Middle(List<WorldObjectPlacement> blocks) => new(blocks.Average(o => o.Position.X), blocks.Min(o => o.Position.Y), blocks.Average(o => o.Position.Z));
        var stone = new List<WorldObjectPlacement>();
        var small = new List<WorldObjectPlacement>();
        Shot("rocks_00_wait", 60, () => { });
        Shot("rocks_00_go", 240, () =>
        {
            var deposits = Deposits("rock_outcrop");
            stone = deposits[0];
            small = deposits[^1];
            _meadow = Middle(stone);
            GD.Print($"[capture] deposit of {stone.Count} blocks at {_meadow}");
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + new Vector3(-9f, 2f, 9f), 0f);
            _camera.MakeCurrent();
            _camera.GlobalPosition = _meadow + new Vector3(0, 30, 0);
        });
        Shot("rocks_01_deposit", 30, () => { _main.Flora.BuildAll(); ViewAt(_meadow, new Vector3(-14f, 7f, 14f), 1.5f); });
        Shot("rocks_02_deposit_other_side", 30, () => ViewAt(_meadow, new Vector3(15f, 5f, -12f), 1.5f));
        Shot("rocks_03_mound_close", 30, () =>
        {
            var mound = stone.Take(Math.Min(stone.Count, 12)).ToList();
            var at = Middle(mound);
            ViewAt(at, new Vector3(-4.5f, 2.2f, 4.5f), 1.2f);
        });
        Shot("rocks_04_with_player", 60, () =>
        {
            Player.ThirdPerson = true;
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + new Vector3(-6f, 1f, 5f), 0f);
            ViewAt(_meadow, new Vector3(-12f, 4f, 10f), 1.2f);
        });
        Shot("rocks_05_small_go", 240, () =>
        {
            _meadow = Middle(small);
            GD.Print($"[capture] small deposit of {small.Count} blocks at {_meadow}");
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + new Vector3(-5f, 2f, 5f), 0f);
        });
        Shot("rocks_05_small_deposit", 30, () => { _main.Flora.BuildAll(); ViewAt(_meadow, new Vector3(-7f, 3.5f, 7f), 1.0f); });
        var metal = new List<WorldObjectPlacement>();
        Shot("rocks_06_metal_go", 240, () =>
        {
            metal = Deposits("metal_outcrop")[0];
            _meadow = Middle(metal);
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + new Vector3(-6f, 2f, 6f), 0f);
        });
        Shot("rocks_06_metal_deposit", 30, () => { _main.Flora.BuildAll(); ViewAt(_meadow, new Vector3(-9f, 4f, 9f), 1.0f); });
        Shot("rocks_07_metal_close", 30, () => ViewAt(_meadow, new Vector3(-4f, 1.8f, 4f), 0.9f));
        Shot("rocks_08_back", 240, () =>
        {
            _meadow = Middle(stone);
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + new Vector3(-9f, 2f, 9f), 0f);
        });
        Shot("rocks_09_large_drilled", 90, () =>
        {
            // Break a large ground block on the deposit's edge as a drill would; its three boulders fall out onto the grass.
            var middle = _meadow with { Y = 0 };
            var block = stone.Where(o => o.Definition.Id!.EndsWith("_large", StringComparison.Ordinal))
                .DefaultIfEmpty(stone[0])
                .OrderBy(o => o.Position.Y).ThenByDescending(o => (o.Position.ToGodot() with { Y = 0 }).DistanceTo(middle)).First();
            _main.Survival.Actions!.StrikeObject(block.Id.ToString(), 10000, DamageKinds.Drill);
            _meadow = block.Position.ToGodot();
            ViewAt(_meadow, new Vector3(-3.5f, 2.6f, 3.5f), 0.6f);
        });
        Shot("rocks_10_boulders", 60, () => ViewLoose(c => c.Id!.EndsWith("_boulder", StringComparison.Ordinal), 3.6f));
        Shot("rocks_11_chunks", 90, () =>
        {
            // Drill every boulder into chunks.
            foreach (var boulder in LooseWith(d => d.Id!.EndsWith("_boulder", StringComparison.Ordinal)))
                _main.Build.Tools!.DamagePart(boulder, boulder.Parts.First().Id, 10000);
        });
        Shot("rocks_12_chunks", 30, () => ViewLoose(c => c.Id!.EndsWith("_chunk", StringComparison.Ordinal), 3.0f));
        Shot("rocks_13_rods", 90, () =>
        {
            // Break every chunk into rods, as the Sledgehammer would.
            foreach (var chunk in LooseWith(d => d.Id!.EndsWith("_chunk", StringComparison.Ordinal)))
            {
                var part = chunk.Parts.First().Id;
                for (var i = 0; i < 20 && _main.Creations.Find(chunk.Id) is not null; i++)
                    _main.Build.Tools!.DamagePart(chunk, part, 25);
            }
        });
        Shot("rocks_14_rods", 30, () => ViewLoose(c => c.Refine is not null, 3.6f));
        Shot("rocks_15_rods_close", 30, () => ViewLoose(c => c.Refine is not null, 2.2f));
    }

    // An oil pond with its geysers, the big Oil Pool, the chemical facility's pools and a chemical pool, each wide and close.
    private void OilShots()
    {
        Engine.TimeScale = 1.0;
        var gen = _main.Streamer.Generator;
        PoiInstance Nearest(string id) => gen.Pois.Where(p => p.Poi.Prefab.Id == id).MinBy(p => p.Centre.Length())!;
        void Visit(string id, string name, float wide, float close)
        {
            PoiInstance? poi = null;
            Shot($"oil_00_{name}_go", 240, () =>
            {
                poi = Nearest(id);
                _meadow = new Vector3(poi.Centre.X, poi.PadHeight, poi.Centre.Y);
                GD.Print($"[capture] {id} at {_meadow}");
                _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + new Vector3(-wide * 0.6f, 2f, wide * 0.6f), 0f);
                _camera.MakeCurrent();
                _camera.GlobalPosition = _meadow + new Vector3(0, 30, 0);
            });
            Shot($"oil_{name}_wide", 30, () => { _main.Flora.BuildAll(); ViewAt(_meadow, new Vector3(-wide, wide * 0.45f, wide), 0f); });
            Shot($"oil_{name}_other_side", 30, () => ViewAt(_meadow, new Vector3(wide * 0.9f, wide * 0.3f, -wide * 0.8f), 0f));
            Shot($"oil_{name}_close", 30, () => ViewAt(_meadow, new Vector3(-close, close * 0.35f, close), 0f));
        }
        Shot("oil_00_wait", 60, () => { });
        Visit("oil_pond", "pond", 20f, 9f);
        Visit("poi_49", "oil_pool", 38f, 16f);
        Visit("chemical_pool", "chemical_pool", 26f, 12f);
        Visit("poi_23", "chemical_facility", 45f, 18f);
        Shot("oil_00_geyser_go", 240, () =>
        {
            // The pond's geyser nearest the origin, seen from outside the pond with the player beside it.
            var pond = Nearest("oil_pond");
            var centre = new Vector3(pond.Centre.X, pond.PadHeight, pond.Centre.Y);
            var geyser = gen.ObjectsIn(pond.Centre.X - 20, pond.Centre.Y - 20, pond.Centre.X + 20, pond.Centre.Y + 20)
                .Where(o => o.Definition.Id == "oil_geyser").MinBy(o => o.Position.Length())!;
            _meadow = geyser.Position.ToGodot();
            _away = ((_meadow - centre) with { Y = 0 }).Normalized();
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + _away.Rotated(Vector3.Up, 1.2f) * 1.8f + Vector3.Up, 0f);
        });
        Shot("oil_geyser_with_player", 60, () => { Player.ThirdPerson = true; ViewAt(_meadow, _away * 5.5f + Vector3.Up * 2.4f, 0.5f); });
        Shot("oil_geyser_close", 30, () => ViewAt(_meadow, _away.Rotated(Vector3.Up, -0.5f) * 2.4f + Vector3.Up * 1.3f, 0.3f));
    }

    private Vector3 _away;

    // The nearest of each small spaced POI kind (World POI plan), wide and close.
    // The small finds nearest the world's centre: a loot crate, the three Bloom Pods and a scrap wreck, wide and close.
    private void FindShots()
    {
        Engine.TimeScale = 1.0;
        var gen = _main.Streamer.Generator;
        Shot("finds_00_wait", 60, () => { });
        foreach (var id in new[] { "loot_crate", "bloom_pod_green", "bloom_pod_purple", "bloom_pod_gold", "scrap_wreck" })
        {
            Shot($"finds_00_{id}_go", 240, () =>
            {
                var find = gen.ObjectsIn(-3000f, -3000f, 3000f, 3000f).Where(o => o.Definition.Id == id).MinBy(o => new Vector2(o.Position.X, o.Position.Z).Length())!;
                _meadow = new Vector3(find.Position.X, find.Position.Y, find.Position.Z);
                _away = new Vector3(1f, 0f, 0.4f).Normalized();
                GD.Print($"[capture] {id} at {_meadow}");
                _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + _away.Rotated(Vector3.Up, 1.2f) * 5f + Vector3.Up * 2f, 0f);
            });
            Shot($"finds_{id}_wide", 30, () => { _main.Flora.BuildAll(); ViewAt(_meadow, _away * 9f + Vector3.Up * 4f, 0.4f); });
            Shot($"finds_{id}_close", 30, () => ViewAt(_meadow, _away.Rotated(Vector3.Up, -0.5f) * 3.5f + Vector3.Up * 1.6f, 0.4f));
        }
    }

    private void SmallPoiShots()
    {
        Engine.TimeScale = 1.0;
        var gen = _main.Streamer.Generator;
        Shot("small_00_wait", 60, () => { });
        foreach (var id in new[] { "camp", "forest_ruin", "roadside_kiosk", "farming_patch", "build_area", "shore_wreck", "ruin_cluster" })
        {
            var wide = id is "build_area" or "farming_patch" ? 36f : 24f;
            Shot($"small_00_{id}_go", 240, () =>
            {
                var poi = gen.Pois.Where(p => p.Poi.Prefab.Id == id).MinBy(p => p.Centre.Length())!;
                _meadow = new Vector3(poi.Centre.X, poi.PadHeight, poi.Centre.Y);
                // Look from the POI's front (−z), where a kiosk's road is.
                var front = poi.ToWorld(new System.Numerics.Vector2(0f, -1f)) - poi.Centre;
                _away = new Vector3(front.X, 0f, front.Y);
                GD.Print($"[capture] {id} at {_meadow}");
                _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + _away.Rotated(Vector3.Up, 0.6f) * 6f + Vector3.Up * 2f, 0f);
            });
            Shot($"small_{id}_wide", 30, () => { _main.Flora.BuildAll(); ViewAt(_meadow, _away.Rotated(Vector3.Up, 0.5f) * wide + Vector3.Up * wide * 0.45f, 0f); });
            Shot($"small_{id}_close", 30, () => ViewAt(_meadow, _away.Rotated(Vector3.Up, -0.4f) * wide * 0.5f + Vector3.Up * wide * 0.2f, 1f));
        }
    }

    // Loose single parts lying in the world whose definition matches.
    private List<Core.Creations.Creation> LooseWith(Func<Core.Data.PartDefinition, bool> which) =>
        [.. _main.Creations.All.Where(c => c.Parts.Count == 1 && c.DefinitionOf(c.Parts.First().Id) is { } d && which(d))];

    private static readonly string[] TreeTypes = ["birch_tree", "leafy_tree", "spruce_tree", "pine_tree", "autumn_leafy_tree", "burnt_tree"];

    // Every tree type alone with the player beside it and close up, each kind of forest wide, and a felled tree's logs.
    private void TreeShots()
    {
        Engine.TimeScale = 1.0;
        var gen = _main.Streamer.Generator;
        var found = new Dictionary<string, WorldObjectPlacement>();
        Shot("trees_00_wait", 60, () =>
        {
            // The clearest example of each type near the start: on flat ground, with nothing else close by.
            var start = _main.Streamer.SpawnPoint();
            var all = gen.ObjectsIn(start.X - 3000, start.Z - 3000, start.X + 3000, start.Z + 3000).ToList();
            var trees = all.Where(o => TreeTypes.Contains(o.Definition.Id)).ToList();
            foreach (var id in TreeTypes)
            {
                var best = trees.Where(o => o.Definition.Id == id && gen.SlopeAt(o.Position.X, o.Position.Z) < 0.12f && !gen.PoisAt(o.Position.X, o.Position.Z).Any())
                    .Select(o => (o, Room: Room(o, trees)))
                    .Where(t => t.Room > 6f)
                    .OrderBy(t => (new Vector3(t.o.Position.X, 0, t.o.Position.Z) - (start with { Y = 0 })).Length() - t.Room * 40f)
                    .Select(t => t.o).FirstOrDefault();
                if (best is not null)
                    found[id] = best;
                GD.Print($"[capture] {id}: {(best is null ? "none" : best.Position.ToString())}");
            }
        });
        foreach (var id in TreeTypes)
        {
            var name = id.Replace("_tree", "");
            Shot($"trees_00_go_{name}", 240, () => GoTo(found.GetValueOrDefault(id), new Vector3(-6f, 2f, 6f)));
            Shot($"trees_{name}_01", 30, () => { _main.Flora.BuildAll(); TreeView(found.GetValueOrDefault(id), new Vector3(-1.6f, 0.3f, 1.25f), 0.48f, 0.0f); });
            Shot($"trees_{name}_02_with_player", 60, () => TreeView(found.GetValueOrDefault(id), new Vector3(1.25f, 0.15f, 1.35f), 0.45f, 0.0f, player: true));
            Shot($"trees_{name}_03_close", 30, () => TreeView(found.GetValueOrDefault(id), new Vector3(0.38f, 0.05f, 0.42f), 0.62f, 0.0f));
        }
        foreach (var (biome, id) in new[] { ("forest", "pine_tree"), ("autumn", "autumn_leafy_tree"), ("burned", "burnt_tree") })
        {
            Shot($"trees_00_go_{biome}_forest", 240, () => GoTo(found.GetValueOrDefault(id), new Vector3(-6f, 2f, 6f)));
            Shot($"trees_{biome}_forest", 40, () =>
            {
                _main.Flora.BuildAll();
                if (found.GetValueOrDefault(id) is { } tree)
                    ViewAt(tree.Position.ToGodot(), new Vector3(-22f, 4f, 22f), 5f);
            });
        }
        // Every tree in a row on open meadow (the far models in a second row behind), with the player in front.
        var row = new List<Node3D>();
        Shot("trees_00_go_lineup", 240, () =>
        {
            _meadow = default;
            for (var r = 100f; r < 3000f && _meadow == default; r += 50f)
            {
                for (var a = 0f; a < Mathf.Tau && _meadow == default; a += 0.2f)
                {
                    var start = _main.Streamer.SpawnPoint();
                    var (x, z) = (start.X + r * Mathf.Cos(a), start.Z + r * Mathf.Sin(a));
                    if (gen.BiomeAt(x, z).Id == "meadow" && gen.HeightAt(x, z) > 6f && new[] { -25f, -12f, 0f, 12f, 25f }.All(d => gen.SlopeAt(x + d, z) < 0.08f && gen.SlopeAt(x + d, z - 14) < 0.08f && Mathf.Abs(gen.HeightAt(x + d, z) - gen.HeightAt(x, z)) < 1.2f)
                        && !gen.PoisAt(x, z).Any() && !gen.ObjectsIn(x - 30, z - 18, x + 30, z + 4).Any(o => o.Definition.Mesh is not null))
                        _meadow = new Vector3(x, gen.HeightAt(x, z), z);
                }
            }
            GD.Print($"[capture] lineup at {_meadow}");
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + new Vector3(0f, 0.5f, 9f), 0f);
            Player.ThirdPerson = true;
            for (var i = 0; i < TreeTypes.Length; i++)
            {
                var definition = gen.Data.Definition.Objects!.First(o => o.Id == TreeTypes[i]);
                foreach (var (file, back) in new[] { (gen.Data.MeshFileOf(definition), 0f), (gen.Data.LodFileOf(definition), 0.01f) })
                {
                    if (file is null || Creations.PartAssets.ReadGltf(file, file)?.FirstOrDefault() is not { } mesh)
                        continue;
                    var x = (i - (TreeTypes.Length - 1) / 2f) * 8.5f;
                    var tree = new MeshInstance3D { Mesh = mesh.Mesh };
                    _main.AddChild(tree);
                    tree.GlobalPosition = Ground(_meadow + new Vector3(x, 0, 0));
                    tree.RotationDegrees = new Vector3(0, 30f * i, 0);
                    tree.Visible = back == 0f; // far models swap in for the second shot
                    row.Add(tree);
                }
            }
        });
        Shot("trees_lineup", 40, () => { _main.Flora.BuildAll(); View(new Vector3(0f, 6f, 36f), new Vector3(0f, 4.5f, 0f)); });
        Shot("trees_lineup_far_models", 20, () => { foreach (var tree in row) tree.Visible = !tree.Visible; });
        Shot("trees_00_lineup_done", 2, () => { foreach (var tree in row) tree.QueueFree(); });
        Shot("trees_00_go_fell", 240, () => GoTo(found.GetValueOrDefault("leafy_tree"), new Vector3(-6f, 2f, 6f)));
        Shot("trees_felled_logs", 240, () =>
        {
            if (found.GetValueOrDefault("leafy_tree") is not { } tree)
                return;
            _main.Survival.Actions!.StrikeObject(tree.Id.ToString(), 10000, DamageKinds.Saw);
            ViewAt(tree.Position.ToGodot(), new Vector3(-9f, 5f, 9f), 0.5f);
        });
    }

    // Borders between two inland biomes near the start, each from above and from head height, looking across the border.
    private void BiomeBorderShots()
    {
        Engine.TimeScale = 1.0;
        var gen = _main.Streamer.Generator;
        var borders = new List<(string Name, Vector3 At, Vector3 Across)>();
        Shot("biomes_00_wait", 60, () =>
        {
            // A spot a little way from the start where one biome lies 45 m behind and another 45 m ahead, on gentle
            // ground with no POI or road close: one per pair of biomes.
            var start = _main.Streamer.SpawnPoint();
            var seen = new HashSet<string>();
            for (var r = 150f; r < 2600f && borders.Count < 6; r += 30f)
            {
                for (var a = 0f; a < Mathf.Tau && borders.Count < 6; a += 0.15f)
                {
                    var (x, z) = (start.X + r * Mathf.Cos(a), start.Z + r * Mathf.Sin(a));
                    for (var d = 0f; d < Mathf.Tau; d += Mathf.Pi / 4f)
                    {
                        var dir = new Vector3(Mathf.Cos(d), 0f, Mathf.Sin(d));
                        var near = gen.BiomeAt(x - 45f * dir.X, z - 45f * dir.Z);
                        var far = gen.BiomeAt(x + 45f * dir.X, z + 45f * dir.Z);
                        if (near == far || near.Hills < 1f || far.Hills < 1f || near == gen.Data.CoastBiome || far == gen.Data.CoastBiome)
                            continue;
                        var pair = string.CompareOrdinal(near.Id, far.Id) < 0 ? $"{near.Id}_{far.Id}" : $"{far.Id}_{near.Id}";
                        if (seen.Contains(pair) || gen.HeightAt(x, z) < gen.SeaLevel + 3f
                            || new[] { -50f, -25f, 0f, 25f, 50f }.Any(s => gen.SlopeAt(x + s * dir.X, z + s * dir.Z) > 0.25f)
                            || gen.Pois.Any(p => new Vector2(p.Centre.X - x, p.Centre.Y - z).Length() < p.Radius + 90f)
                            || gen.Roads.Near(x, z, 70f))
                            continue;
                        seen.Add(pair);
                        borders.Add((pair, new Vector3(x, gen.HeightAt(x, z), z), dir));
                        GD.Print($"[capture] border {near.Id} -> {far.Id} at ({x:0}, {z:0})");
                        break;
                    }
                }
            }
        });
        for (var i = 0; i < 6; i++)
        {
            var index = i;
            Shot($"biomes_00_go_{i}", 300, () =>
            {
                if (index >= borders.Count)
                    return;
                var (_, at, across) = borders[index];
                _meadow = at;
                _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, at - across * 40f + Vector3.Up * 2f, 0f);
                _camera.MakeCurrent();
                _camera.GlobalPosition = at + new Vector3(0, 60, 0);
            });
            Shot($"biomes_{i}_wide", 40, () =>
            {
                if (index >= borders.Count)
                    return;
                var (_, at, across) = borders[index];
                _main.Flora.BuildAll();
                var side = new Vector3(-across.Z, 0f, across.X);
                ViewAt(at, -across * 70f + side * 25f + Vector3.Up * 28f, 0f);
            });
            Shot($"biomes_{i}_eye", 20, () =>
            {
                if (index >= borders.Count)
                    return;
                var (_, at, across) = borders[index];
                ViewAt(at + across * 15f, -across * 45f + Vector3.Up * 2.2f, 1.5f);
            });
        }
    }

    // How far a tree stands from its nearest neighbouring tree.
    private static float Room(WorldObjectPlacement tree, List<WorldObjectPlacement> trees) =>
        trees.Where(o => o != tree && MathF.Abs(o.Position.X - tree.Position.X) < 30 && MathF.Abs(o.Position.Z - tree.Position.Z) < 30)
            .Select(o => new Vector2(o.Position.X - tree.Position.X, o.Position.Z - tree.Position.Z).Length()).DefaultIfEmpty(30f).Min();

    private void GoTo(WorldObjectPlacement? at, Vector3 offset)
    {
        if (at is null)
            return;
        _meadow = at.Position.ToGodot();
        _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + offset, 0f);
        Player.ThirdPerson = true;
        _camera.MakeCurrent();
        _camera.GlobalPosition = _meadow + new Vector3(0, 30, 0);
    }

    // Frames a tree: the camera sits `offset` tree heights away (x, z) and up (y), looking at `look` of its height.
    private void TreeView(WorldObjectPlacement? tree, Vector3 offset, float look, float lookUp, bool player = false)
    {
        if (tree is null)
            return;
        var at = tree.Position.ToGodot();
        var height = (tree.Definition.Fell?.Height ?? 8f) * tree.Scale * 1.15f;
        if (player)
        {
            // Beside the trunk, facing the camera's side.
            var aside = tree.Definition.Footprint * tree.Scale * 0.75f + 1f;
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, at + new Vector3(aside * 0.7f, 0.3f, aside * 0.7f), Mathf.Pi * 0.25f);
        }
        _follow = null;
        _camera.MakeCurrent();
        var from = at + new Vector3(offset.X, 0, offset.Z) * height;
        _camera.GlobalPosition = Ground(from) + Vector3.Up * (offset.Y * height + 1.2f);
        _camera.LookAt(at + Vector3.Up * (look * height * (1f - lookUp) + 1.2f * lookUp));
    }

    // A Soil Bag carried, lifted and tipped out, a mound spilling out of it, and mounds in each of their four looks.
    private void SoilShots()
    {
        const string soil = "vanilla:soil";
        var feet = Vector3.Zero;
        void PlaceMound(float x, float z) => _main.Actions.Request(Core.Building.BuildActions.PlacePart,
            new Core.Building.PlacePartArgs(soil, Core.Creations.GridCell.Zero, Position: Core.Creations.Vec3Numerics.ToVec3((feet + new Vector3(x, 0.125f, z)).ToNumerics())), r => GD.Print($"[capture] mound: {r.Ok} {r.Message}"));
        Shot("soil_00_wait", 60, () => { });
        Shot("soil_01_bag_lift", 3, () => { Hold(CharacterProp.SoilBag); Look(new Vector3(-1.7f, 1.3f, -2.3f)); });
        Shot("soil_02_bag_hold", 40, () => { });
        Shot("soil_03_bag_side", 20, () => Look(new Vector3(-2.6f, 1.2f, 0.3f)));
        Shot("soil_04_bag_spill", 14, () => { Look(new Vector3(-1.7f, 1.3f, -2.3f)); Player.Gesture(CharacterGesture.BagSpill); });
        Shot("soil_05_bag_shake", 10, () => { });
        Shot("soil_06_first_person", 40, () => { Input(); Player.MakeCameraCurrent(); });
        Shot("soil_07_spill_start", 12, () =>
        {
            feet = Player.GlobalPosition;
            Look(new Vector3(-1.6f, 1.6f, -3.2f), 0.4f);
            PlaceMound(0f, -1.5f);
        });
        Shot("soil_08_spill_tip", 10, () => { });
        Shot("soil_09_spill_settle", 12, () => { });
        Shot("soil_10_spilled", 30, () => { });
        Shot("soil_11_row", 60, () =>
        {
            Hold(CharacterProp.None);
            foreach (var x in new[] { -1.5f, -0.5f, 0.5f, 1.5f })
                PlaceMound(x, -3f);
        });
        Shot("soil_12_states", 300, () =>
        {
            Engine.TimeScale = 1.0;
            var host = _main.Survival.Actions!;
            var mounds = _main.Creations.All.Where(c => c.Parts.Count == 1 && c.Parts.First().Part == soil)
                .Select(c => (Creation: c, Part: c.Parts.First().Id, At: c.BodyPose(0).Position))
                .Where(m => m.At.Z < feet.Z - 2.5f).OrderBy(m => m.At.X).ToList();
            GD.Print($"[capture] {mounds.Count} mounds in the row");
            var definition = _main.Build.Catalog.FindDefinition(soil)!.Soil!;
            var wetAt = Core.Survival.SoilState.WetAt(definition);
            var looks = new (float Water, float Then)[] { (0, 0), (60, 60), (300, 300), (300, 100) };
            for (var i = 0; i < mounds.Count && i < looks.Length; i++)
            {
                var state = mounds[i].Creation.SoilOf(mounds[i].Part)!;
                if (looks[i].Water > 0)
                    state.AddWater(looks[i].Water, definition.Capacity ?? 0, wetAt);
                state.Set(water: looks[i].Then, wetAt: wetAt);
            }
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = feet + new Vector3(0f, 1.9f, -0.6f);
            _camera.LookAt(feet + new Vector3(0f, 0f, -3f));
        });
        Shot("soil_13_states_close", 30, () =>
        {
            _camera.GlobalPosition = feet + new Vector3(-0.2f, 1.0f, -1.7f);
            _camera.LookAt(feet + new Vector3(0f, 0f, -3f));
        });
    }

    private void KnockoutBagShots()
    {
        var spawn = Vector3.Zero;
        var at = Vector3.Zero;
        Label3D? label = null;
        void View(Vector3 offset, float height = 0.2f)
        {
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = at + offset;
            _camera.LookAt(at + Vector3.Up * height);
        }
        Shot("knockout_bag_00_wait", 60, () => { });
        Shot("knockout_bag_00_walk", 10, () =>
        {
            // Away from the spawn point, so the respawned player stands beside the bag rather than on it.
            spawn = Player.GlobalPosition;
            Player.GlobalPosition = Ground(spawn + new Vector3(2.5f, 0f, 0f)) + Vector3.Up * 0.1f;
        });
        Shot("knockout_bag_01_knocked_out", 30, () =>
        {
            _main.Survival.Actions!.Damage(Player.PeerId, 1000, "capture");
            Look(new Vector3(-1.6f, 1.4f, -2.2f), 0.4f);
        });
        Shot("knockout_bag_02_front", 10, () =>
        {
            var drop = _main.Survival.DropNodes.Values.First(d => d.Drop!.KnockedOut);
            at = drop.GlobalPosition;
            GD.Print($"[capture] knockout bag at {at}: {drop.Text.Replace('\n', ',')}");
            label = drop.GetNode<Label3D>("Label");
            label.Visible = false; // the turnaround shows the bag alone; the label comes back for its own shot
            View(new Vector3(0f, 0.3f, 1.25f));
        });
        Shot("knockout_bag_03_right", 10, () => View(new Vector3(1.25f, 0.3f, 0f)));
        Shot("knockout_bag_04_left", 10, () => View(new Vector3(-1.25f, 0.3f, 0f)));
        Shot("knockout_bag_05_back", 10, () => View(new Vector3(0f, 0.3f, -1.25f)));
        Shot("knockout_bag_06_top", 10, () => View(new Vector3(0f, 1.3f, 0.3f), 0.1f));
        Shot("knockout_bag_07_three_quarter", 10, () => View(new Vector3(0.85f, 0.7f, 0.95f)));
        Shot("knockout_bag_08_label", 10, () => { label!.Visible = true; View(new Vector3(0.6f, 1.1f, 2.4f), 0.5f); });
        Shot("knockout_bag_09_respawned", 30, () =>
        {
            _main.Actions.Request(Core.Survival.SurvivalActions.Respawn, new Core.Actions.NoArgs(), r => GD.Print($"[capture] respawn: {r.Ok} {r.Message}"));
            Player.GlobalPosition = Ground(at + new Vector3(0.9f, 0f, 0.4f)) + Vector3.Up * 0.1f;
        });
        Shot("knockout_bag_10_with_player", 20, () => View(new Vector3(-1.2f, 1.3f, 2.8f), 0.6f));
    }

    // Frames the loose parts matching a test from the side away from the outcrop's middle.
    private void ViewLoose(Func<Core.Data.PartDefinition, bool> which, float distance)
    {
        var at = _main.Creations.All.Where(c => c.Parts.Count == 1 && c.DefinitionOf(c.Parts.First().Id) is { } d && which(d))
            .Select(c => _main.Creations.NodeOf(c.Id)?.PartNode(c.Parts.First().Id)?.GlobalPosition).OfType<Vector3>().ToList();
        if (at.Count == 0)
            return;
        var centre = at.Aggregate(Vector3.Zero, (a, b) => a + b) / at.Count;
        var away = (centre - _meadow) with { Y = 0 };
        away = away.LengthSquared() < 1e-4f ? Vector3.Back : away.Normalized();
        var side = away.Cross(Vector3.Up);
        _follow = null;
        _camera.MakeCurrent();
        _camera.GlobalPosition = centre + (away + side * 0.5f).Normalized() * distance + Vector3.Up * distance * 0.6f;
        _camera.LookAt(centre);
    }

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

    // Three little cars in a meadow, one on each kind of wheel, then the wheels on the hotbar.
    private void WheelShots()
    {
        Engine.TimeScale = 1.0;
        var kinds = new[] { ("vanilla:scrap_wheel", 1, 4), ("vanilla:wheel", 1, 4), ("vanilla:big_wheel", 2, 6) };
        var spots = new[] { -2.4f, 0f, 2.8f };
        Shot("wheels_00_wait", 60, () => { });
        Shot("wheels_00_go", 240, () =>
        {
            var g = _main.Streamer.Generator;
            Go(Find(p => new[] { (0f, 0f), (12f, 0f), (-12f, 0f), (0f, 12f), (0f, -12f) }.All(d => g.BiomeAt(p.X + d.Item1, p.Y + d.Item2).Id == "meadow")
                && g.SlopeAt(p.X, p.Y) < 0.06f && !g.PoisAt(p.X, p.Y).Any()));
            GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Grass = FoliageLevel.Off, GroundCover = FoliageLevel.Off, Bushes = FoliageLevel.Off } });
            _main.Flora.BuildAll();
        });
        // The player stands at the meadow spot: move them out of the way before the cars come down there.
        Shot("wheels_00_step_aside", 30, () => _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, Ground(_meadow + new Vector3(0.4f, 0, 4.5f)) + Vector3.Up * 0.3f, 0f));
        Shot("wheels_00_build", 240, () =>
        {
            for (var i = 0; i < kinds.Length; i++)
                SpawnCar(9100 + i, kinds[i].Item1, kinds[i].Item2, kinds[i].Item3, Ground(_meadow + new Vector3(spots[i], 0, 0)) + Vector3.Up * (kinds[i].Item2 == 1 ? 1.0f : 1.6f));
            for (var i = 0; i < kinds.Length; i++)
                _main.Hotbar.Set(5 + i, kinds[i].Item1, r => GD.Print($"[capture] hotbar {r}"));
        });
        Shot("wheels_01_row", 30, () => View(new Vector3(-1.2f, 1.5f, 5.2f), new Vector3(0.3f, 0.35f, 0.6f)));
        Shot("wheels_02_row_low", 20, () => View(new Vector3(-4.5f, 0.45f, 3.4f), new Vector3(0.6f, 0.4f, 0.6f)));
        Shot("wheels_03_scrap", 20, () => View(new Vector3(spots[0] - 1.2f, 0.7f, 1.1f), new Vector3(spots[0] - 0.4f, 0.3f, 0f)));
        Shot("wheels_04_wheel", 20, () => View(new Vector3(spots[1] - 1.2f, 0.7f, 1.1f), new Vector3(spots[1] - 0.4f, 0.3f, 0f)));
        Shot("wheels_05_off_road", 20, () => View(new Vector3(spots[2] - 1.9f, 1.0f, 1.8f), new Vector3(spots[2] - 0.6f, 0.55f, 0f)));
        Shot("wheels_06_tread", 20, () => View(new Vector3(spots[2] + 2.0f, 0.5f, -4.2f), new Vector3(spots[2] + 0.4f, 0.45f, -0.6f)));
        Shot("wheels_07_hotbar", 60, () =>
        {
            // The mouse would set the look every tick; stop it, so the player looks at the cars with the Off Road Wheel picked.
            foreach (var reader in _main.FindChildren("*", owned: false).OfType<LocalPlayerInput>())
                reader.SetPhysicsProcess(false);
            _main.Tools!.SetProcess(true);
            _main.Tools.Select(7);
            Input(pitch: -0.5f);
            Player.MakeCameraCurrent();
        });
    }

    // A metal chassis along Z (z 0..length) with a post down at each end and a bearing on each side of each post, a
    // wheel of the given kind on each bearing (as VanillaLogicTest builds its car), dropped onto its wheels at `at`.
    private void SpawnCar(int id, string wheel, int wide, int length, Vector3 at)
    {
        var car = new Core.Creations.Creation(id, _main.Build.Catalog);
        Core.Creations.GridRotation Facing(Core.Creations.GridCell outward) =>
            Core.Creations.GridRotation.FromAxes(new Core.Creations.GridCell(0, -outward.X - outward.Z, 0), outward);
        void Put(string part, Core.Creations.GridCell cell, Core.Creations.GridRotation rotation = default)
        {
            if (!car.TryPlacePart(part, cell, rotation, out _, out var error))
                GD.Print($"[capture] {part} at {cell}: {error}");
        }
        var half = wide == 1 ? 1 : 2;
        for (var z = 0; z <= length; z++)
            Put("vanilla:metal_block", new Core.Creations.GridCell(0, 0, z));
        foreach (var z in new[] { 0, length })
        {
            Put("vanilla:metal_block", new Core.Creations.GridCell(0, -1, z));
            Put("vanilla:metal_block", new Core.Creations.GridCell(0, -2, z));
            foreach (var side in new[] { 1, -1 })
            {
                var outward = new Core.Creations.GridCell(side, 0, 0);
                var bearing = new Core.Creations.GridCell(side, -2, z);
                Put("vanilla:bearing", bearing, Facing(outward));
                var rotation = Facing(outward);
                Put(wheel, bearing + outward - rotation.Apply(new Core.Creations.GridCell(half, 0, half)), rotation);
            }
        }
        car.SetAllBodyPoses(new Core.Creations.Pose(new System.Numerics.Vector3(at.X, at.Y, at.Z - length * 0.125f), System.Numerics.Quaternion.Identity));
        _main.Creations.Add(car, _main.Assets);
    }

    private (Vector3 From, Vector3 To)? _carView;

    // The debug scrap car a new debug world gets (Main.DebugCar): around it, the player beside it, then sitting in it,
    // driving off on W and turning on D (tools/capture-debug-car.sh). Views are in the car's frame: x across its 1.5 m
    // deck, y up, z from its front (0) to its back (3 m); it faces -Z.
    private void DebugCarShots()
    {
        Engine.TimeScale = 1.0;
        var middle = new Vector3(0.625f, 0.4f, 1.375f);
        void Car(Vector3 from, Vector3? to = null)
        {
            _follow = null;
            _camera.MakeCurrent();
            _carView = (from, to ?? middle);
        }
        void Report(string what)
        {
            var id = _main.DebugCarId!.Value;
            var creation = _main.Creations.Find(id)!;
            var container = creation.Parts.First(p => p.Part == "vanilla:gas_container").Id;
            var chassis = _main.Creations.NodeOf(id)!.Bodies[0];
            GD.Print($"[capture] car {what}: at {chassis.GlobalPosition}, up {chassis.GlobalBasis.Y}, speed {chassis.LinearVelocity.Length():F2} m/s, "
                + $"gasoline {creation.InventoryOf(container)!.Count("vanilla:gasoline")}");
        }
        Shot("debug_car_00_wait", 240, () => GameSettings.Apply(GameSettings.Current with
        {
            Graphics = GameSettings.Current.Graphics with { Grass = FoliageLevel.Off, GroundCover = FoliageLevel.Off },
        }));
        Shot("debug_car_01_front", 60, () => { Report("settled"); Car(new Vector3(3.6f, 2.0f, -3.2f)); });
        Shot("debug_car_02_side", 60, () => Car(new Vector3(5.2f, 1.2f, 1.4f)));
        Shot("debug_car_03_rear", 60, () => Car(new Vector3(-2.8f, 2.4f, 6.0f)));
        Shot("debug_car_04_top", 60, () => Car(new Vector3(0.625f, 6.5f, 2.6f), new Vector3(0.625f, 0f, 1.4f)));
        Shot("debug_car_05_axle", 60, () => Car(new Vector3(2.6f, 0.15f, -2.4f), new Vector3(0.6f, -0.4f, 0.4f)));
        Shot("debug_car_06_spawn", 60, () => Car(new Vector3(-5f, 3.2f, 16f), new Vector3(-1f, 0.4f, 3.5f)));
        Shot("debug_car_07_sit", 120, () =>
        {
            var id = _main.DebugCarId!.Value;
            var seat = _main.Creations.Find(id)!.Parts.First(p => p.Part == "vanilla:scrap_drivers_seat").Id;
            // The player walks up to the car's left side first: seats are in reach from there.
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _main.Creations.NodeOf(id)!.Bodies[0].GlobalTransform * new Vector3(-1.2f, 0f, 1.5f), 0f);
            Player.ThirdPerson = true; // shows the driver
            _main.Actions.Request(Core.Logic.LogicActions.EnterSeat, new Core.Logic.LogicPartArgs(id, seat), r => GD.Print($"[capture] sit: {r}"));
            Car(new Vector3(3.2f, 2.0f, -2.6f));
        });
        Shot("debug_car_08_seated_side", 60, () => Car(new Vector3(4.0f, 1.4f, 2.0f)));
        Shot("debug_car_09_drive", 300, () =>
        {
            Report("before W");
            Player.CurrentInput = new PlayerInput(0, 1, Player.CurrentInput.Yaw, 0, false, false);
            Car(new Vector3(2.5f, 2.6f, 8.5f));
        });
        Shot("debug_car_10_turn", 180, () =>
        {
            Report("after 5 s of W");
            Player.CurrentInput = new PlayerInput(1, 1, Player.CurrentInput.Yaw, 0, false, false);
            Car(new Vector3(-3.5f, 3.0f, 7.5f));
        });
        Shot("debug_car_11_stop", 120, () =>
        {
            Report("after 3 s of W and D");
            Player.CurrentInput = new PlayerInput(0, 0, Player.CurrentInput.Yaw, 0, false, false);
            Car(new Vector3(5.5f, 3.0f, -2.5f));
        });
        Shot("debug_car_12_done", 2, () => Report("stopped"));
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

    private const int SeatCreation = 9001;
    private int _seat;

    // A slab of metal blocks with the seat on it, a gas engine behind and a switch and a button beside it, all linked
    // from the seat, put down in front of the player.
    private const int PistonCreation = 9300;

    // Pistons at levels 1, 3 and 5 on a slab, each with a block on its head and all linked to one switch: put down,
    // pushing out (the rod coming out of the neck segment by segment), fully out, and back in.
    private void PistonShots()
    {
        Engine.TimeScale = 1.0;
        var toggle = 0;
        Shot("piston_00_wait", 60, () => { });
        Shot("piston_01_placed", 90, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 3, -3.5f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 12));
            _liftAt = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            var creation = new Core.Creations.Creation(PistonCreation, _main.Mods!);
            int Put(string part, int x, int y, int z) =>
                creation.TryPlacePart(part, new Core.Creations.GridCell(x, y, z), Core.Creations.GridRotation.Identity, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException(error);
            for (var x = -3; x <= 3; x++)
                for (var z = -1; z <= 1; z++)
                    Put("vanilla:metal_block", x, 0, z);
            toggle = Put("vanilla:switch", 0, 1, -1);
            foreach (var (x, level) in new[] { (-2, 0), (0, 2), (2, 4) })
            {
                var id = Put("vanilla:piston", x, 1, 0);
                creation.SetLevel(id, level);
                Put("vanilla:metal_block", x, 2, 0);
                creation.TryLink(toggle, id, out _);
            }
            creation.SetAllBodyPoses(new Core.Creations.Pose(new System.Numerics.Vector3(_liftAt.X, _liftAt.Y + 0.2f, _liftAt.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
            LookAtLift(new Vector3(1.4f, 1.0f, -1.9f), 0.45f);
        });
        Shot("piston_02_close", 2, () => LookAtLift(new Vector3(0.55f, 0.65f, -0.8f), 0.4f));
        Shot("piston_03_extending", 50, () =>
        {
            _main.Actions.Request(Core.Logic.LogicActions.ActivatePart, new Core.Logic.LogicPartArgs(PistonCreation, toggle), r => GD.Print($"[capture] switch: {r}"));
            LookAtLift(new Vector3(2.6f, 1.6f, -3.6f), 1.0f);
        });
        Shot("piston_04_extended", 300, () => LookAtLift(new Vector3(3.6f, 2.2f, -5.2f), 1.8f));
        Shot("piston_05_rod_close", 2, () => LookAtLift(new Vector3(0.8f, 0.9f, -1.1f), 0.7f));
        Shot("piston_06_retracting", 40, () =>
        {
            _main.Actions.Request(Core.Logic.LogicActions.ActivatePart, new Core.Logic.LogicPartArgs(PistonCreation, toggle), r => GD.Print($"[capture] switch: {r}"));
            LookAtLift(new Vector3(3.6f, 2.2f, -5.2f), 1.8f);
        });
    }

    private void ScrapSeatShots()
    {
        Engine.TimeScale = 1.0;
        Shot("scrap_seat_00_wait", 60, () => { });
        Shot("scrap_seat_01_placed", 90, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 3, -3.5f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 12));
            var at = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            var creation = new Core.Creations.Creation(SeatCreation, _main.Mods!);
            int Put(string part, int x, int y, int z) =>
                creation.TryPlacePart(part, new Core.Creations.GridCell(x, y, z), Core.Creations.GridRotation.Identity, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException(error);
            for (var x = -2; x <= 3; x++)
                for (var z = -2; z <= 5; z++)
                    Put("vanilla:metal_block", x, 0, z);
            _seat = Put("vanilla:scrap_drivers_seat", -2, 1, -2);
            var engine = Put("vanilla:gas_engine", -1, 1, 4);
            var toggle = Put("vanilla:switch", 3, 1, -1);
            var press = Put("vanilla:button", 3, 1, 1);
            foreach (var to in new[] { engine, toggle, press })
                creation.TryLink(_seat, to, out _);
            creation.SetAllBodyPoses(new Core.Creations.Pose(new System.Numerics.Vector3(at.X, at.Y + 0.2f, at.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
            LookAtSeat(new Vector3(1.9f, 1.4f, -1.9f), 0.3f);
        });
        Shot("scrap_seat_02_side", 2, () => LookAtSeat(new Vector3(2.6f, 0.6f, 0f), 0.4f));
        Shot("scrap_seat_03_rear", 2, () => LookAtSeat(new Vector3(-1.2f, 1.3f, 2.4f), 0.4f));
        Shot("scrap_seat_04_enter", 6, () =>
        {
            _main.Actions.Request(Core.Logic.LogicActions.EnterSeat, new Core.Logic.LogicPartArgs(SeatCreation, _seat), r => GD.Print($"[capture] sit: {r}"));
            LookAtSeat(new Vector3(1.9f, 1.2f, -1.9f), 0.6f);
        });
        Shot("scrap_seat_05_enter", 12, () => { });
        Shot("scrap_seat_06_seated", 60, () => { });
        Shot("scrap_seat_07_seated_side", 2, () => LookAtSeat(new Vector3(2.4f, 0.8f, 0f), 0.6f));
        Shot("scrap_seat_08_seated_front", 2, () => LookAtSeat(new Vector3(0.6f, 1.0f, -2.4f), 0.6f));
        Shot("scrap_seat_09_seated_hands", 2, () => LookAtSeat(new Vector3(0.9f, 0.9f, -0.6f), 0.7f));
        Shot("scrap_seat_10_engine", 120, () =>
        {
            Player.CurrentInput = new PlayerInput(0, 1, Player.CurrentInput.Yaw, 0, false, false);
            LookAtSeat(new Vector3(1.9f, 1.2f, -1.9f), 0.6f);
        });
        // The item picture the hotbar, the inventory and the creation hotbar show, as the game makes it.
        Shot("scrap_seat_11_icon", 240, () =>
        {
            Player.CurrentInput = new PlayerInput(0, 0, Player.CurrentInput.Yaw, 0, false, false);
            _main.Icons!.For("vanilla:scrap_drivers_seat");
            _iconTick = () =>
            {
                if (_main.Icons.For("vanilla:scrap_drivers_seat") is not { } texture)
                    return;
                var path = $"{_out}/icon_vanilla_scrap_drivers_seat.png";
                texture.GetImage().SavePng(path);
                GD.Print($"[capture] {path}");
                _iconTick = null;
            };
        });
        Shot("scrap_seat_12_exit", 10, () =>
        {
            _main.Actions.Request(Core.Logic.LogicActions.LeaveSeat, new Core.Actions.NoArgs(), r => GD.Print($"[capture] stand: {r}"));
            LookAtSeat(new Vector3(1.9f, 1.2f, -1.9f), 0.4f);
        });
        Shot("scrap_seat_13_empty", 60, () => { });
    }

    private const int FabricatorCreation = 9002;
    private int _fabricator;
    private Transform3D _fabricatorAt;

    // A Portable Fabricator on the ground in front of the player, idle, then crafting a Connect Tool through the real
    // Craft action, so its clips follow the station's work as they do in play.
    private void PortableFabricatorShots()
    {
        Engine.TimeScale = 1.0;
        Shot("portable_fabricator_00_wait", 60, () => { });
        Shot("portable_fabricator_01_placed", 90, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 3, -2.2f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 12));
            var at = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            var creation = new Core.Creations.Creation(FabricatorCreation, _main.Mods!);
            if (!creation.TryPlacePart("vanilla:portable_fabricator", new Core.Creations.GridCell(0, 0, 0), Core.Creations.GridRotation.Identity, out var placed, out var error))
                throw new InvalidOperationException(error);
            _fabricator = placed.Id;
            // Face the player: the model's front is -Z.
            var yaw = Player.GlobalRotation.Y + Mathf.Pi;
            var rotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, yaw);
            creation.SetAllBodyPoses(new Core.Creations.Pose(new System.Numerics.Vector3(at.X, at.Y + 0.05f, at.Z), rotation));
            _main.Creations.Add(creation, _main.Assets);
            _fabricatorAt = new Transform3D(new Basis(Vector3.Up, yaw), at);
            LookAtFabricator(new Vector3(-1.6f, 1.3f, -2.1f));
        });
        Shot("portable_fabricator_02_front", 8, () => LookAtFabricator(new Vector3(0, 0.45f, -1.7f)));
        Shot("portable_fabricator_03_side", 8, () => LookAtFabricator(new Vector3(-2.2f, 0.45f, 0)));
        Shot("portable_fabricator_04_top", 8, () => LookAtFabricator(new Vector3(0, 2.6f, -0.3f)));
        Shot("portable_fabricator_05_back", 8, () => LookAtFabricator(new Vector3(1.6f, 1.2f, 2.1f)));
        Shot("portable_fabricator_06_with_player", 8, () =>
        {
            Player.ThirdPerson = true;
            _camera.MakeCurrent();
            _follow = null;
            _camera.GlobalPosition = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(-3.2f, 1.8f, 0.6f);
            _camera.LookAt(Player.GlobalPosition.Lerp(_fabricatorAt.Origin, 0.5f) + Vector3.Up * 0.6f);
        });
        Shot("portable_fabricator_07_idle", 20, () => LookAtFabricator(new Vector3(-0.9f, 1.0f, -1.3f), 0.45f));
        Shot("portable_fabricator_08_start", 4, () =>
        {
            void Give(string item, int count) =>
                _main.Actions.Request(Core.Items.InventoryActions.GiveItems, new Core.Items.GiveItemsArgs(item, count), r => GD.Print($"[capture] give {item}: {r}"));
            Give("vanilla:scrap_metal_block", 5);
            Give("vanilla:circuit_board", 2);
            _main.Actions.Request(Core.Items.InventoryActions.Craft,
                new Core.Items.CraftArgs(FabricatorCreation, _fabricator, "vanilla:connect_tool", 1), r => GD.Print($"[capture] craft: {r}"));
        });
        Shot("portable_fabricator_09_start", 10, () => { });
        Shot("portable_fabricator_10_crafting", 40, () => { });
        Shot("portable_fabricator_11_crafting", 23, () => { });
        Shot("portable_fabricator_12_crafting_wide", 8, () => LookAtFabricator(new Vector3(-1.6f, 1.3f, -2.1f)));
        Shot("portable_fabricator_13_crafting_close", 11, () => LookAtFabricator(new Vector3(-0.9f, 1.0f, -1.3f), 0.45f));
        // The Connect Tool takes 5 s at speed 0.5: the complete clip starts about 10 s after the craft.
        Shot("portable_fabricator_14_complete", 516, () => { });
        Shot("portable_fabricator_15_complete", 30, () => { });
        Shot("portable_fabricator_16_idle_again", 60, () => { });
        Shot("portable_fabricator_17_icon", 240, () =>
        {
            _main.Icons!.For("vanilla:portable_fabricator");
            _iconTick = () =>
            {
                if (_main.Icons.For("vanilla:portable_fabricator") is not { } texture)
                    return;
                var path = $"{_out}/icon_vanilla_portable_fabricator.png";
                texture.GetImage().SavePng(path);
                GD.Print($"[capture] {path}");
                _iconTick = null;
            };
        });
    }

    private const int ButtonCreation = 9002;
    private int _button, _buttonSeat;
    private Vector3 _buttonAt;

    // A slab with a button, a light it feeds and a seat linked to it: the button raised, held down with E (the warning light on),
    // let go, then held from the seat with its hotbar key, and its item picture.
    private void ButtonShots()
    {
        Engine.TimeScale = 1.0;
        Shot("button_00_wait", 60, () => { });
        Shot("button_01_placed", 90, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 3, -2.5f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 12));
            _buttonAt = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            var creation = new Core.Creations.Creation(ButtonCreation, _main.Mods!);
            int Put(string part, int x, int y, int z) =>
                creation.TryPlacePart(part, new Core.Creations.GridCell(x, y, z), Core.Creations.GridRotation.Identity, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException(error);
            for (var x = -3; x <= 3; x++)
                for (var z = -2; z <= 4; z++)
                    Put("vanilla:metal_block", x, 0, z);
            _button = Put("vanilla:button", 0, 1, 0);
            Put("vanilla:button", 1, 1, 0);
            var light = Put("vanilla:warning_light", -2, 1, 1);
            _buttonSeat = Put("vanilla:drivers_seat", 0, 1, 3);
            creation.TryLink(_button, light, out _);
            creation.TryLink(_buttonSeat, _button, out _);
            creation.SetAllBodyPoses(new Core.Creations.Pose(new System.Numerics.Vector3(_buttonAt.X, _buttonAt.Y + 0.2f, _buttonAt.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
            // The close shots are of the part, without the HUD.
            Ui.HudRoot.Hidden = true;
            LookAtButton(new Vector3(0.9f, 1.0f, -1.2f));
        });
        Shot("button_02_close", 2, () => LookAtButton(new Vector3(0.45f, 0.5f, -0.6f)));
        Shot("button_03_held", 30, () =>
        {
            _main.Actions.Request(Core.Logic.LogicActions.ActivatePart, new Core.Logic.LogicPartArgs(ButtonCreation, _button), r => GD.Print($"[capture] press: {r}"));
            LookAtButton(new Vector3(0.45f, 0.5f, -0.6f));
        });
        Shot("button_04_held_light", 2, () => LookAtButton(new Vector3(0.9f, 1.0f, -1.2f)));
        Shot("button_05_released", 30, () =>
            _main.Actions.Request(Core.Logic.LogicActions.ReleasePart, new Core.Logic.LogicPartArgs(ButtonCreation, _button), r => GD.Print($"[capture] release: {r}")));
        Shot("button_06_seated", 60, () =>
        {
            Ui.HudRoot.Hidden = false;
            _main.Actions.Request(Core.Logic.LogicActions.EnterSeat, new Core.Logic.LogicPartArgs(ButtonCreation, _buttonSeat), r => GD.Print($"[capture] sit: {r}"));
        });
        Shot("button_07_seat_key", 30, () =>
        {
            _main.CreationHotbar!.Use(0, () => true);
            LookAtButton(new Vector3(1.6f, 1.6f, -1.4f));
        });
        Shot("button_08_seat_let_go", 30, () => _main.CreationHotbar!.Release(0));
        Shot("button_09_icon", 240, () =>
        {
            _main.Actions.Request(Core.Logic.LogicActions.LeaveSeat, new Core.Actions.NoArgs(), r => GD.Print($"[capture] stand: {r}"));
            _main.Icons!.For("vanilla:button");
            _iconTick = () =>
            {
                if (_main.Icons.For("vanilla:button") is not { } texture)
                    return;
                var path = $"{_out}/icon_vanilla_button.png";
                texture.GetImage().SavePng(path);
                GD.Print($"[capture] {path}");
                _iconTick = null;
            };
        });
    }

    private void LookAtFabricator(Vector3 offset, float height = 0.3f)
    {
        Player.ThirdPerson = true;
        _camera.MakeCurrent();
        _follow = null;
        _camera.GlobalPosition = _fabricatorAt * offset;
        _camera.LookAt(_fabricatorAt * (Vector3.Up * height));
    }

    private void LookAtButton(Vector3 offset)
    {
        Player.ThirdPerson = true;
        _camera.MakeCurrent();
        _follow = null;
        // The button's plate: on top of the slab (block centres at the pose, 0.2 m up).
        var centre = _buttonAt + new Vector3(0, 0.2f + 0.125f + 0.03f, 0);
        _camera.GlobalPosition = centre + offset;
        _camera.LookAt(centre);
    }

    private static readonly string[] SeatParts = ["drivers_seat", "passenger_seat", "scrap_drivers_seat", "scrap_seat"];

    // Every vanilla seat side by side on one slab; the player sits in each in turn.
    private void SeatsShots()
    {
        Engine.TimeScale = 1.0;
        var seats = new Dictionary<string, int>();
        // The capture main reads no devices, so it has no seat camera of its own.
        if (_main.SeatCamera is null)
            AddChild(new Mechanica.Game.Logic.SeatCamera { Logic = _main.Logic, Players = _main.PlayerSync, ReadDeviceInput = false });
        Shot("seats_00_wait", 60, () => { });
        Shot("seats_00_placed", 90, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 3, -3.5f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 12));
            var at = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            var creation = new Core.Creations.Creation(SeatCreation, _main.Mods!);
            int Put(string part, int x, int y, int z) =>
                creation.TryPlacePart(part, new Core.Creations.GridCell(x, y, z), Core.Creations.GridRotation.Identity, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException(error);
            for (var x = -8; x <= 10; x++)
                for (var z = -2; z <= 3; z++)
                    Put("vanilla:metal_block", x, 0, z);
            var cell = -8;
            foreach (var part in SeatParts)
            {
                var size = _main.Mods!.FindDefinition("vanilla:" + part)!.Size!.Value;
                seats[part] = Put("vanilla:" + part, cell, 1, size.Z > 2 ? -2 : 0);
                cell += size.X + 2;
            }
            creation.SetAllBodyPoses(new Core.Creations.Pose(new System.Numerics.Vector3(at.X, at.Y + 0.2f, at.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
        });
        foreach (var part in SeatParts)
        {
            // Sitting down hands the view to the seat's interior camera: what the player sees from the seat.
            Shot($"seats_{part}_1_view", 90, () =>
            {
                _seat = seats[part];
                Player.ThirdPerson = true; // the game's default view, which drew the seated head around the camera
                Input(pitch: 0f);
                _main.Actions.Request(Core.Logic.LogicActions.EnterSeat, new Core.Logic.LogicPartArgs(SeatCreation, _seat), r => GD.Print($"[capture] sit {part}: {r}"));
            });
            Shot($"seats_{part}_1_view_down", 30, () => Input(pitch: -0.6f));
            Shot($"seats_{part}_1_enter", 2, () =>
            {
                Input(pitch: 0f);
                LookAtSeat(new Vector3(2.2f, 0.9f, -1.6f), 0.6f);
            });
            Shot($"seats_{part}_2_side", 2, () => LookAtSeat(new Vector3(2.2f, 0.7f, 0f), 0.6f));
            Shot($"seats_{part}_3_front", 2, () => LookAtSeat(new Vector3(0.3f, 0.9f, -2.2f), 0.6f));
            Shot($"seats_{part}_4_leave", 40, () =>
                _main.Actions.Request(Core.Logic.LogicActions.LeaveSeat, new Core.Actions.NoArgs(), r => GD.Print($"[capture] stand: {r}")));
        }
    }

    // Puts the capture camera at an offset from the seat (at the driver's feet), in the seat's frame.
    // Three switches on a slab of metal blocks in front of the player: one left off, one switched on, one on a wall; then
    // close-ups of each state, and the first one toggled on and off again (tools/capture-switch.sh).
    private void SwitchShots()
    {
        Engine.TimeScale = 1.0;
        int off = 0, on = 0, wall = 0;
        Node3D? Part(int part) => _main.Creations.NodeOf(SeatCreation)?.PartNode(part);
        void Toggle(int part) =>
            _main.Actions.Request(Core.Logic.LogicActions.ActivatePart, new Core.Logic.LogicPartArgs(SeatCreation, part), r => GD.Print($"[capture] toggle: {r}"));
        void Near(int part, Vector3 offset, float distance)
        {
            if (Part(part) is not { } node)
                return;
            var t = node.GlobalTransform;
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = t.Origin + t.Basis * offset.Normalized() * distance;
            _camera.LookAt(t.Origin + t.Basis * new Vector3(0, -0.09f, 0));
        }
        Shot("switch_00_wait", 60, () => { });
        Shot("switch_01_placed", 60, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 3, -2.5f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 12));
            var at = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            var creation = new Core.Creations.Creation(SeatCreation, _main.Mods!);
            int Put(string part, int x, int y, int z, Core.Creations.GridRotation rotation) =>
                creation.TryPlacePart(part, new Core.Creations.GridCell(x, y, z), rotation, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException(error);
            var up = Core.Creations.GridRotation.Identity;
            for (var x = -2; x <= 2; x++)
                for (var z = -1; z <= 1; z++)
                    Put("vanilla:metal_block", x, 0, z, up);
            Put("vanilla:metal_block", 0, 1, -1, up);
            Put("vanilla:metal_block", 0, 2, -1, up);
            off = Put("vanilla:switch", -1, 1, 0, up);
            on = Put("vanilla:switch", 1, 1, 0, up);
            wall = Put("vanilla:switch", 0, 2, 0, Core.Creations.GridRotation.FromAxes(new Core.Creations.GridCell(1, 0, 0), new Core.Creations.GridCell(0, 0, 1)));
            creation.SetAllBodyPoses(new Core.Creations.Pose(new System.Numerics.Vector3(at.X, at.Y + 0.2f, at.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
        });
        Shot("switch_02_scene", 30, () =>
        {
            Toggle(on);
            Toggle(wall);
            Near(off, new Vector3(0.4f, 1.0f, 1.4f), 1.6f);
        });
        Shot("switch_03_off_three_quarter", 10, () => Near(off, new Vector3(0.9f, 0.9f, 1.1f), 0.55f));
        Shot("switch_04_on_three_quarter", 10, () => Near(on, new Vector3(0.9f, 0.9f, 1.1f), 0.55f));
        Shot("switch_05_off_front", 10, () => Near(off, new Vector3(0f, 0.25f, 1f), 0.5f));
        Shot("switch_06_on_front", 10, () => Near(on, new Vector3(0f, 0.25f, 1f), 0.5f));
        Shot("switch_07_off_top", 10, () => Near(off, new Vector3(0f, 1f, 0.05f), 0.5f));
        Shot("switch_08_on_top", 10, () => Near(on, new Vector3(0f, 1f, 0.05f), 0.5f));
        Shot("switch_09_wall", 10, () =>
        {
            if (Part(wall) is not { } node)
                return;
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = node.GlobalPosition + new Vector3(0.45f, 0.3f, 0.75f);
            _camera.LookAt(node.GlobalPosition + new Vector3(0, 0, -0.09f));
        });
        Shot("switch_10_toggled_on", 30, () => { Toggle(off); Near(off, new Vector3(0.9f, 0.9f, 1.1f), 0.55f); });
        Shot("switch_11_toggled_off", 30, () => Toggle(off));
    }

    private void FabricatorShots()
    {
        Engine.TimeScale = 1.0;
        var recipes = _main.Recipes!;
        int one = 0, five = 0;
        Core.Creations.Creation? made = null;
        Node3D? Part(int part) => _main.Creations.NodeOf(SeatCreation)?.PartNode(part);
        void View(int part, Vector3 offset, float height = 1.0f, float x = 0)
        {
            if (Part(part) is not { } node)
                return;
            var t = node.GlobalTransform;
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = t.Origin + t.Basis * offset;
            _camera.LookAt(t.Origin + t.Basis * new Vector3(x, height - 1.125f, -0.2f));
        }
        void Report(int part)
        {
            var r = _main.Scripts.Runtime.Readouts;
            var q = made!.CraftingOf(part)!;
            GD.Print($"[capture] fabricator {part}: clip={r.Text(SeatCreation, part, "clip")} weld={r.Number(SeatCreation, part, "weld")} jobs={q.Jobs.Count} finished={q.Finished}");
            if (Part(part)?.FindChild("L_item", true, false) is Node3D item)
                GD.Print($"[capture]   item scale {item.Scale.X:0.###}");
        }
        void Craft(int part, int count)
        {
            var recipe = recipes.ForStation("vanilla:fabricator").First();
            made!.CraftingOf(part)!.Enqueue(new Core.Items.CraftJob(recipe.QualifiedId, count));
        }
        Shot("fabricator_00_wait", 60, () => { });
        Shot("fabricator_01_placed", 60, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 4, -6f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 16));
            var at = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            var creation = made = new Core.Creations.Creation(SeatCreation, _main.Mods!);
            int Put(string part, int x, int y, int z) =>
                creation.TryPlacePart(part, new Core.Creations.GridCell(x, y, z), default, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException(error);
            for (var x = -16; x <= 16; x++)
                for (var z = -5; z <= 5; z++)
                    Put("vanilla:metal_block", x, 0, z);
            one = Put("vanilla:fabricator", -8, 1, 0);
            five = Put("vanilla:fabricator", 8, 1, 0);
            creation.SetLevel(five, 4);
            creation.SetAllBodyPoses(new Core.Creations.Pose(new System.Numerics.Vector3(at.X, at.Y + 0.2f, at.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
        });
        Shot("fabricator_02_both", 30, () => View(one, new Vector3(2, 3.6f, -10.5f), 0.4f, 2));
        Shot("fabricator_03_level1", 20, () => View(one, new Vector3(3.2f, 2.4f, -4.4f)));
        Shot("fabricator_04_level1_front", 20, () => View(one, new Vector3(0, 1.2f, -5.2f)));
        Shot("fabricator_05_level1_left_input", 20, () => View(one, new Vector3(3.6f, 0.9f, -1.6f), 0.7f));
        Shot("fabricator_06_level1_right_output", 20, () => View(one, new Vector3(-3.6f, 0.9f, -1.6f), 0.7f));
        Shot("fabricator_07_level1_back", 20, () => View(one, new Vector3(-2.2f, 2.0f, 4.6f)));
        Shot("fabricator_08_level5", 20, () => View(five, new Vector3(3.2f, 2.4f, -4.4f)));
        Shot("fabricator_09_level5_back", 20, () => View(five, new Vector3(2.2f, 2.0f, 4.6f)));
        Shot("fabricator_10_level1_start", 20, () => { Craft(one, 2); Craft(five, 8); View(one, new Vector3(0.6f, 0.9f, -3.2f), 0.75f); });
        Shot("fabricator_11_level1_working", 50, () => Report(one));
        Shot("fabricator_12_level1_weld", 20, () => { Report(one); View(one, new Vector3(1.05f, -0.15f, -2.3f), 0.65f, 0.76f); });
        Shot("fabricator_13_level1_weld_later", 30, () => Report(one));
        Shot("fabricator_14_level1_drill", 20, () => View(one, new Vector3(-1.05f, -0.15f, -2.3f), 0.65f, -0.76f));
        Shot("fabricator_15_level5_working", 20, () => { Report(five); View(five, new Vector3(1.05f, -0.15f, -2.3f), 0.65f, 0.76f); });
        Shot("fabricator_16_level5_drill", 20, () => View(five, new Vector3(-1.05f, -0.15f, -2.3f), 0.65f, -0.76f));
        Shot("fabricator_17_level1_later", 240, () => { Report(one); View(one, new Vector3(1.05f, -0.15f, -2.3f), 0.65f, 0.76f); });
        Shot("fabricator_18_level1_done", 20, () => Report(one));
    }

    private void LookAtSeat(Vector3 offset, float height)
    {
        if (_main.Logic.SeatTransform(SeatCreation, _seat) is not { } seat)
            return;
        Player.ThirdPerson = true;
        _camera.MakeCurrent();
        _follow = null;
        _camera.GlobalPosition = seat.Feet * offset;
        _camera.LookAt(seat.Feet * (Vector3.Up * height));
    }

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

    // CAPTURE_ONLY=text keeps the shots whose names contain it, or any of several separated by commas (and the setup
    // shots, named *_00_*).
    private void Shot(string name, int frames, Action setup)
    {
        if (OS.GetEnvironment("CAPTURE_ONLY") is { Length: > 0 } only && !only.Split(',').Any(o => name.Contains(o, StringComparison.Ordinal))
            && !name.Contains("_00_", StringComparison.Ordinal))
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
        if (_carView is { } car && _main.DebugCarId is { } carId && _main.Creations.NodeOf(carId) is { } carNode)
        {
            var chassis = carNode.Bodies[0].GlobalTransform;
            _camera.GlobalPosition = chassis * car.From;
            _camera.LookAt(chassis * car.To);
        }
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
        if (_recording.TryGetValue(_shot.Value.Name, out var tick))
        {
            Record(_shot.Value, tick);
            return;
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
