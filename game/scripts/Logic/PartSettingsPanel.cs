using System.Globalization;
using System.Text.Json;
using Godot;
using Mechanica.Game.Ui;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Logic;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Logic;

/// <summary>
/// The settings of one placed part (chunk 10), opened with Use (E) on the part and closed with Use or Esc: a field per
/// setting of its definition (<see cref="PartDefinition.Settings"/>), each sent to the host as
/// <c>ConfigurePart</c> when changed. Number lists are typed as numbers separated by commas.
/// </summary>
public partial class PartSettingsPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private VBoxContainer _rows = null!;
    private Label _status = null!;

    /// <summary>Where changes go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>The Creations, for the part's definition and current values. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>The part shown, or null.</summary>
    public (int Creation, int Part)? Target { get; private set; }

    /// <summary>True while the panel is up.</summary>
    public bool IsOpen => _panel.Visible;

    public override void _Ready()
    {
        Layer = 5;
        _panel = UiInput.KeepMouse(new PanelContainer { Name = "Settings", Visible = false, CustomMinimumSize = new Vector2(360, 0) });
        AddChild(_panel);
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.KeepSize);
        var box = new VBoxContainer();
        _panel.AddChild(box);
        _rows = new VBoxContainer();
        box.AddChild(_rows);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_status);
        var close = new Button { Text = "Close" };
        close.Pressed += Close;
        box.AddChild(close);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (IsOpen && (GameInput.Pressed(@event, InputActions.Pause) || GameInput.Pressed(@event, InputActions.Interact)))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Shows a part's settings; a part without settings shows a note instead.</summary>
    public void Open(int creationId, int partId)
    {
        if (Creations.Find(creationId) is not { } creation || creation.FindPart(partId) is not { } part)
            return;
        Target = (creationId, partId);
        foreach (var child in _rows.GetChildren())
            child.QueueFree();
        var definition = creation.DefinitionOf(partId);
        _rows.AddChild(new Label { Text = $"{definition.Name ?? part.Part} (part {partId})" });
        _status.Text = definition.Settings is { Count: > 0 } ? "" : "This part has no settings.";
        foreach (var (id, value) in PartSettings.All(definition, part.Settings))
            _rows.AddChild(Field(PartSettings.Find(definition, id)!, value));
        _panel.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Hides the panel.</summary>
    public void Close()
    {
        if (!IsOpen)
            return;
        _panel.Visible = false;
        Target = null;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private Control Field(SettingDefinition setting, JsonElement value)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = setting.Name ?? setting.Id, CustomMinimumSize = new Vector2(140, 0) });
        switch (setting.Type)
        {
            case SettingType.Number:
                var spin = new SpinBox
                {
                    MinValue = setting.Min ?? 0, MaxValue = setting.Max ?? 1, Step = setting.Step ?? 0.01, Value = value.GetDouble(),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                };
                spin.ValueChanged += v => Send(setting.Id!, JsonSerializer.SerializeToElement(v));
                row.AddChild(spin);
                break;
            case SettingType.Choice:
                var options = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                foreach (var option in setting.Options ?? [])
                    options.AddItem(option);
                options.Selected = Math.Max(0, setting.Options?.IndexOf(value.GetString()!) ?? 0);
                options.ItemSelected += i => Send(setting.Id!, JsonSerializer.SerializeToElement(setting.Options![(int)i]));
                row.AddChild(options);
                break;
            case SettingType.Toggle:
                var check = new CheckBox { ButtonPressed = value.ValueKind == JsonValueKind.True };
                check.Toggled += on => Send(setting.Id!, JsonSerializer.SerializeToElement(on));
                row.AddChild(check);
                break;
            default:
                var edit = new LineEdit
                {
                    Text = string.Join(", ", value.EnumerateArray().Select(e => e.GetDouble().ToString(CultureInfo.InvariantCulture))),
                    PlaceholderText = $"up to {setting.MaxCount} numbers: 0, 45, 90",
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                };
                edit.TextSubmitted += text =>
                {
                    var numbers = new List<double>();
                    foreach (var entry in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!double.TryParse(entry, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                        {
                            _status.Text = $"'{entry}' is not a number.";
                            return;
                        }
                        numbers.Add(n);
                    }
                    Send(setting.Id!, JsonSerializer.SerializeToElement(numbers));
                };
                row.AddChild(edit);
                break;
        }
        return row;
    }

    private void Send(string id, JsonElement value)
    {
        if (Target is not { } target)
            return;
        Actions.Request(LogicActions.ConfigurePart, new ConfigurePartArgs(target.Creation, target.Part, new Dictionary<string, JsonElement?> { [id] = value }),
            result => _status.Text = result.Ok ? "Saved." : result.Message ?? "");
    }
}
