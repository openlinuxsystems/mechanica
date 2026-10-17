using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Sound;
using Mechanica.Core.Ui;
using Mechanica.Game.Player;
using Mechanica.Game.Settings;
using Mechanica.Game.Sound;

namespace Mechanica.Game.Ui;

/// <summary>
/// The world loading screen (UI spec §14–15): deep navy over everything, the blueprint chassis, a small glowing
/// logo, a cyan bar 60% of the screen wide with the percentage above it and the stage below, a tip that changes
/// every few seconds, a turning gear with "Loading World" bottom left and the world's name and mode bottom right.
/// Stage and tip text fade in as they change, with a soft tick, over the menu music. The bar follows
/// <see cref="Report"/>; once the world has entered and the minimum time has passed the screen fades to black and
/// back into the world, then frees itself. A failure turns the bar muted red and offers Return to Menu.
/// </summary>
public partial class LoadingScreen : CanvasLayer
{
    /// <summary>Where the timings live.</summary>
    public const string DataPath = "res://data/loading.json";

    private Control _root = null!;
    private Control _content = null!;
    private Label _percent = null!;
    private Bar _bar = null!;
    private Label _stage = null!;
    private Label _detail = null!;
    private Label _tip = null!;
    private Label _title = null!;
    private Label _world = null!;
    private VBoxContainer _failure = null!;
    private Label _error = null!;
    private Button _return = null!;
    private ColorRect _black = null!;
    private SoundPlayer _sound = null!;
    private Tween? _fade;
    private int _tipShown = -1;
    private LoadStage? _stageShown;
    private bool _leaving;

    /// <summary>The load the screen shows; from the shipped timings unless set before adding to the tree.</summary>
    public WorldLoading Loading { get; set; } = null!;

    /// <summary>Tips; the shipped ones unless set before adding to the tree.</summary>
    public Tutorial? Tips { get; set; }

    /// <summary>The label by the gear: "Loading World" unless set (as "Returning to menu…" is).</summary>
    public string Title { get; set; } = "Loading World";

    /// <summary>The world's name and mode, bottom right, once known. Polled each frame.</summary>
    public Func<string?> World { get; set; } = () => null;

    /// <summary>A line under the stage (the host's name and stage while waiting for it), or null. Polled each frame.</summary>
    public Func<string?> Detail { get; set; } = () => null;

    /// <summary>
    /// A transition with no stages ("Returning to menu…"): shown in place of the stage, with no bar, and the screen
    /// stays until whatever comes next replaces it. Null for a world load.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>Sounds for the music and the tick; quiet when null or in test scenes.</summary>
    public SoundRegistry? Sounds { get; set; }

    /// <summary>Raised by Return to Menu; with no handler it goes to the main menu.</summary>
    public event Action? ReturnRequested;

    /// <summary>Raised when the screen is black, just before it fades back into the world.</summary>
    public event Action? Entering;

    /// <summary>The stage text on screen.</summary>
    public string StageText => _stage.Text;

    /// <summary>The tip on screen.</summary>
    public string TipText => _tip.Text;

    /// <summary>The percentage on screen.</summary>
    public string PercentText => _percent.Text;

    /// <summary>The world's name and mode on screen.</summary>
    public string WorldText => _world.Text;

    /// <summary>The line under the stage on screen.</summary>
    public string DetailText => _detail.Text;

    /// <summary>The bar's colour now.</summary>
    public Color BarColour => _bar.Colour;

    /// <summary>True while the failure message and Return to Menu show.</summary>
    public bool Failed => _failure.Visible;

    /// <summary>The error on screen.</summary>
    public string ErrorText => _error.Text;

    /// <summary>The Return to Menu button.</summary>
    public Button ReturnButton => _return;

    /// <summary>True once the screen has started fading into the world.</summary>
    public bool Leaving => _leaving;

    /// <summary>
    /// Covers the screen with <paramref name="message"/> ("Returning to menu…"), lets it draw, then runs
    /// <paramref name="then"/> (a scene change); the screen goes once the next scene is in, so the change never
    /// shows a frozen frame.
    /// </summary>
    public static LoadingScreen Transition(Node from, string message, Action then)
    {
        var tree = from.GetTree();
        var screen = new LoadingScreen { Name = "Transition", Message = message };
        tree.Root.AddChild(screen);
        var frames = 0;
        void Step()
        {
            // Two frames: one to lay the screen out, one to draw it.
            if (++frames < 2)
                return;
            tree.ProcessFrame -= Step;
            tree.SceneChanged += Gone;
            then();
        }
        void Gone()
        {
            tree.SceneChanged -= Gone;
            if (IsInstanceValid(screen))
                screen.QueueFree();
        }
        tree.ProcessFrame += Step;
        return screen;
    }

