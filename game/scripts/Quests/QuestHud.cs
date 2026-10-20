using Godot;
using Mechanica.Game.Ui;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Quests;
using Mechanica.Core.Ui;
using Mechanica.Core.World;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Quests;

/// <summary>
/// The quest tracker, the Logbook and dialogue (chunk 24, UI spec §5, §10). The tracker in the top right shows each
/// active quest's current step and progress, and the POIs quests revealed with how far away they are. Lines quests
/// say queue up in the dialogue box at the bottom. The Logbook key (L) opens the <see cref="LogbookWindow"/> with the
/// quest list, any choices waiting and the map, and frees the mouse; L, Escape or its X closes it again.
/// </summary>
public partial class QuestHud : CanvasLayer, IHudLayer
{
    private readonly Queue<DialogueLine> _queue = new();
    private PanelContainer _log = null!;
    private Label _logText = null!;
    private LogbookWindow _logbook = null!;
    // How far the dialogue line sits above the bottom edge.
    private const float DialogueMargin = 120;

    private PanelContainer _dialogue = null!;
    private Label _dialogueText = null!;
    private double _lineLeft;

    /// <summary>Quests. Set before adding to the tree.</summary>
    public QuestSync Quests { get; init; } = null!;

    /// <summary>Where choices go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>Players, for the local player's position. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The local player's map markers, or null for none (tests). Set before adding to the tree.</summary>
    public MapMarkerSync? Markers { get; init; }

    /// <summary>The Logbook map's numbers. Set before adding to the tree.</summary>
    public LogbookSettings MapSettings { get; init; } = new();

    /// <summary>The world the map shows, or null before it exists. Set before adding to the tree.</summary>
    public Func<WorldGenerator?> World { get; init; } = () => null;

    /// <summary>The compass, whose primary waypoint the Logbook sets; null for none.</summary>
    public CompassHud? Compass { get; set; }

    /// <summary>The Logbook window.</summary>
    public LogbookWindow Logbook => _logbook;

    /// <summary>True while the Logbook is open.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Hide GUI leaves the Logbook up while it is open.</summary>
    public bool KeepWhenHidden => IsOpen;

    /// <summary>The log's text now, for tests.</summary>
    public string LogText => _logText.Text;

    /// <summary>The line on screen now, or empty.</summary>
    public string Line => _dialogue.Visible ? _dialogueText.Text : "";

    /// <summary>Every line said this session, oldest first, as <c>Speaker: text</c>.</summary>
    public List<string> Heard { get; } = [];

