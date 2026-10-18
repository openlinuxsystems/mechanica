using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Logic;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Logic;

/// <summary>
/// The creation hotbar (UI spec §8, §10): while the local player sits in a seat linked to switches or buttons
/// (<see cref="SeatHotbar"/>), those parts take the hotbar's place at the bottom centre, one slot each with its picture,
/// name and key. Keys 1 to 9 and 0 use a slot's part at once; the wheel picks a slot and Left Click uses the picked
/// one; with the mouse free a click on a slot uses it. A slot glows and pulses while its part is on. The host lets a
/// seated player use the parts on their seat's bar from anywhere on the Creation.
/// </summary>
public partial class CreationHotbar : CanvasLayer
{
    /// <summary>Seconds between pulses of a slot whose part is on.</summary>
    public const double PulseEvery = 0.8;

    private readonly List<ItemSlot> _slots = [];
    private readonly Dictionary<int, double> _pulses = [];
    private HBoxContainer _bar = null!;
    private Label _status = null!;
    private int _picked;

    /// <summary>Seats. Set before adding to the tree.</summary>
    public LogicSync Logic { get; init; } = null!;

    /// <summary>Players, for the local one. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The Creations shown. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Where uses go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>The parts' signals, to show which are on. Set before adding to the tree.</summary>
    public LogicOverlay Signals { get; init; } = null!;

    /// <summary>Item pictures; null shows initials.</summary>
    public ItemIcons? Icons { get; init; }

    /// <summary>The tools, whose own hotbar steps aside while this one shows; null for none.</summary>
    public Func<ToolBelt?>? Belt { get; init; }

    /// <summary>False in automated tests: keys and the mouse do nothing (tests call <see cref="Use"/>).</summary>
    public bool ReadDeviceInput { get; init; } = true;

    /// <summary>The seat whose bar shows, or null.</summary>
    public SeatAssignment? Seat { get; private set; }

    /// <summary>The parts on the bar, left to right.</summary>
    public IReadOnlyList<int> Parts { get; private set; } = [];

    /// <summary>True while the bar shows.</summary>
    public bool Showing => Parts.Count > 0;

    /// <summary>The slot controls, left to right.</summary>
    public IReadOnlyList<ItemSlot> SlotViews => _slots;

    /// <summary>The slot the wheel picked (0 based).</summary>
    public int Picked => _picked;

