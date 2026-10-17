using Godot;
using Mechanica.Core.Items;

namespace Mechanica.Game.Ui;

/// <summary>What is being dragged between the inventory, a container and the hotbar (UI spec §3).</summary>
public partial class ItemDrag : RefCounted
{
    /// <summary>The tool or item id dragged.</summary>
    public string Entry { get; init; } = "";

    /// <summary>The hotbar slot it came from, or null.</summary>
    public int? HotbarSlot { get; init; }

    /// <summary>The inventory it came from, or null (the hotbar or the Creative list).</summary>
    public InventoryRef? From { get; init; }

    /// <summary>The slot in <see cref="From"/>.</summary>
    public int FromSlot { get; init; }

    /// <summary>How many to move from <see cref="From"/>, or null for the whole stack.</summary>
    public int? Count { get; init; }
}

/// <summary>
/// One slot of the hotbar or the inventory: the item's picture (or its initials until there is one), its count
/// and the key that picks it. A selected slot has the bright cyan border (§8) and pulses when it is picked
/// (§9: 0.2 s). Slots drag and drop through the callbacks, and the tooltip shows the item's details.
/// </summary>
public partial class ItemSlot : PanelContainer
{
    /// <summary>Seconds of the selection pulse.</summary>
    public const double PulseSeconds = 0.2;

    private static readonly StyleBoxFlat Normal = MechanicaTheme.Box(UiTokens.ControlFill, UiTokens.Blue, 1, 4);
    private static readonly StyleBoxFlat Picked = MechanicaTheme.SelectedSlot();
    private readonly TextureRect _icon = new() { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _initials = Loc.Verbatim(new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore });
    private readonly Label _count = Loc.Verbatim(new Label { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, MouseFilter = MouseFilterEnum.Ignore });
    private readonly Label _key = Loc.Verbatim(new Label { ThemeTypeVariation = "SecondaryLabel", MouseFilter = MouseFilterEnum.Ignore });
    private Tween? _pulse;
    private bool _selected;
    private bool _dragging;

    /// <summary>The tool or item shown, or null when empty.</summary>
    public string? Entry { get; private set; }

    /// <summary>The count shown, or null for none (a tool, or Creative).</summary>
    public int? Count { get; private set; }

    /// <summary>True when the slot has the cyan border.</summary>
    public bool Selected => _selected;

    /// <summary>The text standing in for a picture, empty when the picture shows.</summary>
    public string Initials => _initials.Text;

    /// <summary>What a drag from here carries, or null to drag nothing.</summary>
    public Func<ItemDrag?>? DragSource { get; set; }

    /// <summary>Whether a drag may drop here.</summary>
    public Func<ItemDrag, bool>? CanDrop { get; set; }

    /// <summary>What a drop here does.</summary>
    public Action<ItemDrag>? Dropped { get; set; }

    /// <summary>Raised on the slot a drag started from when it ends: true when something took the drop.</summary>
    public Action<bool>? DragEnded { get; set; }

    /// <summary>The tooltip's title and lines, or null for none.</summary>
    public Func<(string Title, string Body)?>? Details { get; set; }

    public ItemSlot()
    {
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", Normal);
        var stack = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(stack);
        foreach (var child in new Control[] { _icon, _initials, _count, _key })
        {
            stack.AddChild(child);
            child.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }
        _count.AddThemeFontSizeOverride("font_size", 13);
        _key.AddThemeFontSizeOverride("font_size", 11);
        // Something to show the tooltip for; the custom tooltip below draws it.
        TooltipText = " ";
    }

    /// <summary>Shows <paramref name="entry"/> with its picture (null: initials of <paramref name="name"/>) and count.</summary>
    public void Show(string? entry, Texture2D? icon, string name, int? count, string? key = null, bool selected = false, bool dim = false)
    {
        Entry = entry;
        Count = count;
        _icon.Texture = entry is null ? null : icon;
        _initials.Text = entry is null || icon is not null ? "" : ItemIcons.Initials(name);
        _count.Text = count is { } n && n > 1 ? $"{n}" : "";
        _key.Text = key ?? "";
        Modulate = Colors.White with { A = dim ? 0.35f : 1f };
        if (selected == _selected)
            return;
        _selected = selected;
        AddThemeStyleboxOverride("panel", selected ? Picked : Normal);
        if (selected)
            Pulse();
    }

    /// <summary>Plays the selection pulse: a quick swell and back.</summary>
    public void Pulse()
    {
        _pulse?.Kill();
        PivotOffset = Size / 2;
        _pulse = CreateTween();
        _pulse.TweenProperty(this, "scale", new Vector2(1.12f, 1.12f), PulseSeconds / 2);
        _pulse.TweenProperty(this, "scale", Vector2.One, PulseSeconds / 2);
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (DragSource?.Invoke() is not { } drag)
            return default;
        var preview = new ItemSlot { CustomMinimumSize = Size, Size = Size };
        preview.Show(Entry, _icon.Texture, Entry ?? "", Count);
        SetDragPreview(preview);
        _dragging = true;
        return drag;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationDragEnd || !_dragging)
            return;
        _dragging = false;
        DragEnded?.Invoke(GetViewport().GuiIsDragSuccessful());
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        data.Obj is ItemDrag drag && CanDrop?.Invoke(drag) == true;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (data.Obj is ItemDrag drag)
            Dropped?.Invoke(drag);
    }

    public override GodotObject? _MakeCustomTooltip(string forText)
    {
        if (Details?.Invoke() is not var (title, body))
            return null;
        var panel = new PanelContainer { ThemeTypeVariation = "TooltipPanel" };
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(Loc.Verbatim(new Label { Text = title, ThemeTypeVariation = "TitleLabel" }));
        if (body.Length > 0)
            column.AddChild(Loc.Verbatim(new Label { Text = body, ThemeTypeVariation = "SecondaryLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) }));
        return panel;
    }
}
