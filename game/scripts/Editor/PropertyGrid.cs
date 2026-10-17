using System.Globalization;
using Godot;
using Mechanica.Core.Data;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Editor;

/// <summary>
/// A two-column label/field form for the Part Editor inspector. Each <c>Add…</c> method shows a value
/// and calls back with the new value when the user commits an edit; the callback applies it to the
/// document. Fields are rebuilt from the document after undo, redo and selection changes.
/// </summary>
public partial class PropertyGrid : GridContainer
{
    /// <summary>Fields by label, for tests and keyboard focus.</summary>
    public Dictionary<string, Control> Fields { get; } = [];

    public PropertyGrid()
    {
        Columns = 2;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("h_separation", 8);
    }

    /// <summary>Removes every row.</summary>
    public void Clear()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        Fields.Clear();
    }

    /// <summary>A heading row spanning both columns.</summary>
    public Label AddHeading(string text)
    {
        var heading = new Label { Text = text, ThemeTypeVariation = "HeaderSmall" };
        AddChild(heading);
        AddChild(new Control());
        return heading;
    }

    /// <summary>A text field; an empty text commits null.</summary>
    public LineEdit AddText(string label, string? value, Action<string?> set, string placeholder = "")
    {
        var edit = new LineEdit { Text = value ?? "", PlaceholderText = placeholder, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        void Commit()
        {
            var text = edit.Text.Trim();
            if (text != (value ?? ""))
                set(text.Length == 0 ? null : text);
        }
        edit.TextSubmitted += _ => Commit();
        edit.FocusExited += Commit;
        Row(label, edit);
        return edit;
    }

    /// <summary>A number field. With <paramref name="optional"/>, a checkbox turns the value on and off (null).</summary>
    public SpinBox AddNumber(string label, double? value, double min, double max, double step, Action<double?> set,
        bool optional = false, double fallback = 0, string suffix = "")
    {
        var spin = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value ?? fallback,
            Suffix = suffix,
            AllowGreater = max >= 1e6,
            SelectAllOnFocus = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Editable = !optional || value is not null,
        };
        spin.GetLineEdit().ExpandToTextLength = false;
        spin.ValueChanged += v => set(v);
        if (!optional)
        {
            Row(label, spin);
            return spin;
        }
        var toggle = new CheckBox { ButtonPressed = value is not null, TooltipText = "Set this value" };
        toggle.Toggled += on =>
        {
            spin.Editable = on;
            set(on ? spin.Value : null);
        };
        var box = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddChild(toggle);
        box.AddChild(spin);
        Row(label, box);
        Fields[label + ".value"] = spin;
        return spin;
    }

    /// <summary>A checkbox. With <paramref name="optional"/> the value can also be unset (null), shown as a third state.</summary>
    public OptionButton AddBool(string label, bool? value, Action<bool?> set, bool optional = false)
    {
        var option = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (optional)
            option.AddItem("(not set)");
        option.AddItem("No");
        option.AddItem("Yes");
        var offset = optional ? 1 : 0;
        option.Selected = value switch { true => 1 + offset, false => offset, null => optional ? 0 : 0 };
        option.ItemSelected += i => set(optional && i == 0 ? null : i - offset == 1);
        Row(label, option);
        return option;
    }

    /// <summary>A dropdown of an enum's snake_case names. With <paramref name="optional"/>, "(not set)" commits null.</summary>
    public OptionButton AddEnum<T>(string label, T? value, Action<T?> set, bool optional = false) where T : struct, Enum
    {
        // Enum values are the JSON's own words.
        var option = Loc.Verbatim(new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var values = Enum.GetValues<T>();
        if (optional)
            option.AddItem("(not set)");
        foreach (var v in values)
            option.AddItem(System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()));
        var offset = optional ? 1 : 0;
        option.Selected = value is { } current ? Array.IndexOf(values, current) + offset : optional ? 0 : -1;
        option.ItemSelected += i => set(optional && i == 0 ? null : values[i - offset]);
        Row(label, option);
        return option;
    }

    /// <summary>Three number fields for a vector.</summary>
    public HBoxContainer AddVector(string label, Vec3 value, double step, Action<Vec3> set, double min = -100, double max = 100)
    {
        var box = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var spins = new SpinBox[3];
        float[] current = [value.X, value.Y, value.Z];
        for (var i = 0; i < 3; i++)
        {
            var axis = i;
            spins[i] = new SpinBox
            {
                MinValue = min,
                MaxValue = max,
                Step = step,
                Value = current[i],
                Prefix = "xyz"[i].ToString(),
                SelectAllOnFocus = true,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            spins[i].GetLineEdit().ExpandToTextLength = false;
            spins[i].ValueChanged += v =>
            {
                current[axis] = (float)v;
                set(new Vec3(current[0], current[1], current[2]));
            };
            box.AddChild(spins[i]);
        }
        Row(label, box);
        return box;
    }

    /// <summary>Three whole-number fields for a grid size.</summary>
    public HBoxContainer AddGridSize(string label, GridSize value, Action<GridSize> set)
    {
        var box = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        int[] current = [value.X, value.Y, value.Z];
        for (var i = 0; i < 3; i++)
        {
            var axis = i;
            var spin = new SpinBox { MinValue = 1, MaxValue = 64, Step = 1, Value = current[i], Prefix = "xyz"[i].ToString(), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            spin.ValueChanged += v =>
            {
                current[axis] = (int)v;
                set(new GridSize(current[0], current[1], current[2]));
            };
            box.AddChild(spin);
        }
        Row(label, box);
        return box;
    }

    /// <summary>A colour picker with a clear button; commits <c>"#rrggbb"</c> or null.</summary>
    public HBoxContainer AddColor(string label, string? hex, Action<string?> set)
    {
        var box = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var color = HexColor.TryParse(hex, out var c) ? new Color(c.R, c.G, c.B) : Colors.White;
        var picker = new ColorPickerButton { Color = color, EditAlpha = false, CustomMinimumSize = new Vector2(64, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        picker.PopupClosed += () =>
        {
            var text = HexColor.Format(picker.Color.R, picker.Color.G, picker.Color.B);
            if (text != hex)
                set(text);
        };
        box.AddChild(picker);
        box.AddChild(Loc.Verbatim(new Label { Text = hex ?? Loc.T("(none)") }));
        var clear = new Button { Text = "Clear", Disabled = hex is null };
        clear.Pressed += () => set(null);
        box.AddChild(clear);
        Row(label, box);
        return box;
    }

    /// <summary>A multi-line JSON field with an Apply button; <paramref name="set"/> returns an error to show, or null.</summary>
    public VBoxContainer AddJson(string label, string json, Func<string, string?> set)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var edit = new CodeEdit { Text = json, CustomMinimumSize = new Vector2(0, 120), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        var apply = new Button { Text = "Apply", TooltipText = "Check the JSON and use it" };
        apply.Pressed += () => status.Text = set(edit.Text) ?? "";
        box.AddChild(edit);
        var row = new HBoxContainer();
        row.AddChild(apply);
        row.AddChild(status);
        box.AddChild(row);
        Row(label, box);
        return box;
    }

    /// <summary>A label and any control.</summary>
    public void Row(string label, Control field)
    {
        AddChild(new Label { Text = label, SizeFlagsVertical = SizeFlags.ShrinkBegin });
        AddChild(field);
        Fields[label] = field;
    }

    /// <summary>Formats a number the way part files do.</summary>
    public static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
}
