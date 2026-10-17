using System.Text.Json;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Core.Motion;
using Mechanica.Core.Player;

namespace Mechanica.Scripting.Tests;

/// <summary>
/// The vanilla logic and power scripts (chunk 10) on the real vanilla parts: signals along links, the
/// logic gate's modes, the timer, switch, button and sensor, the seat's throttle and steering, the
/// engine's drive direction and fuel, the controller, and a switch driving the piston through a link.
/// </summary>
public sealed class LogicScriptTests : IDisposable
{
    private const int Car = 1, Peer = 7;
    private static readonly ModLoadResult Vanilla = ModLoader.Load([ItemRig.VanillaRoot]);

    private readonly ScriptRig.FakeWorld _world = new();
    private readonly Physics _physics = new();
    private readonly ScriptRuntime _runtime;
    private PlayerInput? _input;

    public LogicScriptTests()
    {
        Assert.False(Vanilla.HasErrors, string.Join("; ", Vanilla.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var dispatcher = new Core.Actions.ActionDispatcher();
        new MotionActions(new MotionSettings(), _world).Register(dispatcher);
        _runtime = new ScriptRuntime(new ScriptSettings(), dispatcher, _world, _physics, new Source())
        {
            Seats = new SeatRegistry(),
            OccupantInput = _ => _input,
            Creative = true,
        };
        _runtime.Message += m => Assert.NotEqual(ScriptMessageKind.Error, m.Kind);
    }

    public void Dispose() => _runtime.Clear();

    [Theory]
    [InlineData("and", false, false, false)]
    [InlineData("and", true, false, false)]
    [InlineData("and", true, true, true)]
    [InlineData("or", true, false, true)]
    [InlineData("or", false, false, false)]
    [InlineData("xor", true, false, true)]
    [InlineData("xor", true, true, false)]
    [InlineData("nand", true, true, false)]
    [InlineData("nand", true, false, true)]
    [InlineData("nor", false, false, true)]
    [InlineData("xnor", true, true, true)]
    [InlineData("xnor", true, false, false)]
    public void LogicGateModes(string mode, bool a, bool b, bool expected)
    {
        var creation = Board();
        var first = Place(creation, "vanilla:switch", new GridCell(1, 1, 0));
        var second = Place(creation, "vanilla:switch", new GridCell(-1, 1, 0));
        var gate = Place(creation, "vanilla:logic_gate", new GridCell(0, 1, 0));
        Link(creation, first, gate);
        Link(creation, second, gate);
        Configure(creation, gate, "mode", mode);
        Steps(1);
        if (a)
            Activate(creation, first);
        if (b)
            Activate(creation, second);
        Steps(3);
        Assert.Equal(expected ? 1f : 0f, _runtime.Signals.Output(Car, gate));
    }

    [Fact]
    public void AGateWithNoInputsIsOffUnlessInverted()
    {
        var creation = Board();
        var gate = Place(creation, "vanilla:logic_gate", new GridCell(0, 1, 0));
        Steps(2);
        Assert.Equal(0f, _runtime.Signals.Output(Car, gate));
        Configure(creation, gate, "mode", "nor");
        Steps(1);
        Assert.Equal(1f, _runtime.Signals.Output(Car, gate));
    }

    [Fact]
    public void ASwitchStaysAsLeftAndAButtonPulses()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(1, 1, 0));
        var press = Place(creation, "vanilla:button", new GridCell(-1, 1, 0));
        Steps(1);
        Activate(creation, toggle);
        Activate(creation, press);
        Steps(1);
        Assert.Equal(1f, _runtime.Signals.Output(Car, toggle));
        Assert.Equal(1f, _runtime.Signals.Output(Car, press));
        // properties.pulse is 0.5 s: 30 steps.
        Steps(31);
        Assert.Equal(1f, _runtime.Signals.Output(Car, toggle));
        Assert.Equal(0f, _runtime.Signals.Output(Car, press));
        Activate(creation, toggle);
        Steps(1);
        Assert.Equal(0f, _runtime.Signals.Output(Car, toggle));
    }

