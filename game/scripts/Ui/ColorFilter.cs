using Godot;
using Mechanica.Core.Ui;

namespace Mechanica.Game.Ui;

/// <summary>
/// The colour-blind filter (UI spec §16 Interface): a full-screen pass over the world and every window that
/// corrects colours for the chosen <see cref="ColorBlindMode"/> (<see cref="ColorBlindness"/>). It sits on
/// the top layer and never takes the mouse. <see cref="Settings.GameSettings"/> owns one and sets its mode.
/// </summary>
public partial class ColorFilter : CanvasLayer
{
    private readonly Dictionary<ColorBlindMode, Shader> _shaders = [];
    private ColorRect _rect = null!;

    /// <summary>The filter in use.</summary>
    public ColorBlindMode Mode { get; private set; }

    /// <summary>The full-screen rectangle that draws the filter (hidden when off).</summary>
    public ColorRect Rect => _rect;

    public override void _Ready()
    {
        Layer = 128;
        _rect = new ColorRect { Name = "Filter", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        AddChild(_rect);
        _rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Apply(Mode);
    }

    /// <summary>Uses <paramref name="mode"/>; Off removes the filter.</summary>
    public void Apply(ColorBlindMode mode)
    {
        Mode = mode;
        if (_rect is null)
            return;
        _rect.Visible = mode != ColorBlindMode.Off;
        if (mode == ColorBlindMode.Off)
            return;
        if (!_shaders.TryGetValue(mode, out var shader))
            _shaders[mode] = shader = new Shader { Code = ColorBlindness.Shader(mode) };
        _rect.Material = new ShaderMaterial { Shader = shader };
    }
}
