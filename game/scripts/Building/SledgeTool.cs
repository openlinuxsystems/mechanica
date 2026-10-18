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
/// The Sledgehammer in the local player's hand: every player's main tool, always on the hotbar (Chase's sledgehammer
/// spec). Left click swings (<see cref="SledgeSettings.SwingSeconds"/>, a swing every 0.6 s; holding it keeps swinging):
/// <see cref="SledgeSettings.StrikeSeconds"/> after the click (at the click, as shipped) the hit lands on whatever is
/// under the crosshair within <see cref="SledgeSettings.Reach"/>, the ground included, with a thud and a puff of dust
/// where it lands. The swing animation skips its wind-up and brings the head down a few frames after the click. The host pushes what it hits along the swing (<see cref="SledgeSettings.Impulse"/>) and
/// takes health off the part, which breaks off at 0; in Survival it also hurts and knocks back the player or bot
/// (<see cref="SledgeSettings.CombatDamage"/>), or chips at the tree or rock (its own gathering damage). A hit that lands
/// shakes the camera a little. In Creative, holding right click raises the hammer across the chest to block (UI spec
/// §10): everyone sees it, and it does nothing else. Survival has no block.
/// </summary>
public partial class SledgeTool : HandTool
{
    private double _swingStart = double.NegativeInfinity;
    private bool _strikePending;
    private Action<ActionResult>? _strikeDone;
    private double _shakeLeft;

    /// <summary>Swings started so far (tests count them).</summary>
    public int Swings { get; private set; }

    /// <summary>True while left click is held: the hammer keeps swinging, one swing straight after another.</summary>
    public bool HoldingSwing { get; set; }

    /// <summary>True while right click is held to block (as this peer asked; the host keeps the real state).</summary>
    public bool Blocking { get; private set; }

    /// <summary>Whether this player may raise the block pose (Creative only); null always may.</summary>
    public Func<bool>? CanBlock { get; set; }

    /// <summary>True from a swing's click until it is over and the next can start.</summary>
    public bool Swinging => Now - _swingStart < Build.Settings!.Sledge.SwingSeconds;

    /// <summary>The camera shake left, in seconds (0 when still).</summary>
    public double ShakeLeft => _shakeLeft;

    private static double Now => Time.GetTicksMsec() / 1000.0;

    public override void _PhysicsProcess(double delta)
    {
        // A release the tool never heard (the mouse freed for a window, the tool put away) ends the block and the swinging.
        var lost = !Active || (ReadDeviceInput && Input.MouseMode != Input.MouseModeEnum.Captured);
        if (Blocking && lost)
            Block(false);
        if (lost)
            HoldingSwing = false;
        // Holding left click keeps swinging, the next swing starting as the last one ends.
        if (HoldingSwing && !Blocking && !Swinging)
            Swing();
        if (_strikePending && Now - _swingStart >= Build.Settings!.Sledge.StrikeSeconds)
            Strike();
    }

    public override void _Process(double delta)
    {
        if (_shakeLeft <= 0)
            return;
        var sledge = Build.Settings!.Sledge;
        _shakeLeft = Math.Max(0, _shakeLeft - delta);
        // A quick decaying jolt, mostly down then up, as the head stops.
        var k = (float)(_shakeLeft / Math.Max(sledge.ShakeSeconds, 1e-3f));
        var t = (float)(sledge.ShakeSeconds - _shakeLeft);
        Player.Camera.VOffset = -sledge.ShakeMetres * k * k * Mathf.Cos(t * 55f);
        Player.Camera.HOffset = sledge.ShakeMetres * 0.4f * k * k * Mathf.Sin(t * 73f);
        if (_shakeLeft <= 0)
            Player.Camera.HOffset = Player.Camera.VOffset = 0;
    }

    /// <summary>Starts (or stops) blocking. Starting does nothing where blocking is not allowed (Survival).</summary>
    public void Block(bool on, Action<ActionResult>? done = null)
    {
        if (Blocking == on || (on && CanBlock?.Invoke() == false))
            return;
        Blocking = on;
        Player.HeldGuard = on;
        Send(ToolActions.Block, new BlockArgs(on), done);
    }

    /// <inheritdoc/>
    public override string ToolName => "Sledgehammer";

