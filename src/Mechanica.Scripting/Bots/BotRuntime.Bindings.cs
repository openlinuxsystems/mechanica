using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using MoonSharp.Interpreter;

namespace Mechanica.Scripting.Bots;

// The bot Lua API (docs/bot-tutorial.md). Library tables are shared by every bot of a mod and act on the
// bot whose hook is running (_current).
public sealed partial class BotRuntime
{
    private void Bind(LuaContext context)
    {
        var script = context.Script;
        var meta = context.Vec3Meta;
        DynValue Vec(Vector3 v) => LuaValues.Vector(script, meta, v);

        var bot = new Table(script);
        Fn(bot, "id", _ => DynValue.NewNumber(Current().Bot.Id));
        Fn(bot, "type", _ => DynValue.NewString(Current().Bot.Type.QualifiedId));
        Fn(bot, "name", _ => DynValue.NewString(Current().Bot.Type.Name));
        Fn(bot, "position", _ => Vec(Current().Bot.Position));
        Fn(bot, "centre", _ => Vec(Current().Bot.Centre));
        Fn(bot, "home", _ => Vec(Current().Bot.Home));
        Fn(bot, "yaw", _ => DynValue.NewNumber(Current().Bot.Yaw));
        Fn(bot, "health", _ => DynValue.NewNumber(Current().Bot.Health));
        Fn(bot, "max_health", _ => DynValue.NewNumber(Current().Bot.Type.Health));
        Fn(bot, "speed", _ => DynValue.NewNumber(Current().Bot.TopSpeed));
        Fn(bot, "sight", _ => DynValue.NewNumber(Current().Bot.Type.Sight));
        Fn(bot, "radius", _ => DynValue.NewNumber(Current().Bot.Type.Radius));
        Fn(bot, "in_water", _ => DynValue.NewBoolean(Current().Bot.InWater));
        Fn(bot, "random", _ => DynValue.NewNumber(Current().Random.NextDouble()));
        Fn(bot, "memory", _ => DynValue.NewTable(Current().Memory!));
        Fn(bot, "alert", _ =>
        {
            Alerted?.Invoke(Current().Bot.Id);
            return DynValue.Nil;
        });
        Fn(bot, "sound", args =>
        {
            Sounded?.Invoke(Current().Bot.Id, args.AsType(0, "bot.sound", DataType.String).String);
            return DynValue.Nil;
        });
        Fn(bot, "attacks", _ =>
        {
            var brain = Current();
            var list = new Table(script);
            var index = 1;
            foreach (var attack in brain.Bot.Type.Attacks)
                list.Set(index++, AttackInfo(script, brain, attack));
            return DynValue.NewTable(list);
        });
        Fn(bot, "attack_info", args =>
        {
            var brain = Current();
            var id = args.AsType(0, "bot.attack_info", DataType.String).String;
            return brain.Bot.Type.Attack(id) is { } attack ? AttackInfo(script, brain, attack) : DynValue.Nil;
        });
        Fn(bot, "raid", _ =>
        {
            var brain = Current();
            if (_raids is null || brain.Bot.Raid is not { } raid || _raids.Current.Id != raid)
                return DynValue.Nil;
            var table = new Table(script);
            table.Set("id", DynValue.NewNumber(raid));
            table.Set("level", DynValue.NewNumber(_raids.Current.Level));
            table.Set("target", Vec(new Vector3(_raids.Current.Target.X, _raids.Current.Target.Y, _raids.Current.Target.Z)));
            return DynValue.NewTable(table);
        });
        Fn(bot, "move", args =>
        {
            var direction = LuaValues.ToVector(args[0], "bot.move", 1);
            return Move(Current(), direction, LuaValues.OptionalNumber(args[1], "bot.move", 2));
        });
        Fn(bot, "move_to", args =>
        {
            var brain = Current();
            var point = LuaValues.ToVector(args[0], "bot.move_to", 1);
            var away = Flat(point - brain.Bot.Position);
            if (away.Length() < 0.1f)
                return Move(brain, Vector3.Zero, null);
            return Move(brain, away, LuaValues.OptionalNumber(args[1], "bot.move_to", 2));
        });
        Fn(bot, "face", args =>
        {
            var brain = Current();
            var away = Flat(LuaValues.ToVector(args[0], "bot.face", 1) - brain.Bot.Position);
            return away.LengthSquared() < 1e-6f ? DynValue.True : Move(brain, away, 0f);
        });
        Fn(bot, "stop", _ => Move(Current(), Vector3.Zero, null));
        Fn(bot, "attack", args =>
        {
            var brain = Current();
            var id = args.AsType(0, "bot.attack", DataType.String).String;
            var target = args.Count > 1 && !args[1].IsNil() ? Target(args[1], "bot.attack", 2) : new BotAttackArgs(0, "");
            var result = _dispatcher.Dispatch(Caller, BotActions.BotAttack, target with { Bot = brain.Bot.Id, Attack = id });
            return result.Ok ? DynValue.True : Refused(brain, result.Message ?? result.Code ?? "refused");
        });
        Fn(bot, "reach", args =>
        {
            var brain = Current();
            return _bots.Reach(brain.Bot, Target(args[0], "bot.reach", 1)) is { } reach ? DynValue.NewNumber(reach) : DynValue.Nil;
        });
        Fn(bot, "custom", args =>
        {
            var brain = Current();
            var name = args.AsType(0, "bot.custom", DataType.String).String;
            var function = brain.Env!.RawGet(name);
            if (function is null || function.Type != DataType.Function)
                throw new ScriptRuntimeException($"unknown behaviour node '{name}' (no built-in node, and the bot's script defines no function of that name)");
            return function;
        });
        script.Globals.Set("bot", DynValue.NewTable(bot));

        var world = new Table(script);
        Fn(world, "time", _ => DynValue.NewNumber(Time));
        Fn(world, "players", args =>
        {
            var brain = Current();
            var range = LuaValues.OptionalNumber(args[0], "world.players", 1) ?? brain.Bot.Type.Sight;
            var list = new Table(script);
            var index = 1;
            foreach (var (peer, centre, distance) in _bots.LivingPlayers()
                .Select(p => (p.Peer, p.Centre, Distance: Vector3.Distance(p.Centre, brain.Bot.Centre)))
                .Where(p => p.Distance <= range).OrderBy(p => p.Distance).ThenBy(p => p.Peer))
            {
                var entry = new Table(script);
                entry.Set("peer", DynValue.NewNumber(peer));
                entry.Set("position", Vec(centre));
                entry.Set("distance", DynValue.NewNumber(distance));
                list.Set(index++, DynValue.NewTable(entry));
            }
            return DynValue.NewTable(list);
        });
        Fn(world, "crops", args =>
        {
            var brain = Current();
            return Crops(script, Vec, brain.Bot.Centre, LuaValues.OptionalNumber(args[0], "world.crops", 1) ?? brain.Bot.Type.Sight, brain.Bot.Centre);
        });
        Fn(world, "crops_near", args =>
        {
            var brain = Current();
            var point = LuaValues.ToVector(args[0], "world.crops_near", 1);
            return Crops(script, Vec, point, LuaValues.Number(args[1], "world.crops_near", 2), brain.Bot.Centre);
        });
        Fn(world, "bots", args =>
        {
            var brain = Current();
            var range = LuaValues.OptionalNumber(args[0], "world.bots", 1) ?? brain.Bot.Type.Sight;
            var list = new Table(script);
            var index = 1;
            foreach (var other in _bots.Bots.Values
                .Where(b => !b.Dead && b.Id != brain.Bot.Id && Vector3.Distance(b.Centre, brain.Bot.Centre) <= range)
                .OrderBy(b => Vector3.Distance(b.Centre, brain.Bot.Centre)).ThenBy(b => b.Id))
            {
                var entry = new Table(script);
                entry.Set("bot", DynValue.NewNumber(other.Id));
                entry.Set("type", DynValue.NewString(other.Type.QualifiedId));
                entry.Set("position", Vec(other.Centre));
                entry.Set("distance", DynValue.NewNumber(Vector3.Distance(other.Centre, brain.Bot.Centre)));
                list.Set(index++, DynValue.NewTable(entry));
            }
            return DynValue.NewTable(list);
        });
        Fn(world, "objects", args =>
        {
            var brain = Current();
            var range = LuaValues.OptionalNumber(args[0], "world.objects", 1) ?? brain.Bot.Type.Sight;
            var list = new Table(script);
            var index = 1;
            // Measured from the edge of the bot's body, as attack ranges are.
            foreach (var (id, centre, distance) in _bots.ObjectsNear(brain.Bot.Centre, range + brain.Bot.Type.Radius))
            {
                var entry = new Table(script);
                entry.Set("object", DynValue.NewString(id));
                entry.Set("position", Vec(centre));
                entry.Set("distance", DynValue.NewNumber(Math.Max(0, distance - brain.Bot.Type.Radius)));
                list.Set(index++, DynValue.NewTable(entry));
            }
            return DynValue.NewTable(list);
        });
        Fn(world, "spinning", args =>
        {
            var brain = Current();
            var range = LuaValues.OptionalNumber(args[0], "world.spinning", 1) ?? brain.Bot.Type.Sight;
            var minSpeed = LuaValues.OptionalNumber(args[1], "world.spinning", 2) ?? 4f;
            var only = args.Count > 2 && !args[2].IsNil() ? args.AsType(2, "world.spinning", DataType.String).String : null;
            var list = new Table(script);
            var index = 1;
            foreach (var (creation, part, centre, speed, distance) in _bots.SpinningNear(brain.Bot.Centre, range, minSpeed, only))
            {
                var entry = new Table(script);
                entry.Set("creation", DynValue.NewNumber(creation));
                entry.Set("part", DynValue.NewNumber(part));
                entry.Set("position", Vec(centre));
                entry.Set("speed", DynValue.NewNumber(speed));
                entry.Set("distance", DynValue.NewNumber(distance));
                list.Set(index++, DynValue.NewTable(entry));
            }
            return DynValue.NewTable(list);
        });
        Fn(world, "locate", args =>
        {
            Current();
            return _bots.Locate(Target(args[0], "world.locate", 1), out var radius) is { } at
                ? DynValue.NewTuple(Vec(at), DynValue.NewNumber(radius))
                : DynValue.Nil;
        });
        Fn(world, "raycast", args =>
        {
            var brain = Current();
            var from = LuaValues.ToVector(args[0], "world.raycast", 1);
            var to = LuaValues.ToVector(args[1], "world.raycast", 2);
            if ((to - from).Length() > _settings.MaxRayLength)
                throw new ScriptRuntimeException($"world.raycast: the ray is longer than {_settings.MaxRayLength:0.#} m");
            if (_world.Cast(from, to, brain.Bot.Id) is not { } hit)
                return DynValue.Nil;
            var result = new Table(script);
            result.Set("position", Vec(hit.Point));
            if (hit.Player is { } player)
                result.Set("player", DynValue.NewNumber(player));
            if (hit.Creation is { } creation)
                result.Set("creation", DynValue.NewNumber(creation));
            if (hit.Part is { } part)
                result.Set("part", DynValue.NewNumber(part));
            if (hit.Bot is { } other)
                result.Set("bot", DynValue.NewNumber(other));
            return DynValue.NewTable(result);
        });
        script.Globals.Set("world", DynValue.NewTable(world));

        script.Globals.Set("log", DynValue.NewCallback((_, args) =>
        {
            Print(string.Join(" ", Enumerable.Range(0, args.Count).Select(i => args.AsStringUsingMeta(null, i, "log"))));
            return DynValue.Nil;
        }, "log"));
    }

