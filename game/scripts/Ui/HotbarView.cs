using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Items;
using Mechanica.Game.Building;
using Mechanica.Game.Player;

namespace Mechanica.Game.Ui;

/// <summary>
/// The hotbar at the bottom centre of the screen (UI spec §3, §8, §11): ten slots with each one's picture, how many
/// the player carries (none for tools, or in Creative) and its key. Slots are the design language's <c>Tile</c> blue;
/// the slot picked is <c>TileSelected</c> with a white ring and pulses as it is picked. Slots take 48 to 72 pixels by the screen's height. While the inventory is open, items
/// dragged onto a slot go in it, slots drag onto each other to swap, and a slot dragged off the bar empties it.
/// </summary>
public partial class HotbarView : HBoxContainer
{
    /// <summary>Pixels between slots.</summary>
    public const int Gap = 6;

    private readonly List<ItemSlot> _slots = [];

    /// <summary>The tools and slots shown. Set before adding to the tree.</summary>
    public ToolBelt Belt { get; init; } = null!;

    /// <summary>Pixels along each slot's side.</summary>
    public float SlotSize { get; private set; } = 60;

    /// <summary>The slot controls, left to right.</summary>
    public IReadOnlyList<ItemSlot> SlotViews => _slots;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", Gap);
        // The design language's tiles (UI spec §0, §3): Tile blue, the current slot TileSelected with a white ring.
        Theme = StationTheme.Theme;
        // While the mouse is free (the inventory open), clicks between slots stay off the game too.
        UiInput.KeepMouse(this);
        for (var i = 0; i < HotbarActions.SlotCount; i++)
        {
            var index = i;
            var slot = new ItemSlot { Name = $"Slot{i + 1}", CustomMinimumSize = new Vector2(SlotSize, SlotSize), Tiled = true };
            slot.DragSource = () => Belt.Slots[index] is { } entry ? new ItemDrag { Entry = entry, HotbarSlot = index } : null;
            slot.CanDrop = drag => Belt.Hotbar is not null && (drag.HotbarSlot is not null || Accepts(drag.Entry));
            slot.Dropped = drag =>
            {
                if (drag.HotbarSlot is { } from)
                    Belt.Hotbar!.Swap(from, index);
                else
                    Belt.Hotbar!.Set(index, drag.Entry);
            };
            // Dropped on nothing (off the bar, over the world) the slot empties.
            slot.DragEnded = taken =>
            {
                if (!taken)
                    Belt.Hotbar?.Set(index, null);
            };
            slot.Details = () => Belt.Slots[index] is { } entry ? ItemInfo.Details(Belt.Build.Catalog, entry, Count(entry), Belt.ToolFor(entry)) : null;
            slot.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    Belt.Select(index);
            };
            _slots.Add(slot);
            AddChild(slot);
        }
    }

    /// <summary>Pixels along each slot's side on a <paramref name="screen"/> of that size: 48 to 72 by its height.</summary>
    public static float SlotSizeFor(Vector2 screen) => Mathf.Clamp(screen.Y / 12f, 48f, 72f);

    /// <summary>Sizes the slots for a <paramref name="screen"/> and sits the bar at the bottom centre inside <paramref name="margin"/>.</summary>
    public void Layout(Vector2 screen, Vector2 margin)
    {
        SlotSize = SlotSizeFor(screen);
        foreach (var slot in _slots)
            slot.CustomMinimumSize = new Vector2(SlotSize, SlotSize);
        ResetSize();
        var width = SlotSize * HotbarActions.SlotCount + Gap * (HotbarActions.SlotCount - 1);
        Position = new Vector2((screen.X - width) / 2, screen.Y - margin.Y - SlotSize);
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        var slots = Belt.Slots;
        for (var i = 0; i < _slots.Count; i++)
        {
            var entry = slots[i];
            var key = GameInput.Label(InputActions.Hotbar(i + 1));
            var name = entry is null ? "" : Belt.ToolFor(entry) is { } tool ? Loc.T(tool.ToolName) : Belt.Build.Catalog.FindDefinition(entry)?.Name ?? entry;
            var count = entry is null ? null : Count(entry);
            var missing = entry is not null && Belt.ToolFor(entry) is null && count is 0;
            // A weapon shows its ammo in the count's place (UI spec §10).
            var badge = entry is null || Belt.ToolFor(entry) is not null ? null : Belt.Item.CounterFor(entry)?.Text;
            _slots[i].Show(entry, entry is null ? null : Belt.Icons?.For(entry), name, count, key, i == Belt.Selected, dim: missing, badge: badge);
        }
    }

    // How many the player carries; null for tools and in Creative, where items are unlimited.
    private int? Count(string entry)
    {
        if (Belt.ToolFor(entry) is not null || Belt.Survival.Own?.Mode != Core.Survival.GameMode.Survival)
            return null;
        return Belt.Inventory.Own?.Count(entry) ?? 0;
    }

    private bool Accepts(string entry) => HotbarTools.IsTool(entry) || Belt.Build.Catalog.FindDefinition(entry) is not null;
}

/// <summary>What an item's tooltip says (UI spec §3, §8): its name, description, stack and durability.</summary>
public static class ItemInfo
{
    /// <summary>The tooltip for <paramref name="entry"/>: title and body lines.</summary>
    public static (string Title, string Body) Details(Core.Data.IPartCatalog catalog, string entry, int? count, HandTool? tool = null)
    {
        if (tool is not null)
            return (Loc.T(tool.ToolName), Loc.T("A hand tool. Every player always has it."));
        var definition = catalog.FindDefinition(entry);
        var lines = new List<string>();
        if (definition?.Description is { Length: > 0 } description)
            lines.Add(description);
        var stack = Math.Max(1, definition?.StackSize ?? 1);
        if (count is { } n)
            lines.Add(stack > 1 ? Loc.T("Stack: {0} / {1}", n, stack) : Loc.T("Carried: {0}", n));
        else if (stack > 1)
            lines.Add(Loc.T("Stacks to {0}", stack));
        if (definition?.Durability is { } durability)
            lines.Add(Loc.T("Durability: {0}", durability));
        return (definition?.Name ?? entry, string.Join("\n", lines));
    }
}