    public override void _Ready()
    {
        Name = "CreationHotbar";
        AddToGroup(HudRoot.Group);
        _bar = new HBoxContainer { Name = "Bar", Visible = false };
        _bar.AddThemeConstantOverride("separation", HotbarView.Gap);
        _bar.Theme = StationTheme.Theme;
        UiInput.KeepMouse(_bar);
        AddChild(_bar);
        _status = new Label { Name = "Status", HorizontalAlignment = HorizontalAlignment.Center };
        AddChild(_status);
        for (var i = 0; i < SeatHotbar.Slots; i++)
        {
            var index = i;
            var slot = new ItemSlot { Name = $"Slot{i + 1}", Tiled = true };
            slot.Details = () => index < Parts.Count && Seat is { } seat && Creations.Find(seat.Creation) is { } c
                ? (c.DefinitionOf(Parts[index]).Name ?? c.FindPart(Parts[index])!.Part, Loc.T("Linked to your seat. [{0}] to {1} it.", GameInput.Label(InputActions.Hotbar(index + 1)), c.DefinitionOf(Parts[index]).Logic?.Use ?? ""))
                : null;
            slot.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    Use(index);
            };
            _slots.Add(slot);
            _bar.AddChild(slot);
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Ahead of the tools: while the bar shows, its keys and the click are its own.
        if (!ReadDeviceInput || !Showing || Input.MouseMode != Input.MouseModeEnum.Captured)
            return;
        if (Enumerable.Range(1, InputActions.HotbarSlots).FirstOrDefault(s => GameInput.Pressed(@event, InputActions.Hotbar(s))) is var key and > 0)
            Use(key - 1);
        else if (GameInput.Pressed(@event, InputActions.HotbarNext))
            _picked = (_picked + 1) % Parts.Count;
        else if (GameInput.Pressed(@event, InputActions.HotbarPrevious))
            _picked = (_picked + Parts.Count - 1) % Parts.Count;
        else if (GameInput.Pressed(@event, InputActions.Primary))
            Use(_picked);
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Uses the part in slot <paramref name="slot"/> (0 based) on the host. False when the slot is empty.</summary>
    public bool Use(int slot)
    {
        if (Seat is not { } seat || slot < 0 || slot >= Parts.Count)
            return false;
        _picked = slot;
        _slots[slot].Pulse();
        Actions.Request(LogicActions.ActivatePart, new LogicPartArgs(seat.Creation, Parts[slot]),
            result => _status.Text = result.Ok ? "" : result.Message ?? "");
        // Read the bus again soon so the slot shows its part's new state.
        Signals.Poll(seat.Creation);
        return true;
    }

    public override void _Process(double delta)
    {
        Refresh();
        var belt = Belt?.Invoke();
        if (belt is not null)
            belt.CreationBarShown = Showing;
        _bar.Visible = Showing && !HudRoot.Hidden && !HudRoot.IsCovered && Settings.GameSettings.Current.Interface.ShowHotbar;
        _status.Visible = _bar.Visible;
        if (!Showing)
        {
            _status.Text = "";
            return;
        }
        Layout();
        var creation = Creations.Find(Seat!.Value.Creation);
        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            slot.Visible = i < Parts.Count;
            if (i >= Parts.Count || creation is null)
                continue;
            var part = Parts[i];
            var type = creation.FindPart(part)!.Part;
            var on = Signals.SignalOf(creation.Id, part) != 0;
            slot.Show(type, Icons?.For(type), creation.DefinitionOf(part).Name ?? type, null, GameInput.Label(InputActions.Hotbar(i + 1)), i == _picked);
            slot.SelfModulate = on ? UiTokens.Cyan.Lightened(0.4f) : Colors.White;
            // A part that is on pulses its slot, every PulseEvery seconds.
            if (on && (_pulses[part] = _pulses.GetValueOrDefault(part) - delta) <= 0)
            {
                slot.Pulse();
                _pulses[part] = PulseEvery;
            }
            else if (!on)
            {
                _pulses.Remove(part);
            }
        }
    }

    /// <summary>True when the part in slot <paramref name="slot"/> is on, as this peer last read the bus.</summary>
    public bool IsOn(int slot) => Seat is { } seat && slot < Parts.Count && Signals.SignalOf(seat.Creation, Parts[slot]) != 0;

    private void Refresh()
    {
        var peer = Players.LocalPlayer?.PeerId;
        Seat = peer is { } p ? Logic.Seats.SeatOf(p) : null;
        var parts = Seat is { } seat && Creations.Find(seat.Creation) is { } creation ? SeatHotbar.Parts(creation, seat.Part) : [];
        if (!parts.SequenceEqual(Parts))
        {
            Parts = parts;
            _picked = 0;
            _pulses.Clear();
        }
    }

    // Where the hotbar goes: bottom centre inside the safe area, sized like it.
    private void Layout()
    {
        var viewport = GetViewport();
        var screen = viewport.GetVisibleRect().Size;
        var margin = HudRoot.Margin(viewport);
        var size = HotbarView.SlotSizeFor(screen);
        foreach (var slot in _slots)
            slot.CustomMinimumSize = new Vector2(size, size);
        _bar.ResetSize();
        var width = size * Parts.Count + HotbarView.Gap * (Parts.Count - 1);
        _bar.Position = new Vector2((screen.X - width) / 2, screen.Y - margin.Y - size);
        _status.Size = new Vector2(screen.X, 24);
        _status.Position = new Vector2(0, _bar.Position.Y - 30);
    }
}