    private static void Fn(Table table, string name, Func<CallbackArguments, DynValue> body) =>
        table.Set(name, DynValue.NewCallback((_, args) => body(args), name));

    private Brain Current() =>
        _current ?? throw new ScriptRuntimeException("this function only works inside a bot's hook");

    private DynValue AttackInfo(Script script, Brain brain, BotAttack attack)
    {
        var table = new Table(script);
        table.Set("id", DynValue.NewString(attack.Id));
        table.Set("kind", DynValue.NewString(attack.Kind.ToString().ToLowerInvariant()));
        table.Set("damage", DynValue.NewNumber(attack.Damage));
        table.Set("range", DynValue.NewNumber(attack.Range));
        table.Set("radius", DynValue.NewNumber(attack.Radius));
        table.Set("interval", DynValue.NewNumber(attack.Interval));
        var cooldown = brain.Bot.Cooldown(attack, _bots.Now);
        table.Set("cooldown", DynValue.NewNumber(cooldown));
        table.Set("ready", DynValue.NewBoolean(cooldown <= 0));
        return DynValue.NewTable(table);
    }

    private DynValue Crops(Script script, Func<Vector3, DynValue> vec, Vector3 centre, float range, Vector3 from)
    {
        var list = new Table(script);
        var index = 1;
        foreach (var crop in _bots.Crops()
            .Select(c => (Crop: c, Distance: Vector3.Distance(c.Position, from)))
            .Where(c => Vector3.Distance(c.Crop.Position, centre) <= range)
            .OrderBy(c => c.Distance).ThenBy(c => c.Crop.Creation).ThenBy(c => c.Crop.Part))
        {
            var entry = new Table(script);
            entry.Set("creation", DynValue.NewNumber(crop.Crop.Creation));
            entry.Set("part", DynValue.NewNumber(crop.Crop.Part));
            entry.Set("position", vec(crop.Crop.Position));
            entry.Set("value", DynValue.NewNumber(crop.Crop.Value));
            entry.Set("ripe", DynValue.NewBoolean(crop.Crop.Ripe));
            entry.Set("distance", DynValue.NewNumber(crop.Distance));
            list.Set(index++, DynValue.NewTable(entry));
        }
        return DynValue.NewTable(list);
    }

