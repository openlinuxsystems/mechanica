using System.Globalization;
using System.Text.Json;
using Godot;
using Mechanica.Game.Ui;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Logic;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Logic;

/// <summary>
/// A placed part's menu (chunk 10, UI spec §12), opened with Use (E) on the part and closed with Use, Esc or its X. It is
/// built from the part's data alone (D2): the values its script shows (<see cref="MenuDefinition.Readouts"/>, a cyan
/// bar for fuel or charge, numbers such as RPM), its live inputs and output (<see cref="MenuDefinition.Signals"/>), and
/// a field per setting (<see cref="PartDefinition.Settings"/>): a slider with its presets for a number, a list with the
/// option's picture for a choice, toggles side by side for a group, and the frame editor for a sequence. Settings
/// marked <see cref="SettingDefinition.HideWhenLinked"/> give way to a note while something drives the part, and a part
/// whose menu has <see cref="MenuDefinition.LockWhenSequenced"/> shows only a note while a Controller runs it. Each change
/// goes to the host as <c>ConfigurePart</c> at once, or, for a menu with <see cref="MenuDefinition.Apply"/>, when the
/// player presses Apply or closes it. While it is open the part's <see cref="MenuDefinition.Preview"/> shows what it
/// reaches in the world. A part whose E cycles a setting (<see cref="MenuUse.Cycle"/>, a logic gate) does so with
/// <see cref="Cycle"/>, which shows the new option large with its picture and the part's live signals for a moment.
/// </summary>
public partial class PartSettingsPanel : CanvasLayer
{
    /// <summary>Seconds the cycle card stays up after E.</summary>
    public const double CardSeconds = 2.0;

    /// <summary>Seconds between signal reads while the menu or card shows signals.</summary>
    public const double PollSeconds = 0.2;

    private readonly Dictionary<string, JsonElement?> _pending = [];
    private readonly List<(ReadoutDefinition Readout, Control View)> _readoutViews = [];
    private readonly List<(SettingDefinition Setting, Control Row, Control Note)> _linkedRows = [];
    private ToolWindow _panel = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _rows = null!;
    private Label _signals = null!;
    private Label _status = null!;
    private Button _apply = null!;
    private PanelContainer _card = null!;
    private TextureRect _cardIcon = null!;
    private Label _cardName = null!;
    private Label _cardSignals = null!;
    private MeshInstance3D? _preview;
    private float _previewRange = -1;
    private double _cardLeft;
    private bool _locked;
    private double _sincePoll;
    private (int Creation, int Part)? _cardTarget;

    /// <summary>Where changes go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>The Creations, for the part's definition and current values. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>What part scripts show (readouts); null shows none.</summary>
    public Func<PartReadouts?>? Readouts { get; init; }

    /// <summary>Live signals; null shows none.</summary>
    public LogicOverlay? Signals { get; init; }

    /// <summary>Part assets, for option pictures; null shows none.</summary>
    public PartAssets? Assets { get; init; }

    /// <summary>The part shown, or null.</summary>
    public (int Creation, int Part)? Target { get; private set; }

    /// <summary>True while the panel is up.</summary>
    public bool IsOpen => _panel.Visible;

    /// <summary>The menu window.</summary>
    public ToolWindow Window => _panel;

    /// <summary>The setting rows, for tests.</summary>
    public VBoxContainer Rows => _rows;

    /// <summary>The live signals line ("Inputs: … Output: …"), empty when the part's menu shows none.</summary>
    public string SignalsText => _signals.Visible ? _signals.Text : "";

    /// <summary>The last answer or problem.</summary>
    public string Status => _status.Text;

    /// <summary>The Apply button (shown for menus that wait for Apply).</summary>
    public Button ApplyButton => _apply;

    /// <summary>Changes waiting for Apply.</summary>
    public IReadOnlyDictionary<string, JsonElement?> Pending => _pending;

    /// <summary>The sequence editor, when the part has a sequence setting.</summary>
    public SequenceEditor? Sequence { get; private set; }

    /// <summary>The world preview while it shows, for tests.</summary>
    public MeshInstance3D? PreviewMesh => _preview;

    /// <summary>True while the cycle card shows.</summary>
    public bool CardShowing => _card.Visible;

    /// <summary>The cycle card's option name.</summary>
    public string CardName => _cardName.Text;

    /// <summary>The cycle card's picture, when the option has one.</summary>
    public Texture2D? CardIcon => _cardIcon.Texture;

