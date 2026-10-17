using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Input;

namespace Mechanica.Game.Building;

/// <summary>
/// The Paint Tool in the local player's hand. Left click paints the part under the crosshair in the
/// chosen colour, right click takes the paint off, Q/E pick the colour from the palette in
/// <c>building.json</c>. Each click is a <c>Paint</c> request; the host decides.
/// </summary>
public partial class PaintTool : HandTool
{
    private int _index;

    /// <inheritdoc/>
    public override string ToolName => "Paint Tool";

    /// <inheritdoc/>
    public override CharacterProp Prop => CharacterProp.PaintTool;

    /// <inheritdoc/>
    protected override CharacterGesture UseGesture => CharacterGesture.PaintSpray;

    /// <summary>The colour <see cref="Paint"/> uses, <c>"#rrggbb"</c>.</summary>
    public string Color => Build.Settings!.Paint.Palette[_index];

    /// <summary>Picks the next (or previous) palette colour.</summary>
    public void Cycle(int step)
    {
        var count = Build.Settings!.Paint.Palette.Count;
        _index = ((_index + step) % count + count) % count;
    }

    /// <summary>Paints the part under the crosshair. False when no part is aimed at.</summary>
    public bool Paint(Action<ActionResult>? done = null) => Send(Color, done);

    /// <summary>Takes the paint off the part under the crosshair. False when no part is aimed at.</summary>
    public bool Erase(Action<ActionResult>? done = null) => Send(null, done);

    /// <inheritdoc/>
    public override string HudText() => $"Paint Tool ({Color})   {Key(InputActions.Rotate)}/{Key(InputActions.RotateBack)} colour  {Key(InputActions.Primary)} paint  {Key(InputActions.Secondary)} remove paint{Status}";

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
            Paint();
        else if (Pressed(@event, InputActions.Secondary))
            Erase();
        else if (Pressed(@event, InputActions.RotateBack))
            Cycle(-1);
        else if (Pressed(@event, InputActions.Rotate))
            Cycle(1);
        else
            return false;
        return true;
    }

    private bool Send(string? color, Action<ActionResult>? done)
    {
        if (CastRay() is not { Body: { } body, Node: { } node } hit)
            return false;
        var toGrid = body.GlobalTransform.AffineInverse();
        var (cell, _) = BuildAim.FromHit((toGrid * hit.Point).ToNumerics(), (toGrid.Basis * hit.Normal).ToNumerics());
        if (node.Creation.PartAt(cell) is not { } part)
            return false;
        Send(ToolActions.Paint, new PaintArgs(node.Creation.Id, [part.Id], color), done);
        return true;
    }
}
