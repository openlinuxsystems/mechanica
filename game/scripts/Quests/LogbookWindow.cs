using Godot;
using Mechanica.Core.Quests;
using Mechanica.Core.Ui;
using Mechanica.Core.World;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Quests;

/// <summary>
/// The Logbook (UI spec §5, §10, §13), opened with L: the quest list on the left and the map on the right. Active
/// quests show their current step and progress, with a button per option when a quest waits on a choice; finished
/// quests are greyed out. Clicking a quest, a place found or one of the player's markers makes it the compass's
/// primary waypoint and centres the map on it. Below come the places quests revealed and the player's own markers,
/// each with a button to remove it. The map (<see cref="LogbookMap"/>) pans, zooms and takes new markers.
/// </summary>
public partial class LogbookWindow : ToolWindow
{
    private VBoxContainer _list = null!;
    private Label _status = null!;
    private string _shown = "";

    /// <summary>Quests. Set before adding to the tree.</summary>
    public QuestSync Quests { get; init; } = null!;

    /// <summary>Picks a quest's option (quest, choice). Set before adding to the tree.</summary>
    public Action<string, string> Choose { get; init; } = (_, _) => { };

    /// <summary>The local player's map markers, or null where there are none (tests).</summary>
    public MapMarkerSync? Markers { get; init; }

    /// <summary>The waypoints for a player at a place: quest goals and places found. Set before adding to the tree.</summary>
    public Func<System.Numerics.Vector3, IReadOnlyList<Waypoint>> Waypoints { get; init; } = _ => [];

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

    /// <summary>The quest list's rows, for tests.</summary>
    public VBoxContainer List => _list;

    /// <summary>The last refusal from the host, or empty.</summary>
    public string Status => _status.Text;

    public override void _Ready()
    {
        Title = "Logbook";
        Name = "Logbook";
        AnchorLeft = 0.08f;
        AnchorTop = 0.1f;
        AnchorRight = 0.92f;
        AnchorBottom = 0.9f;
        OffsetLeft = OffsetTop = OffsetRight = OffsetBottom = 0;
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 12);
        Body.AddChild(columns);

