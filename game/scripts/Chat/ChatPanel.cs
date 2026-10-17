using Godot;
using Mechanica.Core.Chat;
using Mechanica.Core.Input;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Settings;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Chat;

/// <summary>
/// The chat panel (UI spec §1 Chat, §11, §12): bottom left, growing upward to at most 30% of the screen,
/// each line with the speaker's name in cyan. Chat (Enter) opens a box to type in, Enter sends and Esc
/// closes; while it is closed only lines from the last <see cref="ShowSeconds"/> show. The panel's
/// background follows Options, Interface, Chat opacity.
/// </summary>
public partial class ChatPanel : CanvasLayer, IHudLayer
{
    /// <summary>Seconds a new line stays up while chat is closed.</summary>
    public const double ShowSeconds = 12;

    private readonly List<(RichTextLabel Label, double At)> _lines = [];
    private PanelContainer _panel = null!;
    private StyleBoxFlat _background = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _list = null!;
    private LineEdit _input = null!;
    private Label _status = null!;
    private float _maxHeight = 200;

    /// <summary>Chat lines. Set before adding to the tree.</summary>
    public ChatSync Chat { get; init; } = null!;

    /// <summary>Where requests go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>True while the typing box is open.</summary>
    public bool IsOpen => _input.Visible;

    /// <summary>Hide GUI leaves the chat up while the player types.</summary>
    public bool KeepWhenHidden => IsOpen;

    /// <summary>The typing box.</summary>
    public LineEdit Input => _input;

    /// <summary>The lines shown now, as plain text (<c>Name: text</c>).</summary>
    public IEnumerable<string> Shown => _lines.Where(l => l.Label.Visible).Select(l => l.Label.GetParsedText());

    /// <summary>How opaque the panel's background is now.</summary>
    public float BackgroundAlpha => _background.BgColor.A;

    /// <summary>Why the last line was refused, or empty.</summary>
    public string Status => _status.Text;

    public override void _Ready()
    {
        Layer = 3;
        AddToGroup(HudRoot.Group);
        _panel = UiInput.KeepMouse(new PanelContainer { Name = "Chat" });
        _background = new StyleBoxFlat { BgColor = UiTokens.DeepNavy, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6 };
        _background.SetCornerRadiusAll(4);
        _panel.AddThemeStyleboxOverride("panel", _background);
        AddChild(_panel);
        var column = new VBoxContainer();
        _panel.AddChild(column);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(420, 0) };
        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.End };
        _scroll.AddChild(_list);
        column.AddChild(_scroll);
        _status = new Label { ThemeTypeVariation = "ErrorLabel", Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_status);
        _input = new LineEdit { Name = "Input", MaxLength = ChatLog.MaxLength, Visible = false, PlaceholderText = "Say something (Enter sends, Esc closes)" };
        _input.TextSubmitted += Send;
        _input.GuiInput += e =>
        {
            if (e.IsActionPressed("ui_cancel"))
            {
                Close();
                _input.AcceptEvent();
            }
        };
        column.AddChild(_input);
        foreach (var message in Chat.Log.Lines)
            Add(message);
        Chat.Said += Add;
        GetViewport().SizeChanged += Layout;
        Layout();
        TakeMouse(false);
    }

    public override void _ExitTree()
    {
        Chat.Said -= Add;
        GetViewport().SizeChanged -= Layout;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen && GameInput.Pressed(@event, InputActions.Chat) && Godot.Input.MouseMode == Godot.Input.MouseModeEnum.Captured)
        {
            Open();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        // A click back into the world takes the mouse, which ends typing.
        if (IsOpen && Godot.Input.MouseMode == Godot.Input.MouseModeEnum.Captured)
            Close();
        var now = Time.GetTicksMsec() / 1000.0;
        foreach (var (label, at) in _lines)
            label.Visible = IsOpen || now - at < ShowSeconds;
        var opacity = GameSettings.Current.Interface.ChatOpacity;
        _background.BgColor = UiTokens.DeepNavy with { A = IsOpen ? Mathf.Max(opacity, 0.35f) : _lines.Any(l => l.Label.Visible) ? opacity : 0f };
        // The list grows with its lines up to the cap, then scrolls.
        var wanted = Mathf.Min(_list.GetCombinedMinimumSize().Y, _maxHeight);
        if (!Mathf.IsEqualApprox(_scroll.CustomMinimumSize.Y, wanted))
            _scroll.CustomMinimumSize = _scroll.CustomMinimumSize with { Y = wanted };
    }

    /// <summary>Opens the typing box and frees the mouse.</summary>
    public void Open()
    {
        _input.Visible = true;
        _status.Visible = false;
        TakeMouse(true);
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        _input.GrabFocus();
        ScrollToEnd();
    }

    /// <summary>Closes the typing box and gives the mouse back to the game.</summary>
    public void Close()
    {
        if (!IsOpen)
            return;
        _input.Visible = false;
        _input.Clear();
        _status.Visible = false;
        TakeMouse(false);
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Captured;
    }

    /// <summary>Sends <paramref name="text"/> (as Enter in the box does); an empty line just closes.</summary>
    public void Send(string text)
    {
        if (ChatLog.Clean(text).Length == 0)
        {
            Close();
            return;
        }
        Actions.Request(ChatLog.SendChat, new SendChatArgs(text), result =>
        {
            if (result.Ok || !IsInstanceValid(this))
                return;
            _status.Text = result.Message ?? "";
            _status.Visible = true;
        });
        _input.Clear();
        Close();
    }

    private void Add(ChatMessage message)
    {
        var name = UiTokens.Cyan.ToHtml(false);
        // What players say shows as they wrote it.
        var label = Loc.Verbatim(new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = $"[color=#{name}]{Escape(message.Name)}[/color]: {Escape(message.Text)}",
        });
        _list.AddChild(label);
        _lines.Add((label, Time.GetTicksMsec() / 1000.0));
        while (_lines.Count > ChatLog.Kept)
        {
            _lines[0].Label.QueueFree();
            _lines.RemoveAt(0);
        }
        ScrollToEnd();
    }

    private void ScrollToEnd() => Callable.From(() =>
    {
        if (IsInstanceValid(_scroll))
            _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
    }).CallDeferred();

    // Bottom left with the spec's 2% margin, clear of the tool bar line, and at most 30% of the screen tall.
    private void Layout()
    {
        var size = GetViewport().GetVisibleRect().Size;
        _scroll.CustomMinimumSize = new Vector2(Mathf.Min(460, size.X * 0.4f), 0);
        // Pinned to the bottom-left corner at zero size; it grows up and right to fit its lines.
        _panel.AnchorLeft = _panel.AnchorRight = 0;
        _panel.AnchorTop = _panel.AnchorBottom = 1;
        _panel.GrowHorizontal = Control.GrowDirection.End;
        _panel.GrowVertical = Control.GrowDirection.Begin;
        _panel.OffsetLeft = _panel.OffsetRight = size.X * 0.02f;
        _panel.OffsetTop = _panel.OffsetBottom = -(size.Y * 0.02f + 56);
        _maxHeight = size.Y * 0.3f;
    }

    // Open, the panel keeps clicks and the wheel (and scrolls); closed, the game behind it gets them.
    private void TakeMouse(bool open)
    {
        _panel.MouseFilter = open ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        _scroll.MouseFilter = open ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");
}
