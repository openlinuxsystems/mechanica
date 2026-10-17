using Godot;
using Mechanica.Core.Input;
using Mechanica.Game.Player;

namespace Mechanica.Game.Ui;

/// <summary>
/// The frame every menu and mod tool panel uses (UI spec §13 "Required Elements"): a blue title bar with
/// the title and an X close button top-right, a body, and an action bar at the bottom right for the
/// primary buttons. Esc closes the topmost open window. Closing raises <see cref="CloseRequested"/>; the
/// owner decides whether to hide or free the window, and <see cref="CloseGuard"/> can ask first (for
/// unsaved changes) by calling its argument only when closing is fine. A window never grows past the screen (UI spec
/// §11): when its body needs more room than the screen has, the body scrolls, and a window pushed off an edge moves back.
/// </summary>
public partial class ToolWindow : PanelContainer
{
    private static readonly List<ToolWindow> Open = [];

    /// <summary>The gap kept between a window and the edge of the screen.</summary>
    public const float ScreenMargin = 16;

    private Label _title = null!;
    private HBoxContainer _actions = null!;
    private VBoxContainer _column = null!;
    private ScrollContainer _scroll = null!;

    /// <summary>Window title.</summary>
    public string Title
    {
        get => _titleText;
        set
        {
            _titleText = value;
            if (_title is not null)
                _title.Text = value;
        }
    }

    private string _titleText = "";

    /// <summary>The content area; add the window's controls here.</summary>
    public VBoxContainer Body { get; private set; } = null!;

    /// <summary>The bottom-right action bar; hidden until it has a button.</summary>
    public HBoxContainer Actions => _actions;

    /// <summary>The title bar's label.</summary>
    public Label TitleLabel => _title;

    /// <summary>The X button.</summary>
    public Button CloseButton { get; private set; } = null!;

    /// <summary>True to close on Esc (the default).</summary>
    public bool CloseOnEscape { get; set; } = true;

    /// <summary>Runs before closing; it calls the action it is given to go ahead. Null closes straight away.</summary>
    public Action<Action>? CloseGuard { get; set; }

    /// <summary>Raised when the window should close (X, Esc or <see cref="RequestClose"/>).</summary>
    public event Action? CloseRequested;

    /// <summary>The topmost visible window, which Esc closes.</summary>
    public static ToolWindow? Topmost => Open.LastOrDefault(w => w.IsVisibleInTree());