        var scroll = new ScrollContainer
        {
            Name = "QuestList",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.42f,
            CustomMinimumSize = new Vector2(280, 0),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        columns.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_list);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddChild(right);
        Map = new LogbookMap
        {
            Name = "Map",
            Settings = Settings,
            World = World,
            Marks = Marks,
            Primary = Primary,
            Player = Player,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(320, 240),
        };
        Map.Picked += Pick;
        Map.MarkRequested += at => Markers?.Add(at, done: Answered);
        Map.RemoveRequested += id => Markers?.Remove(id, Answered);
        right.AddChild(Map);
        var tools = new HBoxContainer();
        right.AddChild(tools);
        tools.AddChild(new Label
        {
            Text = Markers is null ? "Drag to pan, scroll to zoom, click a marker to follow it." : "Drag to pan, scroll to zoom, click a marker to follow it, right-click to mark a place.",
            ThemeTypeVariation = "SecondaryLabel",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        AddTool(tools, "−", "Zoom out", () => Map.Zoom(-2));
        AddTool(tools, "+", "Zoom in", () => Map.Zoom(2));
        AddTool(tools, "Whole map", "Show the whole island", Map.ShowAll);
        AddTool(tools, "Find me", "Centre the map on you", () =>
        {
            if (Player() is { } me)
                Map.CentreOn(me.Feet);
        });
        _status = new Label { Name = "Status", ThemeTypeVariation = "ErrorLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        right.AddChild(_status);

        Quests.StateChanged += Refresh;
        if (Markers is not null)
            Markers.OwnChanged += Refresh;
        VisibilityChanged += () =>
        {
            if (Visible)
                Refresh();
        };
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        Quests.StateChanged -= Refresh;
        if (Markers is not null)
            Markers.OwnChanged -= Refresh;
    }

    public override void _Process(double delta)
    {
        // The primary waypoint can change on the compass while the Logbook is open.
        if (Visible && Engine.GetProcessFrames() % 15 == 0)
            Refresh();
    }

    /// <summary>Makes <paramref name="id"/> the primary waypoint and centres the map on it.</summary>
    public void Pick(string id)
    {
        SetPrimary(id);
        if (Marks().FirstOrDefault(m => m.Waypoint.Id == id).Waypoint is { } waypoint)
            Map.CentreOn(waypoint.Position.ToGodot());
        Refresh();
    }

    /// <summary>Everything the map marks: quest goals, places found and the player's own markers.</summary>
    public IReadOnlyList<(Waypoint Waypoint, LogbookMarkKind Kind)> Marks()
    {
        var from = Player() is { } me ? me.Feet.ToNumerics() : System.Numerics.Vector3.Zero;
        var marks = Waypoints(from).Select(w => (w, w.Id.StartsWith("quest:", StringComparison.Ordinal) ? LogbookMarkKind.Quest : LogbookMarkKind.Place)).ToList();
        marks.AddRange((Markers?.Own ?? []).Select(m => (m.ToWaypoint(), LogbookMarkKind.Own)));
        return marks;
    }

    /// <summary>Rebuilds the list when what it shows changed (so a periodic refresh never swallows a click).</summary>
    public void Refresh()
    {
        if (_list is null || Quests.Tracker is null)
            return;
        var quests = Logbook.Quests(Quests.Book, Quests.State, Quests.Tracker.CurrentStep);
        var pending = Quests.PendingChoices().ToList();
        var marks = Marks();
        var primary = Primary();
        var shown = string.Join("|", quests.Select(q => $"{q.Quest.QualifiedId}:{q.Done}:{q.Step?.Id}:{q.Progress?.Count}:{(int)(q.Progress?.Distance ?? 0)}"))
            + "#" + string.Join("|", marks.Select(m => $"{m.Waypoint.Id}={m.Waypoint.Name}")) + "#" + primary
            + "#" + string.Join("|", pending.Select(p => $"{p.Quest.QualifiedId}/{p.Step.Id}"));
        if (shown == _shown)
            return;
        _shown = shown;
        foreach (var child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        Heading("Quests");
        if (quests.Count == 0)
            Note("No quests yet.");
        var waypoints = marks.Select(m => m.Waypoint.Id).ToHashSet();
        foreach (var entry in quests)
        {
            var id = entry.Quest.QualifiedId;
            if (entry.Done)
            {
                // Quest names and steps are the mod's own text.
                _list.AddChild(Loc.Verbatim(new Label
                {
                    Name = NodeName("Done", id),
                    Text = "✓ " + entry.Quest.Name,
                    ThemeTypeVariation = "SecondaryLabel",
                    Modulate = UiTokens.Disabled.Lightened(0.4f),
                }));
                continue;
            }
            var hasWaypoint = waypoints.Contains(entry.WaypointId);
            var button = Entry(NodeName("Quest", id), entry.Quest.Name, entry.WaypointId, primary, hasWaypoint);
            button.TooltipText = hasWaypoint ? "Show it on the map and follow it on the compass" : "This step has no place on the map";
            if (entry.Step is { } step)
            {
                _list.AddChild(Loc.Verbatim(new Label
                {
                    Text = "  " + step.Text + Progress(step, entry.Progress),
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    ThemeTypeVariation = "SecondaryLabel",
                }));
            }
            foreach (var (quest, waiting) in pending.Where(p => p.Quest.QualifiedId == id))
            {
                foreach (var choice in waiting.Choices)
                {
                    var choiceButton = Loc.Verbatim(new Button { Name = $"Choice_{choice.Id}", Text = choice.Text ?? choice.Id, CustomMinimumSize = new Vector2(0, 32) });
                    choiceButton.Pressed += () => Choose(quest.QualifiedId, choice.Id!);
                    _list.AddChild(choiceButton);
                }
            }
        }

        var places = marks.Where(m => m.Kind == LogbookMarkKind.Place).ToList();
        if (places.Count > 0)
        {
            Heading("Places found");
            foreach (var (place, _) in places)
                Entry(NodeName("Place", place.Id), place.Name, place.Id, primary, true).TooltipText = "Show it on the map and follow it on the compass";
        }

        if (Markers is not null)
        {
            Heading("Your markers");
            if (Markers.Own.Count == 0)
                Note("Right-click the map to mark a place.");
            foreach (var marker in Markers.Own)
            {
                var row = new HBoxContainer();
                _list.AddChild(row);
                var go = Entry(NodeName("Marker", marker.Id), marker.Name, marker.Id, primary, true, row);
                go.TooltipText = "Show it on the map and follow it on the compass";
                var remove = new Button { Name = "Remove", Text = "✕", TooltipText = "Remove this marker", CustomMinimumSize = new Vector2(32, 32) };
                var markerId = marker.Id;
                remove.Pressed += () => Markers.Remove(markerId, Answered);
                row.AddChild(remove);
            }
        }
    }

    private Button Entry(string name, string text, string waypoint, string? primary, bool enabled, Container? into = null)
    {
        var button = Loc.Verbatim(new Button
        {
            Name = name,
            Text = (waypoint == primary ? "◆ " : "") + text,
            Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 32),
            Disabled = !enabled,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        });
        button.Pressed += () => Pick(waypoint);
        (into ?? _list).AddChild(button);
        return button;
    }

    /// <summary>The name of a list row for <paramref name="id"/> (node names can't hold a colon).</summary>
    public static string NodeName(string kind, string id) => $"{kind}_{id.Replace(':', '_')}";

    private void Heading(string text) => _list.AddChild(new Label { Text = text, ThemeTypeVariation = "TitleLabel" });

    private void Note(string text) => _list.AddChild(new Label { Text = text, ThemeTypeVariation = "SecondaryLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart });

    private static void AddTool(HBoxContainer row, string text, string tooltip, Action pressed)
    {
        var button = new Button { Text = text, TooltipText = tooltip, CustomMinimumSize = new Vector2(32, 32) };
        button.Pressed += pressed;
        row.AddChild(button);
    }

    /// <summary>Shows why the host refused a request, or clears the last refusal.</summary>
    public void Answered(Core.Actions.ActionResult result) => _status.Text = result.Ok ? "" : result.Message ?? "";

    /// <summary>The progress shown after a step, e.g. " (2/5)".</summary>
    public static string Progress(QuestStepInfo step, QuestProgress? progress) => step.Kind switch
    {
        QuestObjectiveKind.Interact or QuestObjectiveKind.Extinguish or QuestObjectiveKind.Place or QuestObjectiveKind.Craft or QuestObjectiveKind.Crops
            or QuestObjectiveKind.Raid
            when step.Count > 1 => $" ({progress?.Count ?? 0}/{step.Count})",
        QuestObjectiveKind.Drive => " " + Loc.T("({0}/{1} m)", (int)(progress?.Distance ?? 0), (int)step.Distance),
        _ => "",
    };
}
