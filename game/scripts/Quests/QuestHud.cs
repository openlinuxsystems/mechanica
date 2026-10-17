using Godot;
using Mechanica.Game.Ui;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Quests;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Quests;

/// <summary>
/// The quest log and dialogue (chunk 24). The log in the top right shows each active quest's current step
/// and progress, and the POIs quests revealed with how far away they are. Lines quests say queue up in the
/// dialogue box at the bottom. When a quest waits on a choice, J opens the log with a button per choice
/// (and frees the mouse); J or Escape closes it again.
/// </summary>
public partial class QuestHud : CanvasLayer, IHudLayer
{
    private readonly Queue<DialogueLine> _queue = new();
    private PanelContainer _log = null!;
    private Label _logText = null!;
    private VBoxContainer _choices = null!;
    // How far the dialogue line sits above the bottom edge.
    private const float DialogueMargin = 120;

    private PanelContainer _dialogue = null!;
    private Label _dialogueText = null!;
    private Label _status = null!;
    private double _lineLeft;
    private string _shownChoices = "";
    private bool _closeWhenDecided;

    /// <summary>Quests. Set before adding to the tree.</summary>
    public QuestSync Quests { get; init; } = null!;

    /// <summary>Where choices go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>Players, for the local player's position. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>True while the log is open with its choices.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Hide GUI leaves the log up while it is open with its choices.</summary>
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
        _log.GrowHorizontal = Control.GrowDirection.Begin;
        var box = new VBoxContainer();
        _log.AddChild(box);
        // Quest names, steps and lines are the mod's own text.
        _logText = Loc.Verbatim(new Label { Name = "Text", CustomMinimumSize = new Vector2(320, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        box.AddChild(_logText);
        _choices = new VBoxContainer { Name = "Choices", Visible = false };
        box.AddChild(_choices);
        _status = new Label { Name = "Status", ThemeTypeVariation = "ErrorLabel" };
        box.AddChild(_status);

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

    public override void _Process(double delta)
    {
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
        if (GameInput.Pressed(@event, InputActions.Logbook) && (IsOpen || Input.MouseMode == Input.MouseModeEnum.Captured))
        {
            if (IsOpen)
                Close();
            else
                Open();
            GetViewport().SetInputAsHandled();
        }
        else if (IsOpen && GameInput.Pressed(@event, InputActions.Pause))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Opens the log with the pending choices and frees the mouse.</summary>
    public void Open()
    {
        IsOpen = true;
        _status.Text = "";
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
    }

    /// <summary>Closes the log's choices and captures the mouse again.</summary>
    public void Close()
    {
        IsOpen = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        Refresh();
    }

    /// <summary>Asks the host to pick <paramref name="choice"/> for <paramref name="quest"/>.</summary>
    public void Choose(string quest, string choice) =>
        Actions.Request(QuestTracker.ChooseQuestOption, new ChooseQuestOptionArgs(quest, choice), result =>
        {
            _status.Text = result.Ok ? "" : result.Message ?? "";
            // The new state can land after the result; Refresh closes the log once it has.
            _closeWhenDecided = result.Ok;
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
            lines.Add($"  {step.Text}{Progress(step, progress)}");
        }
        var pending = Quests.PendingChoices().ToList();
        if (_closeWhenDecided && pending.Count == 0)
        {
            _closeWhenDecided = false;
            if (IsOpen)
            {
                Close();
                return;
            }
        }
        if (pending.Count > 0 && !IsOpen)
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
        _log.Visible = lines.Count > 0 || IsOpen;

        _choices.Visible = IsOpen && pending.Count > 0;
        // Rebuilt only when the choices change, so a periodic refresh doesn't swallow a click.
        var shown = _choices.Visible ? string.Join(";", pending.Select(p => $"{p.Quest.QualifiedId}/{p.Step.Id}")) : "";
        if (shown == _shownChoices)
            return;
        _shownChoices = shown;
        foreach (var child in _choices.GetChildren())
            child.QueueFree();
        if (!_choices.Visible)
            return;
        foreach (var (quest, step) in pending)
        {
            foreach (var choice in step.Choices)
            {
                var id = quest.QualifiedId;
                var button = Loc.Verbatim(new Button { Name = $"Choice_{choice.Id}", Text = choice.Text ?? choice.Id });
                button.Pressed += () => Choose(id, choice.Id!);
                _choices.AddChild(button);
            }
        }
    }

    private static string Progress(QuestStepInfo step, QuestProgress progress) => step.Kind switch
    {
        QuestObjectiveKind.Interact or QuestObjectiveKind.Extinguish or QuestObjectiveKind.Place or QuestObjectiveKind.Craft or QuestObjectiveKind.Crops
            or QuestObjectiveKind.Raid
            when step.Count > 1 => $" ({progress.Count}/{step.Count})",
        QuestObjectiveKind.Drive => " " + Loc.T("({0}/{1} m)", (int)progress.Distance, (int)step.Distance),
        _ => "",
    };

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
