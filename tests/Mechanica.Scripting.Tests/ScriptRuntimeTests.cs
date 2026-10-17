using System.Numerics;
using System.Text.Json.Nodes;
using Mechanica.Core.Creations;
using static Mechanica.Scripting.Tests.ScriptRig;

namespace Mechanica.Scripting.Tests;

public sealed class ScriptRuntimeTests : IDisposable
{
    private readonly ScriptRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void HooksRunWithPerPartGlobals()
    {
        _rig.Write("scripts/thruster.lua", """
            count = 0
            function on_create() print("created " .. part.id()) end
            function on_update(dt) count = count + 1; state.seconds = (state.seconds or 0) + dt end
            function get_outputs() return count, state.seconds end
            """);
        var a = _rig.AddCreation(1, Thruster);
        var b = _rig.AddCreation(2, Thruster, Thruster);

        _rig.Runtime.Update(0.5f);
        _rig.Runtime.Update(0.5f);

        Assert.Empty(_rig.Errors);
        Assert.Equal(3, _rig.Runtime.Instances.Count);
        Assert.Equal(3, _rig.Prints.Count(p => p.StartsWith("created ", StringComparison.Ordinal)));
        var outputs = _rig.Runtime.Invoke(2, PartOf(b, Thruster), ScriptRuntime.Hooks.GetOutputs);
        Assert.True(outputs.Ok, outputs.Error);
        Assert.Equal(2.0, outputs.Values[0]!.GetValue<double>());
        Assert.Equal(1.0, outputs.Values[1]!.GetValue<double>());
        Assert.Equal(2.0, _rig.Runtime.Invoke(1, PartOf(a, Thruster), ScriptRuntime.Hooks.GetOutputs).Values[0]!.GetValue<double>());
    }

    [Fact]
    public void HotReloadKeepsStateAndRunsTheNewCode()
    {
        _rig.Write("scripts/thruster.lua", """
            function on_create() state.created = (state.created or 0) + 1 end
            function on_update(dt) state.n = (state.n or 0) + 1 end
            """);
        var creation = _rig.AddCreation(1, Thruster);
        var id = PartOf(creation, Thruster);
        _rig.Runtime.Update(0.1f);
        _rig.Runtime.Update(0.1f);

        _rig.Write("scripts/thruster.lua", """
            function on_reload() state.reloaded = true end
            function on_update(dt) state.n = state.n + 100 end
            """);
        var reloads = _rig.Runtime.CheckForChanges();
        _rig.Runtime.Update(0.1f);

        Assert.True(Assert.Single(reloads).Ok);
        var state = _rig.Runtime.ExportState(1, id)!.AsObject();
        Assert.Equal(102.0, state["n"]!.GetValue<double>());
        Assert.Equal(1.0, state["created"]!.GetValue<double>());
        Assert.True(state["reloaded"]!.GetValue<bool>());
        Assert.Equal(2, Assert.Single(_rig.Runtime.Instances).Version);
        Assert.Empty(_rig.Runtime.CheckForChanges());
    }

    [Fact]
    public void ABrokenReloadKeepsTheOldCodeRunning()
    {
        _rig.Write("scripts/thruster.lua", "function on_update(dt) state.n = (state.n or 0) + 1 end");
        var creation = _rig.AddCreation(1, Thruster);
        _rig.Runtime.Update(0.1f);

        _rig.Write("scripts/thruster.lua", "function on_update(dt) state.n = = 2 end");
        var result = _rig.Runtime.Reload("test:scripts/thruster.lua");
        _rig.Runtime.Update(0.1f);

        Assert.False(result.Ok);
        Assert.Contains("test:scripts/thruster.lua:(1,", result.Error);
        Assert.Contains(_rig.Errors, e => e.Text.Contains("previous version keeps running", StringComparison.Ordinal));
        Assert.Equal(2.0, _rig.Runtime.ExportState(1, PartOf(creation, Thruster))!["n"]!.GetValue<double>());
        Assert.Null(Assert.Single(_rig.Runtime.Instances).Fault);
    }

