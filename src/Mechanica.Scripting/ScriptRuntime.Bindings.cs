using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Motion;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

// The Lua API (docs/lua-api.md). The library tables are shared by every script of a mod and act on
// the part whose hook is running (_current), so instances cost no per-part closures.
public sealed partial class ScriptRuntime
{
    private LuaContext CreateContext(string modId)
    {
        var context = new LuaContext(modId, _settings, Print);
        var script = context.Script;
        var globals = script.Globals;
        var meta = context.Vec3Meta;
        DynValue Vec(Vector3 v) => LuaValues.Vector(script, meta, v);

        var part = new Table(script);
        Fn(part, "id", _ => DynValue.NewNumber(Current().Part));
        Fn(part, "type", _ => DynValue.NewString(Current().PartType));
        Fn(part, "creation", _ => DynValue.NewNumber(Current().Creation));
        Fn(part, "position", _ => Here() is { } h ? Vec(h.State.Position) : DynValue.Nil);
        Fn(part, "rotation", _ =>
        {
            if (Here() is not { } h)
                return DynValue.Nil;
            var q = new Table(script);
            q.Set("x", DynValue.NewNumber(h.State.Rotation.X));
            q.Set("y", DynValue.NewNumber(h.State.Rotation.Y));
            q.Set("z", DynValue.NewNumber(h.State.Rotation.Z));
            q.Set("w", DynValue.NewNumber(h.State.Rotation.W));
            return DynValue.NewTable(q);
        });
        Fn(part, "velocity", _ => Here() is { } h ? Vec(h.State.Velocity) : DynValue.Nil);
        Fn(part, "angular_velocity", _ => Here() is { } h ? Vec(h.State.AngularVelocity) : DynValue.Nil);
        Fn(part, "to_world", args => Here() is { } h
            ? Vec(Vector3.Transform(LuaValues.ToVector(args[0], "part.to_world", 1), h.State.Rotation))
            : DynValue.Nil);
        Fn(part, "to_local", args => Here() is { } h
            ? Vec(Vector3.Transform(LuaValues.ToVector(args[0], "part.to_local", 1), Quaternion.Inverse(h.State.Rotation)))
            : DynValue.Nil);
        Fn(part, "point_to_world", args => Here() is { } h
            ? Vec(h.State.Position + Vector3.Transform(LuaValues.ToVector(args[0], "part.point_to_world", 1), h.State.Rotation))
            : DynValue.Nil);
        Fn(part, "mass", _ => Here() is { } h && h.Creation.DefinitionOf(h.Instance.Part).Mass is { } m
            ? DynValue.NewNumber(m)
            : DynValue.Nil);
        Fn(part, "body_mass", _ => Here() is { } h ? DynValue.NewNumber(h.State.BodyMass) : DynValue.Nil);
        Fn(part, "socket", args =>
        {
            var id = args.AsType(0, "part.socket", DataType.String).String;
            if (Here() is not { } h || !h.Creation.TryGetSocketFrame(h.Instance.Part, id, out var position, out var normal))
                return DynValue.Nil;
            var pose = h.Creation.BodyPose(h.State.Body);
            return DynValue.NewTuple(Vec(pose.Apply(position.ToNumerics())), Vec(Vector3.Transform(normal.ToNumerics(), pose.Rotation)));
        });
        Fn(part, "apply_force", args =>
        {
            var force = LuaValues.ToVector(args[0], "part.apply_force", 1);
            Vector3? at = args.Count > 1 && !args[1].IsNil() ? LuaValues.ToVector(args[1], "part.apply_force", 2) : null;
            return Push(force, at, Vector3.Zero);
        });
        Fn(part, "apply_torque", args => Push(Vector3.Zero, null, LuaValues.ToVector(args[0], "part.apply_torque", 1)));
        Fn(part, "set_bearing", args =>
        {
            var instance = Current();
            return Bearing(instance, instance.Part, LuaValues.OptionalNumber(args[0], "part.set_bearing", 1),
                LuaValues.OptionalNumber(args[1], "part.set_bearing", 2));
        });
        globals.Set("part", DynValue.NewTable(part));

        var creation = new Table(script);
        Fn(creation, "id", _ => DynValue.NewNumber(Current().Creation));
        Fn(creation, "mass", _ => _motion.FindCreation(Current().Creation) is { } c
            ? DynValue.NewNumber(c.Bodies.Sum(b => b.Mass))
            : DynValue.Nil);
        Fn(creation, "parts", _ =>
        {
            var list = new Table(script);
            if (_motion.FindCreation(Current().Creation) is { } c)
            {
                var index = 1;
                foreach (var p in c.Parts.OrderBy(p => p.Id))
                {
                    var entry = new Table(script);
                    entry.Set("id", DynValue.NewNumber(p.Id));
                    entry.Set("type", DynValue.NewString(Identifiers.Qualify(p.Part)));
                    list.Set(index++, DynValue.NewTable(entry));
                }
            }
            return DynValue.NewTable(list);
        });
        Fn(creation, "set_bearing", args => Bearing(Current(),
            (int)LuaValues.Number(args[0], "creation.set_bearing", 1),
            LuaValues.OptionalNumber(args[1], "creation.set_bearing", 2),
            LuaValues.OptionalNumber(args[2], "creation.set_bearing", 3)));
        globals.Set("creation", DynValue.NewTable(creation));

        var world = new Table(script);
        Fn(world, "gravity", _ => Vec(_physics.Gravity));
        Fn(world, "time", _ => DynValue.NewNumber(Time));
        Fn(world, "tick", _ => DynValue.NewNumber(Tick));
        Fn(world, "raycast", args =>
        {
            var from = LuaValues.ToVector(args[0], "world.raycast", 1);
            var to = LuaValues.ToVector(args[1], "world.raycast", 2);
            if ((to - from).Length() > _settings.MaxRayLength)
                throw new ScriptRuntimeException($"world.raycast: the ray is longer than {_settings.MaxRayLength:0.#} m");
            if (_physics.Raycast(from, to, Current().Creation) is not { } hit)
                return DynValue.Nil;
            var result = new Table(script);
            result.Set("position", Vec(hit.Position));
            result.Set("normal", Vec(hit.Normal));
            result.Set("distance", DynValue.NewNumber(hit.Distance));
            if (hit.Creation is { } c)
                result.Set("creation", DynValue.NewNumber(c));
            if (hit.Part is { } p)
                result.Set("part", DynValue.NewNumber(p));
            return DynValue.NewTable(result);
        });
        Fn(world, "overlap_sphere", args =>
        {
            var centre = LuaValues.ToVector(args[0], "world.overlap_sphere", 1);
            var radius = LuaValues.Number(args[1], "world.overlap_sphere", 2);
            if (radius <= 0f || radius > _settings.MaxOverlapRadius)
                throw new ScriptRuntimeException($"world.overlap_sphere: radius must be more than 0 and at most {_settings.MaxOverlapRadius:0.#} m");
            var list = new Table(script);
            var index = 1;
            foreach (var hit in _physics.OverlapSphere(centre, radius, Current().Creation))
            {
                var entry = new Table(script);
                if (hit.Creation is { } c)
                    entry.Set("creation", DynValue.NewNumber(c));
                if (hit.Part is { } p)
                    entry.Set("part", DynValue.NewNumber(p));
                list.Set(index++, DynValue.NewTable(entry));
            }
            return DynValue.NewTable(list);
        });
        globals.Set("world", DynValue.NewTable(world));

        var sound = new Table(script);
        Fn(sound, "play", args =>
        {
            var id = args.AsType(0, "sound.play", DataType.String).String;
            var instance = Current();
            Vector3 at;
            if (args.Count > 1 && !args[1].IsNil())
                at = LuaValues.ToVector(args[1], "sound.play", 2);
            else if (Here() is { } h)
                at = h.State.Position;
            else
                return Refused(instance, "the part is gone");
            return Sound.Play(id, ModOf(instance), at) is { } error ? Refused(instance, error) : DynValue.True;
        });
        Fn(sound, "start", args =>
        {
            var id = args.AsType(0, "sound.start", DataType.String).String;
            var instance = Current();
            if (Here() is null)
                return NoHandle(instance, "the part is gone");
            if (instance.Loops.Count >= _settings.MaxLoopsPerPart)
                return NoHandle(instance, $"a part may run at most {_settings.MaxLoopsPerPart} sound loops");
            Dictionary<string, float>? parameters = null;
            if (args.Count > 1 && !args[1].IsNil())
            {
                parameters = [];
                foreach (var pair in args.AsType(1, "sound.start", DataType.Table).Table.Pairs)
                {
                    if (pair.Key.Type != DataType.String)
                        throw new ScriptRuntimeException("sound.start: parameter names must be strings");
                    parameters[pair.Key.String] = LuaValues.Number(pair.Value, "sound.start", 2);
                }
            }
            var handle = Sound.StartLoop(id, ModOf(instance), instance.Creation, instance.Part, parameters, out var error);
            if (handle == 0)
                return NoHandle(instance, error ?? "refused");
            instance.Loops.Add(handle);
            return DynValue.NewNumber(handle);
        });
        Fn(sound, "set_parameter", args =>
        {
            var instance = Current();
            var handle = (int)LuaValues.Number(args[0], "sound.set_parameter", 1);
            var name = args.AsType(1, "sound.set_parameter", DataType.String).String;
            var value = LuaValues.Number(args[2], "sound.set_parameter", 3);
            if (!instance.Loops.Contains(handle))
                return Refused(instance, $"the part runs no sound loop {handle}");
            return Sound.SetLoopParameter(handle, name, value) is { } error ? Refused(instance, error) : DynValue.True;
        });
        Fn(sound, "stop", args =>
        {
            var instance = Current();
            var handle = (int)LuaValues.Number(args[0], "sound.stop", 1);
            if (!instance.Loops.Remove(handle))
                return Refused(instance, $"the part runs no sound loop {handle}");
            Sound.StopLoop(handle);
            return DynValue.True;
        });
        globals.Set("sound", DynValue.NewTable(sound));

        globals.Set("log", DynValue.NewCallback((_, args) =>
        {
            Print(string.Join(" ", Enumerable.Range(0, args.Count).Select(i => args.AsStringUsingMeta(null, i, "log"))));
            return DynValue.Nil;
        }, "log"));
        return context;
    }

