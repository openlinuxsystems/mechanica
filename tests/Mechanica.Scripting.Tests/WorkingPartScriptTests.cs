using System.Numerics;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Motion;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Survival;
using Mechanica.Core.World;

namespace Mechanica.Scripting.Tests;

/// <summary>
/// Parts map batch 6's working parts on the real vanilla data: the Explosive Canisters (on_damage, part.destroy,
/// combat.explode, chains, the fuse and hard knocks), the Alarm Clock (world.time_of_day), the Cash Register, the
/// Scuttler Head's notes, the Plasma Drill and the Sticky Wheel.
/// </summary>
public sealed class WorkingPartScriptTests : IDisposable
{
    private const int Peer = 7;
    private static readonly ModLoadResult Vanilla = ModLoader.Load([ItemRig.VanillaRoot]);

    private readonly SurvivalFixture _f = new();
    private readonly ScriptRuntime _runtime;
    private readonly RecordingSound _sound = new();
    private readonly Motion _motion;
    private readonly Physics _physics = new();
    private readonly List<ScriptMessage> _messages = [];

    public WorkingPartScriptTests()
    {
        Assert.False(Vanilla.HasErrors, string.Join("; ", Vanilla.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var motion = _motion = new Motion(_f);
        new MotionActions(new MotionSettings(), motion).Register(_f.Dispatcher);
        _runtime = new ScriptRuntime(new ScriptSettings(), _f.Dispatcher, motion, _physics, new Source()) { Sound = _sound };
        _runtime.EnableItems(_f.Catalog, _f.Survival, _f.Inventories);
        _runtime.Fire = _f.Fire;
        _runtime.Message += _messages.Add;
    }

    public void Dispose() => _runtime.Clear();

    // ---- Explosive Canisters ----

    [Fact]
    public void ACanisterThatBreaksBlowsUpAndHurtsWhatIsNearIt()
    {
        var creation = Board();
        var canister = Place(creation, "vanilla:explosive_canister_small", new GridCell(0, 1, 0));
        Steps(1);
        var before = creation.Parts.Count;

        Assert.True(_f.World.DamagePart(creation, canister, 1000, DamageKinds.Weapon).Broken);
        Steps(1);
        var blast = Assert.Single(_f.World.Blasts);
        Assert.Equal(1.75f, blast.Radius);
        Assert.Single(_sound.Played, p => p.Sound == "canister_explode");
        Assert.Null(creation.FindPart(canister));
        // The blocks under it took the blast.
        var hurt = creation.Parts.Count(p => creation.PartDamage(p.Id) > 0) + (before - 1 - creation.Parts.Count);
        Assert.True(hurt >= 4, $"{hurt} blocks hurt");
        NoErrors();
    }

    [Fact]
    public void ABlastSetsOffTheCanistersItReachesInTheSameStep()
    {
        var large = Lone("vanilla:explosive_canister_large", new Vector3(0, 0, 0));
        var near = Lone("vanilla:explosive_canister_small", new Vector3(3, 0, 0));
        var nearer = Lone("vanilla:explosive_canister_small", new Vector3(4.5f, 0, 0));
        var far = Lone("vanilla:explosive_canister_small", new Vector3(8, 0, 0));
        Steps(1);

        _f.World.DamagePart(large, 1, 1000, DamageKinds.Weapon);
        Steps(1);
        // The large one's 3.75 m blast reaches the small one 3 m away, whose 1.75 m blast reaches the next 1.5 m on;
        // the last, 3.5 m past that, is out of reach.
        Assert.Equal(3, _f.World.Blasts.Count);
        Assert.Equal(3, _sound.Played.Count(p => p.Sound == "canister_explode"));
        Assert.Null(near.FindPart(1));
        Assert.Null(nearer.FindPart(1));
        Assert.NotNull(far.FindPart(1));
        Assert.Equal(0f, far.PartDamage(1), 3);
        NoErrors();
    }

    [Fact]
    public void CanistersOnACreationTheBlastBreaksApartStillGoOff()
    {
        // The large one's blast breaks the board under them, splitting it: the small one on a broken-off piece still
        // hears its blast damage and goes off with it.
        var creation = Board();
        var first = Place(creation, "vanilla:explosive_canister_large", new GridCell(-4, 1, -4));
        var second = Place(creation, "vanilla:explosive_canister_small", new GridCell(4, 1, 4));
        Steps(1);

        _f.World.DamagePart(creation, first, 1000, DamageKinds.Weapon);
        Steps(1);
        Assert.Equal(2, _f.World.Blasts.Count);
        Assert.Null(FindAnywhere(second));
        Assert.True(_f.World.Creations.Count > 1 || creation.Parts.Count < 81, "the board was blown apart");
        NoErrors();
    }

    [Fact]
    public void ACanisterHitButNotBrokenLightsItsFuseAndGoesOffThreeSecondsLater()
    {
        var creation = Board();
        var canister = Place(creation, "vanilla:explosive_canister_small", new GridCell(0, 1, 0));
        Steps(1);

        Assert.False(_f.World.DamagePart(creation, canister, 1, DamageKinds.Sledge).Broken);
        Steps(1);
        Assert.Contains(_sound.Played, p => p.Sound == "canister_fuse");
        Assert.Equal("fuse_loop", _runtime.Readouts.Text(creation.Id, canister, "pose"));
        // Another knock does not restart it.
        _f.World.DamagePart(creation, canister, 1, DamageKinds.Weapon);
        Steps(168);
        Assert.Empty(_f.World.Blasts);
        Assert.Single(_sound.Played, p => p.Sound == "canister_fuse");
        Steps(15);
        Assert.Single(_f.World.Blasts);
        Assert.Null(creation.FindPart(canister));
        NoErrors();
    }

    [Fact]
    public void ACanisterGoesOffWhenItHitsSomethingHard()
    {
        var id = Lone("vanilla:explosive_canister_small", new Vector3(0, 0, 8)).Id;
        Steps(1);

        // 6 kg: 30 N·s is a 5 m/s knock, under properties.impact_speed (8 m/s); 60 N·s is 10 m/s.
        Assert.True(_runtime.Invoke(id, 1, ScriptRuntime.Hooks.OnCollision, new JsonObject(), 30.0).Ok);
        Steps(1);
        Assert.Empty(_f.World.Blasts);
        Assert.True(_runtime.Invoke(id, 1, ScriptRuntime.Hooks.OnCollision, new JsonObject(), 60.0).Ok);
        Steps(1);
        var blast = Assert.Single(_f.World.Blasts);
        Assert.True(Vector3.Distance(new Vector3(0, 0, 8), blast.At) < 0.5f, $"it went off where it was ({blast.At})");
        Assert.True(_f.World.FindCreation(id)?.FindPart(1) is null, "it broke");
        NoErrors();
    }

    [Fact]
    public void DamageToAPartWhoseScriptHasNoOnDamageIsPassedOver()
    {
        var id = Lone("vanilla:alarm_clock", new Vector3(0, 0, 8)).Id;
        var creation = _f.World.Creations[id];
        Steps(1);
        // The clock's script has no on_damage: a part without the hook is passed over.
        _f.World.DamagePart(creation, 1, 1, DamageKinds.Fire);
        Steps(1);
        Assert.True(creation.PartDamage(1) > 0);
        NoErrors();
    }

    // ---- Time of day and the Alarm Clock ----

    [Fact]
    public void TheTimeOfDayStartsAtSixAndGoesRoundOnceADayLength()
    {
        _runtime.DayLength = () => 2400;
        Assert.Equal(6, _runtime.TimeOfDay, 6);
        _runtime.Time = 600;
        Assert.Equal(12, _runtime.TimeOfDay, 6);
        _runtime.Time = 1800;
        Assert.Equal(0, _runtime.TimeOfDay, 6);
        _runtime.Time = 2400 + 300;
        Assert.Equal(9, _runtime.TimeOfDay, 6);
        _runtime.DayLength = () => 0;
        Assert.InRange(_runtime.TimeOfDay, 0, 24);
    }

    [Fact]
    public void AnAlarmClockRingsAtItsTimeWithItsOutputOnUntilAButtonStopsIt()
    {
        // A 240 s day: an hour is 10 s, so 07:00 comes 10 s after the 06:00 start.
        _runtime.DayLength = () => 240;
        var creation = Board();
        var clock = Place(creation, "vanilla:alarm_clock", new GridCell(2, 1, 2));
        var stop = Place(creation, "vanilla:button", new GridCell(-2, 1, -2));
        Link(creation, stop, clock);
        Configure(creation, clock, "alarm", "chime");
        Steps(9 * 60);
        Assert.Equal("armed", Pose(creation, clock));
        Assert.Equal("07:00", _runtime.Readouts.Text(creation.Id, clock, "rings"));
        Assert.Equal("06:54", _runtime.Readouts.Text(creation.Id, clock, "now"));
        Assert.Equal(0f, _runtime.Signals.Output(creation.Id, clock));
        Assert.DoesNotContain(_sound.Played, p => p.Sound.StartsWith("alarm_clock", StringComparison.Ordinal));

        Steps(2 * 60);
        Assert.Equal("ring_loop", Pose(creation, clock));
        Assert.Equal(1f, _runtime.Signals.Output(creation.Id, clock));
        // It plays its chime every properties.repeat_time (1 s).
        Assert.InRange(_sound.Played.Count(p => p.Sound == "alarm_clock_chime"), 1, 2);

        Activate(creation, stop);
        Steps(2);
        Assert.Equal("armed", Pose(creation, clock));
        Assert.Equal(0f, _runtime.Signals.Output(creation.Id, clock));
        NoErrors();
    }

    [Fact]
    public void ADisarmedAlarmClockStaysQuietAndOneLeftAloneStopsAfterItsRing()
    {
        _runtime.DayLength = () => 240;
        var creation = Board();
        var quiet = Place(creation, "vanilla:alarm_clock", new GridCell(2, 1, 2));
        var loud = Place(creation, "vanilla:alarm_clock", new GridCell(-2, 1, -2));
        Steps(1);
        Configure(creation, quiet, "armed", false);
        Configure(creation, quiet, "minute", 35);
        Steps(12 * 60 - 1);
        Assert.Equal("off", Pose(creation, quiet));
        // Shown to the minute, not a hair under it; each change in the menu ticked.
        Assert.Equal("07:35", _runtime.Readouts.Text(creation.Id, quiet, "rings"));
        Assert.Single(_sound.Played, p => p.Sound == "alarm_clock_set");
        Assert.Equal(0f, _runtime.Signals.Output(creation.Id, quiet));
        Assert.Equal("ring_loop", Pose(creation, loud));

        // properties.ring is 10 s.
        Steps(10 * 60);
        Assert.Equal("armed", Pose(creation, loud));
        Assert.InRange(_sound.Played.Count(p => p.Sound == "alarm_clock_bell"), 9, 11);
        NoErrors();
    }

    // ---- Cash Register ----

    [Fact]
    public void ACashRegisterOpensWithEOrItsInputAndIsOnWhileOpen()
    {
        var creation = Board();
        var till = Place(creation, "vanilla:cash_register", new GridCell(0, 1, 0));
        var trigger = Place(creation, "vanilla:switch", new GridCell(-3, 1, -3));
        Link(creation, trigger, till);
        Steps(2);
        Assert.Equal("close", Clip(creation, till));
        Assert.Equal(0f, _runtime.Signals.Output(creation.Id, till));

        Activate(creation, till);
        Steps(2);
        Assert.Equal("open", Clip(creation, till));
        Assert.Equal(1f, _runtime.Signals.Output(creation.Id, till));
        Assert.Single(_sound.Played, p => p.Sound == "cash_register_open");
        Activate(creation, till);
        Steps(2);
        Assert.Equal("close", Clip(creation, till));
        Assert.Single(_sound.Played, p => p.Sound == "cash_register_close");

        Activate(creation, trigger);
        Steps(3);
        Assert.Equal("open", Clip(creation, till));
        Activate(creation, trigger);
        Steps(3);
        Assert.Equal("close", Clip(creation, till));
        Assert.Equal(0f, _runtime.Signals.Output(creation.Id, till));
        NoErrors();
    }

    // ---- Scuttler Head ----

    [Fact]
    public void AScuttlerHeadPlaysItsNoteInItsVoiceEachTimeItsInputTurnsOn()
    {
        var creation = Board();
        var head = Place(creation, "vanilla:scuttler_head", new GridCell(0, 1, 0));
        var key = Place(creation, "vanilla:switch", new GridCell(-3, 1, -3));
        Link(creation, key, head);
        Steps(2);
        Assert.Empty(_sound.Played);

        // Picking a note in the menu plays it once: 24 is an octave up.
        Configure(creation, head, "note", 24);
        Configure(creation, head, "voice", "bass");
        Steps(2);
        var preview = Assert.Single(_sound.Played);
        Assert.Equal(("scuttler_head_bass", 2f), (preview.Sound, preview.Pitch));

        Activate(creation, key);
        Steps(2);
        Assert.Equal(2, _sound.Played.Count);
        Assert.Equal(("scuttler_head_bass", 2f), (_sound.Played[^1].Sound, _sound.Played[^1].Pitch));
        Assert.Equal("note#2", Pose(creation, head));
        // Held on, it plays no more; off and on again plays once more, now an octave down.
        Steps(10);
        Configure(creation, head, "note", 0);
        Steps(1);
        Assert.Equal(0.5f, _sound.Played[^1].Pitch, 4);
        Activate(creation, key);
        Steps(2);
        Activate(creation, key);
        Steps(2);
        Assert.Equal(4, _sound.Played.Count);
        NoErrors();
    }

    // ---- Plasma Drill ----

    [Fact]
    public void APlasmaDrillBitesAtItsRangeWhileOnAndBurnsBatteries()
    {
        var target = Target();
        var creation = Board();
        var drill = Place(creation, "vanilla:plasma_drill", new GridCell(0, 1, 0));
        var trigger = Place(creation, "vanilla:switch", new GridCell(3, 1, 3));
        Link(creation, trigger, drill);
        _f.World.Ray = (from, direction) => new RayHit(from + direction * 2, Creation: target.Id, Part: target.Parts.First().Id);
        Configure(creation, drill, "range", 3);
        Activate(creation, trigger);
        Steps(30);
        // No batteries: no beam.
        Assert.Empty(_f.World.Casts);
        Assert.Equal("off", Pose(creation, drill));

        var cells = Place(creation, "vanilla:battery", new GridCell(-4, 1, -4));
        creation.InventoryOf(cells)!.Add("vanilla:battery_pack", 2);
        Link(creation, cells, drill);
        Steps(60);
        // One bite every properties.interval (0.25 s) for properties.damage (12), out to the "range" setting.
        Assert.InRange(_f.World.Casts.Count, 4, 5);
        Assert.Equal(3f, _f.World.LastRange, 3);
        Assert.Equal(12f * _f.World.Casts.Count, target.PartDamage(target.Parts.First().Id), 3);
        Assert.Equal(1, creation.InventoryOf(cells)!.Count("vanilla:battery_pack"));
        Assert.Equal("on", Pose(creation, drill));
        Assert.Contains(_sound.Played, p => p.Sound == "plasma_drill_start");
        Assert.Equal(1f, Assert.Single(_sound.Loops.Values).Parameters["load"]);

        Activate(creation, trigger);
        Steps(2);
        Assert.Equal("off", Pose(creation, drill));
        Assert.Empty(_sound.Loops);
        Assert.Contains(_sound.Played, p => p.Sound == "plasma_drill_stop");
        NoErrors();
    }

    // ---- Sticky Wheel ----

    [Fact]
    public void AStickyWheelPullsTowardTheSurfaceItTouchesWhileItHasGel()
    {
        var creation = Board();
        var wheel = Place(creation, "vanilla:sticky_wheel", new GridCell(1, 1, 1));
        // A wall on +X: only the ray straight that way finds it.
        _physics.Ray = (from, to) => Vector3.Normalize(to - from).X > 0.9f
            ? new ScriptRayHit(from + Vector3.UnitX * 0.4f, -Vector3.UnitX, 0.4f, null, null)
            : null;
        Steps(4);
        // No chemicals: it does not stick.
        Assert.Empty(_motion.Forces);
        Assert.Equal("off", Pose(creation, wheel));

        var tank = Place(creation, "vanilla:chemical_container", new GridCell(-4, 1, -4));
        creation.InventoryOf(tank)!.Add("vanilla:chemical", 5);
        Link(creation, tank, wheel);
        Steps(4);
        Assert.Equal("on", Pose(creation, wheel));
        Assert.Equal(4, creation.InventoryOf(tank)!.Count("vanilla:chemical"));
        // properties.force 3000 N at stickiness 5 (half), with one ray of eight touching: 3000 * 0.5 * (0.5 + 0.5 / 8).
        var pull = _motion.Forces[^1];
        Assert.Equal(843.75f, pull.Length(), 1);
        Assert.True(pull.X > 843f, $"it pulls toward the wall ({pull})");
        Assert.Contains(_sound.Played, p => p.Sound == "sticky_wheel_pump");
        Assert.Single(_sound.Loops);

        // Away from the wall it lets go (it looks round its tread every other step).
        _physics.Ray = (_, _) => null;
        Steps(2);
        _motion.Forces.Clear();
        Steps(4);
        Assert.Empty(_motion.Forces);
        Assert.Empty(_sound.Loops);
        NoErrors();
    }

    [Fact]
    public void AStickyWheelWithATriggerSticksOnlyWhileItIsOn()
    {
        var creation = Board();
        var wheel = Place(creation, "vanilla:sticky_wheel", new GridCell(1, 1, 1));
        var trigger = Place(creation, "vanilla:switch", new GridCell(-3, 1, 3));
        Link(creation, trigger, wheel);
        var tank = Place(creation, "vanilla:chemical_container", new GridCell(-4, 1, -4));
        creation.InventoryOf(tank)!.Add("vanilla:chemical", 5);
        Link(creation, tank, wheel);
        _physics.Ray = (from, to) => new ScriptRayHit(to, Vector3.UnitY, 0.3f, null, null);
        Steps(4);
        Assert.Empty(_motion.Forces);
        Assert.Equal(5, creation.InventoryOf(tank)!.Count("vanilla:chemical"));

        Activate(creation, trigger);
        Steps(4);
        // All eight rays touch all round: they cancel out, so it holds without pulling any way.
        Assert.Equal("on", Pose(creation, wheel));
        Assert.Equal(4, creation.InventoryOf(tank)!.Count("vanilla:chemical"));
        NoErrors();
    }

    // ---- Helpers ----

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

    // A lone metal block far away, for things to hit.
    private Creation Target()
    {
        var creation = new Creation(_f.World.NextCreationId(), Vanilla);
        Place(creation, "vanilla:metal_block", GridCell.Zero);
        creation.SetAllBodyPoses(new Pose(new Vector3(30, 0, 0), Quaternion.Identity));
        _f.World.Added(creation);
        return creation;
    }

    // One vanilla part on its own at `at` (its part id is 1).
    private Creation Lone(string part, Vector3 at)
    {
        var creation = new Creation(_f.World.NextCreationId(), Vanilla);
        Place(creation, part, GridCell.Zero);
        creation.SetAllBodyPoses(new Pose(at, Quaternion.Identity));
        _f.World.Added(creation);
        return creation;
    }

    // The part in whichever Creation holds it now (a blast may split the board), or null when it is gone.
    private Creation? FindAnywhere(int part) => _f.World.Creations.Values.FirstOrDefault(c => c.FindPart(part) is not null);

    private static int Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        Assert.True(creation.TryPlacePart(part, cell, rotation, out var placed, out var error), $"{part} at {cell}: {error}");
        return placed.Id;
    }

    private string? Pose(Creation creation, int part) => _runtime.Readouts.Text(creation.Id, part, "pose");

    private string? Clip(Creation creation, int part) => _runtime.Readouts.Text(creation.Id, part, "clip");

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

    private sealed class Physics : IScriptPhysics
    {
        public Func<Vector3, Vector3, ScriptRayHit?> Ray { get; set; } = (_, _) => null;

        public Vector3 Gravity => new(0f, -9.81f, 0f);

        public ScriptRayHit? Raycast(Vector3 from, Vector3 to, int? ignoreCreation) => Ray(from, to);

        public IReadOnlyList<ScriptOverlap> OverlapSphere(Vector3 centre, float radius, int? ignoreCreation) => [];
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

        public List<(string Sound, Vector3 At, float Pitch)> Played { get; } = [];

        public Dictionary<int, (string Sound, Dictionary<string, float> Parameters)> Loops { get; } = [];

        public string? Play(string sound, string ownerModId, Vector3 position) => Play(sound, ownerModId, position, 1f);

        public string? Play(string sound, string ownerModId, Vector3 position, float pitch)
        {
            Played.Add((sound, position, pitch));
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