    [Fact]
    public void ARuntimeErrorStopsThePartUntilAFixedReload()
    {
        _rig.Write("scripts/thruster.lua", """
            function on_update(dt)
              state.n = (state.n or 0) + 1
              if state.n == 2 then local x = nil; x.boom = 1 end
            end
            """);
        var creation = _rig.AddCreation(1, Thruster);
        for (var i = 0; i < 4; i++)
            _rig.Runtime.Update(0.1f);

        var error = Assert.Single(_rig.Errors);
        Assert.Contains("test:scripts/thruster.lua:(3,", error.Text);
        Assert.Contains("in on_update", error.Text);
        Assert.Equal((1, PartOf(creation, Thruster)), (error.Creation, error.Part!.Value));
        Assert.NotNull(Assert.Single(_rig.Runtime.Instances).Fault);
        Assert.Equal(2.0, _rig.Runtime.ExportState(1, PartOf(creation, Thruster))!["n"]!.GetValue<double>());

        _rig.Write("scripts/thruster.lua", "function on_update(dt) state.n = state.n + 10 end");
        Assert.True(_rig.Runtime.Reload("test:scripts/thruster.lua").Ok);
        _rig.Runtime.Update(0.1f);

        Assert.Null(Assert.Single(_rig.Runtime.Instances).Fault);
        Assert.Equal(12.0, _rig.Runtime.ExportState(1, PartOf(creation, Thruster))!["n"]!.GetValue<double>());
    }

    [Fact]
    public void AnEndlessLoopIsStoppedAndOtherPartsKeepRunning()
    {
        using var rig = new ScriptRig(new ScriptSettings { InstructionBudget = 10_000 });
        rig.Write("scripts/thruster.lua", "function on_update(dt) while true do end end");
        rig.Write("scripts/gadget.lua", "function on_update(dt) state.n = (state.n or 0) + 1 end", mod: "other");
        rig.AddCreation(1, Thruster);
        var other = rig.AddCreation(2, Other);

        rig.Runtime.Update(0.1f);
        rig.Runtime.Update(0.1f);

        Assert.Contains("10000 Lua instructions", Assert.Single(rig.Errors).Text);
        Assert.Equal(2.0, rig.Runtime.ExportState(2, PartOf(other, Other))!["n"]!.GetValue<double>());
    }

    [Fact]
    public void YieldingOutOfAHookIsAnError()
    {
        _rig.Write("scripts/thruster.lua", """
            function on_update(dt) coroutine.yield(1) end
            function get_outputs()
              local co = coroutine.wrap(function() coroutine.yield(5) end)
              return co()
            end
            """);
        var creation = _rig.AddCreation(1, Thruster);
        Assert.Equal(5.0, _rig.Runtime.Invoke(1, PartOf(creation, Thruster), ScriptRuntime.Hooks.GetOutputs).Values[0]!.GetValue<double>());

        _rig.Runtime.Update(0.1f);

        Assert.Contains("yielded out of a hook", Assert.Single(_rig.Errors).Text);
    }

    [Theory]
    [InlineData("return io.open('x')")]
    [InlineData("return os.execute('true')")]
    [InlineData("return dofile('x.lua')")]
    [InlineData("return require('x')")]
    [InlineData("return load('return 1')()")]
    [InlineData("return string.rep('ab', 1e9)")]
    public void TheSandboxRefusesFilesProcessesAndHugeStrings(string body)
    {
        _rig.Write("scripts/thruster.lua", $"function get_outputs() {body} end");
        var creation = _rig.AddCreation(1, Thruster);

        var result = _rig.Runtime.Invoke(1, PartOf(creation, Thruster), ScriptRuntime.Hooks.GetOutputs);

        Assert.False(result.Ok);
        Assert.Single(_rig.Errors);
    }

    [Fact]
    public void ModsDoNotShareLibraries()
    {
        _rig.Write("scripts/gadget.lua", "string.upper = function() return 'hacked' end; _G.leak = 1", mod: "other");
        _rig.Write("scripts/thruster.lua", "function get_outputs() return string.upper('a'), leak end");
        var creation = _rig.AddCreation(1, Other, Thruster);

        var result = _rig.Runtime.Invoke(1, PartOf(creation, Thruster), ScriptRuntime.Hooks.GetOutputs);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("A", result.Values[0]!.GetValue<string>());
        Assert.Null(result.Values[1]);
    }

