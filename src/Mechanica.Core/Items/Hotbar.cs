using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;

namespace Mechanica.Core.Items;

/// <summary>
/// The hand tools a hotbar slot can hold, by their catalog ids (CATALOG_IDS.md). They are not inventory items:
/// every player always has them.
/// </summary>
public static class HotbarTools
{
    /// <summary>The Hammer: removes parts (a part in a slot places it).</summary>
    public const string Hammer = "vanilla:hammer";

    /// <summary>The Lift.</summary>
    public const string Lift = "vanilla:lift";

    /// <summary>The Weld Tool.</summary>
    public const string Weld = "vanilla:weld_tool";

    /// <summary>The Paint Tool.</summary>
    public const string Paint = "vanilla:paint_tool";

    /// <summary>The Sledgehammer.</summary>
    public const string Sledge = "vanilla:sledgehammer";

    /// <summary>The Connect Tool.</summary>
    public const string Connect = "vanilla:connect_tool";

    /// <summary>Every tool, in the order a new hotbar holds them.</summary>
    public static IReadOnlyList<string> All { get; } = [Hammer, Lift, Weld, Paint, Sledge, Connect];

    /// <summary>True when <paramref name="id"/> names a tool (a bare id means vanilla).</summary>
    public static bool IsTool(string? id) => id is not null && All.Contains(Identifiers.Qualify(id));
}

/// <summary>Arguments of <c>SetHotbarSlot</c>: what a slot holds.</summary>
/// <param name="Slot">Slot, 0 to 9 (keys 1 to 9 and 0).</param>
/// <param name="Entry">A tool (<see cref="HotbarTools"/>), an item id, or null to empty the slot.</param>
public sealed record SetHotbarSlotArgs(int Slot, string? Entry = null);

/// <summary>The value of <c>GetHotbar</c>: what each slot holds, null for empty.</summary>
/// <param name="Slots">Ten entries, qualified ids or null.</param>
public sealed record HotbarSlots(IReadOnlyList<string?> Slots);

/// <summary>
/// Each player's hotbar (UI spec §3): ten slots of tools and items, picked with 1 to 0 or the wheel. A slot names
/// a tool or an item id; it holds no items itself, so what a slot can do is still decided against the player's
/// inventory when they use it. Slots change only through <c>SetHotbarSlot</c>, are saved with the world under the
/// player's profile and come back when they return. A player who has never changed theirs has
/// <see cref="Default"/>: the tools in key order.
/// </summary>
public sealed class HotbarActions(IPartCatalog catalog)
{
    /// <summary>Puts a tool or an item in one of the caller's slots, or empties it.</summary>
    public const string SetHotbarSlot = "SetHotbarSlot";

    /// <summary>Reads the caller's hotbar (<see cref="HotbarSlots"/>).</summary>
    public const string GetHotbar = "GetHotbar";

    /// <summary>Slots on a hotbar.</summary>
    public const int SlotCount = 10;

    private readonly Dictionary<int, string?[]> _bars = [];

    /// <summary>A new player's hotbar: the six tools, then four empty slots.</summary>
    public static IReadOnlyList<string?> Default { get; } = [.. HotbarTools.All, null, null, null, null];

    /// <summary>A player's hotbar changed: peer and the slots now.</summary>
    public event Action<int, IReadOnlyList<string?>>? Changed;

