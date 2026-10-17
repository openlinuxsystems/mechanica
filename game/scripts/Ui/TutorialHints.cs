using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Ui;
using Mechanica.Game.Player;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Ui;

/// <summary>
/// First-time tips (UI spec §16 Gameplay, "Show tutorial hints"): when the player first starts, picks up
/// each tool or opens each screen, a short tip from <c>game/data/tutorial.json</c> shows at the top of the
/// screen for a few seconds, with keys that follow the player's bindings. Each tip shows once; the ones
/// seen are kept in the player's settings, and Options can turn tips off or show them again.
/// </summary>
public partial class TutorialHints : CanvasLayer
{
    /// <summary>Where the tips live.</summary>
    public const string DataPath = "res://data/tutorial.json";

    private readonly Queue<TutorialHint> _queue = [];
    private PanelContainer _panel = null!;
    private Label _text = null!;
    private double _left;

    /// <summary>The tips; the shipped file unless set before adding to the tree.</summary>
    public Tutorial Tutorial { get; set; } = null!;

    /// <summary>The name of the tool in hand, or null before the player has tools. Set before adding to the tree.</summary>
    public Func<string?> Tool { get; init; } = () => null;

    /// <summary>Screens that show a tip when first open, by name (<c>screen:name</c>). Set before adding to the tree.</summary>
    public IReadOnlyList<(string Name, Func<bool> IsOpen)> Screens { get; init; } = [];

    /// <summary>The tip on screen, or null.</summary>
    public TutorialHint? Showing { get; private set; }

    /// <summary>The text on screen, with keys filled in.</summary>
    public string Text => _text.Text;

    public override void _Ready()
    {
        Layer = 4;
        Tutorial ??= Load();
        _panel = new PanelContainer { Name = "Hint", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _text = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(460, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _panel.AddChild(_text);
        AddChild(_panel);
        // Under the compass (10% from the top, about 44 px tall).
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop, Control.LayoutPresetMode.KeepSize, (int)(GetViewport().GetVisibleRect().Size.Y * 0.1f) + 56);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
    }

    /// <summary>Reads the shipped tips.</summary>
    public static Tutorial Load()
    {
        using var file = Godot.FileAccess.Open(DataPath, Godot.FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {DataPath}: {Godot.FileAccess.GetOpenError()}");
        return Tutorial.Parse(file.GetAsText());
    }

    public override void _Process(double delta)
    {
        Fire("start");
        if (Tool() is { } tool)
            Fire($"tool:{tool.ToLowerInvariant()}");
        foreach (var (name, isOpen) in Screens)
        {
            if (isOpen())
                Fire($"screen:{name}");
        }

        if (!GameSettings.Current.Gameplay.ShowTutorialHints)
        {
            _queue.Clear();
            Clear();
            return;
        }
        if (Showing is not null && (_left -= delta) <= 0)
            Clear();
        if (Showing is null && _queue.TryDequeue(out var next))
            Show(next);
    }

    /// <summary>
    /// Queues the next unseen tip for <paramref name="trigger"/> (<c>start</c>, <c>tool:hammer</c>,
    /// <c>screen:inventory</c>, …), if any; nothing happens while tips are off. Called every frame for what
    /// the player is doing, so tips Options shows again come back as the player plays.
    /// </summary>
    public void Fire(string trigger)
    {
        if (!GameSettings.Current.Gameplay.ShowTutorialHints)
            return;
        var seen = new HashSet<string>(GameSettings.Current.Gameplay.SeenHints ?? [], StringComparer.Ordinal);
        seen.UnionWith(_queue.Select(h => h.Id));
        if (Showing is not null)
            seen.Add(Showing.Id);
        if (Tutorial.Next(trigger, seen) is { } hint)
            _queue.Enqueue(hint);
    }

    private void Show(TutorialHint hint)
    {
        Showing = hint;
        _left = Tutorial.HintSeconds;
        _text.Text = Tutorial.Fill(hint.Text, id => InputActions.Find(id) is null ? null : GameInput.Label(id));
        _panel.Visible = true;
        // Seen as soon as it shows, so quitting mid-tip doesn't show it again.
        var gameplay = GameSettings.Current.Gameplay;
        var seen = new SortedSet<string>(gameplay.SeenHints ?? [], StringComparer.Ordinal) { hint.Id };
        GameSettings.Save(GameSettings.Current with { Gameplay = gameplay with { SeenHints = seen } });
    }

    private void Clear()
    {
        Showing = null;
        _panel.Visible = false;
    }
}
