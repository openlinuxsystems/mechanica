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
        Fn(part, "set_bearing_angle", args =>
        {
            var instance = Current();
            return BearingAngle(instance, instance.Part, args, "part.set_bearing_angle", 0);
        });
        Fn(part, "set_piston", args =>
        {
            var instance = Current();
            return Piston(instance, instance.Part, args, "part.set_piston", 0);
        });
        Fn(part, "joint", _ => Joint(script, Current(), Current().Part));
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
        Fn(creation, "set_bearing_angle", args => BearingAngle(Current(),
            (int)LuaValues.Number(args[0], "creation.set_bearing_angle", 1), args, "creation.set_bearing_angle", 1));
        Fn(creation, "set_piston", args => Piston(Current(),
            (int)LuaValues.Number(args[0], "creation.set_piston", 1), args, "creation.set_piston", 1));
        Fn(creation, "joint", args => Joint(script, Current(), (int)LuaValues.Number(args[0], "creation.joint", 1)));
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

    // set_bearing_angle(angle, speed?, max_torque?), with the angle at args[first].
    private DynValue BearingAngle(Instance instance, int part, CallbackArguments args, string function, int first)
    {
        var angle = LuaValues.Number(args[first], function, first + 1);
        var speed = LuaValues.OptionalNumber(args[first + 1], function, first + 2);
        var torque = LuaValues.OptionalNumber(args[first + 2], function, first + 3);
        var result = _dispatcher.Dispatch(Caller, MotionActions.SetBearingTarget,
            new SetBearingTargetArgs(instance.Creation, part, speed, torque, angle));
        return result.Ok ? DynValue.True : Refused(instance, result.Message ?? result.Code ?? "refused");
    }

    // set_piston(length?, speed?, max_force?), with the length at args[first]; a nil length frees it.
    private DynValue Piston(Instance instance, int part, CallbackArguments args, string function, int first)
    {
        var result = _dispatcher.Dispatch(Caller, MotionActions.SetPistonTarget, new SetPistonTargetArgs(instance.Creation, part,
            LuaValues.OptionalNumber(args[first], function, first + 1),
            LuaValues.OptionalNumber(args[first + 1], function, first + 2),
            LuaValues.OptionalNumber(args[first + 2], function, first + 3)));
        return result.Ok ? DynValue.True : Refused(instance, result.Message ?? result.Code ?? "refused");
    }

    // The joint a part owns (its bearing, piston or suspension side) as {type, other, position, min, max, locked}, or nil.
    private DynValue Joint(Script script, Instance instance, int part)
    {
        if (_motion.FindCreation(instance.Creation) is not { } creation
            || creation.Joints.FirstOrDefault(j => j.Connection.A == part) is not { } link
            || !creation.TryGetJointFrame(link.Connection, out var frame))
            return DynValue.Nil;
        var table = new Table(script);
        table.Set("type", DynValue.NewString(link.Connection.Type.ToString().ToLowerInvariant()));
        table.Set("other", DynValue.NewNumber(link.Connection.B));
        table.Set("locked", DynValue.NewBoolean(link.IsLocked));
        if ((link.IsLocked ? 0f : _motion.JointPosition(creation, link)) is { } position)
            table.Set("position", DynValue.NewNumber(position));
        if (frame.Min is { } min)
            table.Set("min", DynValue.NewNumber(min));
        if (frame.Max is { } max)
            table.Set("max", DynValue.NewNumber(max));
        return DynValue.NewTable(table);
    }

    // A refused action: (false, reason) to the script, and a warning the first time each reason appears.
    private DynValue Refused(Instance instance, string reason)
    {
        if (instance.Warned.Add(reason))
            Post(ScriptMessageKind.Warning, instance.File.File.Key, instance.Creation, instance.Part, reason);
        return DynValue.NewTuple(DynValue.False, DynValue.NewString(reason));
    }

    private static Vec3 ToVec(Vector3 v) => new(v.X, v.Y, v.Z);
}
