using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Logic;
using Mechanica.Core.Ui;
using Mechanica.Game.Building;
using Mechanica.Game.Net;

namespace Mechanica.Game.Ui;

/// <summary>
/// The crosshair and the world hover tooltip (UI spec §6, §8, §9, §11). The crosshair is a simple cross: white,
/// blue when a click or Use would do something here, pulsing cyan over a connection dot with the Connect Tool.
/// Aiming at a part for 0.25 s fades in a tooltip beside the crosshair with its name, short description and what
/// Use does; parts with logic, a seat, storage or settings add a line with their signal, connection count and
/// durability, read from the host. The tooltip goes the moment the aim leaves the part and stays on screen near
/// edges. Both hide while a menu has the mouse and with Hide GUI.
/// </summary>
public partial class WorldHud : CanvasLayer
{
    private CrosshairView _crosshair = null!;
    private ProgressRing _ring = null!;
    private PanelContainer _tooltip = null!;
    private Label _name = null!;
    private Label _description = null!;
    private Label _key = null!;
    private Label _status = null!;
    private HoverTimer _hover = null!;
    private double _time;
    private double _sincePoll;
    private bool _asking;
    private (int Creation, int Part)? _aimed;
    private PartInspection? _inspected;

    /// <summary>Players, for the local camera. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Where the host is asked about a part. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>Content and building settings. Set before adding to the tree.</summary>
    public BuildSync Build { get; init; } = null!;

    /// <summary>The tool in hand, or null. Set before adding to the tree.</summary>
    public Func<HandTool?> Tool { get; init; } = () => null;

    /// <summary>What Use would do now (for example "open the Chest"), or null. Set before adding to the tree.</summary>
    public Func<string?> Interaction { get; init; } = () => null;

    /// <summary>Where the tooltip's tick plays (§10); null plays none.</summary>
    public Sound.SoundSync? Sounds { get; init; }

    /// <summary>True while a menu or panel has the mouse; tests set it.</summary>
    public Func<bool> Covered { get; init; } = () => Input.MouseMode != Input.MouseModeEnum.Captured;

    /// <summary>What the crosshair shows now.</summary>
    public CrosshairState State { get; private set; }

    /// <summary>The ring round the crosshair that fills while a tool's timed action is held (§8, §11), hidden otherwise.</summary>
    public ProgressRing Ring => _ring;

    /// <summary>The crosshair control.</summary>
    public Control CrosshairControl => _crosshair;

    /// <summary>The tooltip panel.</summary>
    public PanelContainer Tooltip => _tooltip;

    /// <summary>True while the tooltip shows at all.</summary>
    public bool TooltipShowing => _tooltip.Visible;

    /// <summary>The tooltip's text, line by line.</summary>
    public string TooltipText => string.Join("\n", new[] { _name, _description, _key, _status }.Where(l => l.Visible).Select(l => l.Text));

    /// <summary>Seconds between reads of the aimed part's state while its tooltip shows.</summary>
    public const double PollSeconds = 0.5;

