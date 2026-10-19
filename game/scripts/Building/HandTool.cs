using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Characters;
using Mechanica.Core.Input;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Building;

/// <summary>
/// Base of the local player's hand tools (building by hand, Lift, Weld Tool, Paint Tool, Sledgehammer). A tool
/// aims with a ray from the player's camera out to <see cref="Core.Building.BuildSettings.Reach"/> and
/// sends its requests through the Action API, so the host decides and nothing changes locally until it
/// answers. Only the <see cref="Active"/> tool reads keys and the mouse and shows its preview;
/// <see cref="ToolBelt"/> picks it. The host plays what an action did for everyone in earshot
/// (<see cref="Core.Building.ToolActions"/>); a tool itself only plays feedback its player alone hears: the
/// swing or spark as a request goes out, and <c>ui_error</c> when the host refuses one.
/// </summary>
public abstract partial class HandTool : Node
{
    private bool _active;

    /// <summary>What a ray from the camera hit.</summary>
    /// <param name="Point">World hit point.</param>
    /// <param name="Normal">World surface normal there.</param>
    /// <param name="Collider">What was hit.</param>
    /// <param name="Shape">Index of the collider's shape that was hit.</param>
    public readonly record struct RayHit(Vector3 Point, Vector3 Normal, GodotObject Collider, int Shape = 0)
    {
        /// <summary>The Creation body hit, or null.</summary>
        public CreationBody? Body => Collider as CreationBody;

        /// <summary>The Creation node hit, or null.</summary>
        public CreationNode? Node => Body?.GetParent() as CreationNode;
    }

    /// <summary>The player whose camera aims. Set before adding to the tree.</summary>
    public PlayerController Player { get; init; } = null!;

    /// <summary>Where requests go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>Settings, content and the world. Set before adding to the tree.</summary>
    public BuildSync Build { get; init; } = null!;

    /// <summary>Where feedback sounds play; null plays none.</summary>
    public Sound.SoundSync? Sounds { get; init; }

    /// <summary>False in automated tests: keys and mouse do nothing.</summary>
    public bool ReadDeviceInput { get; init; } = true;

    /// <summary>Display name.</summary>
    public abstract string ToolName { get; }

    /// <summary>
    /// Whether the player has this tool, or null when every player always has it. In Survival a tool that is also an
    /// item (the Connect Tool) is crafted at a Fabricator first; the host refuses its requests until then.
    /// </summary>
    public Func<bool>? Owned { get; set; }

    /// <summary>True when the player has this tool (<see cref="Owned"/>).</summary>
    public bool Owns => Owned?.Invoke() ?? true;

    /// <summary>The host's answer to the last request, or null while waiting or before any.</summary>
    public ActionResult? LastResult { get; private set; }

