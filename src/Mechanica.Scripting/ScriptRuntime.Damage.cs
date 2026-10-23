using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.World;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting;

// Damage a part takes (on_damage), a part breaking itself (part.destroy) and the time of day (world.time_of_day).
// Damage is dealt in the middle of other work (a blast's splash, a shot inside another script's hook, the fire tick), so
// it is queued and each part hears it when no hook is running, as the instances are next brought in line with the
// Creations (each update, or a call into a part): a part that broke hears it before its script is destroyed, and damage
// a hook deals while the queue is handed out (one blast setting off the next) is heard in the same go.
public sealed partial class ScriptRuntime
{
    // Most damage reports handed out in one go, so parts that keep damaging each other cannot loop forever.
    private const int MaxDamageReports = 4096;

    private readonly Queue<PartDamage> _damages = new();
    private bool _deliveringDamage;

    // The new Creations each Creation split into since damage was last handed out (a blast breaking a vehicle apart).
    private readonly Dictionary<int, List<int>> _splitInto = [];

    /// <summary>
    /// Seconds in one in-game day (the world's <c>day_length</c>), read by <c>world.time_of_day()</c>. Defaults to
    /// <see cref="WorldGenDefinition.DefaultDayLength"/>.
    /// </summary>
    public Func<double> DayLength { get; set; } = () => WorldGenDefinition.DefaultDayLength;

    /// <summary>The hour a new world starts at (<c>world.time_of_day()</c> at time 0).</summary>
    public const double StartHour = WorldClock.StartHour;

    /// <summary>
    /// The in-game time of day in hours, 0 (midnight) up to 24: a world starts at <see cref="StartHour"/> and one
    /// <see cref="DayLength"/> of <see cref="Time"/> is a whole day.
    /// </summary>
    public double TimeOfDay => WorldClock.HourAt(_time, DayLength());

    private void OnPartDamaged(PartDamage damage)
    {
        _damages.Enqueue(damage);
        foreach (var piece in damage.SplitOff ?? [])
        {
            if (!_splitInto.TryGetValue(damage.Creation, out var pieces))
                _splitInto[damage.Creation] = pieces = [];
            pieces.Add(piece);
        }
    }

    // The Creation holding `part` (of `type`) now: `creation`, or a piece it split into while the damage was queued.
    private Creation? Holder(int creation, int part, string type, int depth = 0)
    {
        if (_motion.FindCreation(creation) is { } found && found.FindPart(part)?.Part == type)
            return found;
        if (depth > 64 || !_splitInto.TryGetValue(creation, out var pieces))
            return null;
        foreach (var piece in pieces)
        {
            if (Holder(piece, part, type, depth + 1) is { } holder)
                return holder;
        }
        return null;
    }

    // The script instance that hears damage to a part: the broken part's own, or the one on the Creation holding the part
    // now, started first when the part was just split off onto a new Creation (it would be at the next update).
    private Instance? DamagedInstance(PartDamage damage)
    {
        if (damage.Broken || Holder(damage.Creation, damage.Part, damage.PartType) is not { } holder)
            return _instances.GetValueOrDefault((damage.Creation, damage.Part));
        if (_instances.TryGetValue((holder.Id, damage.Part), out var instance))
            return instance;
        if (ScriptOf(damage.PartType) is not { } file)
            return null;
        instance = new Instance(holder.Id, damage.Part, damage.PartType, EntryFor(file));
        _instances[(holder.Id, damage.Part)] = instance;
        Start(instance);
        return instance;
    }

    // Calls on_damage(amount, kind, broken, at) on each damaged part's script, in the order the damage came.
    private void DeliverDamage()
    {
        if (_deliveringDamage || _current is not null || _damages.Count == 0)
            return;
        _deliveringDamage = true;
        try
        {
            var handed = 0;
            while (_damages.TryDequeue(out var damage))
            {
                if (++handed > MaxDamageReports)
                {
                    _damages.Clear();
                    Post(ScriptMessageKind.Warning, "?", null, null, $"more than {MaxDamageReports} parts were damaged at once; the rest of on_damage was skipped");
                    break;
                }
                if (DamagedInstance(damage) is not { } instance || instance.Fault is not null || !instance.Created
                    || instance.PartType != damage.PartType || instance.Env?.RawGet(Hooks.OnDamage) is not { Type: DataType.Function })
                    continue;
                var script = instance.File.Context;
                CallHook(instance, Hooks.OnDamage, DynValue.NewNumber(damage.Amount), DynValue.NewString(damage.Kind),
                    DynValue.NewBoolean(damage.Broken), LuaValues.Vector(script.Script, script.Vec3Meta, damage.At));
            }
        }
        finally
        {
            _deliveringDamage = false;
            _splitInto.Clear();
        }
    }

    private void AddDamageLibrary(Table part, Table world)
    {
        Fn(part, "destroy", args =>
        {
            var instance = PartInstance();
            Changing("part.destroy");
            if (_survival is null)
                throw new ScriptRuntimeException("part.destroy: damage is not enabled on this host");
            if (_motion.FindCreation(instance.Creation) is not { } creation || creation.FindPart(instance.Part) is null)
                return DynValue.False;
            var kind = args.Count > 0 && !args[0].IsNil() ? args.AsType(0, "part.destroy", DataType.String).String : DamageKinds.Explosion;
            if (!DamageKinds.All.Contains(kind))
                throw new ScriptRuntimeException($"part.destroy: kind must be one of {string.Join(", ", DamageKinds.All)}");
            return DynValue.NewBoolean(_survival.World.DamagePart(creation, instance.Part, float.MaxValue, kind).Broken);
        });
        Fn(world, "time_of_day", _ => DynValue.NewNumber(TimeOfDay));
    }
}
