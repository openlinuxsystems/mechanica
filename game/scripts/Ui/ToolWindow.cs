using Godot;
using Mechanica.Core.Input;
using Mechanica.Game.Player;

namespace Mechanica.Game.Ui;

/// <summary>
/// The frame every menu and mod tool panel uses (UI spec §13 "Required Elements"): a blue title bar with
/// the title and an X close button top-right, a body, and an action bar at the bottom right for the
/// primary buttons. Esc closes the topmost open window. Closing raises <see cref="CloseRequested"/>; the
/// owner decides whether to hide or free the window, and <see cref="CloseGuard"/> can ask first (for
/// unsaved changes) by calling its argument only when closing is fine.
/// </summary>
public partial class ToolWindow : PanelContainer
{
    private static readonly List<ToolWindow> Open = [];

    private Label _title = null!;
    private HBoxContainer _actions = null!;

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
        var column = new VBoxContainer();
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
        Body = new VBoxContainer { Name = "Body", SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(Body);
        _actions = new HBoxContainer { Name = "Actions", Alignment = BoxContainer.AlignmentMode.End, Visible = false };
        column.AddChild(_actions);
    }

    public override void _EnterTree() => Open.Add(this);

    public override void _ExitTree() => Open.Remove(this);

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
