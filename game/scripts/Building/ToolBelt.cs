using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Items;
using Mechanica.Game.Items;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Settings;
using Mechanica.Game.Survival;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// The local player's hand tools and the hotbar that picks them (UI spec §3). Each of the ten hotbar slots
/// (<see cref="HotbarSync"/>) holds a tool, an item or nothing; keys 1 to 9 and 0 pick a slot and the wheel steps
/// along the bar. A tool slot puts that tool in hand. A placeable part puts the Hammer in hand with that part to
/// place, and any other item goes in hand as the item (<see cref="ItemTool"/>); an empty slot leaves the hand
/// empty. In Survival, items the player gets fill empty slots. The bar sits at the bottom centre inside the safe
/// area with the tool's keys above it; the crosshair is <see cref="Ui.WorldHud"/>'s.
/// </summary>
public partial class ToolBelt : Node
{
    private readonly List<HandTool> _tools = [];
    private readonly HashSet<string> _seenItems = [];
    private Label? _hud;
    private Label? _ammo;
    private HotbarView? _bar;
    private int _selected;

    /// <summary>The player whose tools these are. Set before adding to the tree.</summary>
    public PlayerController Player { get; init; } = null!;

    /// <summary>Where requests go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>Settings, content and the world. Set before adding to the tree.</summary>
    public BuildSync Build { get; init; } = null!;

    /// <summary>Where the tools' own feedback sounds play; null plays none.</summary>
    public Sound.SoundSync? Sounds { get; init; }

    /// <summary>Inventories, for the item in hand. Set before adding to the tree.</summary>
    public InventorySync Inventory { get; init; } = null!;

    /// <summary>Survival state, for the item in hand. Set before adding to the tree.</summary>
    public SurvivalSync Survival { get; init; } = null!;

    /// <summary>How items look in hand; null shows every item the same way.</summary>
    public Core.Characters.CharacterSettings? Characters { get; init; }

    /// <summary>The hotbar's slots; null holds every tool in key order and never changes.</summary>
    public HotbarSync? Hotbar { get; init; }

    /// <summary>Item pictures for the bar; null shows initials.</summary>
    public ItemIcons? Icons { get; init; }

    /// <summary>False in automated tests: keys and mouse do nothing and no HUD is shown.</summary>
    public bool ReadDeviceInput { get; init; } = true;

    /// <summary>The Hammer.</summary>
    public HammerTool Hammer { get; private set; } = null!;

    /// <summary>The Lift.</summary>
    public LiftTool Lift { get; private set; } = null!;

    /// <summary>The Weld Tool.</summary>
    public WeldTool Weld { get; private set; } = null!;

    /// <summary>The Paint Tool.</summary>
    public PaintTool Paint { get; private set; } = null!;

    /// <summary>The Sledgehammer.</summary>
    public SledgeTool Sledge { get; private set; } = null!;

    /// <summary>The item in hand: food, weapons, seeds and water.</summary>
    public ItemTool Item { get; private set; } = null!;

    /// <summary>The Connect Tool (chunk 10).</summary>
    public ConnectTool Connector { get; private set; } = null!;

    /// <summary>Every tool, in key order.</summary>
    public IReadOnlyList<HandTool> Tools => _tools;

    /// <summary>The tool in hand.</summary>
    public HandTool Current => _tools.First(t => t.Active);

    /// <summary>The hotbar slot picked, 0 to 9.</summary>
    public int Selected => _selected;

    /// <summary>What each hotbar slot holds.</summary>
    public IReadOnlyList<string?> Slots => Hotbar?.Own ?? HotbarActions.Default;

    /// <summary>The hotbar at the bottom of the screen, or null without a HUD.</summary>
    public HotbarView? Bar => _bar;

    /// <summary>The tool a hotbar entry names, or null when it names an item or nothing.</summary>
    public HandTool? ToolFor(string? entry) => entry switch
    {
        HotbarTools.Hammer => Hammer,
        HotbarTools.Lift => Lift,
        HotbarTools.Weld => Weld,
        HotbarTools.Paint => Paint,
        HotbarTools.Sledge => Sledge,
        HotbarTools.Connect => Connector,
        _ => null,
    };

