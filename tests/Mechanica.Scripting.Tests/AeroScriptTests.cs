using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Core.Motion;
using Mechanica.Core.Player;

namespace Mechanica.Scripting.Tests;

/// <summary>
/// The vanilla aero and propulsion scripts (chunk 11) on the real vanilla parts: the thruster's push, pilot
/// controls and fuel, the wing's lift, drag and stall, the control surface's deflection, the rotor's spool,
/// thrust and counter-torque, and the hover jet's levelling. Also the Aero Tutorial's example mods
/// (<c>examples/mods/example_bearing</c> and <c>example_thruster</c>), loaded beside vanilla.
/// </summary>
public sealed class AeroScriptTests : IDisposable
{
    private const int Craft = 1, Peer = 7;
    private const float Rho = 1.225f;
    private static readonly string Examples = Path.Combine(ItemRig.VanillaRoot, "..", "..", "examples", "mods");
    private static readonly ModLoadResult Vanilla = ModLoader.Load(
        [ItemRig.VanillaRoot, Path.Combine(Examples, "example_bearing"), Path.Combine(Examples, "example_thruster")]);

    private readonly ScriptRig.FakeWorld _world = new();
    private readonly ScriptRuntime _runtime;
    private PlayerInput? _input;

    public AeroScriptTests()
    {
        Assert.False(Vanilla.HasErrors, string.Join("; ", Vanilla.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var dispatcher = new Core.Actions.ActionDispatcher();
        new MotionActions(new MotionSettings(), _world).Register(dispatcher);
        _runtime = new ScriptRuntime(new ScriptSettings(), dispatcher, _world, new NoPhysics(), new Source())
        {
            Seats = new SeatRegistry(),
            OccupantInput = _ => _input,
            Creative = true,
        };
        _runtime.Message += m => Assert.NotEqual(ScriptMessageKind.Error, m.Kind);
    }

    public void Dispose() => _runtime.Clear();

    [Fact]
    public void AThrusterPushesTowardItsBaseWhileItsSwitchIsOn()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(-1, 1, 0));
        var thruster = Place(creation, "vanilla:thruster", new GridCell(0, 1, 0));
        Link(creation, toggle, thruster);
        Steps(2);
        Assert.Null(Last(thruster));

        Activate(creation, toggle);
        Steps(3);
        var push = Last(thruster)!.Value;
        // Its exhaust is its +Y face, so standing on the board it pushes the board down, at the thruster.
        AssertNear(new Vector3(0, -200, 0), push.Force);
        AssertNear(Centre(creation, thruster), push.At!.Value);
        Assert.Equal(1f, _runtime.Signals.Output(Craft, thruster));
    }

    [Fact]
    public void AThrottleLeverMovesWithSpaceAndShiftAndStaysPut()
    {
        var creation = Board();
        var seat = Place(creation, "vanilla:drivers_seat", new GridCell(-2, 1, 0));
        var thruster = Place(creation, "vanilla:thruster", new GridCell(0, 1, 0));
        Link(creation, seat, thruster);
        Configure(creation, thruster, "control", "space_shift");
        _runtime.Seats!.Sit(Peer, Craft, seat);

        // properties.lever_rate is 0.5 a second: a second of Space opens it halfway.
        _input = new PlayerInput(0, 0, 0, 0, true, false);
        Steps(60);
        _input = PlayerInput.None;
        Steps(30);
        AssertNear(new Vector3(0, -100, 0), Last(thruster)!.Value.Force, 4f);
        _input = new PlayerInput(0, 0, 0, 0, false, true);
        Steps(30);
        AssertNear(new Vector3(0, -50, 0), Last(thruster)!.Value.Force, 4f);

        // An empty seat leaves the lever where it was.
        _runtime.Seats.Leave(Peer);
        Steps(30);
        AssertNear(new Vector3(0, -50, 0), Last(thruster)!.Value.Force, 4f);
    }

    [Fact]
    public void AThrusterOnWAndSFiresOnlyForward()
    {
        var creation = Board();
        var seat = Place(creation, "vanilla:drivers_seat", new GridCell(-2, 1, 0));
        var thruster = Place(creation, "vanilla:thruster", new GridCell(0, 1, 0));
        Link(creation, seat, thruster);
        Configure(creation, thruster, "control", "w_s");
        _runtime.Seats!.Sit(Peer, Craft, seat);

        _input = new PlayerInput(0, -1, 0, 0, false, false);
        Steps(3);
        Assert.Null(Last(thruster));
        Configure(creation, thruster, "invert", true);
        Steps(3);
        AssertNear(new Vector3(0, -200, 0), Last(thruster)!.Value.Force);
    }

