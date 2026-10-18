using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Fire;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

// The fire library (D13): the fire simulation is the host's (FireActions), and scripts decide what puts fires
// out or starts them. The watering item and the water cannon douse with it; a mod's flamethrower would ignite.
public sealed partial class ScriptRuntime
{
    /// <summary>The host's fire, for the <c>fire</c> library; while null no fire burns (douse and ignite refuse).</summary>
    public FireActions? Fire { get; set; }

    private void AddFireLibrary(LuaContext context)
    {
        var script = context.Script;
        var fire = new Table(script);
        Fn(fire, "near", args =>
        {
            var at = LuaValues.ToVector(args[0], "fire.near", 1);
            var radius = Radius(args, 1, "fire.near");
            return List(context, Fire?.Near(at, radius) ?? []);
        });
        Fn(fire, "douse", args =>
        {
            Changing("fire.douse");
            var (_, caller, _) = Combatant("fire.douse", acting: true);
            caller = Reaching(caller);
            var at = LuaValues.ToVector(args[0], "fire.douse", 1);
            var radius = Radius(args, 1, "fire.douse");
            var amount = LuaValues.Number(args[2], "fire.douse", 3);
            if (!(amount > 0f) || !float.IsFinite(amount))
                throw new ScriptRuntimeException("fire.douse: amount must be more than 0");
            if (Fire is not { } fires)
                return NilBecause("There is no fire in this world.");
            return fires.CheckReach(caller, at, radius) is { } far ? NilBecause(far) : List(context, fires.Douse(at, radius, amount));
        });
        Fn(fire, "ignite", args =>
        {
            Changing("fire.ignite");
            var (_, caller, _) = Combatant("fire.ignite", acting: true);
            var (creationId, part) = SoilArgs(args, "fire.ignite");
            var strength = LuaValues.OptionalNumber(args[2], "fire.ignite", 3);
            if (Fire is not { } fires)
                return DynValue.NewTuple(DynValue.False, DynValue.NewString("There is no fire in this world."));
            if (_survival!.World.FindCreation(creationId) is not { } creation || creation.FindPart(part) is null)
                return DynValue.NewTuple(DynValue.False, DynValue.NewString($"There is no part {part} in creation {creationId}."));
            var why = fires.CheckReach(caller, CentreOf(creation, part), 0) ?? fires.Ignite(creation, part, strength);
            return why is null ? DynValue.True : DynValue.NewTuple(DynValue.False, DynValue.NewString(why));
        });
        Fn(fire, "strength", args =>
        {
            var (creationId, part) = SoilArgs(args, "fire.strength");
            return DynValue.NewNumber(_survival?.World.FindCreation(creationId)?.FireOf(part) ?? 0);
        });
        script.Globals.Set("fire", DynValue.NewTable(fire));
    }

    private float Radius(CallbackArguments args, int index, string function)
    {
        var radius = LuaValues.Number(args[index], function, index + 1);
        if (!(radius >= 0f) || radius > _settings.MaxOverlapRadius)
            throw new ScriptRuntimeException($"{function}: radius must be 0 to {_settings.MaxOverlapRadius:0.#} m");
        return radius;
    }

    // Fires as scripts see them: a list of {id, position, strength, creation?, part?}.
    private static DynValue List(LuaContext context, IReadOnlyList<FireView> fires)
    {
        var list = new Table(context.Script);
        var index = 1;
        foreach (var f in fires)
        {
            var table = new Table(context.Script);
            table.Set("id", DynValue.NewString(f.Id));
            table.Set("position", LuaValues.Vector(context.Script, context.Vec3Meta, f.Position.ToNumerics()));
            table.Set("strength", DynValue.NewNumber(f.Strength));
            if (f.Creation is { } creation)
                table.Set("creation", DynValue.NewNumber(creation));
            if (f.Part is { } part)
                table.Set("part", DynValue.NewNumber(part));
            list.Set(index++, DynValue.NewTable(table));
        }
        return DynValue.NewTable(list);
    }

    private static Vector3 CentreOf(Creation creation, int partId)
    {
        var part = creation.FindPart(partId)!;
        var pose = creation.BodyPose(creation.BodyIndexOf(partId));
        return pose.Apply(PartGeometry.CentreMetres(creation.DefinitionOf(partId), part.Cell, part.Rotation).ToNumerics());
    }
}