    [Fact]
    public void ForcesGoThroughTheActionApiAndAddUpPerPart()
    {
        _rig.Write("scripts/thruster.lua", """
            function on_update(dt)
              local up = part.to_world(vec3(0, 1, 0))
              assert(part.apply_force(up * properties.thrust))
              assert(part.apply_force(vec3(0, 10, 0)))
              assert(part.apply_torque({ 1, 2, 3 }))
            end
            """);
        var creation = _rig.AddCreation(1, Thruster, Thruster);
        creation.SetAllBodyPoses(new Pose(new Vector3(0, 5, 0), Quaternion.Identity));

        _rig.Runtime.Update(1 / 60f);

        Assert.Empty(_rig.Errors);
        // Each call sends the part's running total, which replaces its earlier push.
        var last = _rig.World.Pushes.GroupBy(p => p.Part).Select(g => g.Last()).ToList();
        Assert.Equal(2, last.Count);
        foreach (var push in last)
        {
            Assert.Equal(new Vector3(0, 60, 0), push.Force);
            Assert.Equal(new Vector3(1, 2, 3), push.Torque);
        }
    }

    [Fact]
    public void AnOffCentreForceTwistsTheBody()
    {
        _rig.Write("scripts/thruster.lua", """
            function on_update(dt) assert(part.apply_force(vec3(10, 0, 0), part.position() + vec3(0, 0, 2))) end
            """);
        var creation = _rig.AddCreation(1, Thruster);
        _rig.Runtime.Update(1 / 60f);

        var push = Assert.Single(_rig.World.Pushes);
        var com = Core.Motion.MotionActions.CentreOfMass(creation, 0);
        var at = PartKinematics.Of(creation, PartOf(creation, Thruster)).Position + new Vector3(0, 0, 2);
        var expected = Vector3.Cross(at - com, new Vector3(10, 0, 0));
        Assert.Equal(expected.X, push.Torque.X, 4);
        Assert.Equal(expected.Y, push.Torque.Y, 4);
        Assert.Equal(expected.Z, push.Torque.Z, 4);
    }

    [Fact]
    public void ARefusedForceReturnsTheReasonAndWarnsOnce()
    {
        _rig.Write("scripts/thruster.lua", """
            function on_update(dt)
              local ok, why = part.apply_force(vec3(1e9, 0, 0))
              state.ok, state.why = ok, why
            end
            """);
        var creation = _rig.AddCreation(1, Thruster);
        _rig.Runtime.Update(0.1f);
        _rig.Runtime.Update(0.1f);

        var state = _rig.Runtime.ExportState(1, PartOf(creation, Thruster))!;
        Assert.False(state["ok"]!.GetValue<bool>());
        Assert.Contains("most allowed", state["why"]!.GetValue<string>());
        Assert.Single(_rig.Messages, m => m.Kind == ScriptMessageKind.Warning);
        Assert.Empty(_rig.World.Pushes);
    }

    [Fact]
    public void ScriptsDriveTheirBearing()
    {
        _rig.Write("scripts/bearing.lua", "function on_create() assert(part.set_bearing(3, 50)) end function on_destroy() print('bye') end");
        var creation = _rig.AddCreation(1, Bearing, Block);
        var bearing = PartOf(creation, Bearing);

        _rig.Runtime.Update(0.1f);

        Assert.Empty(_rig.Errors);
        Assert.Equal([(bearing, (float?)3f, 50f)], _rig.World.Motors);
    }

    [Fact]
    public void ScriptsServoBearingsAndPistonsAndReadTheirJoints()
    {
        _rig.Write("scripts/bearing.lua", "function on_create() assert(part.set_bearing_angle(0.5, 2)) end");
        _rig.Write("scripts/piston.lua", """
            function on_create()
              assert(part.set_piston(0.6, 0.5, 900))
              local ok, why = part.set_piston(2)
              state.refused = why
              local j = part.joint()
              state.joint = { j.type, j.position, j.min, j.max, j.locked }
              state.none = creation.joint(1) == nil
            end
            function on_update(dt) state.freed = part.set_piston(nil) end
            """);
        var creation = _rig.AddCreation(1, Bearing, Block, Piston, Block);
        var bearing = PartOf(creation, Bearing);
        var piston = PartOf(creation, Piston);
        _rig.World.Positions[piston] = 0.25f;

        _rig.Runtime.Update(0.1f);

        Assert.Empty(_rig.Errors);
        var motors = _rig.World.JointMotors;
        Assert.Equal(new Mechanica.Core.Motion.JointMotor(2f, 200f, 0.5f), motors[0]);
        Assert.Equal(new Mechanica.Core.Motion.JointMotor(0.5f, 900f, 0.6f), motors[1]);
        Assert.True(motors[^1].IsFree);
        var state = _rig.Runtime.ExportState(1, piston)!;
        Assert.Contains("length must be from 0 to 1 m", state["refused"]!.GetValue<string>());
        Assert.Equal("""["linear",0.25,0,1,false]""", state["joint"]!.ToJsonString());
        Assert.True(state["none"]!.GetValue<bool>());
        Assert.True(state["freed"]!.GetValue<bool>());
        _ = bearing;
    }