    public override void _Ready()
    {
        Layer = 2;
        AddToGroup(HudRoot.Group);
        _hover = new HoverTimer(HudRoot.Settings);
        _crosshair = new CrosshairView { Name = "Crosshair" };
        AddChild(_crosshair);
        _crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.KeepSize);
        // Screen-space and centred, so it keeps its size and place at any resolution or zoom.
        _ring = new ProgressRing { Name = "Ring", Visible = false, CustomMinimumSize = new Vector2(44, 44), Size = new Vector2(44, 44), Thickness = 4 };
        AddChild(_ring);
        _ring.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.KeepSize);
        _tooltip = new PanelContainer { Name = "Tooltip", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, ThemeTypeVariation = "TooltipPanel" };
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _tooltip.AddChild(column);
        _name = Loc.Verbatim(new Label { Name = "Name", ThemeTypeVariation = "TitleLabel" });
        _description = Loc.Verbatim(new Label { Name = "Description", ThemeTypeVariation = "SecondaryLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(260, 0) });
        _key = Loc.Verbatim(new Label { Name = "Key" });
        _status = Loc.Verbatim(new Label { Name = "Status", ThemeTypeVariation = "SecondaryLabel" });
        foreach (var label in new[] { _name, _description, _key, _status })
            column.AddChild(label);
        AddChild(_tooltip);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        var player = Players.LocalPlayer;
        var playing = player is { Dead: false } && !Covered();
        _crosshair.Visible = playing;
        var progress = playing ? Tool()?.Progress : null;
        _ring.Visible = progress is not null;
        _ring.Value = progress ?? 0;
        if (!playing)
        {
            State = CrosshairState.Default;
            HideTooltip();
            return;
        }
        var interaction = Interaction();
        var tool = Tool()?.Crosshair ?? CrosshairState.Default;
        State = tool == CrosshairState.Connection ? tool : interaction is not null ? CrosshairState.Valid : tool;
        var (hex, alpha) = Crosshair.Look(State, _time, HudRoot.Settings.CrosshairPulseHz);
        _crosshair.Colour = UiTokens.From(hex, alpha);

        var hit = Build.Settings is { } settings ? HandTool.PartHit(HandTool.Cast(player!, settings.Reach)) : null;
        var aimed = hit is var (node, part) ? (node.Creation.Id, part) : ((int, int)?)null;
        // A tool's own tooltip (a Connect Tool node or line) takes the part's place.
        var tip = Tool()?.HoverTip;
        _hover.Tick(tip is { } t ? t.Key : aimed, delta);
        if (tip is var (_, title, body))
        {
            _aimed = null;
            if (!_hover.Showing)
            {
                HideTooltip();
                return;
            }
            _name.Text = title;
            _description.Text = body;
            _description.Visible = body.Length > 0;
            _key.Visible = _status.Visible = false;
            ShowTooltip();
            return;
        }
        if (aimed != _aimed)
        {
            _aimed = aimed;
            _inspected = null;
            _sincePoll = PollSeconds;
        }
        if (!_hover.Showing || hit is not var (aimedNode, aimedPart))
        {
            HideTooltip();
            return;
        }
        var creation = aimedNode.Creation;
        var definition = creation.DefinitionOf(aimedPart);
        var interactive = definition.Logic is not null || definition.Seat is not null || definition.Inventory is not null || definition.Settings is { Count: > 0 };
        if (interactive)
        {
            _sincePoll += delta;
            if (_sincePoll >= PollSeconds)
                Inspect(creation.Id, aimedPart);
        }
        _name.Text = definition.Name ?? creation.FindPart(aimedPart)?.Part ?? "";
        _description.Text = definition.Description ?? "";
        _description.Visible = _description.Text.Length > 0;
        _key.Text = interaction is null ? "" : $"[{Player.GameInput.Label(Core.Input.InputActions.Interact)}] {interaction}";
        _key.Visible = _key.Text.Length > 0;
        _status.Text = interactive ? Status(creation, aimedPart, definition) : "";
        _status.Visible = _status.Text.Length > 0;
        ShowTooltip();
    }

    // Shows the tooltip beside the crosshair, kept on screen, with a soft tick as it appears (§10).
    private void ShowTooltip()
    {
        if (!_tooltip.Visible)
            Sounds?.PlayUi("ui_hover");
        _tooltip.Visible = true;
        _tooltip.Modulate = Colors.White with { A = _hover.Opacity };
        _tooltip.ResetSize();
        var screen = GetViewport().GetVisibleRect().Size;
        var margin = HudRoot.Margin(GetViewport());
        var at = TooltipPlacement.Place((screen / 2).ToNumerics(), _tooltip.Size.ToNumerics(), screen.ToNumerics(), margin.ToNumerics(), HudRoot.Settings.TooltipOffset);
        _tooltip.Position = at.ToGodot();
    }

    // Signal, connections and durability: the signal and damage are the host's, read with InspectCreation.
    private string Status(Core.Creations.Creation creation, int part, Core.Data.PartDefinition definition)
    {
        var bits = new List<string>();
        if (definition.Logic is { } logic)
        {
            bits.Add(Loc.T("{0} connection", Game.Logic.ConnectionNodes.KindName(logic.KindOrDefault)));
            if (logic.Reverse is not null)
                bits.Add(PartSettings.Reversed(definition, creation.FindPart(part)?.Settings) ? Loc.T("reversed") : Loc.T("forward"));
            if (_inspected is { } p)
                bits.Add(p.Output != 0 ? Loc.T("Signal on ({0})", $"{p.Output:0.##}") : Loc.T("Signal off"));
            var links = creation.Links.Count(l => l.From == part || l.To == part);
            bits.Add(links == 1 ? Loc.T("1 connection") : Loc.T("{0} connections", links));
        }
        if (Build.Settings?.Sledge.HealthOf(definition.Durability) is { } full && full > 0)
        {
            var damage = _inspected?.Damage ?? creation.PartDamage(part);
            bits.Add(Loc.T("Durability {0}%", $"{Math.Clamp(100 * (1 - damage / full), 0, 100):0}"));
        }
        return string.Join("  ·  ", bits);
    }

    private void Inspect(int creationId, int part)
    {
        _sincePoll = 0;
        if (_asking)
            return;
        _asking = true;
        Actions.Request(LogicActions.InspectCreation, new InspectCreationArgs(creationId), result =>
        {
            _asking = false;
            if (_aimed != (creationId, part) || result is not { Ok: true, Value: { } value })
                return;
            _inspected = value.Deserialize<CreationInspection>(ActionJson.Options)?.Parts.FirstOrDefault(p => p.Id == part);
        });
    }

    private void HideTooltip()
    {
        _tooltip.Visible = false;
        _tooltip.Modulate = Colors.Transparent;
    }
}

/// <summary>The crosshair: a simple cross with a thin dark outline so it reads on bright ground.</summary>
public partial class CrosshairView : Control
{
    private Color _colour = UiTokens.Text;

    /// <summary>The cross's colour (with its opacity).</summary>
    public Color Colour
    {
        get => _colour;
        set
        {
            if (value == _colour)
                return;
            _colour = value;
            QueueRedraw();
        }
    }

    public CrosshairView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(20, 20);
        Size = CustomMinimumSize;
    }

    public override void _Draw()
    {
        var c = Size / 2;
        const float arm = 8f, thick = 2f;
        var outline = UiTokens.DeepNavy with { A = 0.6f * _colour.A };
        DrawRect(new Rect2(c.X - arm - 1, c.Y - thick / 2 - 1, arm * 2 + 2, thick + 2), outline);
        DrawRect(new Rect2(c.X - thick / 2 - 1, c.Y - arm - 1, thick + 2, arm * 2 + 2), outline);
        DrawRect(new Rect2(c.X - arm, c.Y - thick / 2, arm * 2, thick), _colour);
        DrawRect(new Rect2(c.X - thick / 2, c.Y - arm, thick, arm * 2), _colour);
    }
}
