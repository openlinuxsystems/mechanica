using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Motion;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Survival;

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
    private readonly List<ScriptMessage> _messages = [];

    public ContentScriptTests()
    {
        Assert.False(Vanilla.HasErrors, string.Join("; ", Vanilla.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var motion = new Motion(_f);
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
    public void APoweredSawBladeCutsWhatItsRimTouches()
    {
        var target = Target();
        var creation = Board();
        var saw = Place(creation, "vanilla:saw_blade", new GridCell(-1, 1, -1));
        var power = Place(creation, "vanilla:switch", new GridCell(3, 1, 3));
        Link(creation, power, saw);
        var hits = 0;
        _f.World.Ray = (from, direction) => direction.X > 0.99f ? Hits(target, from, ref hits) : null;
        Steps(30);
        Assert.Empty(_f.World.Casts);
        Assert.Empty(_sound.Loops);

        Activate(creation, power);
        Steps(31);
        Assert.Equal("saw_spin", Assert.Single(_sound.Loops.Values).Sound);
        // Every 0.25 s it casts its eight rim rays, each starting outside the blade.
        Assert.InRange(_f.World.Casts.Count, 16, 24);
        Assert.All(_f.World.Casts, c => Assert.True(Vector3.Distance(c.From, new Vector3(0, 0.25f, 0)) > 0.37f, $"{c.From} is past the rim"));
        Assert.Equal(0.12f, _f.World.LastRange, 3);
        Assert.InRange(hits, 2, 3);
        Assert.Equal(15f * hits, target.PartDamage(target.Parts.First().Id), 3);
        Assert.Contains(_sound.Played, p => p.Sound == "saw_cut");
        Assert.Equal(1f, Assert.Single(_sound.Loops.Values).Parameters["load"]);
        Assert.Equal(30f, _runtime.Animations.Speed(creation.Id, saw));

        Activate(creation, power);
        Steps(2);
        Assert.Empty(_sound.Loops);
        Assert.Equal(0f, _runtime.Animations.Speed(creation.Id, saw));
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
    public void APoweredDrillBoresForwardFromItsTip()
    {
        var creation = Board();
        // A drill's base is its back: it goes on the front of a block.
        Place(creation, "vanilla:metal_block", new GridCell(0, 1, 1));
        var drill = Place(creation, "vanilla:drill", new GridCell(0, 1, -2));
        var power = Place(creation, "vanilla:switch", new GridCell(3, 1, 3));
        Link(creation, power, drill);
        Activate(creation, power);
        // properties.interval is 0.3 s: one bore in the first 10 steps.
        Steps(10);
        var cast = Assert.Single(_f.World.Casts);
        Assert.True(cast.Direction.Z < -0.99f, $"it bores along -Z ({cast.Direction})");
        // Its centre is at z -0.25 m and its tip 0.375 m in front of that.
        Assert.True(cast.From.Z is < -0.63f and > -0.66f, $"from just past its tip ({cast.From})");
        Assert.Equal("drill_spin", Assert.Single(_sound.Loops.Values).Sound);
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

    private static RayHit Hits(Creation target, Vector3 from, ref int hits)
    {
        hits++;
        return new RayHit(from, Creation: target.Id, Part: target.Parts.First().Id);
    }

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

        public void ApplyForce(Creation creation, int body, Vector3 force, Vector3 torque, float? duration, int? part, Vector3? at, Vector3 ownTorque)
        {
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