    public override void _Ready()
    {
        Hammer = Add(new HammerTool { Name = "Hammer", Player = Player, Actions = Actions, Build = Build, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput });
        Lift = Add(new LiftTool { Name = "Lift", Player = Player, Actions = Actions, Build = Build, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput });
        Weld = Add(new WeldTool { Name = "Weld", Player = Player, Actions = Actions, Build = Build, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput });
        Paint = Add(new PaintTool { Name = "Paint", Player = Player, Actions = Actions, Build = Build, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput });
        Sledge = Add(new SledgeTool { Name = "Sledge", Player = Player, Actions = Actions, Build = Build, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput });
        Item = Add(new ItemTool { Name = "Item", Player = Player, Actions = Actions, Build = Build, Inventory = Inventory, Survival = Survival, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput, Characters = Characters });
        Connector = Add(new ConnectTool { Name = "Connect", Player = Player, Actions = Actions, Build = Build, Sounds = Sounds, ReadDeviceInput = ReadDeviceInput });
        Hammer.Active = true;
        // In Survival a part is placed from the inventory, so the ghost turns red with none left.
        Hammer.CanAfford = part => Survival.Own?.Mode != Core.Survival.GameMode.Survival || Inventory.Own?.Count(part) > 0;
        if (Hotbar is not null)
            Hotbar.OwnChanged += OnSlotsChanged;
        Inventory.ViewChanged += OnInventoryChanged;
        if (!ReadDeviceInput)
            return;
        var layer = new CanvasLayer { Name = "Hud" };
        layer.AddToGroup(HudRoot.Group);
        // The tool bar stays up under the inventory, which drops onto it; the tool's key line steps aside.
        layer.SetMeta(HudRoot.KeepWhenCoveredMeta, true);
        // Bottom centre, where the hotbar goes. Built from translated pieces (tool names, each tool's line) around item names.
        _hud = Loc.Verbatim(new Label
        {
            Name = "ToolLabel", GrowVertical = Control.GrowDirection.Begin, GrowHorizontal = Control.GrowDirection.Both,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center,
        });
        layer.AddChild(_hud);
        _bar = new HotbarView { Name = "Hotbar", Belt = this };
        layer.AddChild(_bar);
        // The weapon's ammo counter, bottom right inside the safe area (UI spec §10, §11).
        _ammo = Loc.Verbatim(new Label { Name = "AmmoCounter", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, ThemeTypeVariation = "TitleLabel" });
        layer.AddChild(_ammo);
        AddChild(layer);
        LayoutHud();
        GetViewport().SizeChanged += LayoutHud;
    }

    public override void _Process(double delta)
    {
        // What others see in the player's hand.
        Player.HeldProp = Current.Prop;
        if (_hud is null)
            return;
        _bar!.Visible = GameSettings.Current.Interface.ShowHotbar;
        _hud.Visible = _bar.Visible && !HudRoot.IsCovered;
        if (_hud.Visible)
            _hud.Text = Current.HudText();
        var ammo = AmmoText();
        _ammo!.Visible = ammo is not null && !HudRoot.IsCovered;
        _ammo.Text = ammo ?? "";
        HudTop = _hud.Visible && _hud.Text.Length > 0 ? _hud.GetGlobalRect().Position.Y
            : _bar.Visible ? _bar.GetGlobalRect().Position.Y
            : null;
    }

