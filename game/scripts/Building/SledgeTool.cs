using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Input;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;
using Mechanica.Game.World;

namespace Mechanica.Game.Building;

/// <summary>
/// The Sledgehammer in the local player's hand. Left click swings at the part under the crosshair: the
/// host pushes its Body and takes health off the part, which breaks off at 0 (<see cref="SledgeSettings"/>).
/// In Survival it also hurts the player, or chips at the tree or rock, under the crosshair.
/// Swings faster than <see cref="SledgeSettings.SwingSeconds"/> (0.4 s, UI spec §10) are not sent. Holding right click
/// blocks (UI spec §9): while held, hits of the kinds <see cref="SledgeSettings.BlockCauses"/> names take only
/// <see cref="SledgeSettings.BlockDamageScale"/> of their damage. Creative has no damage, so blocking matters where
/// players can be hurt.
/// </summary>
public partial class SledgeTool : HandTool
{
    private double _lastSwing = double.NegativeInfinity;

    /// <summary>True while right click is held to block (as this peer asked; the host keeps the real state).</summary>
    public bool Blocking { get; private set; }

    public override void _PhysicsProcess(double delta)
    {
        // A release the tool never heard (the mouse freed for a window, the tool put away) ends the block.
        if (Blocking && (!Active || (ReadDeviceInput && Input.MouseMode != Input.MouseModeEnum.Captured)))
            Block(false);
    }

    /// <summary>Starts (or stops) blocking.</summary>
    public void Block(bool on, Action<ActionResult>? done = null)
    {
        if (Blocking == on)
            return;
        Blocking = on;
        Send(ToolActions.Block, new BlockArgs(on), done);
    }

    /// <inheritdoc/>
    public override string ToolName => "Sledgehammer";

    /// <inheritdoc/>
    public override CharacterProp Prop => CharacterProp.Sledgehammer;

    /// <inheritdoc/>
    protected override CharacterGesture UseGesture => CharacterGesture.SledgeSwing;

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
        else if (hit?.Collider is Bots.BotBody bot)
            args = new SledgeArgs(Bot: bot.BotId);
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
            ? " " + Loc.T("(part health {0})", $"{h.GetSingle():0}")
            : "";
        return (Blocking ? Loc.T("Sledgehammer{0}   blocking", health) : Loc.T("Sledgehammer{0}   {1} swing  hold {2} block", health, Key(InputActions.Primary), Key(InputActions.Secondary))) + Status;
    }

    /// <inheritdoc/>
    protected override CharacterGesture GestureFor(InputEvent @event) =>
        Pressed(@event, InputActions.Primary) ? UseGesture : CharacterGesture.None;

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
            Swing();
        else if (Pressed(@event, InputActions.Secondary))
            Block(true);
        else if (Released(@event, InputActions.Secondary) && Blocking)
            Block(false);
        else
            return false;
        return true;
    }

    /// <inheritdoc/>
    protected override void OnActiveChanged()
    {
        if (!Active)
            Block(false);
    }
}