    // A target table: { player = peer }, { creation = c, part = p }, { bot = id } or { object = id }.
    private static BotAttackArgs Target(DynValue value, string function, int argument)
    {
        if (value.Type != DataType.Table)
            throw new ScriptRuntimeException($"bad argument #{argument} to '{function}' (target table expected, got {LuaValues.Describe(value)})");
        int? Field(string key) => value.Table.Get(key) is { Type: DataType.Number } n ? (int)n.Number : null;
        var target = new BotAttackArgs(0, "", Field("player"), Field("creation"), Field("part"), Field("bot"),
            value.Table.Get("object") is { Type: DataType.String } o ? o.String : null);
        if (target.Player is null && target.Target is null && target.Object is null && (target.Creation is null || target.Part is null))
            throw new ScriptRuntimeException($"bad argument #{argument} to '{function}' (a target is {{player = peer}}, {{creation = c, part = p}}, {{bot = id}} or {{object = id}})");
        return target;
    }

    private DynValue Move(Brain brain, Vector3 direction, float? speed)
    {
        var result = _dispatcher.Dispatch(Caller, BotActions.MoveBot,
            new MoveBotArgs(brain.Bot.Id, direction == Vector3.Zero ? null : new Vec3(direction.X, direction.Y, direction.Z),
                speed is { } s ? Math.Min(s, brain.Bot.TopSpeed) : null));
        return result.Ok ? DynValue.True : Refused(brain, result.Message ?? result.Code ?? "refused");
    }

    // A refused action: (false, reason) to the script, and a warning the first time each reason appears.
    private DynValue Refused(Brain brain, string reason)
    {
        // Numbers (distances, seconds) change from call to call; warn once per kind of refusal.
        if (brain.Warned.Add(new string([.. reason.Where(c => !char.IsAsciiDigit(c))])))
            Post(ScriptMessageKind.Warning, brain.File?.File.Key ?? brain.Bot.Type.QualifiedId, brain.Bot.Id, reason);
        return DynValue.NewTuple(DynValue.False, DynValue.NewString(reason));
    }
}
