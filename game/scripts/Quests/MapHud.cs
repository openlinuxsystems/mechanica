using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Settings;
using Mechanica.Core.Ui;
using Mechanica.Core.World;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Settings;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Quests;

/// <summary>
/// The maps: the minimap in the bottom right corner (Options, Interface, Show minimap) and the full map
/// (<see cref="MapWindow"/>), which the Map key (M) opens over the world and frees the mouse for; M, Escape or its X
/// closes it again. Both show the current missions' goals and the player's own labelled pins.
/// </summary>
public partial class MapHud : CanvasLayer, IHudLayer
{
    /// <summary>The minimap's side, in pixels.</summary>
    public const float MinimapSize = 200;

    private MapWindow _window = null!;
    private MinimapView _minimap = null!;

    /// <summary>Quests, for the missions' goals. Set before adding to the tree.</summary>
    public QuestSync Quests { get; init; } = null!;

    /// <summary>Players, for the local player. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The local player's pins, or null for none (tests). Set before adding to the tree.</summary>
    public MapMarkerSync? Markers { get; init; }

    /// <summary>The map's numbers. Set before adding to the tree.</summary>
    public LogbookSettings Settings { get; init; } = new();

    /// <summary>The world, or null before it exists. Set before adding to the tree.</summary>
    public Func<WorldGenerator?> World { get; init; } = () => null;

    /// <summary>The compass, whose primary waypoint a click on a pin sets; null for none.</summary>
    public CompassHud? Compass { get; set; }

    /// <summary>The player's own knockout bags, pinned like places a quest revealed.</summary>
    public Func<IEnumerable<Waypoint>> KnockoutBags { get; init; } = () => [];

    /// <summary>The full map.</summary>
    public MapWindow Window => _window;

    /// <summary>The minimap.</summary>
    public MinimapView Minimap => _minimap;

    /// <summary>True when M may open the full map: while playing, with the mouse captured (not in chat or another window).</summary>
    public Func<bool> CanOpen { get; set; } = () => Input.MouseMode == Input.MouseModeEnum.Captured;

    /// <summary>True while the full map is open.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Hide GUI leaves the full map up while it is open.</summary>
    public bool KeepWhenHidden => IsOpen;

    /// <summary>The top edge of the minimap while it shows, or 0, so what else sits bottom right (the ammo counter) goes above it.</summary>
    public static float MinimapTop { get; private set; }

    public override void _Ready()
    {
        AddToGroup(HudRoot.Group);
        _minimap = new MinimapView
        {
            Name = "Minimap",
            Settings = Settings,
            World = World,
            Pins = Pins,
            Primary = () => Compass?.Primary,
            Player = PlayerView,
            CustomMinimumSize = new Vector2(MinimapSize, MinimapSize),
            Size = new Vector2(MinimapSize, MinimapSize),
        };
        AddChild(_minimap);
        GetViewport().SizeChanged += Layout;
        Layout();

        _window = new MapWindow
        {
            Visible = false,
            Markers = Markers,
            Settings = Settings,
            World = World,
            Pins = Pins,
            Primary = () => Compass?.Primary,
            SetPrimary = id => Compass?.SetPrimary(id),
            Player = PlayerView,
        };
        _window.CloseRequested += Close;
        AddChild(_window);
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        MinimapTop = 0;
    }

    public override void _Process(double delta)
    {
        var show = GameSettings.Current.Interface.ShowMinimap && !IsOpen && Players.LocalPlayer is not null && World() is not null;
        if (_minimap.Visible != show)
            _minimap.Visible = show;
        MinimapTop = show && Visible ? _minimap.Position.Y : 0;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Esc is the window's own.
        if (GameInput.Pressed(@event, InputActions.Map) && (IsOpen || CanOpen()))
        {
            if (IsOpen)
                Close();
            else
                Open();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Opens the full map on the player and frees the mouse.</summary>
    public void Open()
    {
        IsOpen = true;
        _window.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _window.FindMe();
    }

    /// <summary>Closes the full map and captures the mouse again.</summary>
    public void Close()
    {
        IsOpen = false;
        _window.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>The pins both maps show: the current missions' goals, the player's knockout bags and their own pins.</summary>
    public IReadOnlyList<(Waypoint Waypoint, LogbookMarkKind Kind)> Pins()
    {
        if (Players.LocalPlayer is not { } me)
            return [];
        var pins = Quests.Waypoints(me.GlobalPosition.ToNumerics())
            .Where(w => w.Id.StartsWith("quest:", StringComparison.Ordinal))
            .Select(w => (w, LogbookMarkKind.Quest)).ToList();
        pins.AddRange(KnockoutBags().Select(w => (w, LogbookMarkKind.Place)));
        pins.AddRange((Markers?.Own ?? []).Select(m => (m.ToWaypoint(), LogbookMarkKind.Own)));
        return pins;
    }

    private (Vector3 Feet, float Heading)? PlayerView() =>
        Players.LocalPlayer is { } me ? (me.GlobalPosition, Core.Ui.Compass.Heading(me.Camera.GlobalRotation.Y)) : null;

    // Bottom right, inside the safe area.
    private void Layout()
    {
        var margin = HudRoot.Margin(GetViewport());
        var size = GetViewport().GetVisibleRect().Size;
        _minimap.Position = new Vector2(size.X - margin.X - MinimapSize, size.Y - margin.Y - MinimapSize);
    }
}