    /// <summary>True for the tool in the player's hand.</summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
                return;
            _active = value;
            OnActiveChanged();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!ReadDeviceInput || !Active || Input.MouseMode != Input.MouseModeEnum.Captured)
            return;
        if (!HandleInput(@event))
            return;
        GetViewport().SetInputAsHandled();
        Player.Gesture(GestureFor(@event));
    }

    /// <summary>What others see in the player's hand while this tool is out.</summary>
    public virtual CharacterProp Prop => CharacterProp.None;

    /// <summary>
    /// The arm movement others see when <paramref name="event"/> was used by <see cref="HandleInput"/>
    /// (<see cref="CharacterGesture.None"/> for none). By default a click swings or points the tool.
    /// </summary>
    protected virtual CharacterGesture GestureFor(InputEvent @event) =>
        Pressed(@event, InputActions.Primary) || Pressed(@event, InputActions.Secondary) ? UseGesture : CharacterGesture.None;

    /// <summary>The gesture a click with this tool plays.</summary>
    protected virtual CharacterGesture UseGesture => CharacterGesture.ToolUse;

    /// <summary>
    /// What the crosshair shows for this tool now (UI spec §8): blue when a click would do something. By
    /// default that is whenever a part is aimed at.
    /// </summary>
    public virtual Core.Ui.CrosshairState Crosshair => PartHit(CastRay()) is null ? Core.Ui.CrosshairState.Default : Core.Ui.CrosshairState.Valid;

    /// <summary>True while right click aims the item in hand (UI spec §10): the crosshair tightens.</summary>
    public virtual bool Aiming => false;

    /// <summary>How far a timed action being held has got, 0 to 1 (the ring round the crosshair), or null when none is.</summary>
    public virtual float? Progress => null;

    /// <summary>
    /// A tooltip of the tool's own for what the crosshair is over (the Connect Tool's nodes and lines), shown in
    /// place of the part's after the hover delay; <c>Key</c> restarts the delay when it changes. Null shows the
    /// part's tooltip.
    /// </summary>
    public virtual (object Key, string Title, string Body)? HoverTip => null;

    /// <summary>What Use (E) does with this tool in hand ahead of anything else (the Lift's blueprint menu), or null.</summary>
    public virtual (string Label, Action Run)? Interaction => null;

    /// <summary>The sound the player hears when the host refuses one of this tool's requests.</summary>
    protected virtual string RefusalSound => "ui_error";

    /// <summary>One line for the HUD: the tool, its state and its keys.</summary>
    public abstract string HudText();

    /// <summary>Handles a key or mouse event while active; true when it was used.</summary>
    protected abstract bool HandleInput(InputEvent @event);

    /// <summary>Called when <see cref="Active"/> changes, to show or hide previews.</summary>
    protected virtual void OnActiveChanged()
    {
    }

    /// <summary>Casts the aim ray, ignoring the player itself.</summary>
    protected RayHit? CastRay() => Cast(Player, Build.Settings!.Reach);

    /// <summary>
    /// Casts a ray from <paramref name="player"/>'s camera out to <paramref name="reach"/> metres, ignoring the player.
    /// With <paramref name="brush"/> it also hits walk-through objects such as bushes (<see cref="World.TerrainChunk.Brush"/>).
    /// </summary>
    public static RayHit? Cast(PlayerController player, float reach, bool brush = false)
    {
        var from = player.AimOrigin;
        var query = PhysicsRayQueryParameters3D.Create(from, from - player.Camera.GlobalBasis.Z * reach);
        query.Exclude = [player.GetRid()];
        query.CollideWithAreas = brush;
        var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0
            ? null
            : new RayHit(hit["position"].AsVector3(), hit["normal"].AsVector3(), hit["collider"].AsGodotObject(), hit["shape"].AsInt32());
    }

    // The display casts of this frame: the HUD, the crosshair, outlines and prompts all ask what the player aims at.
    private static readonly AimCast?[] _aims = new AimCast?[8];
    private static int _nextAim;

    private readonly record struct AimCast(ulong Frame, ulong Player, float Reach, bool Brush, Vector3 From, Vector3 To, RayHit? Hit);

    /// <summary>
    /// <see cref="Cast"/> for what shows this frame (prompts, outlines, the crosshair): every display that asks the
    /// same of the same aim in one frame shares one ray. A cast that acts on the world uses <see cref="Cast"/>.
    /// </summary>
    public static RayHit? CastShown(PlayerController player, float reach, bool brush = false)
    {
        var frame = Engine.GetProcessFrames() << 32 ^ Engine.GetPhysicsFrames();
        var from = player.AimOrigin;
        var to = from - player.Camera.GlobalBasis.Z * reach;
        var id = player.GetInstanceId();
        foreach (var aim in _aims)
        {
            if (aim is { } a && a.Frame == frame && a.Player == id && a.Reach == reach && a.Brush == brush && a.From == from && a.To == to
                && (a.Hit is not { } hit || IsInstanceValid(hit.Collider)))
                return a.Hit;
        }
        var cast = Cast(player, reach, brush);
        _aims[_nextAim] = new AimCast(frame, id, reach, brush, from, to, cast);
        _nextAim = (_nextAim + 1) % _aims.Length;
        return cast;
    }

    /// <summary>The part a ray hit (its Creation node and part id), or null when it hit no part.</summary>
    public static (CreationNode Node, int Part)? PartHit(RayHit? hit)
    {
        if (hit is not { Body: { } body, Node: { } node } h)
            return null;
        var toGrid = body.GlobalTransform.AffineInverse();
        var (cell, _) = Core.Building.BuildAim.FromHit((toGrid * h.Point).ToNumerics(), (toGrid.Basis * h.Normal).ToNumerics());
        return node.Creation.PartAt(cell) is { } part ? (node, part.Id) : null;
    }

    /// <summary>Where the player aims from (<see cref="PlayerController.AimOrigin"/>).</summary>
    protected Vector3 Eye => Player.AimOrigin;

    /// <summary>Sends a request and records the host's answer.</summary>
    protected void Send<TArgs>(string action, TArgs args, Action<ActionResult>? done)
    {
        LastResult = null;
        Actions.Request(action, args, result =>
        {
            LastResult = result;
            if (!result.Ok)
                Sounds?.PlayUi(RefusalSound);
            if (!result.Ok && ReadDeviceInput)
                GD.Print($"[{ToolName.ToLowerInvariant()}] {result.Message}");
            done?.Invoke(result);
        });
    }

    /// <summary>Plays a vanilla sound only this player hears, at <paramref name="at"/> (the hand when null).</summary>
    protected void PlayFeedback(string sound, Vector3? at = null) =>
        Sounds?.PlayLocal(sound, Core.Data.Identifiers.VanillaModId, at ?? Eye);

    /// <summary>The last refusal as a second HUD line, or empty.</summary>
    protected string Status => LastResult is { Ok: false } failed ? $"\n{failed.Message}" : "";

    /// <summary>True for a fresh press of one of <paramref name="action"/>'s bindings (see <see cref="InputActions"/>).</summary>
    protected static bool Pressed(InputEvent @event, string action) => GameInput.Pressed(@event, action);

    /// <summary>True when <paramref name="event"/> lets go of <paramref name="action"/>.</summary>
    protected static bool Released(InputEvent @event, string action) => GameInput.Released(@event, action);

    /// <summary>The player's key for <paramref name="action"/>, in brackets for a HUD hint.</summary>
    protected static string Key(string action) => $"[{GameInput.Label(action)}]";
}