    [Fact]
    public void PartsComeAndGoWithTheGraph()
    {
        _rig.Write("scripts/thruster.lua", "function on_destroy() print('destroyed ' .. part.id() .. ' ' .. tostring(part.position())) end");
        var creation = _rig.AddCreation(1, Thruster, Thruster);
        _rig.Runtime.Update(0.1f);
        Assert.Equal(2, _rig.Runtime.Instances.Count);

        var top = creation.Parts.Where(p => p.Part == Thruster).Max(p => p.Id);
        creation.RemovePart(top, () => 99);
        _rig.Runtime.Update(0.1f);
        Assert.Single(_rig.Runtime.Instances);
        Assert.Equal($"destroyed {top} nil", Assert.Single(_rig.Prints));

        _rig.World.Creations.Remove(1);
        _rig.Runtime.Update(0.1f);
        Assert.Empty(_rig.Runtime.Instances);
        Assert.Equal(2, _rig.Prints.Count());
    }

    [Fact]
    public void ParkedCreationsDoNotUpdate()
    {
        _rig.Write("scripts/thruster.lua", "function on_update(dt) state.n = (state.n or 0) + 1 end");
        var creation = _rig.AddCreation(1, Thruster);
        _rig.Runtime.Update(0.1f);
        _rig.World.Parked.Add(1);
        _rig.Runtime.Update(0.1f);

        Assert.Equal(1.0, _rig.Runtime.ExportState(1, PartOf(creation, Thruster))!["n"]!.GetValue<double>());
    }

    [Fact]
    public void PartStateReadsTheBodyPoseAndVelocity()
    {
        _rig.Write("scripts/thruster.lua", """
            function get_outputs()
              local p, v = part.position(), part.velocity()
              local forward = part.to_world(vec3(1, 0, 0))
              return { p.x, p.y, p.z }, { v.x, v.y, v.z }, { forward.x, forward.y, forward.z }, part.mass(), part.body_mass(), properties.name
            end
            """);
        var creation = _rig.AddCreation(1, Thruster);
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        creation.SetAllBodyPoses(new Pose(new Vector3(10, 0, 0), turn));
        creation.SetBodyVelocity(0, new BodyVelocity(new Vector3(1, 0, 0), new Vector3(0, 2, 0)));

        var result = _rig.Runtime.Invoke(1, PartOf(creation, Thruster), ScriptRuntime.Hooks.GetOutputs);

        Assert.True(result.Ok, result.Error);
        var expected = PartKinematics.Of(creation, PartOf(creation, Thruster));
        var p = result.Values[0]!.AsArray().Select(n => n!.GetValue<double>()).ToArray();
        Assert.Equal(expected.Position.X, p[0], 4);
        Assert.Equal(expected.Position.Y, p[1], 4);
        Assert.Equal(expected.Position.Z, p[2], 4);
        var v = result.Values[1]!.AsArray().Select(n => n!.GetValue<double>()).ToArray();
        Assert.Equal(expected.Velocity.X, v[0], 4);
        Assert.Equal(expected.Velocity.Z, v[2], 4);
        var forward = result.Values[2]!.AsArray().Select(n => n!.GetValue<double>()).ToArray();
        Assert.Equal(0, forward[0], 4);
        Assert.Equal(-1, forward[2], 4);
        Assert.Equal(2.0, result.Values[3]!.GetValue<double>());
        Assert.Equal(6.0, result.Values[4]!.GetValue<double>());
        Assert.Equal("jet", result.Values[5]!.GetValue<string>());
    }

