using System.Text.Json.Nodes;

namespace Mechanica.Scripting;

/// <summary>What kind of line a script message is.</summary>
public enum ScriptMessageKind
{
    /// <summary>Output of <c>print</c> or <c>log</c>.</summary>
    Print,

    /// <summary>Something the script did was refused (an action failed validation); the script keeps running.</summary>
    Warning,

    /// <summary>The script failed to load, raised an error or ran too long. Its part stops until the script is fixed and reloaded.</summary>
    Error,
}

/// <summary>One line of script output for the editor console and the log.</summary>
/// <param name="Kind">Print, warning or error.</param>
/// <param name="Script">Qualified script path (<c>mod:scripts/x.lua</c>).</param>
/// <param name="Creation">Creation of the part the line came from, if any.</param>
/// <param name="Part">Part id the line came from, if any.</param>
/// <param name="Text">The message.</param>
/// <param name="Tick">Runtime tick the line was written on.</param>
public sealed record ScriptMessage(ScriptMessageKind Kind, string Script, int? Creation, int? Part, string Text, long Tick)
{
    /// <inheritdoc/>
    public override string ToString() =>
        $"[{Kind.ToString().ToLowerInvariant()}] {Script}{(Part is { } p ? $" (creation {Creation}, part {p})" : "")}: {Text}";
}

/// <summary>One running part script, as the editor and tests see it.</summary>
/// <param name="Creation">Creation id.</param>
/// <param name="Part">Part id.</param>
/// <param name="PartType">Qualified part type id.</param>
/// <param name="Script">Qualified script path.</param>
/// <param name="Fault">Why the script is stopped, or null while it runs.</param>
/// <param name="Version">How many times the script has been loaded; goes up on each successful hot reload.</param>
public sealed record ScriptInstanceInfo(int Creation, int Part, string PartType, string Script, string? Fault, int Version);

/// <summary>Result of reloading a script.</summary>
/// <param name="Ok">True when the new code compiled and every instance took it.</param>
/// <param name="Script">Qualified script path.</param>
/// <param name="Error">Compile error, when the old code keeps running.</param>
/// <param name="Instances">Parts running the script.</param>
public sealed record ScriptReloadResult(bool Ok, string Script, string? Error, int Instances);

/// <summary>Result of calling a hook from the host.</summary>
/// <param name="Ok">False when the script is stopped, has no such part, or the call failed.</param>
/// <param name="Values">The hook's return values as plain data (null when missing or not plain data).</param>
/// <param name="Error">Why it failed.</param>
public sealed record ScriptCallResult(bool Ok, IReadOnlyList<JsonNode?> Values, string? Error)
{
    /// <summary>A failure.</summary>
    public static ScriptCallResult Fail(string error) => new(false, [], error);
}

/// <summary>Arguments of <c>UseItem</c>: use the item in a slot of the caller's inventory (its script's <c>on_use</c>).</summary>
/// <param name="Slot">The slot holding the item.</param>
/// <param name="Direction">Where the caller aims from their eyes; the host checks it against where they look. Omitted means where they look.</param>
/// <param name="Creation">The Creation of the part the item is used on (soil for a seed), if any. Give it with <paramref name="Part"/>.</param>
/// <param name="Part">The part the item is used on, if any. The script checks reach (<c>farm.soil</c> does).</param>
/// <param name="Hold">True when Use stays held after this use (a spray): the script sees <c>holder.using()</c> true every step until <c>ReleaseItem</c>.</param>
public sealed record UseItemArgs(int Slot, Mechanica.Core.Data.Vec3? Direction = null, int? Creation = null, int? Part = null, bool Hold = false);

/// <summary>Arguments of <c>ReleaseItem</c>: the caller lets go of Use after a held use (<see cref="UseItemArgs.Hold"/>).</summary>
/// <param name="Slot">The slot they used, if given; the host lets go of whatever they hold either way.</param>
public sealed record ReleaseItemArgs(int? Slot = null);

/// <summary>Arguments of <c>ReloadItem</c>: reload the item in a slot (its script's <c>on_reload_item</c>; UI spec §1, §10).</summary>
/// <param name="Slot">The slot holding the item.</param>
public sealed record ReloadItemArgs(int Slot);

/// <summary>Arguments of <c>AimItem</c>: the caller starts or stops aiming the item in hand (holding right click; UI spec §10).</summary>
/// <param name="On">True while right click is held.</param>
public sealed record AimItemArgs(bool On);

/// <summary>
/// The number an item script shows on its hotbar slot and the HUD counter while it is in hand (<c>item.counter</c>):
/// a weapon's loaded rounds out of its magazine, plus the ammo it draws from so the HUD can count what is carried.
/// The host sends it to the holder alone.
/// </summary>
/// <param name="Item">Qualified id of the item it belongs to.</param>
/// <param name="Value">The number shown (rounds loaded), or null to show only what is carried of <paramref name="Ammo"/>.</param>
/// <param name="Max">What <paramref name="Value"/> is out of (the magazine), or null.</param>
/// <param name="Ammo">Qualified id of the item it draws from, or null.</param>
/// <param name="Busy">Seconds from now the item is busy for (a reload), 0 when it is ready.</param>
public sealed record ItemCounterView(string Item, int? Value = null, int? Max = null, string? Ammo = null, float Busy = 0f);

/// <summary>Arguments of <c>EquipItem</c>: take the item in a slot in hand, or empty the hand.</summary>
/// <param name="Slot">The slot holding the item; omitted (null) empties the hand.</param>
public sealed record EquipItemArgs(int? Slot = null);

/// <summary>One player's script of one held item type, as the editor and tests see it.</summary>
/// <param name="Peer">The player.</param>
/// <param name="Item">Qualified item id.</param>
/// <param name="Script">Qualified script path.</param>
/// <param name="Fault">Why the script is stopped, or null while it runs.</param>
/// <param name="Version">How many times the script has been loaded.</param>
public sealed record ItemScriptInfo(int Peer, string Item, string Script, string? Fault, int Version);