    [Fact]
    public void AThrusterBurnsGasolineOutsideCreative()
    {
        _runtime.Creative = false;
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(-1, 1, 0));
        var thruster = Place(creation, "vanilla:thruster", new GridCell(0, 1, 0));
        var tank = Place(creation, "vanilla:gas_container", new GridCell(2, 1, 0));
        Link(creation, toggle, thruster);
        Activate(creation, toggle);
        Steps(3);
        Assert.Null(Last(thruster));

        creation.InventoryOf(tank)!.Add("vanilla:gasoline", 1);
        Steps(3);
        Assert.Equal(0, creation.InventoryOf(tank)!.Count("vanilla:gasoline"));
        Assert.NotNull(Last(thruster));
        // properties.fuel_time is 112 s of full power per gasoline (spec: 0.5 fuel a second).
        Steps(112 * 60 + 5);
        Assert.Null(Last(thruster));
        Assert.Equal(0f, _runtime.Signals.Output(Craft, thruster));
    }

    [Fact]
    public void AWingLiftsSquareToTheAirAndDragsAlongIt()
    {
        var (creation, wing) = Glider();
        // Flying along -Z at 30 m/s and sinking at 3 m/s: the air meets the wing from below.
        var velocity = new Vector3(0, -3, -30);
        creation.SetBodyVelocity(0, new BodyVelocity(velocity, Vector3.Zero));
        Steps(2);

        var force = Last(wing)!.Value.Force;
        var flow = -Vector3.Normalize(velocity);
        var alpha = MathF.Asin(flow.Y);
        var q = 0.5f * Rho * velocity.LengthSquared() * 1f;
        // camber_lift 0.4 plus lift_slope 4.5 a radian.
        var cl = 0.4f + 4.5f * alpha;
        AssertNear(q * (0.15f + 0.05f * cl * cl), Vector3.Dot(force, flow), 0.5f);
        var lift = force - Vector3.Dot(force, flow) * flow;
        Assert.Equal(q * cl, lift.Length(), 0.5f);
        Assert.True(lift.Y > 0 && lift.Z < 0, $"lift leans up and forward ({lift})");
        AssertNear(Centre(creation, wing), Last(wing)!.Value.At!.Value);
    }

    [Fact]
    public void AWingStallsPastItsMaxLift()
    {
        var (creation, wing) = Glider();
        float Lift(float degrees)
        {
            var a = degrees * MathF.PI / 180;
            var velocity = new Vector3(0, -MathF.Sin(a), -MathF.Cos(a)) * 30;
            creation.SetBodyVelocity(0, new BodyVelocity(velocity, Vector3.Zero));
            Steps(1);
            var force = Last(wing)!.Value.Force;
            var flow = -Vector3.Normalize(velocity);
            return (force - Vector3.Dot(force, flow) * flow).Length() / (0.5f * Rho * 900);
        }
        // The lift coefficient starts at camber_lift 0.4 and grows by lift_slope 4.5 a radian, to max_lift 1.2
        // at about 10°; past it the wing is a flat plate with stall_lift × sin 2α.
        Assert.Equal(0.4f, Lift(0), 0.005f);
        Assert.Equal(0.4f + 4.5f * 8 * MathF.PI / 180, Lift(8), 0.01f);
        Assert.Equal(0.9f * MathF.Sin(60 * MathF.PI / 180), Lift(30), 0.01f);
        Assert.True(Lift(12) < Lift(9), "lift drops past the stall");
    }

    [Fact]
    public void AWingAtRestDoesNothing()
    {
        var (_, wing) = Glider();
        Steps(3);
        Assert.Null(Last(wing));
    }

    [Theory]
    [InlineData(1f, false, 1f)]
    [InlineData(-1f, false, -1f)]
    [InlineData(1f, true, -1f)]
    [InlineData(0f, false, 0f)]
    public void AControlSurfaceFollowsItsPilot(float throttle, bool invert, float sign)
    {
        var creation = NewCreation();
        var seat = Place(creation, "vanilla:drivers_seat", new GridCell(0, 1, 0));
        var surface = Place(creation, "vanilla:control_surface", new GridCell(1, 0, 0));
        Link(creation, seat, surface);
        if (invert)
            Configure(creation, surface, "invert", true);
        _runtime.Seats!.Sit(Peer, Craft, seat);
        _input = new PlayerInput(0, throttle, 0, 0, false, false);
        creation.SetBodyVelocity(0, new BodyVelocity(new Vector3(0, 0, -30), Vector3.Zero));
        Steps(2);

        // Level flight, so all its lift comes from the deflection: up to 10° on 0.5 m².
        var lift = Last(surface)!.Value.Force.Y;
        var expected = sign * 0.5f * Rho * 900 * 0.5f * 4.5f * 10 * MathF.PI / 180;
        Assert.True(MathF.Abs(expected - lift) < 1f, $"lift {lift}, expected {expected}");
    }

    [Theory]
    [InlineData("clockwise", 1f)]
    [InlineData("counterclockwise", -1f)]
    public void ARotorSpoolsUpPullsAndTwistsAgainstItsSpin(string spin, float sign)
    {
        var creation = NewCreation();
        Place(creation, "vanilla:metal_block", new GridCell(-1, 0, 0));
        Place(creation, "vanilla:metal_block", new GridCell(-2, 0, 0));
        var toggle = Place(creation, "vanilla:switch", new GridCell(-2, 1, 0));
        // 3 by 3 cells with its hub over the block at the origin.
        var rotor = Place(creation, "vanilla:rotor", new GridCell(-1, 1, -1));
        Link(creation, toggle, rotor);
        Configure(creation, rotor, "spin", spin);
        Activate(creation, toggle);

        // properties.spool_rate is 0.8 a second: half a second makes 40% speed (the switch's output reaches
        // the rotor a step later).
        Steps(31);
        var push = Last(rotor)!.Value;
        AssertNear(new Vector3(0, 150 * 0.4f, 0), push.Force, 1f);
        AssertNear(new Vector3(0, sign * 40 * 0.4f, 0), push.Torque, 0.5f);
        Steps(60);
        AssertNear(new Vector3(0, 150, 0), Last(rotor)!.Value.Force, 0.5f);

        // Off: it spools down the same way and then stops pushing.
        Activate(creation, toggle);
        Steps(31);
        AssertNear(new Vector3(0, 150 * 0.6f, 0), Last(rotor)!.Value.Force, 1f);
        Steps(60);
        Assert.Null(Last(rotor));
    }

    [Theory]
    [InlineData(5f, 0.5f)]
    [InlineData(12f, 0f)]
    [InlineData(-2f, 1.2f)]
    [InlineData(-10f, 1.3f)]
    public void ARotorPullsLessClimbingAndMoreSinking(float climb, float gain)
    {
        var creation = NewCreation();
        Place(creation, "vanilla:metal_block", new GridCell(-1, 0, 0));
        Place(creation, "vanilla:metal_block", new GridCell(-2, 0, 0));
        var toggle = Place(creation, "vanilla:switch", new GridCell(-2, 1, 0));
        var rotor = Place(creation, "vanilla:rotor", new GridCell(-1, 1, -1));
        Link(creation, toggle, rotor);
        Activate(creation, toggle);
        Steps(120);
        AssertNear(new Vector3(0, 150, 0), Last(rotor)!.Value.Force, 0.5f);

        // properties.inflow_speed is 10 m/s, up to properties.max_inflow_gain 1.3 times the thrust.
        creation.SetBodyVelocity(0, new BodyVelocity(new Vector3(0, climb, 0), Vector3.Zero));
        Steps(2);
        AssertNear(new Vector3(0, 150 * gain, 0), Last(rotor)?.Force ?? Vector3.Zero, 0.5f);
    }

    [Fact]
    public void TheExampleMotorBearingSpinsWithItsInput()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(-1, 1, 0));
        var bearing = Place(creation, "example_bearing:motor_bearing", new GridCell(0, 1, 0));
        Place(creation, "vanilla:metal_block", new GridCell(0, 2, 0));
        Link(creation, toggle, bearing);
        Activate(creation, toggle);
        Steps(3);
        // properties.speed is 2 rad/s per unit of input, with at most properties.torque 400 N·m.
        Assert.Equal(new JointMotor(2, 400), _world.JointMotors[^1]);

        Activate(creation, toggle);
        Steps(3);
        Assert.True(_world.JointMotors[^1].IsFree);
    }

    [Fact]
    public void TheExampleToyThrusterPushesByItsInputUpToItsCap()
    {
        var creation = Board();
        var switches = new[] { -1, -2, 1 }.Select(x => Place(creation, "vanilla:switch", new GridCell(x, 1, 0))).ToList();
        var thruster = Place(creation, "example_thruster:toy_thruster", new GridCell(0, 1, 0));
        foreach (var toggle in switches)
            Link(creation, toggle, thruster);
        Steps(2);
        Assert.Null(Last(thruster));

        // 50 N toward its base per unit of input, at most properties.max_power 2 units.
        Activate(creation, switches[0]);
        Steps(3);
        AssertNear(new Vector3(0, -50, 0), Last(thruster)!.Value.Force);
        Activate(creation, switches[1]);
        Activate(creation, switches[2]);
        Steps(3);
        AssertNear(new Vector3(0, -100, 0), Last(thruster)!.Value.Force);
    }

    [Fact]
    public void AHoverJetLiftsAndLevelsItsBody()
    {
        var creation = Board();
        var toggle = Place(creation, "vanilla:switch", new GridCell(-2, 1, 0));
        var jet = Place(creation, "vanilla:hover_jet", new GridCell(0, 1, 0));
        Link(creation, toggle, jet);
        Activate(creation, toggle);
        // Upside down and tipped 0.2 rad about X: the jet points down-ish, so it lifts, and levels the tip.
        creation.SetAllBodyPoses(new Pose(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI + 0.2f)));
        Steps(2);

        var push = Last(jet)!.Value;
        var down = Vector3.Transform(-Vector3.UnitY, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI + 0.2f));
        AssertNear(down * 300, push.Force);
        // level_torque 300 N·m a radian, turning the jet's push back toward the sky.
        AssertNear(new Vector3(-300 * 0.2f, 0, 0), push.Torque, 0.5f);

        Configure(creation, jet, "stabilize", false);
        Steps(2);
        AssertNear(Vector3.Zero, Last(jet)!.Value.Torque);
    }

    [Fact]
    public void ALinkedSeatsPilotIsReadable()
    {
        var creation = Board();
        var seat = Place(creation, "vanilla:drivers_seat", new GridCell(-2, 1, 0));
        var rotor = Place(creation, "vanilla:hover_jet", new GridCell(0, 1, 0));
        Link(creation, seat, rotor);
        Configure(creation, rotor, "control", "a_d");
        Steps(2);
        Assert.Null(Last(rotor));
        _runtime.Seats!.Sit(Peer, Craft, seat);
        _input = new PlayerInput(0.5f, 0, 0, 0, false, false);
        Steps(2);
        AssertNear(new Vector3(0, -150, 0), Last(rotor)!.Value.Force);
    }

    // A wing by its root on a block, level, at 100 m.
    private (Creation Creation, int Wing) Glider()
    {
        var creation = NewCreation();
        var wing = Place(creation, "vanilla:wing", new GridCell(1, 0, -1));
        Assert.Single(creation.Bodies);
        creation.SetAllBodyPoses(new Pose(new Vector3(0, 100, 0), Quaternion.Identity));
        return (creation, wing);
    }

    // A row of metal blocks along X (x -2 to 2).
    private Creation Board()
    {
        var creation = NewCreation();
        foreach (var x in new[] { -1, -2, 1, 2 })
            Place(creation, "vanilla:metal_block", new GridCell(x, 0, 0));
        return creation;
    }

    private Creation NewCreation()
    {
        var creation = new Creation(Craft, Vanilla);
        Place(creation, "vanilla:metal_block", GridCell.Zero);
        _world.Creations[Craft] = creation;
        return creation;
    }

    // The part's push in the last step, or null when it did not push.
    private (Vector3 Force, Vector3? At, Vector3 Torque)? Last(int part)
    {
        var tick = _pushedAt.GetValueOrDefault(part, -1);
        if (tick != _steps)
            return null;
        var push = _world.Applied.Last(a => a.Part == part);
        return (push.Force, push.At, push.Torque);
    }

    private readonly Dictionary<int, int> _pushedAt = [];
    private int _steps;

    private void Steps(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var before = _world.Applied.Count;
            _runtime.Update(1f / 60);
            _steps++;
            foreach (var push in _world.Applied.Skip(before))
            {
                if (push.Part is { } part)
                    _pushedAt[part] = _steps;
            }
        }
    }

    private static Vector3 Centre(Creation creation, int part)
    {
        var p = creation.FindPart(part)!;
        return creation.BodyPose(creation.BodyIndexOf(part)).Apply(PartGeometry.CentreMetres(creation.DefinitionOf(part), p.Cell, p.Rotation).ToNumerics());
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 0.05f) =>
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");

    private static void AssertNear(float expected, float actual, float tolerance) =>
        Assert.True(MathF.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    private static int Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        Assert.True(creation.TryPlacePart(part, cell, rotation, out var placed, out var error), $"{part} at {cell}: {error}");
        return placed.Id;
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
        Assert.Null(_runtime.CheckActivate(creation, part));
        _runtime.Activate(new Core.Actions.ActionCaller(Peer, Core.Actions.ActionSource.Player), creation, part);
    }

    private sealed class Source : IScriptSource
    {
        public ScriptFile? FindScript(string partId)
        {
            var id = Identifiers.Qualify(partId);
            if (Vanilla.FindDefinition(id)?.Script is not { } script)
                return null;
            var mod = id[..id.IndexOf(':')];
            var root = mod == "vanilla" ? ItemRig.VanillaRoot : Path.Combine(Examples, mod);
            return new ScriptFile($"{mod}:{script}", mod, Path.Combine(root, script));
        }
    }
}
