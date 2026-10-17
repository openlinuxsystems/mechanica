using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;

namespace Mechanica.Game.Building;

/// <summary>
/// The Sledgehammer in the local player's hand. Left click swings at the part under the crosshair: the
/// host pushes its Body and takes health off the part, which breaks off at 0 (<see cref="SledgeSettings"/>).
/// Swings faster than <see cref="SledgeSettings.SwingSeconds"/> are not sent.
/// </summary>
public partial class SledgeTool : HandTool
{
    private double _lastSwing = double.NegativeInfinity;

    /// <inheritdoc/>
    public override string ToolName => "Sledgehammer";

    /// <summary>Swings at the part under the crosshair. False when no part is aimed at or the last swing is still going.</summary>
    public bool Swing(Action<ActionResult>? done = null)
    {
        var now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastSwing < Build.Settings!.Sledge.SwingSeconds)
            return false;
        if (CastRay() is not { Body: { } body, Node: { } node } hit)
            return false;
        var toGrid = body.GlobalTransform.AffineInverse();
        var (cell, _) = BuildAim.FromHit((toGrid * hit.Point).ToNumerics(), (toGrid.Basis * hit.Normal).ToNumerics());
        if (node.Creation.PartAt(cell) is not { } part)
            return false;
        _lastSwing = now;
        Send(ToolActions.Sledge, new SledgeArgs(node.Creation.Id, part.Id), done);
        return true;
    }

    /// <inheritdoc/>
    public override string HudText()
    {
        var health = LastResult is { Ok: true, Value: { } value } && value.TryGetProperty("health", out var h) && h.ValueKind == System.Text.Json.JsonValueKind.Number
            ? $" (part health {h.GetSingle():0})"
            : "";
        return $"Sledgehammer{health}   [LMB] swing{Status}";
    }

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (!Clicked(@event, MouseButton.Left))
            return false;
        Swing();
        return true;
    }
}
