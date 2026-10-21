using System.Numerics;
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
    private readonly LoopSound _sound = new();
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
            Sound = _sound,
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
    public void ASwitchStaysAsLeftAndAButtonIsOnWhileHeld()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(1, 1, 0));
        var press = Place(creation, "vanilla:button", new GridCell(-1, 1, 0));
        Steps(1);
        Assert.Equal("off", _runtime.Readouts.Text(Car, toggle, "pose"));
        Activate(creation, toggle);
        Activate(creation, press);
        Steps(1);
        Assert.Equal(1f, _runtime.Signals.Output(Car, toggle));
        Assert.Equal(1f, _runtime.Signals.Output(Car, press));
        // The button has no timer: it stays on for as long as it is held.
        Steps(120);
        Assert.Equal(1f, _runtime.Signals.Output(Car, press));
        _runtime.Release(Peer, creation, press);
        Steps(1);
        Assert.Equal(1f, _runtime.Signals.Output(Car, toggle));
        Assert.Equal("on", _runtime.Readouts.Text(Car, toggle, "pose"));
        Assert.Equal(0f, _runtime.Signals.Output(Car, press));
        Activate(creation, toggle);
        Steps(1);
        Assert.Equal(0f, _runtime.Signals.Output(Car, toggle));
        Assert.Equal("off", _runtime.Readouts.Text(Car, toggle, "pose"));
    }

    [Fact]
    public void AButtonHeldByTwoPlayersStaysDownUntilBothLetGo()
    {
        var creation = Board();
        var press = Place(creation, "vanilla:button", new GridCell(0, 1, 0));
        Steps(1);
        Activate(creation, press);
        _runtime.Activate(new Core.Actions.ActionCaller(8, Core.Actions.ActionSource.Player), creation, press);
        Steps(1);
        _runtime.Release(Peer, creation, press);
        Steps(1);
        Assert.Equal(1f, _runtime.Signals.Output(Car, press));
        _runtime.Release(8, creation, press);
        Steps(1);
        Assert.Equal(0f, _runtime.Signals.Output(Car, press));
        // Letting go of a button nobody holds changes nothing.
        _runtime.Release(Peer, creation, press);
        Steps(1);
        Assert.Equal(0f, _runtime.Signals.Output(Car, press));
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
        // Piston 1 pushes out its full 7 blocks at 2 blocks/s with 5000 N.
        Assert.Equal(1.75f, LastMotor(piston).Target);
        Assert.Equal(0.5f, LastMotor(piston).Velocity);
        Assert.Equal(5000f, LastMotor(piston).MaxEffort);
    }

    [Fact]
    public void ThePistonsLengthAndSpeedSettingsTakeEffectAtOnce()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(1, 1, 0));
        var piston = Place(creation, "vanilla:piston", new GridCell(0, 1, 0));
        Place(creation, "vanilla:metal_block", new GridCell(0, 2, 0));
        Link(creation, toggle, piston);
        Activate(creation, toggle);
        Steps(3);
        Configure(creation, piston, "length", 3);
        Configure(creation, piston, "speed", 1);
        Steps(2);
        Assert.Equal(0.75f, LastMotor(piston).Target);
        Assert.Equal(0.25f, LastMotor(piston).Velocity);
        Activate(creation, toggle);
        Steps(2);
        Assert.Equal(0f, LastMotor(piston).Target);
    }

    [Fact]
    public void AnUpgradedPistonReachesFurtherFasterAndHarder()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(1, 1, 0));
        var piston = Place(creation, "vanilla:piston", new GridCell(0, 1, 0));
        Place(creation, "vanilla:metal_block", new GridCell(0, 2, 0));
        Link(creation, toggle, piston);
        Assert.True(creation.SetLevel(piston, 4));
        Assert.Equal("Piston 5", creation.DefinitionOf(piston).Name);
        Activate(creation, toggle);
        Steps(3);
        // Level 5: 15 blocks (3.75 m) at 10 blocks/s (2.5 m/s) with 25000 N.
        Assert.Equal(3.75f, LastMotor(piston).Target);
        Assert.Equal(2.5f, LastMotor(piston).Velocity);
        Assert.Equal(25000f, LastMotor(piston).MaxEffort);
    }

    [Fact]
    public void ACarDrivesTowardItsEngineAndSteersRight()
    {
        // Electric Engine 2 drives four bearings; the driver sitting down starts it in 0.6 s.
        var (creation, seat, engine, wheels, steering) = BuildCar("vanilla:electric_engine", level: 1);
        Configure(creation, engine, "gear", 3);
        _runtime.Seats!.Sit(Peer, Car, seat);
        Steps(45);
        // Nobody presses anything: the engine brakes and the wheels point ahead.
        foreach (var (bearing, _) in wheels)
            Assert.Equal(new JointMotor(0f, 225f), LastMotor(bearing));
        Assert.Equal(0f, LastMotor(steering).Target);

        _input = new PlayerInput(1, 1, 0, 0, false, false);
        Steps(3);
        Assert.Equal(1f, _runtime.Signals.Output(Car, seat));
        Assert.Equal(1f, _runtime.Signals.Output(Car, engine));
        // Gear 3 of 7 of 28 rad/s. Rolling toward -Z (the engine's front) is a negative spin about +X
        // (right-handed), so the right wheels turn negative and the left ones, about -X, positive.
        foreach (var (bearing, side) in wheels)
            Assert.Equal(new JointMotor(-side * 12f, 300f), LastMotor(bearing));
        // Right is clockwise from above: about the steering bearing's -Y axis that is +27° (the seat's steer_angle).
        Assert.Equal(27 * MathF.PI / 180, LastMotor(steering).Target!.Value, 4);

        _runtime.Seats.Leave(Peer);
        Steps(3);
        Assert.Equal(0f, _runtime.Signals.Output(Car, engine));
        Assert.Equal(0f, LastMotor(steering).Target);
    }

    [Fact]
    public void TheScrapEngineMustBeSwitchedOnAndCranksBeforeItDrives()
    {
        var (creation, seat, engine, wheels, _) = BuildCar("vanilla:scrap_gas_engine", linked: 2);
        // It drives two bearings at most (Scrap Engine Design Spec).
        Assert.False(creation.TryLink(engine, wheels[2].Bearing, out _));
        // Switched off, sitting down does not start it.
        Configure(creation, engine, "on", false);
        _runtime.Seats!.Sit(Peer, Car, seat);
        _input = new PlayerInput(0, 1, 0, 0, false, false);
        Steps(10);
        Assert.StartsWith("off_loop", _runtime.Readouts.Text(Car, engine, "clip"));
        Assert.DoesNotContain(_world.Motors, m => m.Part == wheels[0].Bearing && m.Velocity is not (null or 0f));

        Configure(creation, engine, "on", true);
        Steps(1);
        var first = _runtime.Readouts.Text(Car, engine, "clip")!;
        Assert.True(first.StartsWith("start#", StringComparison.Ordinal) || first.StartsWith("false_start#", StringComparison.Ordinal), first);
        Assert.Equal(0.5f, _runtime.Readouts.Number(Car, engine, "lever")!.Value, 4);
        Steps(120);                                   // a false start (0.7 s) and the start (1 s) are over
        Assert.StartsWith("run_loop#", _runtime.Readouts.Text(Car, engine, "clip"));
        Assert.Equal(1f, _runtime.Readouts.Number(Car, engine, "running"));
        // A gas engine's slider is its power: power 2 of 3 turns the full 15 rad/s (uneven by up to 12 %) toward the
        // engine's front with 2/3 of its 240 N·m.
        foreach (var (bearing, side) in wheels.Take(2))
        {
            var motor = LastMotor(bearing);
            Assert.Equal(160f, motor.MaxEffort);
            Assert.InRange(-side * motor.Velocity!.Value, 15f * 0.87f, 15f * 1.13f);
        }

        Configure(creation, engine, "gear", 3);
        Steps(1);
        Assert.StartsWith("gear#", _runtime.Readouts.Text(Car, engine, "clip"));
        Steps(30);
        Assert.StartsWith("run_hard_loop#", _runtime.Readouts.Text(Car, engine, "clip"));
        Assert.Equal(1f, _runtime.Readouts.Number(Car, engine, "lever"));
        foreach (var (bearing, side) in wheels.Take(2))
        {
            var motor = LastMotor(bearing);
            Assert.Equal(240f, motor.MaxEffort);
            Assert.InRange(-side * motor.Velocity!.Value, 15f * 0.87f, 15f * 1.13f);
        }

        Configure(creation, engine, "on", false);
        Steps(1);
        Assert.StartsWith("stop#", _runtime.Readouts.Text(Car, engine, "clip"));
        Steps(60);
        Assert.StartsWith("off_loop#", _runtime.Readouts.Text(Car, engine, "clip"));
        Assert.Equal(0f, _runtime.Readouts.Number(Car, engine, "running"));
        Assert.Equal(0f, LastMotor(wheels[0].Bearing).Velocity);
    }

    [Fact]
    public void AnEngineLinkedToADriversSeatRunsOnlyWhileSomeoneSitsInIt()
    {
        var (creation, seat, engine, wheels, _) = BuildCar("vanilla:scrap_gas_engine", linked: 2);
        // On by default, but the seat controls it: nobody in the seat, nothing runs and nothing sounds.
        Steps(120);
        Assert.StartsWith("off_loop", _runtime.Readouts.Text(Car, engine, "clip"));
        Assert.Equal(0f, _runtime.Readouts.Number(Car, engine, "running"));
        Assert.Empty(_sound.Loops);
        Assert.DoesNotContain(_world.Motors, m => wheels.Any(w => w.Bearing == m.Part) && m.Velocity is not (null or 0f));

        // Sitting down starts it: it idles (holding the wheels) until W, then drives.
        _runtime.Seats!.Sit(Peer, Car, seat);
        Steps(120);                                   // a false start (0.7 s) and the start (1 s) are over
        Assert.StartsWith("run_loop#", _runtime.Readouts.Text(Car, engine, "clip"));
        Assert.Single(_sound.Loops);
        foreach (var (bearing, _) in wheels.Take(2))
            Assert.Equal(new JointMotor(0f, 180f), LastMotor(bearing));
        _input = new PlayerInput(0, 1, 0, 0, false, false);
        Steps(10);
        Assert.Equal(1f, _runtime.Signals.Output(Car, engine));
        foreach (var (bearing, side) in wheels.Take(2))
            Assert.InRange(-side * LastMotor(bearing).Velocity!.Value, 15f * 0.87f, 15f * 1.13f);

        // Getting up (still holding W) stops it: the drive and the hum go, and it holds the wheels with its brake so the
        // parked car stays put.
        _runtime.Seats.Leave(Peer);
        Steps(1);
        Assert.StartsWith("stop#", _runtime.Readouts.Text(Car, engine, "clip"));
        Assert.Empty(_sound.Loops);
        Assert.Equal(0f, _runtime.Signals.Output(Car, engine));
        Steps(120);
        Assert.StartsWith("off_loop#", _runtime.Readouts.Text(Car, engine, "clip"));
        Assert.Equal(0f, _runtime.Readouts.Number(Car, engine, "running"));
        Assert.Empty(_sound.Loops);
        foreach (var (bearing, _) in wheels.Take(2))
            Assert.Equal(new JointMotor(0f, 180f), LastMotor(bearing));
    }

    [Fact]
    public void AnEngineStopsWhenItsSeatIsRemovedWithTheDriverInIt()
    {
        var (creation, seat, engine, wheels, _) = BuildCar("vanilla:scrap_gas_engine", linked: 2);
        _runtime.Seats!.Sit(Peer, Car, seat);
        Steps(120);
        Assert.StartsWith("run_loop#", _runtime.Readouts.Text(Car, engine, "clip"));

        // With no control left it stops and holds the wheels: it never drives off on its own.
        creation.RemovePart(seat, () => 99);
        Steps(60);
        Assert.Equal(0f, _runtime.Readouts.Number(Car, engine, "running"));
        Assert.Equal(0f, _runtime.Signals.Output(Car, engine));
        Assert.Empty(_sound.Loops);
        foreach (var (bearing, _) in wheels.Take(2))
            Assert.Equal(0f, LastMotor(bearing).Velocity);
    }

    [Fact]
    public void AnEngineLinkedToASwitchRunsWhileTheSwitchIsOn()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(-2, 1, 0));
        var engine = Place(creation, "vanilla:electric_engine", new GridCell(2, 1, 0));
        Link(creation, toggle, engine);
        Steps(60);
        Assert.Equal(0f, _runtime.Readouts.Number(Car, engine, "running"));

        Activate(creation, toggle);
        Steps(60);                                    // the start is 0.6 s
        Assert.Equal(1f, _runtime.Readouts.Number(Car, engine, "running"));
        Assert.Equal(1f, _runtime.Signals.Output(Car, engine));

        Activate(creation, toggle);
        Steps(60);
        Assert.Equal(0f, _runtime.Readouts.Number(Car, engine, "running"));
        Assert.Empty(_sound.Loops);
    }

    [Fact]
    public void AnUpgradedGasEngineDrivesMoreBearingsFasterAndHeatsUp()
    {
        var (creation, seat, engine, wheels, _) = BuildCar(linked: 2);
        // Gas Engine 1 drives two bearings; level 5 (upgrade 4) drives ten, with thirteen power steps.
        Assert.False(creation.TryLink(engine, wheels[2].Bearing, out _));
        Assert.True(creation.SetLevel(engine, 4));
        Assert.Equal("Gas Engine 5", creation.DefinitionOf(engine).Name);
        foreach (var (bearing, _) in wheels.Skip(2))
            Link(creation, engine, bearing);
        _runtime.Seats!.Sit(Peer, Car, seat);
        _input = new PlayerInput(0, 1, 0, 0, false, false);
        Configure(creation, engine, "on", true);
        Configure(creation, engine, "gear", 13);
        Steps(60);
        Assert.Equal(1f, _runtime.Readouts.Number(Car, engine, "lever"));
        Assert.StartsWith("run_hard_loop#", _runtime.Readouts.Text(Car, engine, "clip"));
        // Top gear of 57 rad/s (under the 60 rad/s bearing limit even when it wobbles) with 820 N·m, uneven by up to 3 %.
        foreach (var (bearing, side) in wheels)
        {
            var motor = LastMotor(bearing);
            Assert.Equal(1300f, motor.MaxEffort);
            Assert.InRange(-side * motor.Velocity!.Value, 57f * 0.97f, 57f * 1.03f);
        }
        Assert.InRange(_runtime.Readouts.Number(Car, engine, "heat")!.Value, 0.2f, 1f);
        Steps(30);
        Assert.Equal(1f, _runtime.Readouts.Number(Car, engine, "heat"));

        Configure(creation, engine, "on", false);
        Steps(60);
        Assert.Equal(0f, _runtime.Readouts.Number(Car, engine, "heat"));
    }

    [Fact]
    public void TheElectricEngineMustBeSwitchedOnAndItsUpgradesDriveMoreBearingsFaster()
    {
        var (creation, seat, engine, wheels, _) = BuildCar("vanilla:electric_engine", linked: 2);
        // Electric Engine 1 drives two bearings; level 5 (upgrade 4) drives ten, with sixteen speed settings.
        Assert.Equal("Electric Engine 1", creation.DefinitionOf(engine).Name);
        Assert.False(creation.TryLink(engine, wheels[2].Bearing, out _));
        // Switched off, sitting down does not start it.
        Configure(creation, engine, "on", false);
        _runtime.Seats!.Sit(Peer, Car, seat);
        _input = new PlayerInput(0, 1, 0, 0, false, false);
        Steps(10);
        Assert.StartsWith("off_loop", _runtime.Readouts.Text(Car, engine, "clip"));
        Assert.DoesNotContain(_world.Motors, m => m.Part == wheels[0].Bearing && m.Velocity is not (null or 0f));

        Configure(creation, engine, "on", true);
        Steps(1);
        Assert.StartsWith("start#", _runtime.Readouts.Text(Car, engine, "clip"));
        Steps(45);                                   // the start (0.6 s) is over: gear 3 of 5 of 20 rad/s, no wobble
        Assert.StartsWith("run_loop#", _runtime.Readouts.Text(Car, engine, "clip"));
        foreach (var (bearing, side) in wheels.Take(2))
            Assert.Equal(new JointMotor(-side * 12f, 300f), LastMotor(bearing));

        Assert.True(creation.SetLevel(engine, 4));
        Assert.Equal("Electric Engine 5", creation.DefinitionOf(engine).Name);
        foreach (var (bearing, _) in wheels.Skip(2))
            Link(creation, engine, bearing);
        Configure(creation, engine, "gear", 16);
        Steps(60);
        Assert.Equal(1f, _runtime.Readouts.Number(Car, engine, "lever"));
        Assert.StartsWith("run_hard_loop#", _runtime.Readouts.Text(Car, engine, "clip"));
        // Top speed of 57 rad/s (under the 60 rad/s bearing limit), the same 160 N·m pull, and blue arcs.
        foreach (var (bearing, side) in wheels)
            Assert.Equal(new JointMotor(-side * 57f, 300f), LastMotor(bearing));
        Assert.Equal(1f, _runtime.Readouts.Number(Car, engine, "heat"));
        Assert.True(_runtime.Readouts.Number(Car, engine, "sparks") > 0f);

        Configure(creation, engine, "on", false);
        Steps(45);
        Assert.StartsWith("off_loop#", _runtime.Readouts.Text(Car, engine, "clip"));
        Assert.Equal(0f, _runtime.Readouts.Number(Car, engine, "heat"));
        Assert.Equal(0f, LastMotor(wheels[0].Bearing).Velocity);
    }

    [Fact]
    public void EveryEngineLevelStaysUnderTheBearingSpeedLimit()
    {
        // engine.lua sends speed × (1 + uneven) at its top gear; a bearing refuses more than the motion limit.
        var limit = new MotionSettings().MaxBearingSpeed;
        var engines = Vanilla.Parts.Values.Where(p => p.Definition.Script?.EndsWith("engine.lua", StringComparison.Ordinal) == true).ToList();
        Assert.NotEmpty(engines);
        foreach (var engine in engines)
        {
            for (var level = 0; level <= PartUpgrades.MaxLevel(engine.Definition); level++)
            {
                var properties = PartUpgrades.At(engine.Definition, level).Properties!;
                var top = (float)properties["speed"]!.GetValue<double>() * (1 + (float)(properties["uneven"]?.GetValue<double>() ?? 0));
                Assert.True(top <= limit, $"{engine.Definition.Name} turns up to {top} rad/s, over {limit}");
            }
        }
    }

    [Fact]
    public void ReversedBearingsTurnTheOtherWay()
    {
        var (creation, seat, engine, wheels, steering) = BuildCar("vanilla:electric_engine", level: 1);
        Configure(creation, engine, "gear", 3);
        _runtime.Seats!.Sit(Peer, Car, seat);
        Steps(45);
        var (reversed, side) = wheels[0];
        Configure(creation, reversed, "reverse", true);
        Configure(creation, steering, "reverse", true);
        _input = new PlayerInput(1, 1, 0, 0, false, false);
        Steps(3);
        Assert.Equal(new JointMotor(side * 12f, 300f), LastMotor(reversed));
        foreach (var (bearing, other) in wheels.Skip(1))
            Assert.Equal(new JointMotor(-other * 12f, 300f), LastMotor(bearing));
        Assert.Equal(-27 * MathF.PI / 180, LastMotor(steering).Target!.Value, 4);
    }

    [Fact]
    public void AnEngineBurnsFuelFromTheCreationsGasContainers()
    {
        _runtime.Creative = false;
        var (creation, seat, engine, wheels, _) = BuildCar(linked: 2);
        var tank = Tank(creation);
        _runtime.Seats!.Sit(Peer, Car, seat);
        _input = new PlayerInput(0, 1, 0, 0, false, false);
        // Switched on without fuel it only sputters.
        Configure(creation, engine, "on", true);
        Steps(60);
        Assert.StartsWith("off_loop#", _runtime.Readouts.Text(Car, engine, "clip"));
        Assert.DoesNotContain(_world.Motors, m => wheels.Any(w => w.Bearing == m.Part) && m.Velocity is not (null or 0f));

        creation.InventoryOf(tank)!.Add("vanilla:gasoline", 1);
        Configure(creation, engine, "on", false);
        Steps(1);
        Configure(creation, engine, "on", true);
        Steps(60);                                    // the start is 0.8 s
        Assert.Equal(0, creation.InventoryOf(tank)!.Count("vanilla:gasoline"));
        // Power 3 of 5: the full 25 rad/s, uneven by up to 3 %, with 3/5 of the torque.
        foreach (var (bearing, side) in wheels.Take(2))
            Assert.InRange(-side * LastMotor(bearing).Velocity!.Value, 25f * 0.97f, 25f * 1.03f);
        // properties.fuel_time is 40 s of full throttle at full power per gasoline; at power 3 of 5 it burns 3/5 as
        // fast, so the can lasts 66.7 s.
        Steps(40 * 60 + 5);
        Assert.Equal(1f, _runtime.Signals.Output(Car, engine));
        Steps(27 * 60);
        Assert.Equal(0f, _runtime.Signals.Output(Car, engine));
        // Out of fuel it holds the wheels with its brake.
        Assert.All(wheels.Take(2), w => Assert.Equal(0f, LastMotor(w.Bearing).Velocity));
    }

    [Fact]
    public void AnEngineBurnsOnlyFromTheGasContainersLinkedToIt()
    {
        _runtime.Creative = false;
        var (creation, seat, engine, wheels, _) = BuildCar(linked: 2);
        var linked = Tank(creation);
        foreach (var z in new[] { -3, -4, -5 })
            Place(creation, "vanilla:metal_block", new GridCell(0, 0, z));
        var other = Place(creation, "vanilla:gas_container", new GridCell(-1, 1, -6));
        creation.InventoryOf(linked)!.Add("vanilla:gasoline", 1);
        creation.InventoryOf(other)!.Add("vanilla:gasoline", 1);
        Link(creation, linked, engine);
        _runtime.Seats!.Sit(Peer, Car, seat);
        _input = new PlayerInput(0, 1, 0, 0, false, false);
        Configure(creation, engine, "on", true);
        Steps(60);                                    // the start is 0.8 s
        Assert.Equal(0, creation.InventoryOf(linked)!.Count("vanilla:gasoline"));
        Assert.Equal(1, creation.InventoryOf(other)!.Count("vanilla:gasoline"));
        Assert.Equal(0f, _runtime.Readouts.Number(Car, engine, "fuel_items"));
        // The seat still drives it: a linked tank is not a control.
        foreach (var (bearing, side) in wheels.Take(2))
            Assert.InRange(-side * LastMotor(bearing).Velocity!.Value, 25f * 0.97f, 25f * 1.03f);

        // With only the tank linked in (no seat), the engine has no control, so it stops.
        _runtime.Seats!.Leave(Peer);
        Assert.True(creation.Unlink(seat, engine));
        Steps(150);
        Assert.Equal(0f, _runtime.Readouts.Number(Car, engine, "running"));
        foreach (var (bearing, _) in wheels.Take(2))
            Assert.Equal(0f, LastMotor(bearing).Velocity);
    }

    [Fact]
    public void AGasContainerShowsHowFullItIs()
    {
        var creation = NewCreation();
        var tank = Tank(creation);
        var readouts = _runtime.Readouts;
        Steps(2);
        Assert.Equal(0f, readouts.Number(Car, tank, "fill"));
        Assert.StartsWith("rest#", readouts.Text(Car, tank, "clip"));

        // 50 of its 100 gasoline (5 slots of 20): the gauge pulses and eases up to half over 0.3 s.
        creation.InventoryOf(tank)!.Add("vanilla:gasoline", 50);
        Steps(6);
        Assert.StartsWith("pulse#", readouts.Text(Car, tank, "clip"));
        Assert.InRange(readouts.Number(Car, tank, "fill")!.Value, 0.01f, 0.49f);
        Steps(30);
        Assert.Equal(0.5f, readouts.Number(Car, tank, "fill"));
        Assert.StartsWith("idle_loop#", readouts.Text(Car, tank, "clip"));

        // Something drew fuel: the supply clip, then the shimmer again.
        creation.InventoryOf(tank)!.Remove("vanilla:gasoline", 10);
        Steps(6);
        Assert.StartsWith("supply#", readouts.Text(Car, tank, "clip"));
        Steps(60);
        Assert.Equal(0.4f, readouts.Number(Car, tank, "fill"));
        Assert.StartsWith("idle_loop#", readouts.Text(Car, tank, "clip"));

        creation.InventoryOf(tank)!.Add("vanilla:gasoline", 60);
        Steps(60);
        Assert.Equal(1f, readouts.Number(Car, tank, "fill"));
        Assert.StartsWith("rest#", readouts.Text(Car, tank, "clip"));
    }

    [Fact]
    public void ABatteryShowsHowChargedItIs()
    {
        var creation = NewCreation();
        var battery = Tank(creation, "vanilla:battery");
        var readouts = _runtime.Readouts;
        Steps(2);
        Assert.Equal(0f, readouts.Number(Car, battery, "charge"));

        // 20 of its 50 batteries (5 slots of 10): the gauge eases up to 0.4 over 0.3 s.
        creation.InventoryOf(battery)!.Add("vanilla:battery_pack", 20);
        Steps(6);
        Assert.InRange(readouts.Number(Car, battery, "charge")!.Value, 0.01f, 0.39f);
        Steps(30);
        Assert.Equal(0.4f, readouts.Number(Car, battery, "charge"));

        // An engine drew one: it eases down.
        creation.InventoryOf(battery)!.Remove("vanilla:battery_pack", 1);
        Steps(60);
        Assert.Equal(0.38f, readouts.Number(Car, battery, "charge"));
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

    [Fact]
    public void AControllerLinkedFromASeatStartsOnWAndStopsOnS()
    {
        var creation = Board();
        var seat = Place(creation, "vanilla:drivers_seat", new GridCell(-2, 1, 0));
        var controller = Place(creation, "vanilla:controller", new GridCell(0, 1, 0));
        var arm = Bearing(creation, new GridCell(3, 0, 0), new GridCell(1, 0, 0));
        Place(creation, "vanilla:metal_block", new GridCell(4, 0, 0));
        Link(creation, seat, controller);
        Link(creation, controller, arm);
        Configure(creation, controller, "frames", new[] { 0, 90 });
        Configure(creation, controller, "frame_time", 0.5);
        Configure(creation, controller, "loop", true);
        _runtime.Seats!.Sit(Peer, Car, seat);
        Steps(3);
        Assert.Equal(0f, _runtime.Signals.Output(Car, controller));

        // W starts it, and it keeps running after W is let go.
        _input = new PlayerInput(0, 1, 0, 0, false, false);
        Steps(3);
        _input = new PlayerInput(0, 0, 0, 0, false, false);
        Steps(40);
        Assert.Equal(1f, _runtime.Signals.Output(Car, controller));
        Assert.Equal(MathF.PI / 2, LastMotor(arm).Target!.Value, 4);

        // S stops it, and it stays stopped after S is let go.
        _input = new PlayerInput(0, -1, 0, 0, false, false);
        Steps(3);
        _input = new PlayerInput(0, 0, 0, 0, false, false);
        Steps(40);
        Assert.Equal(0f, _runtime.Signals.Output(Car, controller));
    }

    [Fact]
    public void TheScrapSeatShowsItsDriverAndRattlesWhileAnEngineRuns()
    {
        var creation = Board();
        var seat = Place(creation, "vanilla:scrap_drivers_seat", new GridCell(-2, 1, -2));
        var engine = Place(creation, "vanilla:electric_engine", new GridCell(2, 1, 0));
        Link(creation, seat, engine);
        Configure(creation, engine, "on", false);
        Steps(3);
        Assert.Equal(0f, _runtime.Readouts.Number(Car, seat, "seated"));
        Assert.Equal(0f, _runtime.Readouts.Number(Car, seat, "rattle"));

        _runtime.Seats!.Sit(Peer, Car, seat);
        _input = new PlayerInput(0, 0, 0, 0, false, false);
        Steps(12);
        Assert.Equal(1f, _runtime.Readouts.Number(Car, seat, "seated"));
        Assert.Equal(0f, _runtime.Readouts.Number(Car, seat, "rattle"));
        Assert.StartsWith("enter#", _runtime.Readouts.Text(Car, seat, "clip"));
        Steps(48);
        Assert.StartsWith("seated_loop#", _runtime.Readouts.Text(Car, seat, "clip"));

        Configure(creation, engine, "on", true);
        _input = new PlayerInput(0, 1, 0, 0, false, false);
        Steps(60);                                   // the start (0.6 s) and a menu refresh
        Assert.Equal(1f, _runtime.Readouts.Number(Car, engine, "running"));
        Assert.Equal(1f, _runtime.Readouts.Number(Car, seat, "rattle"));
        Assert.StartsWith("seated_rattle_loop#", _runtime.Readouts.Text(Car, seat, "clip"));

        _runtime.Seats.Leave(Peer);
        Steps(3);
        Assert.StartsWith("exit#", _runtime.Readouts.Text(Car, seat, "clip"));
        Assert.Equal(0f, _runtime.Readouts.Number(Car, seat, "seated"));
        Assert.Equal(0f, _runtime.Readouts.Number(Car, seat, "rattle"));
    }

    [Fact]
    public void TheScrapSeatTakesFourLinks()
    {
        var creation = Board();
        var seat = Place(creation, "vanilla:scrap_drivers_seat", new GridCell(-2, 1, -2));
        for (var x = 3; x < 8; x++)
            Place(creation, "vanilla:metal_block", new GridCell(x, 0, 0));
        for (var i = 0; i < 4; i++)
            Link(creation, seat, Place(creation, "vanilla:switch", new GridCell(i + 3, 1, 0)));
        var fifth = Place(creation, "vanilla:switch", new GridCell(7, 1, 0));
        Assert.False(creation.TryLink(seat, fifth, out _));
    }

    [Fact]
    public void TheScrapPassengerSeatRidesAlongWithoutDriving()
    {
        var creation = Board();
        var seat = Place(creation, "vanilla:scrap_seat", new GridCell(-2, 1, -2));
        var engine = Place(creation, "vanilla:electric_engine", new GridCell(2, 1, 0));
        Link(creation, seat, engine);
        Steps(3);
        _runtime.Seats!.Sit(Peer, Car, seat);
        _input = new PlayerInput(0, 1, 0, 0, false, false);
        Steps(12);
        Assert.Equal(1f, _runtime.Readouts.Number(Car, seat, "seated"));
        Assert.StartsWith("enter#", _runtime.Readouts.Text(Car, seat, "clip"));
        Assert.Equal(0f, _runtime.Signals.Output(Car, seat));
        Steps(48);
        Assert.StartsWith("seated_loop#", _runtime.Readouts.Text(Car, seat, "clip"));
        Assert.Equal(0f, _runtime.Readouts.Number(Car, engine, "running"));
        Assert.Empty(_sound.Loops);

        _runtime.Seats.Leave(Peer);
        Steps(3);
        Assert.StartsWith("exit#", _runtime.Readouts.Text(Car, seat, "clip"));
    }

    [Fact]
    public void TheScrapPassengerSeatTakesTwoLinks()
    {
        var creation = Board();
        var seat = Place(creation, "vanilla:scrap_seat", new GridCell(-2, 1, -2));
        for (var x = 3; x < 6; x++)
            Place(creation, "vanilla:metal_block", new GridCell(x, 0, 0));
        for (var i = 0; i < 2; i++)
            Link(creation, seat, Place(creation, "vanilla:switch", new GridCell(i + 3, 1, 0)));
        var third = Place(creation, "vanilla:switch", new GridCell(5, 1, 0));
        Assert.False(creation.TryLink(seat, third, out _));
    }

    [Theory]
    [InlineData(null, "blocks")]
    [InlineData("player", "players")]
    [InlineData("bot", "bots")]
    [InlineData("creation", "creations")]
    public void ASensorOnlySeesWhatItIsSetToDetect(string? kind, string setting)
    {
        var creation = Board();
        var sensor = Place(creation, "vanilla:sensor", new GridCell(0, 1, 0));
        _physics.WallAt = 2f;
        _physics.WallKind = kind;
        Steps(3);
        Assert.Equal(1f, _runtime.Signals.Output(Car, sensor));
        Configure(creation, sensor, setting, false);
        Steps(3);
        Assert.Equal(0f, _runtime.Signals.Output(Car, sensor));
    }

    [Fact]
    public void ARepeatTimerBlinksWhileItsInputIsOn()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(1, 1, 0));
        var timer = Place(creation, "vanilla:timer", new GridCell(0, 1, 0));
        Link(creation, toggle, timer);
        Configure(creation, timer, "delay", 0.5);
        Configure(creation, timer, "mode", "repeat");
        Steps(1);
        Activate(creation, toggle);
        Steps(20);
        Assert.Equal(0f, _runtime.Signals.Output(Car, timer));
        Steps(15);
        Assert.Equal(1f, _runtime.Signals.Output(Car, timer));
        Steps(30);
        Assert.Equal(0f, _runtime.Signals.Output(Car, timer));
        Steps(30);
        Assert.Equal(1f, _runtime.Signals.Output(Car, timer));
        Activate(creation, toggle);
        Steps(3);
        Assert.Equal(0f, _runtime.Signals.Output(Car, timer));
        Steps(60);
        Assert.Equal(0f, _runtime.Signals.Output(Car, timer));
    }

    [Fact]
    public void SwitchesAndButtonsShowWhetherTheyArePressed()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(1, 1, 0));
        var press = Place(creation, "vanilla:button", new GridCell(-1, 1, 0));
        Steps(1);
        Assert.Equal(0f, _runtime.Readouts.Number(Car, toggle, "on"));
        Assert.Equal(0f, _runtime.Readouts.Number(Car, press, "on"));
        Activate(creation, toggle);
        Activate(creation, press);
        Steps(1);
        Assert.Equal(1f, _runtime.Readouts.Number(Car, toggle, "on"));
        Assert.Equal(1f, _runtime.Readouts.Number(Car, press, "on"));
        // The button plays its model's press clip going down and its release clip coming back up.
        Assert.Equal("press", _runtime.Readouts.Text(Car, press, "clip"));
        Steps(31);
        Assert.Equal(1f, _runtime.Readouts.Number(Car, press, "on"));
        _runtime.Release(Peer, creation, press);
        Steps(1);
        Assert.Equal(0f, _runtime.Readouts.Number(Car, press, "on"));
        Assert.Equal("release", _runtime.Readouts.Text(Car, press, "clip"));
    }

    [Fact]
    public void AnEngineShowsItsFuelSpeedAndTorqueAndLightsItsWheels()
    {
        _runtime.Creative = false;
        var (creation, seat, engine, wheels, steering) = BuildCar(linked: 2);
        var tank = Tank(creation);
        creation.InventoryOf(tank)!.Add("vanilla:gasoline", 3);
        _runtime.Seats!.Sit(Peer, Car, seat);
        _input = new PlayerInput(1, 1, 0, 0, false, false);
        Configure(creation, engine, "on", true);
        Steps(60);                                    // the start is 0.8 s
        var readouts = _runtime.Readouts;
        Assert.Equal(1f, readouts.Number(Car, engine, "running"));
        Assert.Equal(2f, readouts.Number(Car, engine, "fuel_items"));
        Assert.InRange(readouts.Number(Car, engine, "fuel")!.Value, 0.9f, 1f);
        Assert.Equal(216f, readouts.Number(Car, engine, "torque"));      // power 3 of 5 of 360 N·m
        Assert.True(readouts.Has(Car, engine, "rpm"));
        Assert.Equal(239f, readouts.Number(Car, engine, "top_rpm"));   // every power step turns 25 rad/s
        foreach (var (bearing, side) in wheels.Take(2))
            Assert.InRange(-side * readouts.Number(Car, bearing, "drive")!.Value, 25f * 0.97f, 25f * 1.03f);
        Assert.Equal(1f, readouts.Number(Car, steering, "drive"));

        _input = null;
        Configure(creation, engine, "on", false);
        Steps(60);
        Assert.Equal(0f, readouts.Number(Car, engine, "running"));
        Assert.Equal(0f, readouts.Number(Car, engine, "torque"));
        Assert.Equal(0f, readouts.Number(Car, engine, "top_rpm"));
        foreach (var (bearing, _) in wheels.Take(2))
            Assert.False(readouts.Has(Car, bearing, "drive"));
        Assert.False(readouts.Has(Car, steering, "drive"));
    }

    [Fact]
    public void AnEngineWithNothingLinkedInStaysOffWhileItIsOn()
    {
        var (creation, seat, engine, wheels, _) = BuildCar("vanilla:electric_engine", level: 1);
        Assert.True(creation.Unlink(seat, engine));
        Configure(creation, engine, "on", true);
        Configure(creation, engine, "gear", 5);
        Steps(120);
        // Placed with no seat, switch or button linked in, it never starts.
        Assert.StartsWith("off_loop", _runtime.Readouts.Text(Car, engine, "clip"));
        Assert.Equal(0f, _runtime.Readouts.Number(Car, engine, "running"));
        Assert.Equal(0f, _runtime.Signals.Output(Car, engine));
        Assert.DoesNotContain(_world.Motors, m => wheels.Any(w => w.Bearing == m.Part) && m.Velocity is not (null or 0f));
    }

    [Fact]
    public void AControllerDrivesBearingsAndPistonsThroughItsSequence()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(-1, 1, 0));
        var controller = Place(creation, "vanilla:controller", new GridCell(0, 1, 0));
        var arm = Bearing(creation, new GridCell(3, 0, 0), new GridCell(1, 0, 0));
        Place(creation, "vanilla:metal_block", new GridCell(4, 0, 0));
        var piston = Place(creation, "vanilla:piston", new GridCell(2, 1, 0));
        Place(creation, "vanilla:metal_block", new GridCell(2, 2, 0));
        Link(creation, toggle, controller);
        Link(creation, controller, arm);
        Link(creation, controller, piston);
        Configure(creation, controller, "frames", new object[]
        {
            new object[] { new { part = arm, target = 45, speed = 2 }, new { part = piston, target = 0.5, speed = 0.25 } },
            new object[] { new { part = arm, target = -30, speed = 1 } },
        });
        Configure(creation, controller, "frame_time", 0.5);
        Steps(1);
        Activate(creation, toggle);
        Steps(3);
        Assert.Equal(MathF.PI / 4, LastMotor(arm).Target!.Value, 4);
        Assert.Equal(2f, LastMotor(arm).Velocity);
        // The controller's target wins over the piston pushing out on its own input.
        Assert.Equal(0.5f, LastMotor(piston).Target!.Value, 4);
        Assert.Equal(0.25f, LastMotor(piston).Velocity);
        Steps(31);
        Assert.Equal(-MathF.PI / 6, LastMotor(arm).Target!.Value, 4);
        Assert.Equal(0.5f, LastMotor(piston).Target!.Value, 4);
    }

    [Fact]
    public void AControllerPreviewsFramesThatAreNotAppliedYet()
    {
        var creation = Board();
        var controller = Place(creation, "vanilla:controller", new GridCell(0, 1, 0));
        var arm = Bearing(creation, new GridCell(3, 0, 0), new GridCell(1, 0, 0));
        Place(creation, "vanilla:metal_block", new GridCell(4, 0, 0));
        Link(creation, controller, arm);
        Configure(creation, controller, "frame_time", 0.5);
        Steps(1);
        var caller = new Core.Actions.ActionCaller(Peer, Core.Actions.ActionSource.Player);
        Assert.Null(_runtime.CheckCommand(creation, controller));
        var draft = new { playing = true, frames = new[] { new[] { new { part = arm, target = 60, speed = 1 } }, new[] { new { part = arm, target = 0, speed = 1 } } } };
        _runtime.Command(caller, creation, controller, "preview", JsonSerializer.SerializeToElement(draft));
        Steps(2);
        Assert.Equal(MathF.PI / 3, LastMotor(arm).Target!.Value, 4);
        Steps(31);
        Assert.Equal(0f, LastMotor(arm).Target!.Value, 4);
        // It loops while it previews, and the controller's output stays off.
        Steps(31);
        Assert.Equal(MathF.PI / 3, LastMotor(arm).Target!.Value, 4);
        Assert.Equal(0f, _runtime.Signals.Output(Car, controller));
        _runtime.Command(caller, creation, controller, "preview", JsonSerializer.SerializeToElement(new { playing = false }));
        var motors = _world.Motors.Count;
        Steps(62);
        Assert.Equal(motors, _world.Motors.Count);
    }

    // A metal chassis along Z (z 0..4) with a driver's seat and a gas engine (upgraded `level` times) on top, both
    // facing -Z. The front (-Z) wheels hang off an axle under a steering bearing at z 0; the rear ones off a post at z 4.
    private (Creation Creation, int Seat, int Engine, List<(int Bearing, int Side)> Wheels, int Steering) BuildCar(
        string engineId = "vanilla:gas_engine", int linked = 4, int level = 0)
    {
        var creation = NewCreation();
        for (var z = 1; z <= 4; z++)
            Place(creation, "vanilla:metal_block", new GridCell(0, 0, z));
        var seat = Place(creation, "vanilla:drivers_seat", new GridCell(0, 1, 1));
        var engine = Place(creation, engineId, new GridCell(0, 1, 3));
        if (level > 0)
            Assert.True(creation.SetLevel(engine, level));
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
        foreach (var (bearing, _) in wheels.Take(linked))
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

    // A Gas Container (or a Battery Container, the same 4 x 3 x 3 cells) behind the car, standing on blocks of its own.
    private static int Tank(Creation creation, string part = "vanilla:gas_container")
    {
        foreach (var z in new[] { -1, -2 })
            if (creation.PartAt(new GridCell(0, 0, z)) is null)
                Place(creation, "vanilla:metal_block", new GridCell(0, 0, z));
        return Place(creation, part, new GridCell(-1, 1, -3));
    }

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

        /// <summary>What the wall is: null for the ground, else "player", "bot" or "creation".</summary>
        public string? WallKind { get; set; }

        public (System.Numerics.Vector3 From, System.Numerics.Vector3 To)? LastRay { get; private set; }

        public System.Numerics.Vector3 Gravity => new(0, -9.81f, 0);

        public ScriptRayHit? Raycast(System.Numerics.Vector3 from, System.Numerics.Vector3 to, int? ignoreCreation)
        {
            LastRay = (from, to);
            if (WallAt is not { } y || to.Y < y || from.Y > y)
                return null;
            return new ScriptRayHit(new System.Numerics.Vector3(from.X, y, from.Z), -System.Numerics.Vector3.UnitY, y - from.Y,
                WallKind == "creation" ? 9 : null, WallKind == "creation" ? 1 : null, WallKind == "player" ? 3 : null, WallKind == "bot" ? 5 : null);
        }

        public IReadOnlyList<ScriptOverlap> OverlapSphere(System.Numerics.Vector3 centre, float radius, int? ignoreCreation) => [];
    }

    /// <summary>Keeps the sound loops scripts start, so a test can tell what still hums.</summary>
    private sealed class LoopSound : IScriptSound
    {
        private int _next = 1;

        public Dictionary<int, string> Loops { get; } = [];

        public string? Play(string sound, string ownerModId, Vector3 position) => null;

        public int StartLoop(string sound, string ownerModId, int creation, int part, IReadOnlyDictionary<string, float>? parameters, out string? error)
        {
            error = null;
            Loops[_next] = sound;
            return _next++;
        }

        public string? SetLoopParameter(int handle, string name, float value) => Loops.ContainsKey(handle) ? null : "the loop has stopped";

        public void StopLoop(int handle) => Loops.Remove(handle);
    }
}
