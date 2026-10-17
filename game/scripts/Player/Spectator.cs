using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Player;
using Mechanica.Game.Net;
using Mechanica.Game.Settings;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Player;

/// <summary>
/// Watch players: follows another player with the camera, to watch an AI agent (or a friend) play. Watch
/// players (V) starts with the first AI player, or anyone else when no AI plays; Create (left click) and
/// Remove (right click) go to the next and previous player, Zoom in and Zoom out move between first person
/// (what they see) and the camera behind them, and Watch players or Esc stops. Meanwhile this player stands
/// still and its tools ignore the keys. A banner names who is watched and shows an AI's latest thought
/// (<c>SetThought</c>). Watching is local: nobody else sees it.
/// </summary>
public partial class Spectator : CanvasLayer
{
    private PlayerController? _target;
    private Camera3D? _before;
    private float _distance = -1f;
    private PanelContainer _panel = null!;
    private Label _title = null!;
    private Label _thought = null!;
    private Label _hints = null!;

    /// <summary>Players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>False in automated tests: keys and mouse are not read.</summary>
    public bool ReadDeviceInput { get; init; } = true;

    /// <summary>The player being watched, or null.</summary>
    public PlayerController? Target => _target is not null && IsInstanceValid(_target) ? _target : null;

    /// <summary>True while following another player.</summary>
    public bool Watching => Target is not null;

    /// <summary>The banner's text: who is watched, the AI's thought and the keys (for tests).</summary>
    public string BannerText => _panel.Visible ? $"{_title.Text}\n{_thought.Text}\n{_hints.Text}" : "";

    /// <summary>The players that can be watched, AI players first, each group by peer.</summary>
    public IReadOnlyList<PlayerController> Candidates => [.. Players.Players.Values
        .Where(p => p != Players.LocalPlayer && IsInstanceValid(p))
        .OrderBy(p => Players.Agents?.IsAgent(p.PeerId) == true || p.Thought.Length > 0 ? 0 : 1)
        .ThenBy(p => p.PeerId)];

    public override void _Ready()
    {
        AddToGroup(HudRoot.Group);
        _panel = new PanelContainer { Name = "Banner", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _title = new Label { Name = "Title", AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled, HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 22);
        _thought = new Label
        {
            Name = "Thought", AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled, HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(520, 0),
        };
        _thought.AddThemeColorOverride("font_color", UiTokens.Cyan);
        _hints = new Label { Name = "Hints", AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled, HorizontalAlignment = HorizontalAlignment.Center };
        _hints.AddThemeColorOverride("font_color", UiTokens.TextSecondary);
        rows.AddChild(_title);
        rows.AddChild(_thought);
        rows.AddChild(_hints);
        _panel.AddChild(rows);
        AddChild(_panel);
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop, Control.LayoutPresetMode.KeepSize, (int)HudRoot.Margin(GetViewport()).Y);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
    }

    public override void _ExitTree() => Stop();

    /// <summary>Starts watching <paramref name="player"/> (another player), or the first candidate when null. False when there is nobody to watch.</summary>
    public bool Watch(PlayerController? player = null)
    {
        player ??= Candidates.FirstOrDefault();
        if (player is null || player == Players.LocalPlayer || !IsInstanceValid(player))
            return false;
        _before ??= GetViewport().GetCamera3D();
        var previous = Target;
        _target = player;
        if (_distance < 0f)
            _distance = player.Settings.CameraDistance;
        player.Camera.Fov = GameSettings.Current.FieldOfView;
        player.Camera.MakeCurrent();
        player.CameraDistance = _distance;
        if (previous is not null && previous != player)
            previous.CameraDistance = 0f;
        RefreshLocalBody();
        return true;
    }

    /// <summary>Watches the next (or with <paramref name="forward"/> false the previous) player, wrapping round.</summary>
    public void Cycle(bool forward = true)
    {
        var list = Candidates;
        if (list.Count == 0)
        {
            Stop();
            return;
        }
        var at = Target is { } target ? list.ToList().IndexOf(target) : -1;
        var next = at < 0 ? 0 : (at + (forward ? 1 : list.Count - 1)) % list.Count;
        Watch(list[next]);
    }

    /// <summary>Moves the watching camera one zoom step in or out (all the way in is the player's own view).</summary>
    public void Zoom(bool zoomIn)
    {
        if (Target is not { } target)
            return;
        _distance = CameraZoom.Step(target.CameraDistance, zoomIn, target.Settings);
        target.CameraDistance = _distance;
    }

    /// <summary>Stops watching and gives the view back.</summary>
    public void Stop()
    {
        var target = Target;
        _target = null;
        if (target is not null)
        {
            if (_before is not null && IsInstanceValid(_before) && _before.IsInsideTree())
                _before.MakeCurrent();
            else
                Players.LocalPlayer?.Camera.MakeCurrent();
            target.CameraDistance = 0f;
            RefreshLocalBody();
        }
        _before = null;
        if (_panel is not null)
            _panel.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_target is not null && (!IsInstanceValid(_target) || !Players.Players.ContainsKey(_target.PeerId)))
        {
            // The watched player left: on to the next one, or back to our own view.
            _target = null;
            if (Candidates.Count > 0)
                Watch();
            else
                Stop();
        }
        if (Target is not { } target)
        {
            _panel.Visible = false;
            return;
        }
        if (!target.Camera.Current)
            target.Camera.MakeCurrent();
        _panel.Visible = !HudRoot.Hidden;
        _title.Text = Loc.T("Watching {0}", target.DisplayName);
        _thought.Text = target.Thought;
        _thought.Visible = target.Thought.Length > 0;
        _hints.Text = Loc.T("{0} next   {1} previous   {2}/{3} zoom   {4} stop",
            GameInput.Label(InputActions.Primary), GameInput.Label(InputActions.Secondary),
            GameInput.Label(InputActions.ZoomIn), GameInput.Label(InputActions.ZoomOut), GameInput.Label(InputActions.Spectate));
    }

    // While watching, keys go here before the tools and menus see them.
    public override void _Input(InputEvent @event)
    {
        if (ReadDeviceInput && Watching && Input.MouseMode == Input.MouseModeEnum.Captured && HandleWatching(@event))
            GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!ReadDeviceInput || Watching || Input.MouseMode != Input.MouseModeEnum.Captured || GameInput.Typing(GetViewport()))
            return;
        if (GameInput.Pressed(@event, InputActions.Spectate) && Watch())
            GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// What a key or mouse event does while watching. Returns true when it is used up, which is every key,
    /// click and look but Chat and Hide GUI, so this player doesn't act while its owner watches.
    /// </summary>
    public bool HandleWatching(InputEvent @event)
    {
        if (GameInput.Pressed(@event, InputActions.Spectate) || GameInput.Pressed(@event, InputActions.Pause))
            Stop();
        else if (GameInput.Pressed(@event, InputActions.Primary))
            Cycle(true);
        else if (GameInput.Pressed(@event, InputActions.Secondary))
            Cycle(false);
        else if (GameInput.Pressed(@event, InputActions.ZoomIn))
            Zoom(true);
        else if (GameInput.Pressed(@event, InputActions.ZoomOut))
            Zoom(false);
        else if (GameInput.Pressed(@event, InputActions.Chat) || GameInput.Pressed(@event, InputActions.HideGui))
            return false;
        else
            return @event is InputEventKey or InputEventMouseButton or InputEventMouseMotion;
        return true;
    }

    // Our own body shows while another camera is current, and hides again in first person.
    private void RefreshLocalBody()
    {
        if (Players.LocalPlayer is { } me)
            me.CameraDistance = me.CameraDistance;
    }
}