    [Fact]
    public void RaycastsAskThePhysicsAndAreBounded()
    {
        var physics = new FakePhysics();
        using var rig = new ScriptRig(new ScriptSettings { MaxRayLength = 100 }, physics);
        rig.Write("scripts/thruster.lua", """
            function get_outputs()
              local hit = world.raycast(vec3(0, 10, 0), vec3(0, -10, 0))
              return hit.distance, hit.normal.y, hit.creation, world.gravity().y
            end
            function on_activate() world.raycast(vec3(0, 0, 0), vec3(0, 1000, 0)) end
            """);
        var creation = rig.AddCreation(4, Thruster);
        var id = PartOf(creation, Thruster);

        var result = rig.Runtime.Invoke(4, id, ScriptRuntime.Hooks.GetOutputs);

        Assert.True(result.Ok, result.Error);
        Assert.Equal([10.0, 1.0, 7.0, -9.81], result.Values.Select(v => Math.Round(v!.GetValue<double>(), 2)));
        Assert.Equal(4, physics.LastIgnore);
        Assert.False(rig.Runtime.Invoke(4, id, ScriptRuntime.Hooks.OnActivate).Ok);
        Assert.Contains("longer than 100 m", Assert.Single(rig.Errors).Text);
    }

    [Fact]
    public void StateRoundTripsThroughJson()
    {
        _rig.Write("scripts/thruster.lua", "function get_outputs() return state.list[2], state.nested.flag end function on_activate() state.f = print end");
        var creation = _rig.AddCreation(1, Thruster);
        var id = PartOf(creation, Thruster);

        Assert.True(_rig.Runtime.ImportState(1, id, JsonNode.Parse("""{ "list": [1, 2, 3], "nested": { "flag": true } }""")!.AsObject()));
        Assert.Equal("""{"list":[1,2,3],"nested":{"flag":true}}""", _rig.Runtime.ExportState(1, id)!.ToJsonString());
        var outputs = _rig.Runtime.Invoke(1, id, ScriptRuntime.Hooks.GetOutputs);
        Assert.Equal(2.0, outputs.Values[0]!.GetValue<double>());
        Assert.True(outputs.Values[1]!.GetValue<bool>());

        _rig.Runtime.Invoke(1, id, ScriptRuntime.Hooks.OnActivate);
        Assert.Throws<ArgumentException>(() => _rig.Runtime.ExportState(1, id));
    }

    [Fact]
    public void Vec3Maths()
    {
        _rig.Write("scripts/thruster.lua", """
            function get_outputs()
              local a, b = vec3(1, 2, 3), vec3(4, 5, 6)
              local c = a:cross(b)
              return a:dot(b), (a + b).z, (b - a).x, (2 * a).y, (a / 2).z, vec3(3, 4, 0):length(), c.x, tostring(-a), a == vec3(1, 2, 3), vec3.is(a)
            end
            """);
        var creation = _rig.AddCreation(1, Thruster);

        var result = _rig.Runtime.Invoke(1, PartOf(creation, Thruster), ScriptRuntime.Hooks.GetOutputs);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("32 9 3 4 1.5 5 -3 (-1, -2, -3) true true",
            string.Join(" ", result.Values.Select(v => v!.ToJsonString().Trim('"'))));
    }

    [Fact]
    public void AMissingScriptStopsOnlyItsParts()
    {
        _rig.AddCreation(1, Thruster);
        _rig.Runtime.Update(0.1f);

        Assert.Contains("cannot read", Assert.Single(_rig.Runtime.Instances).Fault);

        _rig.Write("scripts/thruster.lua", "function on_create() print('late') end");
        Assert.True(Assert.Single(_rig.Runtime.CheckForChanges()).Ok);
        Assert.Equal(["late"], _rig.Prints);
    }

    [Fact]
    public void SettingsLoadFromJsonAndReject()
    {
        var settings = ScriptSettings.FromJson("""{ "instruction_budget": 5000, /* per call */ }""");
        Assert.Equal(5000, settings.InstructionBudget);
        Assert.Throws<FormatException>(() => ScriptSettings.FromJson("""{ "instruction_budget": 5 }"""));
        Assert.Throws<FormatException>(() => ScriptSettings.FromJson("""{ "budget": 5000 }"""));
    }

    [Fact]
    public void ShippedSettingsAreValid()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "game", "data", "scripting.json");
        ScriptSettings.FromJson(File.ReadAllText(path));
    }

    private sealed class FakePhysics : IScriptPhysics
    {
        public int? LastIgnore { get; private set; }

        public Vector3 Gravity => new(0, -9.81f, 0);

        public ScriptRayHit? Raycast(Vector3 from, Vector3 to, int? ignoreCreation)
        {
            LastIgnore = ignoreCreation;
            return new ScriptRayHit(new Vector3(0, 0, 0), Vector3.UnitY, 10, 7, 3);
        }

        public IReadOnlyList<ScriptOverlap> OverlapSphere(Vector3 centre, float radius, int? ignoreCreation) => [];
    }
}
