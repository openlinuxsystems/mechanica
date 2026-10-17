using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Game.Player;
using Mechanica.Game.World;

namespace Mechanica.Game.Building;

/// <summary>
/// The Sledgehammer in the local player's hand. Left click swings at the part under the crosshair: the
/// host pushes its Body and takes health off the part, which breaks off at 0 (<see cref="SledgeSettings"/>).
/// In Survival it also hurts the player, or chips at the tree or rock, under the crosshair.
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
        var hit = CastRay();
        SledgeArgs args;
        if (hit?.Collider is PlayerController target)
            args = new SledgeArgs(Player: target.PeerId);
        else if (hit?.Collider is StaticBody3D objects && objects.GetParent() is TerrainChunk chunk && objects == chunk.Objects
            && chunk.ObjectAt(hit.Value.Shape) is { } id)
            args = new SledgeArgs(Object: id.ToString());
        else if (PartHit(hit) is { } aimed)
            args = new SledgeArgs(aimed.Node.Creation.Id, aimed.Part);
        else
            return false;
        _lastSwing = now;
        Send(ToolActions.Sledge, args, done);
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