    public override void _Ready()
    {
        AddToGroup(HudRoot.Group);
        _log = UiInput.KeepMouse(new PanelContainer { Name = "QuestLog" });
        AddChild(_log);
        // Top right, inside the safe area.
        _log.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight, Control.LayoutPresetMode.KeepSize, (int)HudRoot.Margin(GetViewport()).X);
        _log.OffsetTop = (int)HudRoot.Margin(GetViewport()).Y;
        _log.GrowHorizontal = Control.GrowDirection.Begin;
        var box = new VBoxContainer();
        _log.AddChild(box);
        // Quest names, steps and lines are the mod's own text.
        _logText = Loc.Verbatim(new Label { Name = "Text", CustomMinimumSize = new Vector2(320, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        box.AddChild(_logText);

        _logbook = new LogbookWindow
        {
            Visible = false,
            Quests = Quests,
            Choose = Choose,
            Markers = Markers,
            Settings = MapSettings,
            World = World,
            Waypoints = Quests.Waypoints,
            Primary = () => Compass?.Primary,
            SetPrimary = id => Compass?.SetPrimary(id),
            Player = () => Players.LocalPlayer is { } me ? (me.GlobalPosition, Core.Ui.Compass.Heading(me.Camera.GlobalRotation.Y)) : null,
        };
        _logbook.CloseRequested += Close;
        AddChild(_logbook);

        _dialogue = new PanelContainer { Name = "Dialogue", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_dialogue);
        _dialogue.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.KeepSize, (int)DialogueMargin);
        _dialogue.GrowHorizontal = Control.GrowDirection.Both;
        _dialogue.GrowVertical = Control.GrowDirection.Begin;
        _dialogueText = Loc.Verbatim(new Label
        {
            Name = "Text",
            CustomMinimumSize = new Vector2(560, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        _dialogue.AddChild(_dialogueText);

        Quests.Said += OnSaid;
        Quests.StateChanged += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        Quests.Said -= OnSaid;
        Quests.StateChanged -= Refresh;
    }

    /// <summary>True while dialogue waits (the opening cinematic plays): no line starts or runs out.</summary>
    public Func<bool> Holding { get; set; } = () => false;

    public override void _Process(double delta)
    {
        if (Holding())
            return;
        if (_dialogue.Visible)
        {
            // Above a tutorial tip, which shares the bottom centre, rather than on top of it.
            var bottom = GetViewport().GetVisibleRect().Size.Y - DialogueMargin;
            if (Ui.TutorialHints.Shown is { } tip)
                bottom = Mathf.Min(bottom, tip.Position.Y - 8);
            _dialogue.Position = new Vector2(_dialogue.Position.X, bottom - _dialogue.Size.Y);
            _lineLeft -= delta;
            if (_lineLeft <= 0)
                NextLine();
        }
        else if (_queue.Count > 0)
        {
            NextLine();
        }
        // Distances to revealed POIs change as the player moves.
        if (Engine.GetProcessFrames() % 30 == 0)
            Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Esc is the Logbook window's own.
        if (GameInput.Pressed(@event, InputActions.Logbook) && (IsOpen || Input.MouseMode == Input.MouseModeEnum.Captured))
        {
            if (IsOpen)
                Close();
            else
                Open();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Opens the Logbook and frees the mouse.</summary>
    public void Open()
    {
        IsOpen = true;
        _logbook.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
    }

    /// <summary>Closes the Logbook and captures the mouse again.</summary>
    public void Close()
    {
        IsOpen = false;
        _logbook.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        Refresh();
    }

    /// <summary>Asks the host to pick <paramref name="choice"/> for <paramref name="quest"/>.</summary>
    public void Choose(string quest, string choice) =>
        Actions.Request(QuestTracker.ChooseQuestOption, new ChooseQuestOptionArgs(quest, choice), result =>
        {
            _logbook.Answered(result);
            Refresh();
        });

    private void OnSaid(QuestDialogue dialogue)
    {
        foreach (var line in dialogue.Lines)
        {
            _queue.Enqueue(line);
            Heard.Add($"{line.Speaker}: {line.Text}");
        }
    }

    private void NextLine()
    {
        if (!_queue.TryDequeue(out var line))
        {
            _dialogue.Visible = false;
            return;
        }
        _dialogueText.Text = string.IsNullOrEmpty(line.Speaker) ? line.Text ?? "" : $"{line.Speaker}: {line.Text}";
        _lineLeft = Math.Max(4.0, (line.Text?.Length ?? 0) * 0.06);
        _dialogue.Visible = true;
    }

    private void Refresh()
    {
        if (Quests.Tracker is null)
            return;
        var state = Quests.State;
        var lines = new List<string>();
        foreach (var quest in Quests.Book.All)
        {
            if (state.Of(quest.QualifiedId) is not { Status: QuestStatus.Active } progress || Quests.Tracker.CurrentStep(quest.QualifiedId) is not { } step)
                continue;
            lines.Add(quest.Name);
            lines.Add($"  {step.Text}{LogbookWindow.Progress(step, progress)}");
        }
        if (Quests.PendingChoices().Any() && !IsOpen)
            lines.Add(Loc.T("[{0}] decide", GameInput.Label(InputActions.Logbook)));
        var feet = Players.LocalPlayer?.GlobalPosition;
        foreach (var poi in state.Revealed)
        {
            foreach (var site in Quests.SitesOf(poi))
            {
                var name = Quests.World.WorldGen.Pois.FirstOrDefault(p => p.QualifiedId == poi)?.Prefab.Name ?? poi;
                lines.Add(feet is { } f ? $"{name}: {Where(f, site.Centre)}" : name);
            }
        }
        _logText.Text = lines.Count == 0 ? Loc.T("No quests right now.") : string.Join("\n", lines);
        // The Logbook shows all of it while it is open.
        _log.Visible = lines.Count > 0 && !IsOpen;
        _logbook.Refresh();
    }

    // Distance and compass direction, e.g. "420 m north-east".
    private static string Where(Vector3 from, System.Numerics.Vector3 to)
    {
        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        // -z is north.
        var bearing = (MathF.Atan2(dx, -dz) * 180f / MathF.PI + 360f) % 360f;
        string[] names = [Loc.T("north"), Loc.T("north-east"), Loc.T("east"), Loc.T("south-east"), Loc.T("south"), Loc.T("south-west"), Loc.T("west"), Loc.T("north-west")];
        return Loc.T("{0} m {1}", $"{distance:0}", names[(int)MathF.Round(bearing / 45f) % 8]);
    }
}