    private static void Fn(Table table, string name, Func<CallbackArguments, DynValue> body) =>
        table.Set(name, DynValue.NewCallback((_, args) => body(args), name));

    private Instance Current() =>
        _current ?? throw new ScriptRuntimeException("this function only works inside a part's hook");

    // The running part, its Creation and where it is; null once the part is gone (in on_destroy).
    private (Instance Instance, Creation Creation, PartKinematics State)? Here()
    {
        var instance = Current();
        if (_motion.FindCreation(instance.Creation) is not { } creation || creation.FindPart(instance.Part) is null)
            return null;
        if (instance.Kinematics is not { } cached || cached.Tick != Tick || cached.Revision != creation.Revision)
            instance.Kinematics = cached = (Tick, creation.Revision, PartKinematics.Of(creation, instance.Part));
        return (instance, creation, cached.State);
    }

    // Adds to the part's push for this step and sends the sum, which replaces the part's earlier push.
    private DynValue Push(Vector3 force, Vector3? at, Vector3 torque)
    {
        if (Here() is not { } h)
            return Refused(Current(), "the part is gone");
        var instance = h.Instance;
        if (instance.PushTick != Tick)
            (instance.PushTick, instance.PushForce, instance.PushTorque, instance.PushAt) = (Tick, Vector3.Zero, Vector3.Zero, null);
        var (oldForce, oldTorque, oldAt) = (instance.PushForce, instance.PushTorque, instance.PushAt);
        var newTorque = oldTorque + torque;
        var newAt = oldAt;
        if (at is { } point && force != Vector3.Zero)
        {
            // One application point per step: a force at another point is the same force there plus its twist about it.
            if (newAt is { } first)
                newTorque += Vector3.Cross(point - first, force);
            else
            {
                // Earlier central pushes this step move to the point too; take back the twist that adds.
                newAt = point;
                newTorque -= Vector3.Cross(point - MotionActions.CentreOfMass(h.Creation, h.State.Body), oldForce);
            }
        }
        else if (force != Vector3.Zero)
        {
            // Through the centre of mass: the same force at the step's point, minus the twist that adds there.
            if (newAt is { } first)
                newTorque -= Vector3.Cross(first - MotionActions.CentreOfMass(h.Creation, h.State.Body), force);
        }
        var newForce = oldForce + force;
        var result = _dispatcher.Dispatch(Caller, MotionActions.ApplyForce, new ApplyForceArgs(
            instance.Creation, ToVec(newForce), h.State.Body, newTorque == Vector3.Zero ? null : ToVec(newTorque),
            At: newAt is { } a ? ToVec(a) : null, Part: instance.Part));
        if (!result.Ok)
            return Refused(instance, result.Message ?? result.Code ?? "refused");
        (instance.PushForce, instance.PushTorque, instance.PushAt) = (newForce, newTorque, newAt);
        return DynValue.True;
    }

