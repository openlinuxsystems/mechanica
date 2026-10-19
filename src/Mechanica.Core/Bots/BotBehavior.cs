namespace Mechanica.Core.Bots;

/// <summary>Where a behaviour node sits in a tree.</summary>
public enum BotNodeShape
{
    /// <summary>Runs its children (<c>sequence</c>, <c>selector</c>).</summary>
    Composite,

    /// <summary>Wraps exactly one child (<c>invert</c>, <c>cooldown</c>).</summary>
    Decorator,

    /// <summary>A condition or an action; no children.</summary>
    Leaf,
}

/// <summary>A parameter of a built-in behaviour node.</summary>
/// <param name="Name">Key in the node object.</param>
/// <param name="Kind"><c>number</c>, <c>string</c> or <c>attack</c> (one of the bot's attack ids).</param>
/// <param name="Default">Value used when the key is left out, as text (empty when it is required).</param>
/// <param name="Help">What it does.</param>
public sealed record BotNodeParameter(string Name, string Kind, string Default, string Help);

/// <summary>A built-in behaviour node, for validation, the Bot Behavior Editor's palette and the docs.</summary>
/// <param name="Type">Node type, as written in <c>"type"</c>.</param>
/// <param name="Shape">Composite, decorator or leaf.</param>
/// <param name="Help">What it does and what it returns.</param>
/// <param name="Parameters">Its parameters.</param>
public sealed record BotNodeInfo(string Type, BotNodeShape Shape, string Help, IReadOnlyList<BotNodeParameter> Parameters);

/// <summary>
/// The nodes of the built-in behaviour library. The library itself is Lua (D2:
/// <c>Mechanica.Scripting</c>'s <c>behavior.lua</c>); this list describes the same nodes to C# so data can
/// be checked and the editor can offer them. Every node returns <c>success</c>, <c>failure</c> or
/// <c>running</c>; a tree is re-run from the root on every think.
/// </summary>
public static class BotBehavior
{
    /// <summary>Every built-in node, composites first.</summary>
    public static IReadOnlyList<BotNodeInfo> Nodes { get; } =
    [
        new("sequence", BotNodeShape.Composite, "Runs its children in order. Fails at the first child that fails and is running while a child runs; succeeds when all succeed.", []),
        new("selector", BotNodeShape.Composite, "Tries its children in order and returns the first result that is not failure; fails when every child fails.", []),
        new("invert", BotNodeShape.Decorator, "Turns its child's success into failure and failure into success.", []),
        new("succeed", BotNodeShape.Decorator, "Runs its child and succeeds unless it is still running.", []),
        new("cooldown", BotNodeShape.Decorator, "Fails for a while after its child succeeds.",
            [new("seconds", "number", "5", "Seconds to wait after a success.")]),
        new("has_target", BotNodeShape.Leaf, "Succeeds while the bot has a target that still exists (and a target player is alive).", []),
        new("find_player", BotNodeShape.Leaf, "Targets the nearest living player in range. Fails when there is none.",
            [new("range", "number", "", "How far to look, in m; omitted means the bot's sight.")]),
        new("find_crop", BotNodeShape.Leaf, "Targets the nearest planted crop in range (near the raid's farm during a raid). Fails when there is none.",
            [new("range", "number", "", "How far to look, in m; omitted means the bot's sight.")]),
        new("clear_target", BotNodeShape.Leaf, "Forgets the target. Succeeds.", []),
        new("target_in_range", BotNodeShape.Leaf, "Succeeds when the target is within reach of an attack.",
            [new("attack", "attack", "", "The attack whose range counts.")]),
        new("chase", BotNodeShape.Leaf, "Walks toward the target. Running until it is within reach of the attack, then success; failure without a target.",
            [new("attack", "attack", "", "The attack whose range to close to; omitted means 1.5 m."), new("speed", "number", "1", "Share of top speed, 0 to 1.")]),
        new("attack", BotNodeShape.Leaf, "Uses an attack on the target. Succeeds when the host accepts it; fails when it is out of range or not ready.",
            [new("attack", "attack", "", "Which attack.")]),
        new("go_to_raid", BotNodeShape.Leaf, "Walks to the farm its raid targets. Running on the way, success on arrival, failure outside a raid.",
            [new("radius", "number", "6", "How close counts as arrived, in m.")]),
        new("in_raid", BotNodeShape.Leaf, "Succeeds when the bot is part of a raid.", []),
        new("health_below", BotNodeShape.Leaf, "Succeeds when health is below a share of full health.",
            [new("fraction", "number", "0.3", "Share of full health, 0 to 1.")]),
        new("in_water", BotNodeShape.Leaf, "Succeeds while the bot stands in water.", []),
        new("flee", BotNodeShape.Leaf, "Runs from the target. Running until it is far enough, then success; failure without a target.",
            [new("range", "number", "15", "How far is far enough, in m.")]),
        new("wander", BotNodeShape.Leaf, "Strolls between random spots near where it spawned. Always running.",
            [new("radius", "number", "8", "How far from home, in m."), new("speed", "number", "0.4", "Share of top speed, 0 to 1.")]),
        new("wait", BotNodeShape.Leaf, "Stands still. Running for the time, then success.",
            [new("seconds", "number", "1", "How long, in s.")]),
        new("stop", BotNodeShape.Leaf, "Stops walking. Succeeds.", []),
        new("hesitate", BotNodeShape.Leaf,
            "Balks at something spinning near the bot (a part on a turning bearing: a wheel, rotor or saw): backs away from it, running, for a while, then fails so the tree goes on anyway. Fails at once when nothing spins near. Backing out of range does not end it early, and it hesitates again only once the way has been clear for as long.",
            [new("range", "number", "4", "How near a spinning part must be, in m."), new("min_speed", "number", "4", "How fast it must turn to count, in rad/s."),
                new("seconds", "number", "2", "How long it hesitates, in s."),
                new("part", "string", "", "Only this part type counts (vanilla:saw_blade), on the turning side; empty for anything spinning.")]),
        new("break_objects", BotNodeShape.Leaf,
            "Uses an attack on the nearest world object (tree, rock, bush) within its reach, so the bot clears its way. Succeeds when it hit one; fails when none is in reach or the attack is not ready. The attack needs object_damage.",
            [new("attack", "attack", "", "Which attack.")]),
        new("call", BotNodeShape.Leaf, "Calls a function from the bot's script with (node, dt); its return value (a status string or a boolean) is the result.",
            [new("function", "string", "", "Name of the global function in the bot's script.")]),
    ];

    private static readonly Dictionary<string, BotNodeInfo> ByType = Nodes.ToDictionary(n => n.Type, StringComparer.Ordinal);

    /// <summary>The built-in node of this type, or null for a custom leaf.</summary>
    public static BotNodeInfo? Find(string type) => ByType.GetValueOrDefault(type);
}
