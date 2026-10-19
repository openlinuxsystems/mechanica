using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Motion;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Survival;
using Mechanica.Core.World;

namespace Mechanica.Scripting.Tests;

/// <summary>
/// Chunk 23's vanilla scripts on the real vanilla parts: lights (steady, switched, toggled with F and
/// blinking), the beacon, radio and horn, and the parts that act on the world through the combat and farm
/// libraries (saw blade, drill, water cannon, mounted potato blaster).
/// </summary>
public sealed class ContentScriptTests : IDisposable
{
    private const int Peer = 7;
    private static readonly ModLoadResult Vanilla = ModLoader.Load([ItemRig.VanillaRoot]);

    private readonly SurvivalFixture _f = new();
    private readonly ScriptRuntime _runtime;
    private readonly RecordingSound _sound = new();
    private readonly Motion _motion;
    private readonly List<ScriptMessage> _messages = [];

    public ContentScriptTests()
    {
        Assert.False(Vanilla.HasErrors, string.Join("; ", Vanilla.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var motion = _motion = new Motion(_f);
        new MotionActions(new MotionSettings(), motion).Register(_f.Dispatcher);
        _runtime = new ScriptRuntime(new ScriptSettings(), _f.Dispatcher, motion, new NoPhysics(), new Source()) { Sound = _sound };
        _runtime.EnableItems(_f.Catalog, _f.Survival, _f.Inventories);
        _runtime.Fire = _f.Fire;
        _runtime.Message += _messages.Add;
    }

    public void Dispose() => _runtime.Clear();

    [Fact]
    public void ALightWithNothingLinkedShinesAndASwitchedOneFollowsItsSwitch()
    {
        var creation = Board();
        // A headlight's base is its back: it goes on the front of a block.
        Place(creation, "vanilla:metal_block", new GridCell(2, 1, -1));
        var lone = Place(creation, "vanilla:headlight", new GridCell(2, 1, -2));
        var linked = Place(creation, "vanilla:spotlight", new GridCell(-3, 1, -3));
        var toggle = Place(creation, "vanilla:switch", new GridCell(0, 1, 0));
        Link(creation, toggle, linked);
        Steps(3);
        Assert.Equal(1f, _runtime.Lights.Level(creation.Id, lone));
        Assert.Equal(0f, _runtime.Lights.Level(creation.Id, linked));
        Assert.Contains(_sound.Played, p => p.Sound == "light_on");

        Activate(creation, toggle);
        Steps(3);
        Assert.Equal(1f, _runtime.Lights.Level(creation.Id, linked));
        Activate(creation, toggle);
        Steps(3);
        Assert.Equal(0f, _runtime.Lights.Level(creation.Id, linked));
        Assert.Contains(_sound.Played, p => p.Sound == "light_off");
        NoErrors();
    }

    [Fact]
    public void TheFabricatorPlaysItsClipsAndThrowsSparksFromItsCraftingQueue()
    {
        var creation = Board();
        var fab = Place(creation, "vanilla:fabricator", new GridCell(0, 1, 0));
        var queue = creation.CraftingOf(fab)!;
        var output = creation.InventoryOf(fab)!;
        var recipes = RecipeBook.Load(Vanilla);
        var recipe = recipes.ForStation("vanilla:fabricator").First();
        string Clip() => _runtime.Readouts.Text(creation.Id, fab, "clip")!.Split('#')[0];
        float Weld() => _runtime.Readouts.Number(creation.Id, fab, "weld") ?? 0f;

        Steps(2);
        Assert.Equal("idle_loop", Clip());
        Assert.Equal(0f, Weld());

        // Two crafts: it unfolds (start, properties.start_time 0.8 s), then works with sparks.
        queue.Enqueue(new CraftJob(recipe.QualifiedId, 2));
        Steps(2);
        Assert.Equal("start", Clip());
        Steps(60);
        Assert.Equal("work_loop", Clip());
        Assert.Equal(1f, Weld());
        Assert.Equal(1f, _runtime.Readouts.Number(creation.Id, fab, "drill"));

        // The first craft finishes with the second queued: it keeps working, the loop from the top.
        var before = _runtime.Readouts.Text(creation.Id, fab, "clip");
        queue.Tick(recipe.Time, 1f, recipes, output);
        Assert.Equal(1, queue.Finished);
        Steps(2);
        Assert.Equal("work_loop", Clip());
        Assert.NotEqual(before, _runtime.Readouts.Text(creation.Id, fab, "clip"));
        Assert.Equal(1f, Weld());

        // The last craft finishes: it folds back (complete), then idles.
        queue.Tick(recipe.Time, 1f, recipes, output);
        Assert.True(queue.IsEmpty);
        Steps(2);
        Assert.Equal("complete", Clip());
        Assert.Equal(0f, Weld());
        Steps(60);
        Assert.Equal("idle_loop", Clip());

        // A new order unfolds it again.
        queue.Enqueue(new CraftJob(recipe.QualifiedId, 1));
        Steps(2);
        Assert.Equal("start", Clip());
        Steps(60);
        Assert.Equal("work_loop", Clip());
        NoErrors();
    }

    [Fact]
    public void AnInteriorLightTogglesWithF()
    {
        var creation = Board();
        var lamp = Place(creation, "vanilla:interior_light", new GridCell(0, -1, 0));
        Steps(1);
        Assert.Equal(1f, _runtime.Lights.Level(creation.Id, lamp));
        Activate(creation, lamp);
        Steps(1);
        Assert.Equal(0f, _runtime.Lights.Level(creation.Id, lamp));
        Activate(creation, lamp);
        Steps(1);
        Assert.Equal(1f, _runtime.Lights.Level(creation.Id, lamp));
        NoErrors();
    }

    [Fact]
    public void AWarningLightBlinksSilently()
    {
        var creation = Board();
        var lamp = Place(creation, "vanilla:warning_light", new GridCell(0, 1, 0));
        var levels = new List<float>();
        // properties.blink is 1 s, half of it lit: 60 steps.
        for (var i = 0; i < 120; i++)
        {
            Steps(1);
            levels.Add(_runtime.Lights.Level(creation.Id, lamp));
        }
        Assert.InRange(levels.Count(l => l > 0), 55, 65);
        Assert.InRange(levels.Zip(levels.Skip(1)).Count(p => p.First != p.Second), 2, 4);
        Assert.Empty(_sound.Played);
        NoErrors();
    }

    [Fact]
    public void SetLightIsRefusedOnAPartWithoutALight()
    {
        var creation = Board();
        Place(creation, "vanilla:horn", new GridCell(0, 1, 0));
        Steps(1);
        NoErrors();
        Assert.Empty(_runtime.Lights.All);
    }

    [Fact]
    public void ABeaconFlashesAndPingsWhileOn()
    {
        var creation = Board();
        var beacon = Place(creation, "vanilla:beacon", new GridCell(0, 1, 0));
        Steps(10);
        Assert.Equal(0f, _runtime.Lights.Level(creation.Id, beacon));
        Activate(creation, beacon);
        Steps(1);
        Assert.Equal(1f, _runtime.Lights.Level(creation.Id, beacon));
        Assert.Equal(["beacon_activate", "beacon_ping"], _sound.Played.Select(p => p.Sound));
        // properties.flash is 0.4 s, then it glows dimly until the next ping 3 s later.
        Steps(60);
        Assert.InRange(_runtime.Lights.Level(creation.Id, beacon), 0.1f, 0.2f);
        Steps(130);
        Assert.Equal(2, _sound.Played.Count(p => p.Sound == "beacon_ping"));
        NoErrors();
    }

    [Fact]
    public void ARadioPlaysItsStationAndRetunes()
    {
        var creation = Board();
        var radio = Place(creation, "vanilla:radio", new GridCell(0, 1, 0));
        Steps(2);
        Assert.Empty(_sound.Loops);
        Activate(creation, radio);
        Steps(1);
        Assert.Equal("radio_loop_01", Assert.Single(_sound.Loops.Values).Sound);
        Configure(creation, radio, "station", 4);
        Steps(1);
        Assert.Equal("radio_loop_04", Assert.Single(_sound.Loops.Values).Sound);
        Assert.Contains(_sound.Played, p => p.Sound == "radio_tune");
        Activate(creation, radio);
        Steps(1);
        Assert.Empty(_sound.Loops);
        Assert.Equal(["radio_on", "radio_tune", "radio_off"], _sound.Played.Select(p => p.Sound));
        NoErrors();
    }

    [Fact]
    public void AHornHonksWhilePoweredAndBrieflyOnF()
    {
        var creation = Board();
        var horn = Place(creation, "vanilla:horn", new GridCell(0, 1, 0));
        var button = Place(creation, "vanilla:switch", new GridCell(1, 1, 0));
        Link(creation, button, horn);
        Steps(2);
        Assert.Empty(_sound.Loops);
        Activate(creation, horn);
        Steps(1);
        Assert.Equal("horn_honk", Assert.Single(_sound.Loops.Values).Sound);
        Steps(40);
        Assert.Empty(_sound.Loops);
        Activate(creation, button);
        Steps(60);
        Assert.Single(_sound.Loops);
        NoErrors();
    }

    [Fact]
    public void ASawBladeCutsWhatItsRimTouchesOnlyWhileItSpinsAtCuttingSpeed()
    {
        var target = Target();
        var creation = Board();
        var saw = Place(creation, "vanilla:saw_blade", new GridCell(-2, 1, -2));
        var centre = PartKinematics.Of(creation, saw).Position;
        var hits = 0;
        _f.World.Ray = (from, direction) => direction.X > 0.99f ? Hits(target, from, ref hits) : null;
        Steps(30);
        // It has no input: standing still it does nothing.
        Assert.Empty(_f.World.Casts);
        Assert.Empty(_sound.Loops);

        // Turning slower than properties.min_spin (9 rad/s) it whirs and glows part way, but cuts nothing.
        Spin(creation, saw, 5f);
        Steps(30);
        Assert.Equal("saw_spin", Assert.Single(_sound.Loops.Values).Sound);
        Assert.Equal(0.5f, _runtime.Readouts.Number(creation.Id, saw, "spin")!.Value, 3);
        Assert.Empty(_f.World.Casts);

        // At cutting speed (either way round), every 0.25 s it casts 16 rim rays and 8 rays out of each face, all from
        // inside the disc, and bites the target once per turn however many rays find it.
        Spin(creation, saw, -12f);
        Steps(31);
        Assert.Equal(1f, _runtime.Readouts.Number(creation.Id, saw, "spin")!.Value, 3);
        Assert.InRange(_f.World.Casts.Count, 64, 96);
        Assert.Equal(0, _f.World.Casts.Count % 32);
        Assert.All(_f.World.Casts, c => Assert.True(Vector3.Distance(c.From, centre) < 0.6f, $"{c.From} starts outside the disc"));
        Assert.InRange(hits, 2, 3);
        Assert.Equal(15f * hits, target.PartDamage(target.Parts.First().Id), 3);
        Assert.Contains(_sound.Played, p => p.Sound == "saw_cut");
        Assert.Equal(1f, Assert.Single(_sound.Loops.Values).Parameters["load"]);

        // Spun down, its whir stops.
        Spin(creation, saw, 0f);
        Steps(2);
        Assert.Empty(_sound.Loops);
        NoErrors();
    }

    [Fact]
    public void ASawBladeCutsALogFasterThanAPartAndNeverItsOwnCreation()
    {
        var log = Target("vanilla:pine_log");
        var creation = Board();
        var saw = Place(creation, "vanilla:saw_blade", new GridCell(-2, 1, -2));
        var hits = 0;
        // One rim ray finds the log, another a block of the saw's own creation.
        _f.World.Ray = (from, direction) => direction.X > 0.99f ? Hits(log, from, ref hits)
            : direction.Z > 0.99f ? new RayHit(from, Creation: creation.Id, Part: creation.Parts.First().Id) : null;
        Spin(creation, saw, 12f);
        Steps(10);
        Assert.Equal(1, hits);
        // properties.damage_to: a felled log takes 30 a bite, twice what a standing tree or another part takes.
        Assert.Equal(30f, log.PartDamage(log.Parts.First().Id), 3);
        Assert.Equal(0f, creation.PartDamage(creation.Parts.First().Id), 3);
        NoErrors();
    }

    [Fact]
    public void ARotorsModelSpinsWithItsSpeedAndAFlapTiltsWithItsCommand()
    {
        var creation = Board();
        var rotor = Place(creation, "vanilla:rotor", new GridCell(-1, 1, -1));
        var flap = Place(creation, "vanilla:control_surface", new GridCell(-4, 1, 3));
        var power = Place(creation, "vanilla:switch", new GridCell(3, 1, 3));
        Link(creation, power, rotor);
        Link(creation, power, flap);
        Configure(creation, flap, "control", "link");
        Steps(2);
        Assert.Equal(0f, _runtime.Animations.Speed(creation.Id, rotor));
        Activate(creation, power);
        // properties.spool_rate is 0.8 a second: full speed after 1.25 s.
        Steps(90);
        // Clockwise seen from the top: about -Y at properties.spin_rate.
        Assert.Equal(-40f, _runtime.Animations.Speed(creation.Id, rotor), 1);
        // The flap tilts by max_deflection (10°), trailing edge down.
        Assert.Equal(10f * MathF.PI / 180, _runtime.Animations.AngleAt(creation.Id, flap, _runtime.Time), 3);
        NoErrors();
    }

    [Fact]
    public void ADrillBitesWhatItsTipTouchesOnlyWhileABearingSpinsItAtDrillingSpeed()
    {
        // A boulder off a rock deposit: only a drill breaks it down, and it takes a few bites.
        var target = Target("vanilla:stone_boulder");
        var boulder = target.Parts.First().Id;
        var creation = Board();
        var drill = Place(creation, "vanilla:drill", new GridCell(-1, 1, -1));
        var centre = PartKinematics.Of(creation, drill).Position;
        var hits = 0;
        // Only the tip ray, straight up the axis, finds the target.
        _f.World.Ray = (from, direction) => direction.Y > 0.99f ? Hits(target, from, ref hits) : null;
        Steps(30);
        // It has no input: standing still it does nothing.
        Assert.Empty(_f.World.Casts);
        Assert.Empty(_sound.Loops);

        // Turning slower than properties.min_spin (9 rad/s) it whines and glows part way, but drills nothing.
        Spin(creation, drill, 5f);
        Steps(30);
        Assert.Equal("drill_spin", Assert.Single(_sound.Loops.Values).Sound);
        Assert.Equal(0.5f, _runtime.Readouts.Number(creation.Id, drill, "spin")!.Value, 3);
        Assert.Empty(_f.World.Casts);

        // At drilling speed (either way round), every 0.3 s it casts its tip ray and 8 flank rays, all from inside the
        // bit, bites the target for properties.damage and shakes its own Body.
        Spin(creation, drill, -12f);
        Steps(37);
        Assert.Equal(1f, _runtime.Readouts.Number(creation.Id, drill, "spin")!.Value, 3);
        Assert.Equal(0, _f.World.Casts.Count % 9);
        Assert.InRange(_f.World.Casts.Count, 18, 27);
        Assert.All(_f.World.Casts, c => Assert.True(Vector3.Distance(c.From, centre) < 0.375f, $"{c.From} starts outside the drill"));
        var tip = _f.World.Casts.First(c => c.Direction.Y > 0.99f);
        Assert.Equal(centre.Y + 0.27f, tip.From.Y, 3);
        Assert.InRange(hits, 2, 3);
        Assert.Equal(25f * hits, target.PartDamage(boulder), 3);
        Assert.Contains(_sound.Played, p => p.Sound == "drill_hit");
        Assert.Equal(1f, Assert.Single(_sound.Loops.Values).Parameters["load"]);
        Assert.Equal(hits, _motion.Forces.Count);
        Assert.All(_motion.Forces, f => Assert.True(MathF.Abs(f.Y) < 1e-3f && f.Length() > 500f, $"a sideways shake ({f})"));

        // Spun down, its whine stops.
        Spin(creation, drill, 0f);
        Steps(2);
        Assert.Empty(_sound.Loops);
        NoErrors();
    }

    [Fact]
    public void ADrillBreaksARockDepositBlockThatNothingElseHurts()
    {
        var rock = new WorldObjectId(0, 0, 0, 0);
        _f.World.Objects[rock] = new WorldObjectPlacement(rock, new WorldObjectDefinition
        {
            Id = "rock_outcrop",
            Collider = new ShapeDefinition { Shape = PrimitiveShape.Box, Size = new Vec3(1, 1, 1), At = new Vec3(0, 0.5f, 0) },
            Health = 100,
            DamageFrom = [DamageKinds.Drill],
        }, new Vector3(0, 0, -3), 0, 1);
        var creation = Board();
        var drill = Place(creation, "vanilla:drill", new GridCell(-1, 1, -1));
        _f.World.Ray = (from, direction) => direction.Y > 0.99f ? new RayHit(from + direction * 0.1f, Object: rock) : null;
        Spin(creation, drill, 12f);
        // Four bites of 25, one every 0.3 s.
        Steps(80);
        Assert.True(_f.World.State.IsDestroyed(rock), $"the block broke ({_f.World.State.DamageOf(rock)} damage)");
        Assert.Contains(_f.World.ObjectHits, h => h.Id == rock && h.Broke);
        NoErrors();
    }

    [Fact]
    public void ADrillLeavesItsOwnCreationAlone()
    {
        var creation = Board();
        var drill = Place(creation, "vanilla:drill", new GridCell(-1, 1, -1));
        _f.World.Ray = (from, direction) => new RayHit(from, Creation: creation.Id, Part: creation.Parts.First().Id);
        Spin(creation, drill, 12f);
        Steps(10);
        Assert.NotEmpty(_f.World.Casts);
        Assert.Equal(0f, creation.PartDamage(creation.Parts.First().Id), 3);
        Assert.Empty(_motion.Forces);
        NoErrors();
    }

    [Fact]
    public void AWaterCannonWatersSoilAndShovesAnythingElse()
    {
        var soil = _f.World.Creations[_f.Add(SurvivalFixture.Soil, new Vector3(0, 0, -10))];
        var soilPart = soil.Parts.First().Id;
        var creation = Board();
        var cannon = Place(creation, "vanilla:water_cannon", new GridCell(0, 1, 0));
        _f.World.Ray = (from, direction) => new RayHit(from + direction * 5, Creation: soil.Id, Part: soilPart);
        Steps(1);
        Activate(creation, cannon);
        Steps(1);
        Assert.True(soil.SoilOf(soilPart)!.Water > 0, "the soil got water");
        Assert.Contains(_sound.Played, p => p.Sound == "water_cannon_fire");
        Assert.Empty(_f.World.Pushes);

        var block = Target();
        _f.World.Ray = (from, direction) => new RayHit(from + direction * 5, Creation: block.Id, Part: block.Parts.First().Id);
        Steps(40);
        Activate(creation, cannon);
        Steps(1);
        var push = Assert.Single(_f.World.Pushes);
        Assert.Equal(60f, push.Length(), 2);
        Assert.Equal(0f, block.PartDamage(block.Parts.First().Id));
        NoErrors();
    }

    [Fact]
    public void AWaterCannonPutsOutFiresAlongItsJet()
    {
        _f.Fire.LightSites([new Mechanica.Core.Quests.QuestSite(0, "test:camp", Vector3.Zero, 10,
            [new Mechanica.Core.Quests.QuestMarker(0, "fire", null, new Vector3(0, 0, -6)), new Mechanica.Core.Quests.QuestMarker(1, "fire", null, new Vector3(4, 0, -6))])]);
        var creation = Board();
        var cannon = Place(creation, "vanilla:water_cannon", new GridCell(0, 1, 0));
        Steps(1);
        for (var shot = 0; shot < 3; shot++)
        {
            Activate(creation, cannon);
            Steps(40);
        }
        // 0.35 a shot: three put the fire in front out; the one off to the side is untouched.
        Assert.Equal([0f, 1f], _f.Fire.Fires().Select(f => f.Strength));
        NoErrors();
    }

    [Fact]
    public void AMountedBlasterFiresPotatoesFromTheCreationsChest()
    {
        var target = Target();
        var creation = Board();
        var gun = Place(creation, "vanilla:potato_blaster_mounted", new GridCell(0, 1, 0));
        var chest = Place(creation, "vanilla:chest", new GridCell(-4, 1, -4));
        _f.World.Ray = (from, direction) => new RayHit(from + direction * 20, Creation: target.Id, Part: target.Parts.First().Id);
        Steps(1);
        Activate(creation, gun);
        Steps(1);
        Assert.Empty(_f.World.Casts);
        Assert.DoesNotContain(_sound.Played, p => p.Sound == "blaster_fire");

        creation.InventoryOf(chest)!.Add("vanilla:potato", 2);
        Activate(creation, gun);
        Steps(1);
        Assert.Single(_f.World.Casts);
        Assert.Equal(1, creation.InventoryOf(chest)!.Count("vanilla:potato"));
        Assert.Equal(40f, target.PartDamage(target.Parts.First().Id), 3);
        // properties.interval is 0.4 s.
        Activate(creation, gun);
        Steps(1);
        Assert.Single(_f.World.Casts);
        Steps(30);
        Activate(creation, gun);
        Steps(1);
        Assert.Equal(2, _f.World.Casts.Count);
        Assert.Equal(0, creation.InventoryOf(chest)!.Count("vanilla:potato"));
        NoErrors();
    }

    [Fact]
    public void AMountedBlasterHurtsPlayersOnlyWithFriendlyFireOn()
    {
        var creation = Board();
        var gun = Place(creation, "vanilla:potato_blaster_mounted", new GridCell(0, 1, 0));
        var chest = Place(creation, "vanilla:chest", new GridCell(-4, 1, -4));
        creation.InventoryOf(chest)!.Add("vanilla:potato", 2);
        _f.World.Ray = (from, direction) => new RayHit(from + direction * 5, Player: SurvivalFixture.You.Peer);
        _f.Survival.FriendlyFire = false;
        Steps(1);
        Activate(creation, gun);
        Steps(1);
        Assert.Single(_f.World.Casts);
        Assert.Equal(100f, _f.Survival.View(SurvivalFixture.You.Peer)!.Health);

        _f.Survival.FriendlyFire = true;
        Steps(30);
        Activate(creation, gun);
        Steps(1);
        Assert.Equal(2, _f.World.Casts.Count);
        Assert.True(_f.Survival.View(SurvivalFixture.You.Peer)!.Health < 100f);
        NoErrors();
    }

    // A 9 x 9 metal plate to build on, centred on the origin.
    private Creation Board()
    {
        var creation = new Creation(_f.World.NextCreationId(), Vanilla);
        for (var x = -4; x <= 4; x++)
            for (var z = -4; z <= 4; z++)
                Place(creation, "vanilla:metal_block", new GridCell(x, 0, z));
        _f.World.Added(creation);
        return creation;
    }

    // A lone metal block (or another part) far away, for things to hit.
    private Creation Target(string part = "vanilla:metal_block")
    {
        var creation = new Creation(_f.World.NextCreationId(), Vanilla);
        Place(creation, part, GridCell.Zero);
        creation.SetAllBodyPoses(new Pose(new Vector3(30, 0, 0), Quaternion.Identity));
        _f.World.Added(creation);
        return creation;
    }

    private static RayHit Hits(Creation target, Vector3 from, ref int hits)
    {
        hits++;
        return new RayHit(from, Creation: target.Id, Part: target.Parts.First().Id);
    }

    // Spins the saw's (or drill's) Body about the world's up (the blade's axis as placed) at rad/s, as a bearing would.
    private static void Spin(Creation creation, int part, float speed) =>
        creation.SetBodyVelocity(creation.BodyIndexOf(part), new BodyVelocity(Vector3.Zero, new Vector3(0, speed, 0)));

    private static int Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        Assert.True(creation.TryPlacePart(part, cell, rotation, out var placed, out var error), $"{part} at {cell}: {error}");
        return placed.Id;
    }

    private static void Link(Creation creation, int from, int to) =>
        Assert.True(creation.TryLink(from, to, out var error), error);

    private static void Configure(Creation creation, int part, string id, object value)
    {
        var changes = new Dictionary<string, System.Text.Json.JsonElement?> { [id] = System.Text.Json.JsonSerializer.SerializeToElement(value) };
        creation.SetPartSettings(part, Core.Logic.PartSettings.Apply(creation.DefinitionOf(part), creation.FindPart(part)!.Settings, changes));
    }

    private void Activate(Creation creation, int part)
    {
        var caller = new ActionCaller(Peer, ActionSource.Player);
        Assert.Null(_runtime.CheckActivate(caller, creation, part));
        _runtime.Activate(caller, creation, part);
    }

    private void Steps(int count)
    {
        for (var i = 0; i < count; i++)
            _runtime.Update(1f / 60);
    }

    private void NoErrors() =>
        Assert.True(_messages.All(m => m.Kind != ScriptMessageKind.Error && m.Kind != ScriptMessageKind.Warning),
            string.Join("; ", _messages.Select(m => m.Text)));

    private sealed class Source : IScriptSource
    {
        public ScriptFile? FindScript(string partId)
        {
            if (Vanilla.FindDefinition(Identifiers.Qualify(partId))?.Script is not { } script)
                return null;
            return new ScriptFile($"vanilla:{script}", "vanilla", Path.Combine(ItemRig.VanillaRoot, script));
        }
    }

    private sealed class Motion(SurvivalFixture f) : IMotionWorld
    {
        public Creation? FindCreation(int id) => f.World.FindCreation(id);

        public IEnumerable<Creation> AllCreations => f.World.AllCreations;

        public bool IsLive(int id) => f.World.Creations.ContainsKey(id);

        public List<Vector3> Forces { get; } = [];

        public void ApplyForce(Creation creation, int body, Vector3 force, Vector3 torque, float? duration, int? part, Vector3? at, Vector3 ownTorque)
        {
            if (force != Vector3.Zero)
                Forces.Add(force);
        }

        public void SetJointMotor(Creation creation, JointLink joint, JointMotor motor)
        {
        }

        public float? JointPosition(Creation creation, JointLink joint) => null;
    }

    private sealed class RecordingSound : IScriptSound
    {
        private int _next = 1;

        public List<(string Sound, Vector3 At)> Played { get; } = [];

        public Dictionary<int, (string Sound, Dictionary<string, float> Parameters)> Loops { get; } = [];

        public string? Play(string sound, string ownerModId, Vector3 position)
        {
            Played.Add((sound, position));
            return null;
        }

        public int StartLoop(string sound, string ownerModId, int creation, int part, IReadOnlyDictionary<string, float>? parameters, out string? error)
        {
            error = null;
            Loops[_next] = (sound, new Dictionary<string, float>(parameters ?? new Dictionary<string, float>()));
            return _next++;
        }

        public string? SetLoopParameter(int handle, string name, float value)
        {
            if (!Loops.TryGetValue(handle, out var loop))
                return "the loop has stopped";
            loop.Parameters[name] = value;
            return null;
        }

        public void StopLoop(int handle) => Loops.Remove(handle);
    }
}