    /// <summary>The cycle card's live signals line.</summary>
    public string CardSignals => _cardSignals.Text;

    public override void _Ready()
    {
        Layer = 5;
        _panel = new ToolWindow { Name = "Settings", Title = "Part settings", Visible = false, CustomMinimumSize = new Vector2(420, 0) };
        // The title is the part's name, from mod data.
        Loc.Verbatim(_panel.TitleLabel);
        _panel.CloseRequested += Close;
        AddChild(_panel);
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.KeepSize);
        _signals = Loc.Verbatim(new Label { Name = "Signals", Visible = false });
        _panel.Body.AddChild(_signals);
        _scroll = new ScrollContainer { Name = "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows = new VBoxContainer { Name = "Rows", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_rows);
        _panel.Body.AddChild(_scroll);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _panel.Body.AddChild(_status);
        _apply = _panel.AddAction("Apply", Apply, primary: true, tooltip: "Apply the changes");
        _panel.AddAction("Close", Close, tooltip: "Close the menu");

        // The cycle card: the new option large, in the upper middle of the screen, with the part's signals.
        _card = new PanelContainer { Name = "CycleCard", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _card.AddChild(column);
        _cardIcon = new TextureRect
        {
            Name = "Icon", CustomMinimumSize = new Vector2(128, 128), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        column.AddChild(_cardIcon);
        _cardName = Loc.Verbatim(new Label { Name = "Name", HorizontalAlignment = HorizontalAlignment.Center });
        _cardName.AddThemeFontSizeOverride("font_size", 40);
        column.AddChild(_cardName);
        _cardSignals = Loc.Verbatim(new Label { Name = "Signals", HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(_cardSignals);
        AddChild(_card);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (IsOpen && GameInput.Pressed(@event, InputActions.Interact) && !GameInput.Typing(GetViewport()))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (_card.Visible && (_cardLeft -= delta) <= 0)
        {
            _card.Visible = false;
            _cardTarget = null;
        }
        var watched = IsOpen ? Target : _card.Visible ? _cardTarget : null;
        if (watched is not { } target || Creations.Find(target.Creation) is not { } creation || creation.FindPart(target.Part) is null)
        {
            if (IsOpen)
                Close();
            return;
        }
        _sincePoll += delta;
        if (_sincePoll >= PollSeconds && Signals is not null)
        {
            _sincePoll = 0;
            Signals.Poll(target.Creation);
        }
        var signals = SignalLine(creation, target.Part);
        if (_card.Visible)
        {
            _cardSignals.Text = signals ?? "";
            var size = GetViewport().GetVisibleRect().Size;
            _card.ResetSize();
            _card.Position = new Vector2((size.X - _card.Size.X) / 2, size.Y * 0.22f);
        }
        if (!IsOpen)
            return;
        _signals.Visible = signals is not null;
        _signals.Text = signals ?? "";
        if ((PartSettings.Sequencer(creation, target.Part) is not null) != _locked)
        {
            // A Controller was linked in or out while the menu was open: show what the part allows now.
            Open(target.Creation, target.Part);
            return;
        }
        UpdateReadouts(creation, target.Part);
        var linked = creation.LinksTo(target.Part).Count > 0;
        foreach (var (_, row, note) in _linkedRows)
        {
            row.Visible = !linked;
            note.Visible = linked;
        }
        UpdatePreview(creation, target.Part);
        // The rows scroll rather than push the window off a small screen.
        _scroll.CustomMinimumSize = new Vector2(0, Math.Min(_rows.GetCombinedMinimumSize().Y, GetViewport().GetVisibleRect().Size.Y * 0.6f));
    }

    /// <summary>Shows a part's menu; a part without settings or readouts shows a note instead.</summary>
    public void Open(int creationId, int partId)
    {
        if (Creations.Find(creationId) is not { } creation || creation.FindPart(partId) is not { } part)
            return;
        if (IsOpen)
            Close();
        Target = (creationId, partId);
        _pending.Clear();
        _readoutViews.Clear();
        _linkedRows.Clear();
        Sequence = null;
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        var definition = creation.DefinitionOf(partId);
        _panel.Title = Loc.T("{0} (part {1})", definition.Name ?? part.Part, partId);
        var menu = definition.Menu;
        _locked = false;
        if (PartSettings.Sequencer(creation, partId) is { } sequencer)
        {
            // The Controller runs the part (a piston): its settings stay shut until the Controller is unlinked.
            _locked = true;
            _status.Text = Loc.T("This {0} is run by the {1} linked to it. Unlink the {1} to change its settings.",
                definition.Name ?? part.Part, creation.DefinitionOf(sequencer).Name ?? Loc.T("Controller"));
            _apply.Visible = false;
            _panel.Visible = true;
            _previewRange = -1;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            return;
        }
        _status.Text = definition.Settings is { Count: > 0 } || menu?.Readouts is { Count: > 0 } || menu?.Signals == true ? "" : "This part has no settings.";
        foreach (var readout in menu?.Readouts ?? [])
            _rows.AddChild(ReadoutRow(readout));
        var settings = definition.Settings ?? [];
        for (var i = 0; i < settings.Count; i++)
        {
            var setting = settings[i];
            Control row;
            if (setting is { Type: SettingType.Toggle, Group: { } group })
            {
                var members = new List<SettingDefinition> { setting };
                while (i + 1 < settings.Count && settings[i + 1] is { Type: SettingType.Toggle } next && next.Group == group)
                    members.Add(settings[++i]);
                row = GroupRow(group, members, definition, part);
            }
            else
            {
                row = Field(creation, partId, setting, PartSettings.Value(definition, part.Settings, setting.Id!)!.Value);
            }
            _rows.AddChild(row);
            if (setting.HideWhenLinked == true)
            {
                var note = new Label { Text = "Driven by what is linked to it.", Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
                _rows.AddChild(note);
                _linkedRows.Add((setting, row, note));
            }
        }
        _apply.Visible = menu?.Apply == true;
        _apply.Disabled = true;
        _panel.Visible = true;
        _previewRange = -1;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _Process(0);
    }

    /// <summary>Hides the panel, sending changes that wait for Apply.</summary>
    public void Close()
    {
        if (!IsOpen)
            return;
        Apply();
        Sequence?.SetPlaying(false);
        _panel.Visible = false;
        Target = null;
        RemovePreview();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Sends the changes waiting for Apply.</summary>
    public void Apply()
    {
        if (_pending.Count == 0 || Target is not { } target)
            return;
        var changes = new Dictionary<string, JsonElement?>(_pending);
        _pending.Clear();
        _apply.Disabled = true;
        Actions.Request(LogicActions.ConfigurePart, new ConfigurePartArgs(target.Creation, target.Part, changes),
            result => _status.Text = result.Ok ? "Applied." : result.Message ?? "");
    }

    /// <summary>The option E would switch a cycling part (<see cref="MenuUse.Cycle"/>) to, or null.</summary>
    public string? NextOption(int creationId, int partId)
    {
        if (Creations.Find(creationId) is not { } creation || creation.FindPart(partId) is not { } part)
            return null;
        var definition = creation.DefinitionOf(partId);
        if (definition.Menu is not { Use: MenuUse.Cycle, Cycle: { } id } || PartSettings.Find(definition, id) is not { Options: { Count: > 0 } options })
            return null;
        var current = PartSettings.Value(definition, part.Settings, id)?.GetString();
        return options[(options.IndexOf(current ?? "") + 1) % options.Count];
    }

    /// <summary>
    /// Steps a cycling part's setting to its next option on the host and shows it large, with its picture and the
    /// part's live signals, for <see cref="CardSeconds"/>. False when the part does not cycle.
    /// </summary>
    public bool Cycle(int creationId, int partId)
    {
        if (NextOption(creationId, partId) is not { } next)
            return false;
        var creation = Creations.Find(creationId)!;
        var definition = creation.DefinitionOf(partId);
        var setting = PartSettings.Find(definition, definition.Menu!.Cycle!)!;
        Actions.Request(LogicActions.ConfigurePart,
            new ConfigurePartArgs(creationId, partId, new Dictionary<string, JsonElement?> { [setting.Id!] = JsonSerializer.SerializeToElement(next) }),
            result =>
            {
                if (!result.Ok)
                    _cardName.Text = result.Message ?? "";
            });
        _cardTarget = (creationId, partId);
        _cardName.Text = OptionLabel(setting, next);
        _cardIcon.Texture = Icon(creation, partId, setting, next);
        _cardIcon.Visible = _cardIcon.Texture is not null;
        _cardSignals.Text = definition.Menu.Signals == true ? SignalLine(creation, partId) ?? "" : "";
        _cardLeft = CardSeconds;
        _card.Visible = true;
        Signals?.Poll(creationId);
        return true;
    }

    /// <summary>How an option shows: its id with spaces, in capitals when its setting has pictures (gate names).</summary>
    public static string OptionLabel(SettingDefinition setting, string option)
    {
        var words = option.Replace('_', ' ');
        return setting.Icons is { Count: > 0 } ? words.ToUpperInvariant() : char.ToUpperInvariant(words[0]) + words[1..];
    }

    /// <summary>The label E's hint uses for the next option of a cycling part.</summary>
    public static string OptionLabel(string option) => option.Replace('_', ' ').ToUpperInvariant();

    // "Inputs: on, off   Output: on" for a part whose menu shows signals; null otherwise.
    private string? SignalLine(Creation creation, int part)
    {
        if (creation.DefinitionOf(part).Menu?.Signals != true || Signals is null)
            return null;
        string State(float value) => value == 0 ? Loc.T("off") : value == 1 ? Loc.T("on") : value.ToString("0.##", CultureInfo.InvariantCulture);
        var inputs = creation.LinksTo(part).Select(from => State(Signals.SignalOf(creation.Id, from))).ToList();
        return Loc.T("Inputs: {0}   Output: {1}", inputs.Count == 0 ? Loc.T("none") : string.Join(", ", inputs), State(Signals.SignalOf(creation.Id, part)));
    }

    private Control ReadoutRow(ReadoutDefinition readout)
    {
        var row = new HBoxContainer { Name = $"Readout_{readout.Id}" };
        row.AddChild(Loc.Verbatim(new Label { Text = readout.Name ?? readout.Id ?? "", CustomMinimumSize = new Vector2(140, 0) }));
        Control view;
        if (readout.Kind == ReadoutKind.Bar)
        {
            var bar = new ProgressBar { MinValue = 0, MaxValue = readout.Max ?? 1, Step = 0, ShowPercentage = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 22) };
            bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = UiTokens.Cyan, CornerRadiusTopLeft = 3, CornerRadiusBottomLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomRight = 3 });
            view = bar;
        }
        else
        {
            view = Loc.Verbatim(new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        }
        view.Name = "Value";
        row.AddChild(view);
        _readoutViews.Add((readout, view));
        return row;
    }

    private void UpdateReadouts(Creation creation, int part)
    {
        var readouts = Readouts?.Invoke();
        foreach (var (readout, view) in _readoutViews)
        {
            var number = readouts?.Number(creation.Id, part, readout.Id!);
            var text = readouts?.Text(creation.Id, part, readout.Id!);
            switch (view)
            {
                case ProgressBar bar:
                    bar.Value = number ?? 0;
                    break;
                case Label label:
                    label.Text = text ?? (number is { } n
                        ? $"{n.ToString($"F{readout.Decimals ?? 0}", CultureInfo.InvariantCulture)}{(readout.Unit is { } unit ? $" {unit}" : "")}"
                        : "—");
                    break;
            }
        }
    }

    private Control Field(Creation creation, int partId, SettingDefinition setting, JsonElement value)
    {
        var column = new VBoxContainer { Name = $"Setting_{setting.Id}" };
        var row = new HBoxContainer();
        column.AddChild(row);
        row.AddChild(Loc.Verbatim(new Label { Text = setting.Name ?? setting.Id, CustomMinimumSize = new Vector2(140, 0) }));
        switch (setting.Type)
        {
            case SettingType.Number:
                var slider = new HSlider
                {
                    Name = "Slider", MinValue = setting.Min ?? 0, MaxValue = setting.Max ?? 1, Step = setting.Step ?? 0.01, Value = value.GetDouble(),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                };
                var spin = new SpinBox
                {
                    Name = "Value", MinValue = setting.Min ?? 0, MaxValue = setting.Max ?? 1, Step = setting.Step ?? 0.01, Value = value.GetDouble(),
                    CustomMinimumSize = new Vector2(96, 0),
                };
                slider.ValueChanged += v =>
                {
                    spin.SetValueNoSignal(v);
                    Send(setting.Id!, JsonSerializer.SerializeToElement(v));
                };
                spin.ValueChanged += v => slider.Value = v;
                row.AddChild(slider);
                row.AddChild(spin);
                if (setting.Presets is { Count: > 0 } presets)
                {
                    var buttons = new HBoxContainer { Name = "Presets", Alignment = BoxContainer.AlignmentMode.End };
                    foreach (var preset in presets)
                    {
                        var button = Loc.Verbatim(new Button { Name = $"Preset_{preset.Name}", Text = preset.Name ?? "", CustomMinimumSize = new Vector2(72, 32) });
                        button.Pressed += () => slider.Value = preset.Value ?? 0;
                        buttons.AddChild(button);
                    }
                    column.AddChild(buttons);
                }
                break;
            case SettingType.Choice:
                var picture = new TextureRect
                {
                    Name = "Icon", CustomMinimumSize = new Vector2(48, 48), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Visible = setting.Icons is { Count: > 0 },
                };
                var options = Loc.Verbatim(new OptionButton { Name = "Options", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
                foreach (var option in setting.Options ?? [])
                    options.AddItem(OptionLabel(setting, option));
                options.Selected = Math.Max(0, setting.Options?.IndexOf(value.GetString()!) ?? 0);
                picture.Texture = Icon(creation, partId, setting, setting.Options?.ElementAtOrDefault(options.Selected));
                options.ItemSelected += i =>
                {
                    var option = setting.Options![(int)i];
                    picture.Texture = Icon(creation, partId, setting, option);
                    Send(setting.Id!, JsonSerializer.SerializeToElement(option));
                };
                row.AddChild(picture);
                row.AddChild(options);
                break;
            case SettingType.Toggle:
                var check = new CheckBox { Name = "Toggle", ButtonPressed = value.ValueKind == JsonValueKind.True };
                check.Toggled += on => Send(setting.Id!, JsonSerializer.SerializeToElement(on));
                row.AddChild(check);
                break;
            case SettingType.Sequence:
                row.QueueFree();
                column.RemoveChild(row);
                Sequence = new SequenceEditor
                {
                    Name = "Sequence",
                    Setting = setting,
                    Joints = LinkedJoints(creation, partId, setting),
                    Initial = PartSettings.Frames(value, [.. LinkedJoints(creation, partId, setting).Where(j => !j.Piston).Select(j => j.Part)], Math.Min(SequenceEditor.NewSpeed, setting.MaxSpeed ?? 1)),
                    Changed = frames => Send(setting.Id!, frames),
                    Preview = setting.Preview is { } command ? (playing, frames) => Command(command, playing, setting, frames) : null,
                };
                column.AddChild(Sequence);
                break;
            default:
                var edit = new LineEdit
                {
                    Text = string.Join(", ", value.EnumerateArray().Select(e => e.GetDouble().ToString(CultureInfo.InvariantCulture))),
                    PlaceholderText = Loc.T("up to {0} numbers: 0, 45, 90", setting.MaxCount),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                };
                edit.TextSubmitted += text =>
                {
                    var numbers = new List<double>();
                    foreach (var entry in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!double.TryParse(entry, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                        {
                            _status.Text = Loc.T("'{0}' is not a number.", entry);
                            return;
                        }
                        numbers.Add(n);
                    }
                    Send(setting.Id!, JsonSerializer.SerializeToElement(numbers));
                };
                row.AddChild(edit);
                break;
        }
        return column;
    }

    // Toggles of one group side by side under its heading (a sensor's Players, Bots, Blocks, Creations).
    private Control GroupRow(string group, List<SettingDefinition> members, PartDefinition definition, PartInstance part)
    {
        var row = new HBoxContainer { Name = $"Group_{members[0].Id}" };
        row.AddChild(Loc.Verbatim(new Label { Text = group, CustomMinimumSize = new Vector2(140, 0) }));
        foreach (var setting in members)
        {
            var on = PartSettings.Value(definition, part.Settings, setting.Id!) is { ValueKind: JsonValueKind.True };
            var button = Loc.Verbatim(new Button { Name = $"Toggle_{setting.Id}", Text = setting.Name ?? setting.Id ?? "", ToggleMode = true, ButtonPressed = on, CustomMinimumSize = new Vector2(80, 32) });
            button.Toggled += value => Send(setting.Id!, JsonSerializer.SerializeToElement(value));
            row.AddChild(button);
        }
        return row;
    }

    // The bearings and pistons the part is linked to, for its sequence.
    private static List<SequenceEditor.Joint> LinkedJoints(Creation creation, int partId, SettingDefinition setting)
    {
        var joints = new List<SequenceEditor.Joint>();
        foreach (var to in creation.LinksFrom(partId))
        {
            var definition = creation.DefinitionOf(to);
            var socket = definition.Sockets?.FirstOrDefault(s => s.Type is SocketType.Rotational or SocketType.Linear);
            if (socket is null)
                continue;
            var piston = socket.Type == SocketType.Linear;
            var (min, max) = piston
                ? (Math.Max(setting.Min ?? 0, socket.Joint?.Min ?? 0), Math.Min(setting.Max ?? 1, socket.Joint?.Max ?? 1))
                : (setting.Min ?? -180, setting.Max ?? 180);
            joints.Add(new SequenceEditor.Joint(to, definition.Name ?? creation.FindPart(to)!.Part, piston, min, max));
        }
        return joints;
    }

    private void Command(string command, bool playing, SettingDefinition setting, JsonElement frames)
    {
        if (Target is not { } target)
            return;
        var args = JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["playing"] = playing, [setting.Id!] = frames });
        Actions.Request(LogicActions.CommandPart, new PartCommandArgs(target.Creation, target.Part, command, args),
            result => _status.Text = result.Ok ? playing ? "Previewing." : "" : result.Message ?? "");
    }

    private void Send(string id, JsonElement value)
    {
        if (Target is not { } target || Creations.Find(target.Creation) is not { } creation)
            return;
        if (creation.DefinitionOf(target.Part).Menu?.Apply == true)
        {
            _pending[id] = value;
            _apply.Disabled = false;
            _status.Text = "Not applied yet: press Apply or close the menu.";
            return;
        }
        Actions.Request(LogicActions.ConfigurePart, new ConfigurePartArgs(target.Creation, target.Part, new Dictionary<string, JsonElement?> { [id] = value }),
            result => _status.Text = result.Ok ? "Saved." : result.Message ?? "");
    }

    private Texture2D? Icon(Creation creation, int part, SettingDefinition setting, string? option) =>
        option is not null && setting.Icons?.GetValueOrDefault(option) is { } path
            ? Assets?.TextureFor(Identifiers.Qualify(creation.FindPart(part)!.Part), path)
            : null;

    // The part's reach drawn in the world while the menu is open, sized by its range setting (pending changes included).
    private void UpdatePreview(Creation creation, int part)
    {
        var definition = creation.DefinitionOf(part);
        if (definition.Menu?.Preview is not { } preview || Creations.NodeOf(creation.Id)?.PartNode(part) is not { } node)
        {
            RemovePreview();
            return;
        }
        var range = PreviewRange(definition, creation.FindPart(part)!, preview);
        if (_preview is not null && IsInstanceValid(_preview) && _preview.GetParent() == node && Mathf.IsEqualApprox(range, _previewRange))
            return;
        RemovePreview();
        _previewRange = range;
        var axis = (preview.Axis ?? new Vec3(0, 1, 0)).ToGodot().Normalized();
        var turn = axis.Y < -0.99f ? new Basis(Vector3.Right, Mathf.Pi) : new Basis(new Quaternion(Vector3.Up, axis));
        Mesh mesh = preview.Shape switch
        {
            PreviewShape.Sphere => new SphereMesh { Radius = range, Height = range * 2, RadialSegments = 32, Rings = 16 },
            PreviewShape.Cone => new CylinderMesh { TopRadius = range * Mathf.Tan(Mathf.DegToRad(preview.Angle ?? 10)), BottomRadius = 0.01f, Height = range, RadialSegments = 32 },
            _ => new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = range },
        };
        _preview = new MeshInstance3D
        {
            Name = "MenuPreview",
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // A cone or ray starts at the part and runs out along the axis; a sphere sits on the part.
            Transform = new Transform3D(turn, preview.Shape == PreviewShape.Sphere ? Vector3.Zero : axis * (range / 2)),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = UiTokens.Cyan with { A = 0.18f },
            },
        };
        node.AddChild(_preview);
    }

    private float PreviewRange(PartDefinition definition, PartInstance part, PreviewDefinition preview)
    {
        var id = preview.Range!;
        if (_pending.TryGetValue(id, out var pending) && pending is { ValueKind: JsonValueKind.Number } p)
            return p.GetSingle();
        return PartSettings.Value(definition, part.Settings, id) is { ValueKind: JsonValueKind.Number } v ? v.GetSingle() : 1f;
    }

    private void RemovePreview()
    {
        if (_preview is not null && IsInstanceValid(_preview))
        {
            _preview.GetParent()?.RemoveChild(_preview);
            _preview.QueueFree();
        }
        _preview = null;
    }
}
