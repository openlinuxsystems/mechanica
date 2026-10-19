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
/// name and key. Keys 1 to 9 and 0 use a slot's part at once, Left Click uses the first slot's and Right Click the
/// second's; with the mouse free a click on a slot uses it. A part that is held (a button, <see cref="Core.Data.LogicDefinition.Hold"/>)
/// stays held while its key or the mouse button is down and lets go with it. A slot glows and pulses while its part is on.
/// The host lets a seated player use the parts on their seat's bar from anywhere on the Creation.
/// </summary>
public partial class CreationHotbar : CanvasLayer
{
    /// <summary>Seconds between pulses of a slot whose part is on.</summary>
    public const double PulseEvery = 0.8;

    private readonly List<ItemSlot> _slots = [];
    private readonly Dictionary<int, double> _pulses = [];
    private readonly Dictionary<int, (LogicPartArgs Part, Func<bool> Down)> _held = [];
    private HBoxContainer _bar = null!;
    private Label _status = null!;

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
                ? (c.DefinitionOf(Parts[index]).Name ?? c.FindPart(Parts[index])!.Part, Loc.T("Linked to your seat. [{0}] to {1} it.", Key(index), c.DefinitionOf(Parts[index]).Logic?.Use ?? ""))
                : null;
            slot.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    Use(index, () => Input.IsMouseButtonPressed(MouseButton.Left));
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
            Use(key - 1, () => Input.IsActionPressed(InputActions.Hotbar(key)));
        else if (GameInput.Pressed(@event, InputActions.Primary))
            Use(0, () => Input.IsActionPressed(InputActions.Primary));
        else if (GameInput.Pressed(@event, InputActions.Secondary) && Parts.Count > 1)
            Use(1, () => Input.IsActionPressed(InputActions.Secondary));
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Uses the part in slot <paramref name="slot"/> (0 based) on the host. A held part (a button) stays held until
    /// <see cref="Release"/>, or until <paramref name="down"/> (the key or mouse button that used it) says it is up.
    /// False when the slot is empty.
    /// </summary>
    public bool Use(int slot, Func<bool>? down = null)
    {
        if (Seat is not { } seat || slot < 0 || slot >= Parts.Count)
            return false;
        _slots[slot].Pulse();
        var part = new LogicPartArgs(seat.Creation, Parts[slot]);
        var held = Creations.Find(seat.Creation)?.DefinitionOf(Parts[slot]).Logic?.Hold == true;
        if (held)
        {
            if (_held.ContainsKey(slot))
                return true;
            _held[slot] = (part, down ?? (() => true));
        }
        Actions.Request(LogicActions.ActivatePart, part, result =>
        {
            _status.Text = result.Ok ? "" : result.Message ?? "";
            if (!result.Ok && held && _held.TryGetValue(slot, out var h) && h.Part == part)
                _held.Remove(slot);
        });
        // Read the bus again soon so the slot shows its part's new state.
        Signals.Poll(seat.Creation);
        return true;
    }

    /// <summary>Lets go of the held part in slot <paramref name="slot"/>. False when it holds none.</summary>
    public bool Release(int slot)
    {
        if (!_held.Remove(slot, out var held))
            return false;
        Actions.Request(LogicActions.ReleasePart, held.Part, result => _status.Text = result.Ok ? "" : result.Message ?? "");
        Signals.Poll(held.Part.Creation);
        return true;
    }

    /// <summary>True while the part in slot <paramref name="slot"/> is held down from this bar.</summary>
    public bool Holding(int slot) => _held.ContainsKey(slot);

    // Lets go of held parts whose key or mouse button is up, or that left the bar (the player stood up).
    private void ReleaseLetGo()
    {
        foreach (var (slot, (part, down)) in _held.ToList())
        {
            var still = Seat is { } seat && seat.Creation == part.Creation && slot < Parts.Count && Parts[slot] == part.Part;
            if (!still || (ReadDeviceInput && !down()))
                Release(slot);
        }
    }

    public override void _Process(double delta)
    {
        Refresh();
        ReleaseLetGo();
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
            slot.Show(type, Icons?.For(type), creation.DefinitionOf(part).Name ?? type, null, GameInput.Label(InputActions.Hotbar(i + 1)));
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

    // A slot's keys for its tooltip: its number, and the mouse button that also uses it (the first two slots).
    private static string Key(int slot) => slot switch
    {
        0 => $"{GameInput.Label(InputActions.Hotbar(1))} / {GameInput.Label(InputActions.Primary)}",
        1 => $"{GameInput.Label(InputActions.Hotbar(2))} / {GameInput.Label(InputActions.Secondary)}",
        _ => GameInput.Label(InputActions.Hotbar(slot + 1)),
    };

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