    /// <summary>Reads the shipped timings.</summary>
    public static LoadingSettings LoadSettings() =>
        Godot.FileAccess.FileExists(DataPath) ? LoadingSettings.Parse(Godot.FileAccess.GetFileAsString(DataPath)) : new LoadingSettings();

    public override void _Ready()
    {
        Layer = 20;
        Loading ??= new WorldLoading(LoadSettings());
        Tips ??= TutorialHints.Load();
        _root = UiInput.KeepMouse(new Control { Name = "Loading" });
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        _root.AddChild(new ColorRect { Color = UiTokens.DeepNavy, AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore });
        _content = new Control { Name = "Content", AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_content);
        var art = new BlueprintChassis { Name = "Art", AnchorLeft = 0.2f, AnchorRight = 0.8f, AnchorTop = 0.16f, AnchorBottom = 0.56f };
        _content.AddChild(art);

        var logo = Logo.Create(44);
        logo.AnchorRight = 1;
        logo.OffsetTop = 24;
        _content.AddChild(logo);

        // The bar and its text, centred, 60% of the screen wide.
        var column = new VBoxContainer { Name = "Progress", AnchorLeft = 0.2f, AnchorRight = 0.8f, AnchorTop = 0.6f, AnchorBottom = 0.6f };
        column.AddThemeConstantOverride("separation", 8);
        _content.AddChild(column);
        _percent = Loc.Verbatim(new Label { Name = "Percent", HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(_percent);
        _bar = new Bar { Name = "Bar", CustomMinimumSize = new Vector2(0, 8), Colour = UiTokens.Cyan };
        column.AddChild(_bar);
        _stage = Loc.Verbatim(new Label { Name = "Stage", HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(_stage);
        _detail = Loc.Verbatim(new Label { Name = "Detail", HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "SecondaryLabel" });
        column.AddChild(_detail);
        _tip = Loc.Verbatim(new Label
        {
            Name = "Tip", HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "SecondaryLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        column.AddChild(_tip);
        _failure = new VBoxContainer { Name = "Failure", Visible = false };
        _failure.AddThemeConstantOverride("separation", 8);
        column.AddChild(_failure);
        _failure.AddChild(new Label { Text = "Unable to load world", ThemeTypeVariation = "ErrorLabel", HorizontalAlignment = HorizontalAlignment.Center });
        _error = Loc.Verbatim(new Label { Name = "Error", HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _failure.AddChild(_error);
        var centre = new CenterContainer();
        _failure.AddChild(centre);
        _return = new Button { Name = "Return", Text = "Return to Menu", ThemeTypeVariation = "PrimaryButton", CustomMinimumSize = new Vector2(200, 40) };
        _return.Pressed += Return;
        centre.AddChild(_return);

        // Bottom left the gear and title, bottom right the world.
        var bottom = new HBoxContainer { Name = "Bottom", AnchorTop = 1, AnchorRight = 1, AnchorBottom = 1, OffsetLeft = 24, OffsetRight = -24, OffsetTop = -56, OffsetBottom = -20 };
        _content.AddChild(bottom);
        bottom.AddChild(new Gear { CustomMinimumSize = new Vector2(28, 28) });
        _title = new Label { Name = "Title", Text = Title, VerticalAlignment = VerticalAlignment.Center };
        bottom.AddChild(_title);
        _world = Loc.Verbatim(new Label { Name = "World", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, ThemeTypeVariation = "SecondaryLabel" });
        bottom.AddChild(_world);

        _black = new ColorRect { Name = "Black", Color = Colors.Black with { A = 0 }, AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_black);

        _sound = new SoundPlayer { Name = "Sound" };
        AddChild(_sound);
        if (Sounds is { } sounds && GameSettings.Persist)
        {
            _sound.Registry = sounds;
            _sound.Play(MenuMusic.SoundId, Identifiers.VanillaModId, Vector3.Zero);
        }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
    }

    /// <summary>Reports <paramref name="stage"/> at <paramref name="fraction"/>; see <see cref="WorldLoading.Report"/>.</summary>
    public void Report(LoadStage stage, double fraction = 0) => Loading.Report(stage, fraction);

    /// <summary>Stops the load with <paramref name="message"/>: the bar turns red and Return to Menu shows.</summary>
    public void Fail(string message) => Loading.Fail(message);

    public override void _Process(double delta)
    {
        if (Message is not null)
        {
            Refresh();
            return;
        }
        Loading.Tick(delta, Loc.T("The world took too long to load."));
        Refresh();
        if (Loading.CanLeave && !_leaving)
            Leave();
    }

    // Goes to the main menu, or whatever handles it.
    private void Return()
    {
        if (ReturnRequested is { } handler)
            handler();
        else
            GetTree().ChangeSceneToFile(MainMenu.MenuScene);
    }

    private void Refresh()
    {
        _title.Text = Title;
        if (Message is { } message)
        {
            _percent.Visible = _bar.Visible = _detail.Visible = false;
            _stage.Text = Loc.T(message);
            _world.Text = World() ?? "";
            return;
        }
        _percent.Text = $"{Loading.Percent}%";
        _bar.Value = (float)Loading.Progress;
        if (Loading.Stage != _stageShown)
        {
            _stageShown = Loading.Stage;
            _stage.Text = Loc.T(WorldLoading.Text(Loading.Stage));
            FadeIn(_stage);
            if (_sound.Registry != SoundRegistry.Empty)
                _sound.Play("ui_click", Identifiers.VanillaModId, Vector3.Zero);
        }
        _detail.Text = Detail() ?? "";
        _detail.Visible = _detail.Text.Length > 0;
        _world.Text = World() ?? "";
        var tips = Tips?.Tips ?? [];
        var tip = Loading.TipIndex(tips.Count);
        if (tip != _tipShown && tip >= 0)
        {
            _tipShown = tip;
            _tip.Text = Tutorial.Fill(Loc.T(tips[tip]), id => InputActions.Find(id) is null ? null : GameInput.Label(id));
            FadeIn(_tip);
        }
        if (Loading.Error is { } error && !_failure.Visible)
        {
            _bar.Colour = UiTokens.Error with { A = 0.7f };
            _error.Text = error;
            _failure.Visible = true;
            _tip.Visible = false;
            _return.GrabFocus();
        }
    }

    private void FadeIn(Control label)
    {
        label.Modulate = Colors.White with { A = 0 };
        label.CreateTween().TweenProperty(label, "modulate:a", 1f, Loading.Settings.FadeSeconds);
    }

    // Fade to black, drop the screen, fade back in to the world.
    private void Leave()
    {
        _leaving = true;
        var half = Loading.Settings.EnterFadeSeconds;
        _fade = CreateTween();
        _fade.TweenProperty(_black, "color:a", 1f, half);
        _fade.TweenCallback(Callable.From(() =>
        {
            _sound.StopAll();
            _root.GetChild<ColorRect>(0).Visible = false;
            _content.Visible = false;
            _root.MouseFilter = Control.MouseFilterEnum.Ignore;
            Entering?.Invoke();
        }));
        _fade.TweenProperty(_black, "color:a", 0f, half);
        _fade.TweenCallback(Callable.From(QueueFree));
    }

    // The progress bar: a dark track with the filled part in the bar's colour.
    private sealed partial class Bar : Control
    {
        private float _value;
        private Color _colour;

        public float Value
        {
            get => _value;
            set
            {
                _value = Mathf.Clamp(value, 0, 1);
                QueueRedraw();
            }
        }

        public Color Colour
        {
            get => _colour;
            set
            {
                _colour = value;
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), UiTokens.ControlFill);
            DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X * _value, Size.Y)), _colour);
            DrawRect(new Rect2(Vector2.Zero, Size), UiTokens.Blue with { A = 0.6f }, false, 1);
        }
    }

    // A small gear outline, turning.
    private sealed partial class Gear : Control
    {
        private double _time;

        public override void _Process(double delta)
        {
            _time += delta;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var centre = Size / 2;
            var r = Mathf.Min(Size.X, Size.Y) / 2 - 2;
            const int teeth = 8;
            var points = new Vector2[teeth * 4 + 1];
            for (var i = 0; i < teeth * 4; i++)
            {
                var a = (float)_time * 1.5f + i * Mathf.Tau / (teeth * 4);
                points[i] = centre + Vector2.FromAngle(a) * (i % 4 < 2 ? r : r * 0.75f);
            }
            points[^1] = points[0];
            DrawPolyline(points, UiTokens.Cyan, 2);
            DrawArc(centre, r * 0.3f, 0, Mathf.Tau, 16, UiTokens.Blue, 2);
        }
    }
}
