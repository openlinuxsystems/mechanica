using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.World;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// The Lift in the local player's hand (UI spec §2, §8). Aimed at the ground it shows where the platform would go and
/// <see cref="Place"/> puts the player's Lift there (carrying whatever it holds); aimed at a Creation,
/// <see cref="Place"/> slides the Lift under it and picks it up. Keys: left click places or picks up, the arrow keys
/// raise and lower a level a time (held, the column runs on smoothly until let go), Q turns the held Creation, holding
/// right click aimed at the Lift (or what it holds) picks the Lift back up (a ring fills round the crosshair; what it held
/// drops), and Use (E) aimed at the Lift opens the blueprint menu (<see cref="BlueprintMenu"/>), which saves what the
/// Lift holds or loads a saved Creation onto it. Every change is a <see cref="ToolActions"/> request; the host decides.
/// </summary>
public partial class LiftTool : HandTool
{
    private Node3D _preview = null!;
    private double? _removeFrom;
    private int _raiseHeld;

    /// <summary>What the Lift would do now.</summary>
    /// <param name="Position">Where the middle of the platform's base would go.</param>
    /// <param name="Creation">The Creation it would pick up, or null to place (or move) the Lift.</param>
    public sealed record Target(Vec3 Position, int? Creation);

    /// <inheritdoc/>
    public override string ToolName => "Lift";

    /// <inheritdoc/>
    public override CharacterProp Prop => CharacterProp.LiftRemote;

    /// <inheritdoc/>
    protected override CharacterGesture UseGesture => CharacterGesture.ToolUse;

    /// <summary>The current aim, updated every physics frame while in hand (and by <see cref="UpdateAim"/>).</summary>
    public Target? Aim { get; private set; }

    /// <summary>This player's Lift as this peer last saw it, or null.</summary>
    public LiftState? Own => Build.Lifts.TryGetValue(Player.PeerId, out var lift) ? lift.State : null;

    /// <summary>The Creation this player's Lift holds, as this peer shows it, or null.</summary>
    public Creation? Held => Own?.Creation is { } id ? Build.Creations.Find(id) : null;

    /// <summary>Opens the blueprint menu; Use (E) on the Lift calls it. Set by the game.</summary>
    public Action? OpenBlueprints { get; set; }

    /// <summary>Where a new Lift would go: the Lift's model as it stands once put down, in see-through blue.</summary>
    public Node3D Ghost => _preview;

    /// <summary>True while right click is held to take the Lift away.</summary>
    public bool Removing => _removeFrom is not null;