    [Fact]
    public void ATimerPassesItsInputOnAfterItsDelay()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(1, 1, 0));
        var timer = Place(creation, "vanilla:timer", new GridCell(0, 1, 0));
        Link(creation, toggle, timer);
        Configure(creation, timer, "delay", 0.5);
        Steps(1);
        Activate(creation, toggle);
        Steps(2);
        Assert.Equal(1f, _runtime.Signals.Input(Car, timer));
        Assert.Equal(0f, _runtime.Signals.Output(Car, timer));
        Steps(25);
        Assert.Equal(0f, _runtime.Signals.Output(Car, timer));
        Steps(10);
        Assert.Equal(1f, _runtime.Signals.Output(Car, timer));
    }

    [Fact]
    public void ASensorSeesWhatIsInFrontOfItsFaceWithinRange()
    {
        var creation = Board();
        var sensor = Place(creation, "vanilla:sensor", new GridCell(0, 1, 0));
        Steps(3);
        Assert.Equal(0f, _runtime.Signals.Output(Car, sensor));
        _physics.WallAt = 2f;
        Steps(3);
        Assert.Equal(1f, _runtime.Signals.Output(Car, sensor));
        Assert.True(_physics.LastRay is { } ray && ray.To.Y - ray.From.Y is > 3.9f and < 4.1f, "it looks up to its default range along its +Y");
        Configure(creation, sensor, "range", 1.0);
        Steps(3);
        Assert.Equal(0f, _runtime.Signals.Output(Car, sensor));
    }

    [Fact]
    public void ASwitchDrivesTheVanillaPistonThroughALink()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(1, 1, 0));
        var piston = Place(creation, "vanilla:piston", new GridCell(0, 1, 0));
        Place(creation, "vanilla:metal_block", new GridCell(0, 2, 0));
        Link(creation, toggle, piston);
        Steps(2);
        Assert.Equal(0f, LastMotor(piston).Target);
        Activate(creation, toggle);
        Steps(3);
        Assert.Equal(1f, LastMotor(piston).Target);
    }

    [Fact]
    public void ACarDrivesTowardItsEngineAndSteersRight()
    {
        var (creation, seat, engine, wheels, steering) = BuildCar();
        _runtime.Seats!.Sit(Peer, Car, seat);
        Steps(3);
        // Nobody presses anything: the engine brakes and the wheels point ahead.
        foreach (var (bearing, _) in wheels)
            Assert.Equal(new JointMotor(0f, 80f), LastMotor(bearing));
        Assert.Equal(0f, LastMotor(steering).Target);

        _input = new PlayerInput(1, 1, 0, 0, false, false);
        Steps(3);
        Assert.Equal(1f, _runtime.Signals.Output(Car, seat));
        Assert.Equal(1f, _runtime.Signals.Output(Car, engine));
        // Gear 3 of 5 of 25 rad/s. Rolling toward -Z (the engine's front) is a negative spin about +X
        // (right-handed), so the right wheels turn negative and the left ones, about -X, positive.
        foreach (var (bearing, side) in wheels)
            Assert.Equal(new JointMotor(-side * 15f, 120f), LastMotor(bearing));
        // Right is clockwise from above: about the steering bearing's -Y axis that is +30°.
        Assert.Equal(MathF.PI / 6, LastMotor(steering).Target!.Value, 4);

        _runtime.Seats.Leave(Peer);
        Steps(3);
        Assert.Equal(0f, _runtime.Signals.Output(Car, engine));
        Assert.Equal(0f, LastMotor(steering).Target);
    }

    [Fact]
    public void ReversedBearingsTurnTheOtherWay()
    {
        var (creation, seat, _, wheels, steering) = BuildCar();
        var (reversed, side) = wheels[0];
        Configure(creation, reversed, "reverse", true);
        Configure(creation, steering, "reverse", true);
        _runtime.Seats!.Sit(Peer, Car, seat);
        _input = new PlayerInput(1, 1, 0, 0, false, false);
        Steps(3);
        Assert.Equal(new JointMotor(side * 15f, 120f), LastMotor(reversed));
        foreach (var (bearing, other) in wheels.Skip(1))
            Assert.Equal(new JointMotor(-other * 15f, 120f), LastMotor(bearing));
        Assert.Equal(-MathF.PI / 6, LastMotor(steering).Target!.Value, 4);
    }

    [Fact]
    public void AnEngineBurnsFuelFromTheCreationsGasContainers()
    {
        _runtime.Creative = false;
        var (creation, seat, engine, wheels, _) = BuildCar();
        var tank = Place(creation, "vanilla:gas_container", new GridCell(0, 1, -1));
        _runtime.Seats!.Sit(Peer, Car, seat);
        _input = new PlayerInput(0, 1, 0, 0, false, false);
        Steps(3);
        Assert.All(wheels, w => Assert.Equal(0f, LastMotor(w.Bearing).Velocity));

        creation.InventoryOf(tank)!.Add("vanilla:gasoline", 1);
        Steps(3);
        Assert.Equal(0, creation.InventoryOf(tank)!.Count("vanilla:gasoline"));
        Assert.All(wheels, w => Assert.Equal(-w.Side * 15f, LastMotor(w.Bearing).Velocity));
        // properties.fuel_time is 40 s of full throttle per gasoline.
        Steps(40 * 60 + 5);
        Assert.Equal(0f, _runtime.Signals.Output(Car, engine));
        Assert.All(wheels, w => Assert.Equal(0f, LastMotor(w.Bearing).Velocity));
    }

    [Fact]
    public void AControllerStepsItsBearingsThroughItsFrames()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(-1, 1, 0));
        var controller = Place(creation, "vanilla:controller", new GridCell(0, 1, 0));
        var arm = Bearing(creation, new GridCell(3, 0, 0), new GridCell(1, 0, 0));
        Place(creation, "vanilla:metal_block", new GridCell(4, 0, 0));
        Link(creation, toggle, controller);
        Link(creation, controller, arm);
        Configure(creation, controller, "frames", new[] { 0, 90, -45 });
        Configure(creation, controller, "frame_time", 0.5);
        Steps(1);
        Activate(creation, toggle);
        Steps(3);
        Assert.Equal(0f, LastMotor(arm).Target);
        Steps(31);
        Assert.Equal(MathF.PI / 2, LastMotor(arm).Target!.Value, 4);
        Steps(31);
        Assert.Equal(-MathF.PI / 4, LastMotor(arm).Target!.Value, 4);
        Assert.Equal(1f, _runtime.Signals.Output(Car, controller));
        // Without loop it stops after the last frame.
        Steps(31);
        Assert.Equal(0f, _runtime.Signals.Output(Car, controller));
        Assert.Equal(-MathF.PI / 4, LastMotor(arm).Target!.Value, 4);
    }

    // A metal chassis along Z (z 0..4) with a driver's seat and a gas engine on top, both facing -Z.
    // The front (-Z) wheels hang off an axle under a steering bearing at z 0; the rear ones off a post at z 4.
    private (Creation Creation, int Seat, int Engine, List<(int Bearing, int Side)> Wheels, int Steering) BuildCar()
    {
        var creation = NewCreation();
        for (var z = 1; z <= 4; z++)
            Place(creation, "vanilla:metal_block", new GridCell(0, 0, z));
        var seat = Place(creation, "vanilla:drivers_seat", new GridCell(0, 1, 1));
        var engine = Place(creation, "vanilla:gas_engine", new GridCell(0, 1, 3));
        Place(creation, "vanilla:metal_block", new GridCell(0, -1, 4));
        Place(creation, "vanilla:metal_block", new GridCell(0, -2, 4));
        var steering = Bearing(creation, new GridCell(0, -1, 0), new GridCell(0, -1, 0));
        Place(creation, "vanilla:metal_block", new GridCell(0, -2, 0));
        var wheels = new List<(int, int)>();
        foreach (var z in new[] { 0, 4 })
        {
            foreach (var side in new[] { 1, -1 })
            {
                var outward = new GridCell(side, 0, 0);
                var bearing = Bearing(creation, new GridCell(side, -2, z), outward);
                Wheel(creation, new GridCell(side, -2, z), outward);
                wheels.Add((bearing, side));
            }
        }
        Link(creation, seat, engine);
        Link(creation, seat, steering);
        foreach (var (bearing, _) in wheels)
            Link(creation, engine, bearing);
        return (creation, seat, engine, wheels, steering);
    }

    // A row of metal blocks along X (x -2 to 2) for logic parts to sit on.
    private Creation Board()
    {
        var creation = NewCreation();
        foreach (var x in new[] { -1, -2, 1, 2 })
            Place(creation, "vanilla:metal_block", new GridCell(x, 0, 0));
        return creation;
    }

    private Creation NewCreation()
    {
        var creation = new Creation(Car, Vanilla);
        Place(creation, "vanilla:metal_block", GridCell.Zero);
        _world.Creations[Car] = creation;
        return creation;
    }

    private static int Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        Assert.True(creation.TryPlacePart(part, cell, rotation, out var placed, out var error), $"{part} at {cell}: {error}");
        return placed.Id;
    }

    // A vanilla bearing with its rotor facing outward.
    private static int Bearing(Creation creation, GridCell cell, GridCell outward) =>
        Place(creation, "vanilla:bearing", cell, Facing(outward));

    // A vanilla wheel with its hub on the rotor of the bearing at cell.
    private static void Wheel(Creation creation, GridCell bearing, GridCell outward)
    {
        var rotation = Facing(outward);
        var middle = rotation.Apply(new GridCell(1, 0, 1));
        Place(creation, "vanilla:wheel", bearing + outward - middle, rotation);
    }

    // Local +Y turned to point along outward.
    private static GridRotation Facing(GridCell outward) =>
        outward.Y != 0 ? GridRotation.FromAxes(new GridCell(1, 0, 0), outward) : GridRotation.FromAxes(new GridCell(0, -outward.X - outward.Z, 0), outward);

    private static void Link(Creation creation, int from, int to) =>
        Assert.True(creation.TryLink(from, to, out var error), error);

    private static void Configure(Creation creation, int part, string id, object value)
    {
        var definition = creation.DefinitionOf(part);
        var changes = new Dictionary<string, JsonElement?> { [id] = JsonSerializer.SerializeToElement(value) };
        creation.SetPartSettings(part, PartSettings.Apply(definition, creation.FindPart(part)!.Settings, changes));
    }

    private void Activate(Creation creation, int part)
    {
        var caller = new Core.Actions.ActionCaller(Peer, Core.Actions.ActionSource.Player);
        Assert.Null(_runtime.CheckActivate(caller, creation, part));
        _runtime.Activate(caller, creation, part);
    }

    private void Steps(int count)
    {
        for (var i = 0; i < count; i++)
            _runtime.Update(1f / 60);
    }

    private JointMotor LastMotor(int part)
    {
        var index = _world.Motors.FindLastIndex(m => m.Part == part);
        Assert.True(index >= 0, $"part {part} got a motor");
        return _world.JointMotors[index];
    }

    private sealed class Source : IScriptSource
    {
        public ScriptFile? FindScript(string partId)
        {
            if (Vanilla.FindDefinition(Identifiers.Qualify(partId))?.Script is not { } script)
                return null;
            return new ScriptFile($"vanilla:{script}", "vanilla", Path.Combine(ItemRig.VanillaRoot, script));
        }
    }

    // A wall straight above the origin when WallAt is set; nothing otherwise.
    private sealed class Physics : IScriptPhysics
    {
        public float? WallAt { get; set; }

        public (System.Numerics.Vector3 From, System.Numerics.Vector3 To)? LastRay { get; private set; }

        public System.Numerics.Vector3 Gravity => new(0, -9.81f, 0);

        public ScriptRayHit? Raycast(System.Numerics.Vector3 from, System.Numerics.Vector3 to, int? ignoreCreation)
        {
            LastRay = (from, to);
            if (WallAt is not { } y || to.Y < y || from.Y > y)
                return null;
            return new ScriptRayHit(new System.Numerics.Vector3(from.X, y, from.Z), -System.Numerics.Vector3.UnitY, y - from.Y, null, null);
        }

        public IReadOnlyList<ScriptOverlap> OverlapSphere(System.Numerics.Vector3 centre, float radius, int? ignoreCreation) => [];
    }
}