    /// <inheritdoc/>
    public override CharacterProp Prop => CharacterProp.Sledgehammer;

    /// <inheritdoc/>
    protected override CharacterGesture UseGesture => CharacterGesture.SledgeSwing;

    /// <summary>
    /// Starts a swing: the gesture and whoosh now, the hit <see cref="SledgeSettings.StrikeSeconds"/> later on what is
    /// under the crosshair then. False while the last swing is still going. <paramref name="done"/> hears the host's
    /// answer, or a refusal when the swing hit nothing it can smash.
    /// </summary>
    public bool Swing(Action<ActionResult>? done = null)
    {
        if (Swinging)
            return false;
        _swingStart = Now;
        Swings++;
        _strikePending = true;
        _strikeDone = done;
        Player.Gesture(CharacterGesture.SledgeSwing);
        PlayFeedback(SwingSound);
        if (Build.Settings!.Sledge.StrikeSeconds <= 0)
            Strike();
        return true;
    }

    /// <summary>The whoosh of a swing, heard by the swinging player.</summary>
    public const string SwingSound = "sledgehammer_swing";

    // The head comes down: hit what is under the crosshair, within reach.
    private void Strike()
    {
        _strikePending = false;
        var done = _strikeDone;
        _strikeDone = null;
        if (!Active)
            return;
        if (Cast(Player, Build.Settings!.Sledge.Reach, brush: true) is not { } hit)
        {
            done?.Invoke(ActionResult.Failure("miss", "The swing hit nothing."));
            return;
        }
        // The head lands where the crosshair meets the first thing in reach; what it hit decides what it does there.
        var at = new Core.Data.Vec3(hit.Point.X, hit.Point.Y, hit.Point.Z);
        SledgeArgs args;
        if (hit.Collider is PlayerController target)
            args = new SledgeArgs(Player: target.PeerId, At: at);
        else if (hit.Collider is Bots.BotBody bot)
            args = new SledgeArgs(Bot: bot.BotId, At: at);
        else if (TerrainChunk.ObjectHit(hit.Collider, hit.Shape) is { } id)
            args = new SledgeArgs(Object: id.ToString(), At: at);
        else if (PartHit(hit) is { } aimed)
            args = new SledgeArgs(aimed.Node.Creation.Id, aimed.Part, At: at);
        else
            args = new SledgeArgs(At: at);   // the ground, a rock face: it thuds and kicks up dust
        Send(ToolActions.Sledge, args, result =>
        {
            if (result.Ok)
                _shakeLeft = Build.Settings!.Sledge.ShakeSeconds;
            done?.Invoke(result);
        });
    }

    /// <inheritdoc/>
    public override Core.Ui.CrosshairState Crosshair => PartHit(Cast(Player, Build.Settings!.Sledge.Reach)) is null
        ? Core.Ui.CrosshairState.Default
        : Core.Ui.CrosshairState.Valid;

    /// <inheritdoc/>
    public override string HudText()
    {
        var health = LastResult is { Ok: true, Value: { } value } && value.TryGetProperty("health", out var h) && h.ValueKind == System.Text.Json.JsonValueKind.Number
            ? " " + Loc.T("(part health {0})", $"{h.GetSingle():0}")
            : "";
        return (Blocking ? Loc.T("Sledgehammer{0}   blocking", health)
            : CanBlock?.Invoke() == false ? Loc.T("Sledgehammer{0}   {1} swing", health, Key(InputActions.Primary))
            : Loc.T("Sledgehammer{0}   {1} swing  hold {2} block", health, Key(InputActions.Primary), Key(InputActions.Secondary))) + Status;
    }

    /// <inheritdoc/>
    protected override CharacterGesture GestureFor(InputEvent @event) => CharacterGesture.None;   // Swing plays it

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
        {
            HoldingSwing = true;
            Swing();
        }
        else if (Released(@event, InputActions.Primary))
            HoldingSwing = false;
        else if (Pressed(@event, InputActions.Secondary) && CanBlock?.Invoke() != false)
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
        if (Active)
            return;
        Block(false);
        HoldingSwing = false;
        _strikePending = false;
        _strikeDone = null;
        _shakeLeft = 0;
        Player.Camera.HOffset = Player.Camera.VOffset = 0;
    }
}