    private DynValue Bearing(Instance instance, int part, float? velocity, float? maxTorque)
    {
        var result = _dispatcher.Dispatch(Caller, MotionActions.SetBearingTarget,
            new SetBearingTargetArgs(instance.Creation, part, velocity, maxTorque));
        return result.Ok ? DynValue.True : Refused(instance, result.Message ?? result.Code ?? "refused");
    }

    // A refused action: (false, reason) to the script, and a warning the first time each reason appears.
    private DynValue Refused(Instance instance, string reason)
    {
        if (instance.Warned.Add(reason))
            Post(ScriptMessageKind.Warning, instance.File.File.Key, instance.Creation, instance.Part, reason);
        return DynValue.NewTuple(DynValue.False, DynValue.NewString(reason));
    }

    // nil and the reason, for calls that return a value when they work.
    private DynValue NoHandle(Instance instance, string reason) =>
        DynValue.NewTuple(DynValue.Nil, Refused(instance, reason).Tuple[1]);

    // Unqualified sound ids belong to the part's own mod.
    private static string ModOf(Instance instance) =>
        instance.PartType.IndexOf(':') is var colon and > 0 ? instance.PartType[..colon] : Identifiers.VanillaModId;

    private static Vec3 ToVec(Vector3 v) => new(v.X, v.Y, v.Z);
}
