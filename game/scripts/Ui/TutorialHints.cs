using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Ui;
using Mechanica.Game.Player;
using Mechanica.Game.Building;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Ui;

/// <summary>
/// First-time tips (UI spec §16 Gameplay, "Show tutorial hints"): when the player first starts, picks up
/// each tool or opens each screen, a short tip from <c>game/data/tutorial.json</c> shows at the bottom centre of
/// the screen (§8), above the tool bar, for a few seconds, with keys that follow the player's bindings. Holding Use
/// dismisses it early, with a ring filling as it is held; losing window focus cancels the hold. Each tip shows
/// once; the ones seen are kept in the player's settings, and Options can turn tips off or show them again.
/// </summary>
public partial class TutorialHints : CanvasLayer, IHudLayer
{
    /// <summary>Where the tips live.</summary>
    public const string DataPath = "res://data/tutorial.json";

    // Over every window (the inventory and station screens, tool windows and dialogs).
    private const int CoveredLayer = 15;

    private readonly Queue<TutorialHint> _queue = [];
    private PanelContainer _panel = null!;
    private Label _text = null!;
    private Label _dismissHint = null!;
    private ProgressRing _ring = null!;
    private HoldTimer _dismiss = null!;
    private double _left;

    /// <summary>The tips; the shipped file unless set before adding to the tree.</summary>
    public Tutorial Tutorial { get; set; } = null!;

    /// <summary>The name of the tool in hand, or null before the player has tools. Set before adding to the tree.</summary>
    public Func<string?> Tool { get; init; } = () => null;

    /// <summary>Screens that show a tip when first open, by name (<c>screen:name</c>). Set before adding to the tree.</summary>
    public IReadOnlyList<(string Name, Func<bool> IsOpen)> Screens { get; init; } = [];

    /// <summary>True while tips wait (the loading screen is up). Set before adding to the tree.</summary>
    public Func<bool> Waiting { get; init; } = () => false;

    /// <summary>True while Use is held to dismiss a tip; tests set it.</summary>
    public Func<bool> UseHeld { get; init; } = () => Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionPressed(InputActions.Interact);

    /// <summary>The ring that fills while Use is held.</summary>
    public ProgressRing Ring => _ring;

    /// <summary>How far (0..1) the hold on Use to put the tip away has got; the ring shows it from the next frame.</summary>
    public float HoldFraction => _dismiss.Fraction;

    /// <summary>The tip panel.</summary>
    public PanelContainer Panel => _panel;

    /// <summary>The tip on screen, or null.</summary>
    public TutorialHint? Showing { get; private set; }

    /// <summary>Where the tip shows on screen, or null when none does; other bottom-centre lines stack above it.</summary>
    public static Rect2? Shown { get; private set; }

    /// <inheritdoc/>
    public bool KeepWhenHidden => false;

    /// <summary>A tip about the window that is open stays over it; any other tip waits until the window closes.</summary>
    public bool KeepWhenCovered => Showing is { } hint && IsScreenTip(hint);

    /// <summary>The text on screen, with keys filled in.</summary>
    public string Text => _text.Text;

    public override void _Ready()
    {
        Layer = 4;
        AddToGroup(HudRoot.Group);
        Tutorial ??= Load();
        _dismiss = new HoldTimer(HudRoot.Settings.HintDismissSeconds);
        _panel = new PanelContainer { Name = "Hint", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _text = Loc.Verbatim(new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(460, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        column.AddChild(_text);
        _dismissHint = Loc.Verbatim(new Label { ThemeTypeVariation = "SecondaryLabel", HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(_dismissHint);
        row.AddChild(column);
        _ring = new ProgressRing { Name = "Ring", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Modulate = Colors.Transparent };
        row.AddChild(_ring);
        _panel.AddChild(row);
        AddChild(_panel);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Begin;
        Layout();
        GetViewport().SizeChanged += Layout;
        HudRoot.FocusLost += CancelHold;
    }

    public override void _ExitTree()
    {
        Shown = null;
        GetViewport().SizeChanged -= Layout;
        HudRoot.FocusLost -= CancelHold;
    }

    // Bottom centre, above the tool bar and the status line, inside the safe area.
    private void Layout() =>
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.KeepSize, (int)HudRoot.Margin(GetViewport()).Y + 110);

    // Just over the tool bar and its key line, however tall they are. A tip about the open window shows over it, the
    // window's own layer being under this one only while nothing covers the world.
    private void Place()
    {
        Layer = HudRoot.IsCovered ? CoveredLayer : 4;
        if (ToolBelt.HudTop is { } top)
            _panel.Position = new Vector2(_panel.Position.X, top - 8 - _panel.Size.Y);
    }

    private void CancelHold() => _dismiss.Cancel();

    /// <summary>Reads the shipped tips.</summary>
    public static Tutorial Load()
    {
        using var file = Godot.FileAccess.Open(DataPath, Godot.FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {DataPath}: {Godot.FileAccess.GetOpenError()}");
        return Tutorial.Parse(file.GetAsText());
    }

    public override void _Process(double delta)
    {
        if (Waiting())
            return;
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
        if (HudRoot.IsCovered && Showing is { } waiting && !IsScreenTip(waiting))
        {
            // The open window's own tip goes first; the one it covered comes back after.
            if (!_queue.Any(IsScreenTip))
                return;
            var rest = _queue.Where(h => !IsScreenTip(h)).Prepend(waiting).ToList();
            var screen = _queue.Where(IsScreenTip).ToList();
            _queue.Clear();
            foreach (var hint in screen.Concat(rest))
                _queue.Enqueue(hint);
            Clear();
        }
        if (Showing is not null && (_dismiss.Tick(UseHeld(), delta) || (_left -= delta) <= 0))
            Clear();
        // A hold that dismissed one tip has to be let go before it can dismiss the next.
        var fraction = Showing is null ? 0f : _dismiss.Fraction;
        _ring.Value = fraction;
        _ring.Modulate = Colors.White with { A = fraction > 0 ? 1 : 0 };
        if (Showing is null && _queue.TryDequeue(out var next))
            Show(next);
        Place();
        Shown = Visible && _panel.Visible ? _panel.GetGlobalRect() : null;
    }

    /// <summary>
    /// Queues the next unseen tip for <paramref name="trigger"/> (<c>start</c>, <c>tool:build</c>,
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
        _text.Text = Tutorial.Fill(Loc.T(hint.Text), id => InputActions.Find(id) is null ? null : GameInput.Label(id));
        _dismissHint.Text = Loc.T("Hold [{0}] to dismiss", GameInput.Label(InputActions.Interact));
        _panel.Visible = true;
        // Seen as soon as it shows, so quitting mid-tip doesn't show it again.
        var gameplay = GameSettings.Current.Gameplay;
        var seen = new SortedSet<string>(gameplay.SeenHints ?? [], StringComparer.Ordinal) { hint.Id };
        GameSettings.Save(GameSettings.Current with { Gameplay = gameplay with { SeenHints = seen } });
    }

    private static bool IsScreenTip(TutorialHint hint) => hint.Trigger.StartsWith("screen:", StringComparison.Ordinal);

    private void Clear()
    {
        Showing = null;
        _panel.Visible = false;
    }
}