    public override void _Ready()
    {
        var size = Build.Settings!.Lift;
        _preview = new Node3D { Name = "Preview", TopLevel = true, Visible = false };
        // The Lift as it will stand once put down, in see-through blue; a plain box when the model does not load.
        var meshes = LiftBody.StillModel(Build.Assets, size, deployed: true) is { } model ? Flatten(model) : [];
        if (meshes.Count == 0)
            meshes.Add((new BoxMesh { Size = new Vector3(size.PlatformSize, size.BaseHeight, size.PlatformSize) }, new Transform3D(Basis.Identity, new Vector3(0, size.BaseHeight / 2, 0))));
        foreach (var (mesh, transform) in meshes)
        {
            _preview.AddChild(new MeshInstance3D
            {
                Mesh = mesh, Transform = transform, MaterialOverride = GhostMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        AddChild(_preview);
    }

    /// <summary>The look of the Lift's placement ghost: see-through blue, only its outer surface drawn.</summary>
    public static readonly StandardMaterial3D GhostMaterial = new()
    {
        AlbedoColor = UiTokens.Cyan with { A = 0.45f },
        Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass,
        EmissionEnabled = true,
        Emission = UiTokens.Cyan,
        EmissionEnergyMultiplier = 0.35f,
    };

    // Every mesh in a model with its place relative to the model's root, then frees the model.
    private static List<(Mesh Mesh, Transform3D Transform)> Flatten(Node3D model)
    {
        var meshes = new List<(Mesh, Transform3D)>();
        void Walk(Node node, Transform3D parent)
        {
            var here = node is Node3D spatial ? parent * spatial.Transform : parent;
            if (node is MeshInstance3D { Mesh: { } mesh, Visible: true })
                meshes.Add((mesh, here));
            foreach (var child in node.GetChildren())
                Walk(child, here);
        }
        Walk(model, Transform3D.Identity);
        model.Free();
        return meshes;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Active)
        {
            // Riding the deck with something else in hand: the arrow keys still drive it, until the player steps off.
            if (_raiseHeld != 0 && (!StandsOnOwnLift || (ReadDeviceInput && Input.MouseMode != Input.MouseModeEnum.Captured)))
                _raiseHeld = 0;
            RepeatRaise();
            return;
        }
        UpdateAim();
        // A release the tool never heard (the mouse freed for a window) ends the holds without removing.
        if (ReadDeviceInput && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            _removeFrom = null;
            _raiseHeld = 0;
        }
        if (_removeFrom is { } from)
        {
            // Looking away from the Lift ends the hold, like letting go.
            if (!AimsAtOwnLift())
                _removeFrom = null;
            else if (BuildTool.Now - from >= Build.Settings!.Lift.RemoveHoldSeconds)
            {
                _removeFrom = null;
                Remove();
            }
        }
        RepeatRaise();
    }

    /// <summary>
    /// Right click pressed: starts the hold that picks the Lift back up. False without a Lift or when the crosshair is
    /// not on it (or on what it holds).
    /// </summary>
    public bool PressRemove()
    {
        if (Own is null || !AimsAtOwnLift())
            return false;
        _removeFrom = BuildTool.Now;
        return true;
    }

    /// <summary>
    /// An arrow key pressed (<paramref name="direction"/> +1 up, -1 down): one level now, and while it is held the next
    /// before the column gets there, so it keeps moving. False without a Lift.
    /// </summary>
    public bool PressRaise(int direction)
    {
        _raiseHeld = Math.Sign(direction);
        return RequestNextLevel();
    }

    /// <summary>The arrow key let go: the column stops at the level it is going to.</summary>
    public void ReleaseRaise() => _raiseHeld = 0;

    /// <summary>True when the local player stands on the deck of their own Lift, where the arrow keys drive it whatever is in hand.</summary>
    public bool StandsOnOwnLift => Build.Lifts.TryGetValue(Player.PeerId, out var body) && body.Carries(Player.GlobalPosition);

    /// <summary>True while an arrow key is held.</summary>
    public bool RaiseHeld => _raiseHeld != 0;

    // A held arrow key asks for the next level while the deck is still on its way to the last one, so the column runs
    // on smoothly instead of stopping at every level; let go, it stops at the level it is going to.
    private void RepeatRaise()
    {
        if (_raiseHeld == 0 || _raisePending || Own is not { } lift)
            return;
        var settings = Build.Settings!.Lift;
        var top = lift.Level + _raiseHeld;
        if (top < 0 || top > settings.MaxLevel)
            return;
        if (!Build.Lifts.TryGetValue(Player.PeerId, out var body) || !body.Deployed || body.State.Level != lift.Level
            || Math.Abs(settings.TopAt(lift.Level) - body.ShownTop) > settings.LevelHeight * 0.9f)
            return;
        RequestNextLevel();
    }

    private bool _raisePending;

    // Asks for the next level in the held direction. Pending until the host answers, which on the host itself is
    // before Raise returns, so the flag is set first and never left on by a reply that already came.
    private bool RequestNextLevel()
    {
        _raisePending = true;
        if (Raise(_raiseHeld, _ => _raisePending = false))
            return true;
        _raisePending = false;
        return false;
    }

    /// <summary>Right click let go: a hold too short to take the Lift away ends without doing anything.</summary>
    public void ReleaseRemove() => _removeFrom = null;

    /// <inheritdoc/>
    public override float? Progress => _removeFrom is { } from
        ? (float)Math.Clamp((BuildTool.Now - from) / Math.Max(1e-6, Build.Settings!.Lift.RemoveHoldSeconds), 0, 1)
        : null;

    /// <summary>
    /// What Use (E) does with the Lift in hand: aimed at this player's Lift, or what it holds, it opens the blueprint
    /// menu. Null otherwise.
    /// </summary>
    public override (string Label, Action Run)? Interaction =>
        Active && OpenBlueprints is { } open && AimsAtOwnLift() ? (Loc.T("open blueprints"), open) : null;

    /// <summary>Saves what the Lift holds to <paramref name="store"/> as <paramref name="name"/>. The path, or null when it holds nothing.</summary>
    public string? SaveBlueprint(Core.Saves.BlueprintStore store, string name) =>
        Held is { } creation ? store.Write(creation, name, DateTimeOffset.Now) : null;

    /// <summary>Asks the host to build a blueprint on the empty Lift. False when the text is not JSON or too long to send.</summary>
    public bool LoadBlueprint(string json, Action<ActionResult>? done = null)
    {
        if (json.Length > Build.Settings!.Lift.BlueprintMaxBytes)
            return false;
        System.Text.Json.JsonElement blueprint;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
            blueprint = document.RootElement.Clone();
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
        Send(ToolActions.LoadBlueprint, new LoadBlueprintArgs(blueprint), done);
        return true;
    }

    /// <summary>True when the crosshair is on this player's Lift or the Creation it holds.</summary>
    public bool AimsAtOwnLift()
    {
        var hit = CastRay();
        if (hit?.Collider is LiftBody body)
            return body.State.Owner == Player.PeerId;
        return Own?.Creation is { } held && hit?.Node?.Creation.Id == held;
    }

    /// <summary>Casts the aim ray now and moves the preview.</summary>
    public void UpdateAim()
    {
        Aim = Cast();
        _preview.Visible = Active && Aim is { Creation: null };
        if (Aim is not null)
            _preview.GlobalPosition = Aim.Position.ToGodot();
    }

    /// <summary>Places the Lift or picks up the aimed-at Creation. False when nothing is aimed at.</summary>
    public bool Place(Action<ActionResult>? done = null)
    {
        if (Aim is not { } aim)
            return false;
        // A new Lift shows where it goes until the host answers; a refusal fades it out red.
        var ghost = aim.Creation is null && _preview.Visible ? PendingGhost.Leave(this, _preview) : null;
        Send(ToolActions.PlaceLift, new PlaceLiftArgs(aim.Position, aim.Creation), result =>
        {
            if (ghost is not null && IsInstanceValid(ghost))
                ghost.Resolve(result, Build.Settings!.Placement.RollbackSeconds);
            done?.Invoke(result);
        });
        return true;
    }

    /// <summary>Raises (positive) or lowers the Lift by <paramref name="steps"/> levels. False without a Lift.</summary>
    public bool Raise(int steps, Action<ActionResult>? done = null)
    {
        if (Own is not { } lift)
            return false;
        var level = Math.Clamp(lift.Level + steps, 0, Build.Settings!.Lift.MaxLevel);
        Send(ToolActions.SetLiftLevel, new SetLiftLevelArgs(level), done);
        return true;
    }

    /// <summary>Turns the held Creation a quarter turn. False without a Lift.</summary>
    public bool Turn(Action<ActionResult>? done = null)
    {
        if (Own is null)
            return false;
        Send(ToolActions.TurnLift, new TurnLiftArgs(1), done);
        return true;
    }

    /// <summary>Takes the Lift away; what it held drops. False without a Lift.</summary>
    public bool Remove(Action<ActionResult>? done = null)
    {
        if (Own is null)
            return false;
        Send(ToolActions.RemoveLift, new NoArgs(), done);
        return true;
    }

    /// <inheritdoc/>
    public override string HudText()
    {
        var lift = Own is { } own ? own.Creation is { } c ? Loc.T("level {0}, holding creation {1}", own.Level, c) : Loc.T("level {0}", own.Level) : Loc.T("not placed");
        return Loc.T("Lift ({0})   {1} place / pick up  {2}/{3} raise/lower  {4} turn  hold {5} on the Lift: pick up  {6} on the Lift: blueprints", lift, Key(InputActions.Primary), Key(InputActions.LiftUp), Key(InputActions.LiftDown),
            Key(InputActions.Rotate), Key(InputActions.Secondary), Key(InputActions.Interact)) + Status;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Active || !ReadDeviceInput || Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            base._UnhandledInput(@event);
            return;
        }
        if (StandsOnOwnLift && HandleArrows(@event))
            GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
            Place();
        else if (Pressed(@event, InputActions.Secondary))
            PressRemove();
        else if (Released(@event, InputActions.Secondary))
        {
            if (_removeFrom is null)
                return false;
            ReleaseRemove();
        }
        else if (HandleArrows(@event))
            return true;
        else if (Pressed(@event, InputActions.RotateBack))
        {
            for (var i = 0; i < 3; i++)
                Turn();
        }
        else if (Pressed(@event, InputActions.Rotate))
            Turn();
        else
            return false;
        return true;
    }

    // The arrow keys: pressed, the Lift goes a level and keeps going while held; let go, it stops at the next level.
    private bool HandleArrows(InputEvent @event)
    {
        if (Pressed(@event, InputActions.LiftUp))
            PressRaise(1);
        else if (Pressed(@event, InputActions.LiftDown))
            PressRaise(-1);
        else if ((Released(@event, InputActions.LiftUp) || Released(@event, InputActions.LiftDown)) && _raiseHeld != 0)
            ReleaseRaise();
        else
            return false;
        return true;
    }

    /// <inheritdoc/>
    protected override void OnActiveChanged()
    {
        _preview.Visible = Active && Aim is { Creation: null };
        if (!Active)
        {
            _removeFrom = null;
            _raiseHeld = 0;
        }
    }

    /// <inheritdoc/>
    public override Core.Ui.CrosshairState Crosshair => Aim is null ? Core.Ui.CrosshairState.Default : Core.Ui.CrosshairState.Valid;

    private Target? Cast()
    {
        if (CastRay() is not { } hit || hit.Collider is LiftBody)
            return null;
        if (hit.Node is { } node)
        {
            // Under the Creation: the middle of its box, on whatever is beneath it.
            var creation = node.Creation;
            var box = CreationBounds.World(node.LocalBounds, [.. node.Bodies.Select(b => b.GlobalTransform.ToPose())]);
            var middle = ((box.Min + box.Max) / 2).ToGodot();
            var ground = GroundBelow(new Vector3(middle.X, box.Min.Y, middle.Z), node) ?? box.Min.Y;
            return new Target(new Vec3(middle.X, ground, middle.Z), creation.Id);
        }
        if (hit.Normal.Y < 0.7f)
            return null;
        var snapped = BuildAim.Snap(hit.Point.ToNumerics()).ToGodot();
        return new Target(new Vec3(snapped.X, hit.Point.Y, snapped.Z), null);
    }

    private float? GroundBelow(Vector3 from, Creations.CreationNode skip)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * Build.Settings!.Reach);
        var exclude = new Godot.Collections.Array<Rid>([.. skip.Bodies.Select(b => b.GetRid()), Player.GetRid()]);
        if (Build.Lifts.TryGetValue(Player.PeerId, out var own))
            exclude.Add(own.GetRid());
        query.Exclude = exclude;
        var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? null : hit["position"].AsVector3().Y;
    }
}