    public ToolWindow()
    {
        // A window keeps every click and wheel over it, even past the end of a scroll, so none reaches the
        // game behind it (where a click takes the mouse back and closes the menu).
        UiInput.KeepMouse(this);
        var column = _column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);
        var bar = new PanelContainer { Name = "TitleBar", ThemeTypeVariation = "TitleBar" };
        column.AddChild(bar);
        var row = new HBoxContainer();
        bar.AddChild(row);
        _title = new Label { Name = "Title", Text = _titleText, ThemeTypeVariation = "TitleLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        row.AddChild(_title);
        CloseButton = new Button { Name = "Close", Text = "✕", TooltipText = "Close (Esc)", CustomMinimumSize = new Vector2(32, 32), FocusMode = FocusModeEnum.None };
        CloseButton.Pressed += RequestClose;
        row.AddChild(CloseButton);
        // The body sits in a scroll area as big as the body wants, up to what the screen has room for (see Fit).
        _scroll = new ScrollContainer
        {
            Name = "Scroll", SizeFlagsVertical = SizeFlags.ExpandFill, FollowFocus = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(_scroll);
        Body = new VBoxContainer { Name = "Body", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _scroll.AddChild(Body);
        Body.MinimumSizeChanged += QueueFit;
        _actions = new HBoxContainer { Name = "Actions", Alignment = BoxContainer.AlignmentMode.End, Visible = false };
        column.AddChild(_actions);
    }

    public override void _EnterTree()
    {
        Open.Add(this);
        GetViewport().SizeChanged += QueueFit;
        QueueFit();
    }

    public override void _ExitTree()
    {
        Open.Remove(this);
        GetViewport().SizeChanged -= QueueFit;
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationResized or (int)NotificationVisibilityChanged)
            QueueFit();
    }

    private bool _fitQueued;
    private Vector2 _requestedMinimum;
    private Vector2 _appliedMinimum;

    private void QueueFit()
    {
        if (_fitQueued || !IsInsideTree())
            return;
        _fitQueued = true;
        Callable.From(Fit).CallDeferred();
    }

    /// <summary>
    /// Sizes the body's scroll area to what the body needs, but no taller or wider than the screen leaves once the title
    /// bar, the action bar, the frame and <see cref="ScreenMargin"/> are taken off, then moves the window back inside the screen if it reaches
    /// past an edge. Runs by itself whenever the body or the screen changes size.
    /// </summary>
    public void Fit()
    {
        _fitQueued = false;
        if (!IsInsideTree())
            return;
        var screen = GetViewport().GetVisibleRect().Size - new Vector2(ScreenMargin, ScreenMargin) * 2;
        // A size the owner asked for (Options is 860×560) gives way to a smaller screen, and comes back on a bigger one.
        if (CustomMinimumSize != _appliedMinimum)
            _requestedMinimum = CustomMinimumSize;
        _appliedMinimum = _requestedMinimum.Min(screen);
        if (CustomMinimumSize != _appliedMinimum)
            CustomMinimumSize = _appliedMinimum;
        // The title bar, action bar and frame take this much height; the body may have the rest.
        var chrome = _column.GetCombinedMinimumSize().Y - _scroll.GetCombinedMinimumSize().Y + (GetThemeStylebox("panel")?.GetMinimumSize().Y ?? 0);
        var room = Mathf.Max(0, screen.Y - chrome);
        var height = Mathf.Min(Body.GetCombinedMinimumSize().Y, room);
        // The body sets the window's width, so wrapped text measures the same as it shows, unless it is wider than the
        // screen (a mod tool at 150% UI scale): then the body scrolls sideways too, in what the screen has room for.
        var frame = GetThemeStylebox("panel")?.GetMinimumSize().X ?? 0;
        var across = screen.X - frame;
        var wide = Body.GetCombinedMinimumSize().X > across + 0.5f;
        var width = wide ? across : 0;
        _scroll.HorizontalScrollMode = wide ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.Disabled;
        var was = _scroll.CustomMinimumSize.Y;
        if (!Mathf.IsEqualApprox(_scroll.CustomMinimumSize.X, width))
            _scroll.CustomMinimumSize = new Vector2(width, _scroll.CustomMinimumSize.Y);
        if (!Mathf.IsEqualApprox(was, height))
        {
            _scroll.CustomMinimumSize = new Vector2(width, height);
            // A window that sizes to its content (not stretched between anchors) gives back the height it no longer
            // needs; Godot grows a control to its minimum but never shrinks it.
            if (height < was && GetParent() is not Container && Mathf.IsEqualApprox(AnchorTop, AnchorBottom))
            {
                var shrink = Size.Y - Mathf.Max(GetCombinedMinimumSize().Y, Size.Y - (was - height));
                Size -= new Vector2(0, shrink);
                // A centred window stays centred.
                if (GrowVertical == GrowDirection.Both)
                    Position += new Vector2(0, shrink / 2);
            }
        }
        // Only a window placed by hand can be moved; one in a container goes where the container puts it.
        if (GetParent() is Container || !Visible)
            return;
        var rect = GetGlobalRect();
        var visible = GetViewport().GetVisibleRect();
        if (visible.Encloses(rect))
            return;
        var low = visible.Position + Vector2.One * ScreenMargin;
        var high = (visible.End - rect.Size - Vector2.One * ScreenMargin).Max(low);
        GlobalPosition = rect.Position.Clamp(low, high);
    }

    public override void _Input(InputEvent @event)
    {
        if (CloseOnEscape && GameInput.Pressed(@event, InputActions.Pause) && Topmost == this && !Completing())
        {
            GetViewport().SetInputAsHandled();
            RequestClose();
        }
    }

    // Esc in a script editor first closes its completion list.
    private bool Completing() => GetViewport().GuiGetFocusOwner() is CodeEdit code && code.GetCodeCompletionSelectedIndex() >= 0;

    /// <summary>Adds a button to the action bar; a primary one is cyan.</summary>
    public Button AddAction(string text, Action pressed, bool primary = false, string? tooltip = null)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tooltip ?? "",
            ThemeTypeVariation = primary ? "PrimaryButton" : "",
            CustomMinimumSize = new Vector2(96, 32),
        };
        button.Pressed += pressed;
        _actions.AddChild(button);
        _actions.Visible = true;
        return button;
    }

    /// <summary>Asks to close: runs the guard, then raises <see cref="CloseRequested"/>.</summary>
    public void RequestClose()
    {
        if (CloseGuard is { } guard)
            guard(() => CloseRequested?.Invoke());
        else
            CloseRequested?.Invoke();
    }
}
