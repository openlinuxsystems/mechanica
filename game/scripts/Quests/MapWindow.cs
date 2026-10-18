using Godot;
using Mechanica.Core.Ui;
using Mechanica.Core.World;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Quests;

/// <summary>
/// The full map, opened with M: the whole world with the player, the current missions' goals (named) and the player's
/// own pins with their labels. Drag to pan, scroll to zoom; clicking a pin makes it the compass's primary waypoint.
/// A right click on open ground asks for a label and drops a pin there; a right click on one of the player's pins asks
/// before taking it away. The pins are the Logbook's map markers, so they show in both and on the compass.
/// </summary>
public partial class MapWindow : ToolWindow
{
    private HBoxContainer _naming = null!;
    private LineEdit _label = null!;
    private Label _status = null!;
    private Vector3? _pending;

    /// <summary>The local player's pins, or null where there are none (tests).</summary>
    public MapMarkerSync? Markers { get; init; }

    /// <summary>The pins to show: mission goals and the player's own. Set before adding to the tree.</summary>
    public Func<IReadOnlyList<(Waypoint Waypoint, LogbookMarkKind Kind)>> Pins { get; init; } = () => [];

    /// <summary>The primary waypoint's id, or null.</summary>
    public Func<string?> Primary { get; init; } = () => null;

    /// <summary>Makes a waypoint primary.</summary>
    public Action<string> SetPrimary { get; init; } = _ => { };

    /// <summary>The local player's feet and compass heading, or null.</summary>
    public Func<(Vector3 Feet, float Heading)?> Player { get; init; } = () => null;

    /// <summary>The world, for the map.</summary>
    public Func<WorldGenerator?> World { get; init; } = () => null;

    /// <summary>Map numbers.</summary>
    public LogbookSettings Settings { get; init; } = new();

    /// <summary>The map.</summary>
    public LogbookMap Map { get; private set; } = null!;

    /// <summary>The row that asks for a new pin's label; visible while a pin waits for one.</summary>
    public HBoxContainer Naming => _naming;

    /// <summary>The new pin's label.</summary>
    public LineEdit LabelEdit => _label;

    /// <summary>The last refusal from the host, or empty.</summary>
    public string Status => _status.Text;

    public override void _Ready()
    {
        Title = "Map";
        Name = "Map";
        AnchorLeft = 0.05f;
        AnchorTop = 0.06f;
        AnchorRight = 0.95f;
        AnchorBottom = 0.94f;
        OffsetLeft = OffsetTop = OffsetRight = OffsetBottom = 0;
        Map = new LogbookMap
        {
            Name = "WorldMap",
            Settings = Settings,
            World = World,
            Marks = Pins,
            Primary = Primary,
            Player = Player,
            LabelQuests = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(480, 280),
        };
        Map.Picked += SetPrimary;
        Map.MarkRequested += AskLabel;
        Map.RemoveRequested += AskRemove;
        Body.AddChild(Map);

        _naming = new HBoxContainer { Name = "Naming", Visible = false };
        Body.AddChild(_naming);
        _naming.AddChild(new Label { Text = "New pin:" });
        _label = new LineEdit
        {
            Name = "Label",
            PlaceholderText = "Label",
            MaxLength = Settings.MarkerNameLength,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _label.TextSubmitted += _ => PlacePin();
        _naming.AddChild(_label);
        var place = new Button { Name = "Place", Text = "Place pin", ThemeTypeVariation = "PrimaryButton", CustomMinimumSize = new Vector2(0, 32) };
        place.Pressed += PlacePin;
        _naming.AddChild(place);
        var cancel = new Button { Name = "Cancel", Text = "Cancel", CustomMinimumSize = new Vector2(0, 32) };
        cancel.Pressed += CancelPin;
        _naming.AddChild(cancel);

        var tools = new HBoxContainer();
        Body.AddChild(tools);
        tools.AddChild(new Label
        {
            Text = Markers is null ? "Drag to pan, scroll to zoom, click a pin to follow it." : "Drag to pan, scroll to zoom, click a pin to follow it, right-click to drop a pin.",
            ThemeTypeVariation = "SecondaryLabel",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        AddTool(tools, "−", "Zoom out", () => Map.Zoom(-2));
        AddTool(tools, "+", "Zoom in", () => Map.Zoom(2));
        AddTool(tools, "Whole map", "Show the whole island", Map.ShowAll);
        AddTool(tools, "Find me", "Centre the map on you", FindMe);
        _status = new Label { Name = "Status", ThemeTypeVariation = "ErrorLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Body.AddChild(_status);
        VisibilityChanged += () =>
        {
            if (!Visible)
                CancelPin();
        };
    }

    /// <summary>Centres the map on the player.</summary>
    public void FindMe()
    {
        if (Player() is { } me)
            Map.CentreOn(me.Feet);
    }

    /// <summary>Asks for the label of a pin at world <paramref name="at"/>.</summary>
    public void AskLabel(Vector3 at)
    {
        if (Markers is null)
            return;
        _pending = at;
        _label.Text = "";
        _naming.Visible = true;
        _label.GrabFocus();
    }

    /// <summary>Drops the pin waiting for a label, named with what was typed (or numbered when nothing was).</summary>
    public void PlacePin()
    {
        if (_pending is not { } at || Markers is null)
            return;
        var label = _label.Text.Trim();
        Markers.Add(at, label.Length == 0 ? null : label, Answered);
        CancelPin();
    }

    /// <summary>Forgets the pin waiting for a label.</summary>
    public void CancelPin()
    {
        _pending = null;
        if (_naming is null)
            return;
        _naming.Visible = false;
        _label.ReleaseFocus();
    }

    /// <summary>Asks before taking away the player's pin <paramref name="id"/>.</summary>
    public void AskRemove(string id)
    {
        if (Markers?.Own.FirstOrDefault(m => m.Id == id) is not { } marker)
            return;
        ConfirmDialog.Ask(this, "Remove pin", Loc.T("Remove the pin “{0}”?", marker.Name), "Remove", () => Markers.Remove(id, Answered));
    }

    /// <summary>Shows why the host refused a request, or clears the last refusal.</summary>
    public void Answered(Core.Actions.ActionResult result) => _status.Text = result.Ok ? "" : result.Message ?? "";

    private static void AddTool(HBoxContainer row, string text, string tooltip, Action pressed)
    {
        var button = new Button { Text = text, TooltipText = tooltip, CustomMinimumSize = new Vector2(32, 32) };
        button.Pressed += pressed;
        row.AddChild(button);
    }
}
