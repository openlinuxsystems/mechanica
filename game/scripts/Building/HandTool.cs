using Godot;
using Mechanica.Core.Actions;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Building;

/// <summary>
/// Base of the local player's hand tools (Hammer, Lift, Weld Tool, Paint Tool, Sledgehammer). A tool
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
        if (HandleInput(@event))
            GetViewport().SetInputAsHandled();
    }

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

    /// <summary>Casts a ray from <paramref name="player"/>'s camera out to <paramref name="reach"/> metres, ignoring the player.</summary>
    public static RayHit? Cast(PlayerController player, float reach)
    {
        var camera = player.Camera.GlobalTransform;
        var from = camera.Origin;
        var query = PhysicsRayQueryParameters3D.Create(from, from - camera.Basis.Z * reach);
        query.Exclude = [player.GetRid()];
        var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0
            ? null
            : new RayHit(hit["position"].AsVector3(), hit["normal"].AsVector3(), hit["collider"].AsGodotObject(), hit["shape"].AsInt32());
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

    /// <summary>The camera's world position.</summary>
    protected Vector3 Eye => Player.Camera.GlobalPosition;

    /// <summary>Sends a request and records the host's answer.</summary>
    protected void Send<TArgs>(string action, TArgs args, Action<ActionResult>? done)
    {
        LastResult = null;
        Actions.Request(action, args, result =>
        {
            LastResult = result;
            if (!result.Ok)
                Sounds?.PlayUi("ui_error");
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

    /// <summary>True for a fresh press of <paramref name="key"/>.</summary>
    protected static bool Pressed(InputEvent @event, Key key) =>
        @event is InputEventKey { Pressed: true, Echo: false } k && k.PhysicalKeycode == key;

    /// <summary>True for a press of a mouse button.</summary>
    protected static bool Clicked(InputEvent @event, MouseButton button) =>
        @event is InputEventMouseButton { Pressed: true } m && m.ButtonIndex == button;
}