    /// <summary>
    /// The HUD's ammo counter for the weapon in hand (UI spec §10): the rounds loaded out of the magazine and the ammo
    /// carried, "Reloading…" while it reloads, or a reload hint when it is empty. Null with no weapon in hand.
    /// </summary>
    public string? AmmoText()
    {
        if (Current != Item || Item.InHand is not { } hand || Item.CounterFor(hand.Item) is not { } counter
            || Survival.CounterOf(hand.Item) is not { } view)
            return null;
        var ammoName = view.Ammo is { } ammo ? Build.Catalog.FindDefinition(ammo)?.Name ?? ammo : null;
        var carried = ammoName is null ? null : $"{ammoName} {counter.Carried}";
        var lines = new List<string> { counter.Loaded is null ? carried ?? counter.Text : carried is null ? counter.Text : $"{counter.Text}   {carried}" };
        if (Survival.BusyFor(hand.Item) > 0)
            lines.Add(Loc.T("Reloading…"));
        else if (counter.Loaded == 0 && counter.Carried > 0)
            lines.Add(Loc.T("[{0}] reload", GameInput.Label(InputActions.Reload)));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The top of the tool bar and the key line over it on screen, or null when neither shows; lines that share the
    /// bottom centre (tutorial tips) sit above it.
    /// </summary>
    public static float? HudTop { get; private set; }

    public override void _ExitTree()
    {
        HudTop = null;
        if (_hud is not null)
            GetViewport().SizeChanged -= LayoutHud;
        if (Hotbar is not null)
            Hotbar.OwnChanged -= OnSlotsChanged;
        Inventory.ViewChanged -= OnInventoryChanged;
    }

    // The bar at the bottom centre inside the safe area, the tool's keys just above it (wrapping at 60% of the width).
    private void LayoutHud()
    {
        var size = GetViewport().GetVisibleRect().Size;
        var margin = HudRoot.Margin(GetViewport());
        _bar!.Layout(size, margin);
        _hud!.CustomMinimumSize = new Vector2(size.X * 0.6f, 0);
        _hud.Size = _hud.CustomMinimumSize;
        _hud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.KeepSize, (int)(margin.Y + _bar.SlotSize + 8));
        // Anchored to the bottom-right corner and growing up and left as the text does.
        _ammo!.GrowHorizontal = _ammo.GrowVertical = Control.GrowDirection.Begin;
        _ammo.AnchorLeft = _ammo.AnchorRight = _ammo.AnchorTop = _ammo.AnchorBottom = 1f;
        _ammo.OffsetLeft = _ammo.OffsetRight = -margin.X;
        _ammo.OffsetTop = _ammo.OffsetBottom = -margin.Y;
    }

    /// <summary>The tool bar text at the bottom of the screen, or null without a HUD.</summary>
    public Label? HudLabel => _hud;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!ReadDeviceInput || Input.MouseMode != Input.MouseModeEnum.Captured)
            return;
        // The wheel steps along the bar unless a tool uses it with a modifier (Alt zooms, Shift turns).
        if (GameInput.Pressed(@event, InputActions.HotbarNext))
            Select(InventoryTabs.Step(_selected, 1));
        else if (GameInput.Pressed(@event, InputActions.HotbarPrevious))
            Select(InventoryTabs.Step(_selected, -1));
        else if (Enumerable.Range(1, InputActions.HotbarSlots).FirstOrDefault(s => GameInput.Pressed(@event, InputActions.Hotbar(s))) is var slot and > 0)
            Select(slot - 1);
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Picks hotbar slot <paramref name="slot"/> (0 to 9) and puts what it holds in hand.</summary>
    public void Select(int slot)
    {
        _selected = Math.Clamp(slot, 0, HotbarActions.SlotCount - 1);
        Apply();
    }

    // Puts the picked slot's tool or item in hand.
    private void Apply()
    {
        var entry = Slots[_selected];
        if (ToolFor(entry) is { } tool)
        {
            Use(tool);
            return;
        }
        if (entry is not null && Build.Catalog.FindDefinition(entry) is { IsPlaceable: true } && Hammer.Select(entry))
        {
            Use(Hammer);
            return;
        }
        Item.Choose(entry);
        Use(Item);
    }

    private void OnSlotsChanged() => Apply();

    // Survival: an item the player gets for the first time goes in the first empty slot, once.
    private void OnInventoryChanged(Core.Items.InventoryView view)
    {
        if (Hotbar is null || view.Inventory != InventoryRef.OfPlayer(Inventory.Session.LocalPeer) || Survival.Own?.Mode != Core.Survival.GameMode.Survival)
            return;
        var slots = Slots.ToList();
        foreach (var item in view.Items.Select(s => s.Item).Distinct())
        {
            if (!_seenItems.Add(item) || slots.Contains(item) || Build.Catalog.FindDefinition(item) is not { } d || !(d.IsPlaceable || d.Script is not null))
                continue;
            var empty = slots.IndexOf(null);
            if (empty < 0)
                return;
            slots[empty] = item;
            Hotbar.Set(empty, item);
        }
    }

    /// <summary>Puts <paramref name="tool"/> in hand.</summary>
    public void Use(HandTool tool)
    {
        foreach (var t in _tools)
            t.Active = t == tool;
    }

    private T Add<T>(T tool) where T : HandTool
    {
        AddChild(tool);
        _tools.Add(tool);
        return tool;
    }
}