    /// <summary>Registers <see cref="SetHotbarSlot"/> and <see cref="GetHotbar"/>.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<SetHotbarSlotArgs>(SetHotbarSlot, Validate, (caller, args) =>
        {
            var bar = Bar(caller.Peer);
            bar[args.Slot] = args.Entry is null ? null : Identifiers.Qualify(args.Entry);
            Changed?.Invoke(caller.Peer, bar);
            return Value(new HotbarSlots(bar));
        });
        dispatcher.Register<NoArgs>(GetHotbar, (caller, _) => caller.Source == ActionSource.Player ? null : "Only players have a hotbar.",
            (caller, _) => Value(new HotbarSlots(SlotsOf(caller.Peer))), readOnly: true);
    }

    /// <summary>What a player's slots hold.</summary>
    public IReadOnlyList<string?> SlotsOf(int peer) => _bars.TryGetValue(peer, out var bar) ? bar : Default;

    /// <summary>A player's slots to save, or null while they are still the default.</summary>
    public IReadOnlyList<string?>? SavedOf(int peer) => _bars.TryGetValue(peer, out var bar) && !bar.SequenceEqual(Default) ? bar : null;

    /// <summary>
    /// Host: gives a player back the slots saved for them. Entries this world no longer knows (an item whose mod
    /// is gone) come back empty; a missing list is the default.
    /// </summary>
    public void Restore(int peer, IReadOnlyList<string?>? saved)
    {
        if (saved is null)
        {
            _bars.Remove(peer);
            return;
        }
        var bar = new string?[SlotCount];
        for (var i = 0; i < SlotCount && i < saved.Count; i++)
            bar[i] = Known(saved[i]) ? Identifiers.Qualify(saved[i]!) : null;
        _bars[peer] = bar;
        Changed?.Invoke(peer, bar);
    }

    /// <summary>Host: forgets a player who left.</summary>
    public void Forget(int peer) => _bars.Remove(peer);

    private string?[] Bar(int peer)
    {
        if (!_bars.TryGetValue(peer, out var bar))
            _bars[peer] = bar = [.. Default];
        return bar;
    }

    private bool Known(string? entry) => entry is not null && (HotbarTools.IsTool(entry) || catalog.FindDefinition(Identifiers.Qualify(entry)) is not null);

    private string? Validate(ActionCaller caller, SetHotbarSlotArgs args)
    {
        if (caller.Source != ActionSource.Player)
            return "Only players have a hotbar.";
        if (args.Slot is < 0 or >= SlotCount)
            return $"Hotbar slots are 0 to {SlotCount - 1}.";
        if (args.Entry is { } entry && !Known(entry))
            return $"There is no tool or item '{entry}'.";
        return null;
    }

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);
}

/// <summary>The inventory's category tabs (UI spec §3).</summary>
public enum InventoryTab
{
    /// <summary>Everything.</summary>
    All,

    /// <summary>Blocks and wedges.</summary>
    Blocks,

    /// <summary>Placeable parts with behaviour: interactive parts, containers, stations, lights and decor.</summary>
    Interactive,

    /// <summary>Hand tools and weapons.</summary>
    Tools,

    /// <summary>Crafting materials.</summary>
    Resources,

    /// <summary>Food, seeds, fuel and other things used up.</summary>
    Consumables,
}

/// <summary>Which tab an item shows under.</summary>
public static class InventoryTabs
{
    /// <summary>True when an item of <paramref name="category"/> (or a tool, <paramref name="tool"/>) shows under <paramref name="tab"/>.</summary>
    public static bool Shows(InventoryTab tab, PartCategory? category, bool tool = false) => tab == InventoryTab.All || Of(category, tool) == tab;

    /// <summary>The tab an item of <paramref name="category"/> belongs to (tools under Tools); null for none but All.</summary>
    public static InventoryTab? Of(PartCategory? category, bool tool = false) => tool ? InventoryTab.Tools : category switch
    {
        PartCategory.Block => InventoryTab.Blocks,
        PartCategory.Interactive or PartCategory.Container or PartCategory.Station or PartCategory.Light or PartCategory.Decor => InventoryTab.Interactive,
        PartCategory.Tool or PartCategory.Weapon => InventoryTab.Tools,
        PartCategory.Resource => InventoryTab.Resources,
        PartCategory.Consumable => InventoryTab.Consumables,
        _ => null,
    };

    /// <summary>The slot index after <paramref name="selected"/> moved <paramref name="step"/> along a <paramref name="count"/>-slot bar, wrapping.</summary>
    public static int Step(int selected, int step, int count = HotbarActions.SlotCount) => ((selected + step) % count + count) % count;
}
