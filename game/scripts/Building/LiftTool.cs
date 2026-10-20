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
/// The Lift in the local player's hand (UI spec §2, §8; capture as in Scrap Mechanic). Aimed at the ground it shows
/// where the platform would go and <see cref="Place"/> puts the player's Lift there (carrying whatever it holds). Aimed
/// at a Creation it may capture, that Creation shows blue; <see cref="Place"/> captures it (<see cref="Capture"/>): it
/// turns red where it stands and a blue ghost of it, level and centred on the Lift's ghost, follows the aim. The ghost
/// turns red where the Lift and the Creation would not fit, and a click there only plays the refusal; a click where it
/// is blue puts the Lift down with the Creation on it, upright, and the red original goes. Right click lets a capture go,
/// Q turns the captured Creation's ghost. A Creation built onto the ground, one with resources on it, one too big and
/// one on another player's Lift never turn blue (<see cref="ToolActions.LiftRefusal"/>). Keys besides: the arrow keys
/// raise and lower a level a time (held, the column runs on smoothly until let go), Q turns the held Creation, holding
/// right click aimed at the Lift (or what it holds) picks the Lift back up (a ring fills round the crosshair; what it held
/// drops), and Use (E) aimed at the Lift opens the blueprint menu (<see cref="BlueprintMenu"/>), which saves what the
/// Lift holds or loads a saved Creation onto it. Every change is a <see cref="ToolActions"/> request; the host decides.
/// </summary>
public partial class LiftTool : HandTool
{
    // Seconds between re-checks of where the captured Creation's ghost stands while the aim stays put.
    private const double RecheckSeconds = 0.25;
    // Seconds a refused Creation flashes red.
    private const double FlashSeconds = 0.35;

    private Node3D _preview = null!;
    private Node3D _carry = null!;
    private readonly List<MeshInstance3D> _ghostMeshes = [];
    private readonly CreationTint _hoverTint = new();
    private readonly CreationTint _capturedTint = new();
    private double? _removeFrom;
    private int _raiseHeld;
    private int? _captured;
    private int _turns;
    private int _carryLevel; // the level the Lift goes down at with the captured Creation (ToolActions.LevelFor)
    private (Vec3 Position, int Turns, int Level, int Revision)? _checkedFor;
    private double _checkedAt;
    private double _captureCheckedAt;
    private string? _placeProblem;
    private (int Creation, double Until)? _flash;

    /// <summary>What the Lift would do now.</summary>
    /// <param name="Position">Where the middle of the platform's base would go.</param>
    /// <param name="Creation">The Creation it would capture, or null to place (or move) the Lift.</param>
    /// <param name="Refusal">Why that Creation cannot be captured, or null when it can.</param>
    public sealed record Target(Vec3 Position, int? Creation, string? Refusal = null);

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

    /// <summary>The Creation captured to go onto the Lift where it is put down next, or null.</summary>
    public int? Captured => _captured;

    /// <summary>Quarter turns the captured Creation's ghost is turned by (Q).</summary>
    public int CaptureTurns => _turns;

    /// <summary>The captured Creation's ghost riding the Lift's ghost (empty without a capture).</summary>
    public Node3D CarryGhost => _carry;

    /// <summary>
    /// Why the Lift and the captured Creation would not fit where the aim is now (the ghost shows red), or null when they
    /// would (blue). Null without a capture.
    /// </summary>
    public string? PlacementProblem => _captured is not null && Aim is { Creation: null } ? _placeProblem : null;

    /// <summary>The Creation shown blue as one the Lift may capture, or null.</summary>
    public int? Highlighted => _hoverTint.Material == HoverTint ? _hoverTint.Creation : null;

    /// <summary>The Creation shown red: the captured one, or one that just refused.</summary>
    public int? MarkedRed => _capturedTint.Creation ?? (_hoverTint.Material == DeniedTint ? _hoverTint.Creation : null);

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
            var instance = new MeshInstance3D
            {
                Mesh = mesh, Transform = transform, MaterialOverride = GhostMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _preview.AddChild(instance);
            _ghostMeshes.Add(instance);
        }
        _carry = new Node3D { Name = "Carry" };
        _preview.AddChild(_carry);
        AddChild(_preview);
    }

    public override void _ExitTree()
    {
        _hoverTint.Clear();
        _capturedTint.Clear();
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

    /// <summary>The look of the placement ghost where the Lift and its captured Creation would not fit: see-through red.</summary>
    public static readonly StandardMaterial3D InvalidGhostMaterial = new()
    {
        AlbedoColor = UiTokens.Error with { A = 0.45f },
        Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass,
        EmissionEnabled = true,
        Emission = UiTokens.Error,
        EmissionEnergyMultiplier = 0.35f,
    };

    /// <summary>The blue laid over a Creation the Lift may capture.</summary>
    public static readonly StandardMaterial3D HoverTint = CreationTint.Look(UiTokens.Cyan, 0.35f);

    /// <summary>The red laid over the captured Creation where it stands.</summary>
    public static readonly StandardMaterial3D CapturedTint = CreationTint.Look(UiTokens.Error, 0.45f);

    /// <summary>The red a Creation flashes when the Lift refuses it.</summary>
    public static readonly StandardMaterial3D DeniedTint = CreationTint.Look(UiTokens.Error, 0.3f);

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
            // Riding the deck, or aiming at the Lift or what it holds, with something else in hand: the arrow keys still
            // drive it, until the player steps off and looks away.
            if (_raiseHeld != 0 && (!DrivesOwnLift || (ReadDeviceInput && Input.MouseMode != Input.MouseModeEnum.Captured)))
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
                ReleaseCapture();
                Remove();
            }
        }
        RepeatRaise();
    }

    /// <summary>
    /// Right click pressed: starts the hold that picks the Lift back up, whether or not something is captured. False
    /// without a Lift or when the crosshair is not on it (or on what it holds).
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

    /// <summary>
    /// True when the arrow keys drive this player's Lift whatever is in hand: they stand on its deck, or aim at the Lift
    /// or the Creation on it.
    /// </summary>
    public bool DrivesOwnLift => Own is not null && (StandsOnOwnLift || AimsAtOwnLift());

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

    /// <summary>Casts the aim ray now, moves the preview and shows what the Lift would capture or where it would go.</summary>
    public void UpdateAim()
    {
        Aim = Cast();
        CheckCapture();
        CheckPlacement(force: false);
        _preview.Visible = Active && Aim is { Creation: null };
        if (Aim is not null)
            _preview.GlobalPosition = Aim.Position.ToGodot();
        _carry.Visible = _captured is not null;
        _carry.Position = new Vector3(0, Build.Settings!.Lift.TopAt(_captured is null ? Own?.Level ?? 0 : _carryLevel), 0);
        var look = PlacementProblem is null ? GhostMaterial : InvalidGhostMaterial;
        foreach (var mesh in _ghostMeshes.Where(IsInstanceValid))
            mesh.MaterialOverride = look;
        ShowTints();
    }

    /// <summary>
    /// Left click: with a Creation aimed at, captures it (<see cref="Capture"/>); on the ground, puts the Lift down there,
    /// carrying the captured Creation when there is one. False when nothing is aimed at.
    /// </summary>
    public bool Place(Action<ActionResult>? done = null)
    {
        if (Aim is not { } aim)
            return false;
        if (aim.Creation is { } id)
            return Capture(id, done);
        if (_captured is { } held)
            return PlaceCaptured(aim, held, done);
        // A new Lift shows where it goes until the host answers; a refusal fades it out red.
        var ghost = _preview.Visible ? PendingGhost.Leave(this, _preview) : null;
        Send(ToolActions.PlaceLift, new PlaceLiftArgs(aim.Position), result =>
        {
            if (ghost is not null && IsInstanceValid(ghost))
                ghost.Resolve(result, Build.Settings!.Placement.RollbackSeconds);
            done?.Invoke(result);
        });
        return true;
    }

    /// <summary>
    /// Captures Creation <paramref name="id"/>: it shows red where it stands and its ghost rides the Lift's ghost until the
    /// Lift is put down (or right click lets it go). A Creation the Lift refuses flashes red and plays the refusal
    /// instead. Capturing another Creation lets the last one go. False when the Creation is not shown here.
    /// </summary>
    public bool Capture(int id, Action<ActionResult>? done = null)
    {
        if (Build.Creations.NodeOf(id) is not { } node)
            return false;
        if (id == _captured)
            return true;
        if (Refusal(node.Creation) is { } refused)
        {
            _flash = (id, BuildTool.Now + FlashSeconds);
            Refuse(refused);
            ShowTints();
            done?.Invoke(ActionResult.Failure("refused", refused));
            return true;
        }
        _captured = id;
        _turns = 0;
        BuildCarry(node);
        ShowTints();
        Send(ToolActions.CaptureLift, new CaptureLiftArgs(id), result =>
        {
            if (!result.Ok && _captured == id)
                DropCapture();
            done?.Invoke(result);
        });
        return true;
    }

    /// <summary>Right click with a capture: lets it go, leaving the Creation as it was. False without one.</summary>
    public bool ReleaseCapture(Action<ActionResult>? done = null)
    {
        if (_captured is null)
            return false;
        DropCapture();
        Send(ToolActions.CaptureLift, new CaptureLiftArgs(), done);
        return true;
    }

    // Puts the Lift down with the captured Creation on it, when its ghost is blue there.
    private bool PlaceCaptured(Target aim, int held, Action<ActionResult>? done)
    {
        CheckPlacement(force: true);
        if (_captured is null)
        {
            done?.Invoke(LastResult ?? ActionResult.Failure("refused", "The capture was let go."));
            return true;
        }
        if (_placeProblem is { } problem)
        {
            Refuse(problem);
            done?.Invoke(LastResult!);
            return true;
        }
        // The red original goes and the ghost stays where it was put until the host answers; a refusal brings the capture
        // back as it was.
        var turns = _turns;
        var ghost = PendingGhost.Leave(this, _preview);
        DropCapture();
        Send(ToolActions.PlaceLift, new PlaceLiftArgs(aim.Position, held, turns), result =>
        {
            if (IsInstanceValid(ghost))
                ghost.Resolve(result, Build.Settings!.Placement.RollbackSeconds);
            if (!result.Ok && _captured is null && Build.Creations.NodeOf(held) is { } node)
            {
                _captured = held;
                _turns = turns;
                BuildCarry(node);
                ShowTints();
            }
            done?.Invoke(result);
        });
        return true;
    }

    // Why this player's Lift may not capture a Creation (one on its own Lift it may always take).
    private string? Refusal(Creation creation) =>
        Own?.Creation == creation.Id ? null : ToolActions.LiftRefusal(creation, Build.Settings!.Lift, Build.Lifts.Values.Select(l => l.State), Player.PeerId);

    private void DropCapture()
    {
        _captured = null;
        _turns = 0;
        _checkedFor = null;
        _placeProblem = null;
        foreach (var child in _carry.GetChildren())
        {
            _ghostMeshes.Remove((MeshInstance3D)child);
            child.QueueFree();
        }
        _capturedTint.Clear();
    }

    // A captured Creation that is gone, or that the Lift may no longer take (another player lifted it, it was built onto
    // the ground), is let go.
    private void CheckCapture()
    {
        if (_captured is not { } id)
            return;
        if (Build.Creations.Find(id) is not { } creation)
        {
            DropCapture();
            return;
        }
        if (BuildTool.Now - _captureCheckedAt < RecheckSeconds)
            return;
        _captureCheckedAt = BuildTool.Now;
        if (Refusal(creation) is not { } refused)
            return;
        Send(ToolActions.CaptureLift, new CaptureLiftArgs(), null);
        DropCapture();
        Refuse(refused);
    }

    // Works out whether the Lift and the captured Creation fit where the aim is: when the aim, the turn, the level or the
    // Creation changed, and every RecheckSeconds anyway, as other things move.
    private void CheckPlacement(bool force)
    {
        if (_captured is not { } id || Aim is not { Creation: null } aim || Build.Creations.Find(id) is not { } creation)
            return;
        var level = Own?.Level ?? 0;
        var key = (aim.Position, _turns, level, creation.Revision);
        if (!force && _checkedFor == key && BuildTool.Now - _checkedAt < RecheckSeconds)
            return;
        // Raised as far as what hangs past the platform needs, as the host will (PlaceLift).
        level = _carryLevel = ToolActions.LevelFor(creation, Build.Settings!.Lift, _turns, level);
        _checkedFor = key;
        _checkedAt = BuildTool.Now;
        var p = aim.Position;
        var snapped = new Vec3(Core.MechanicaInfo.ToGridCell(p.X) * Core.MechanicaInfo.GridSize, p.Y, Core.MechanicaInfo.ToGridCell(p.Z) * Core.MechanicaInfo.GridSize);
        _placeProblem = ToolActions.CheckLiftPlacement(Build, Build.Settings!.Lift, new LiftState(Player.PeerId, snapped, level, id), creation, _turns);
    }

    // The captured Creation's ghost: a copy of each of its meshes where Seat puts it on a platform whose top middle is the
    // carry node's origin, so it shows level and centred on the Lift's ghost.
    private void BuildCarry(Creations.CreationNode node)
    {
        foreach (var child in _carry.GetChildren())
        {
            _ghostMeshes.Remove((MeshInstance3D)child);
            child.QueueFree();
        }
        _checkedFor = null;
        var seat = ToolActions.Seat(node.Creation, System.Numerics.Vector3.Zero, _turns, Build.Settings!.Lift.PlatformSize);
        for (var i = 0; i < node.Bodies.Count && i < seat.Count; i++)
        {
            var body = node.Bodies[i];
            var place = seat[i].ToTransform() * body.GlobalTransform.AffineInverse();
            foreach (var mesh in body.FindChildren("*", nameof(MeshInstance3D), owned: false).OfType<MeshInstance3D>().Where(m => m.IsVisibleInTree()))
            {
                var copy = new MeshInstance3D
                {
                    Mesh = mesh.Mesh, Transform = place * mesh.GlobalTransform, MaterialOverride = GhostMaterial,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                _carry.AddChild(copy);
                _ghostMeshes.Add(copy);
            }
        }
    }

    // Blue on the Creation the Lift may capture, red on the captured one (and briefly on one it refused).
    private void ShowTints()
    {
        _capturedTint.Set(_captured is { } held ? Build.Creations.NodeOf(held) : null, CapturedTint);
        if (_flash is { } flash && (BuildTool.Now > flash.Until || flash.Creation == _captured))
            _flash = null;
        if (!Active)
            _hoverTint.Clear();
        else if (_flash is { } denied)
            _hoverTint.Set(Build.Creations.NodeOf(denied.Creation), DeniedTint);
        else if (Aim is { Creation: { } aimed, Refusal: null } && aimed != _captured)
            _hoverTint.Set(Build.Creations.NodeOf(aimed), HoverTint);
        else
            _hoverTint.Clear();
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

    /// <summary>
    /// Turns the captured Creation's ghost a quarter turn, or without a capture the Creation the Lift holds. False with
    /// neither a capture nor a Lift.
    /// </summary>
    public bool Turn(Action<ActionResult>? done = null)
    {
        if (_captured is { } held)
        {
            _turns = (_turns + 1) % 4;
            if (Build.Creations.NodeOf(held) is { } node)
                BuildCarry(node);
            UpdateAim();
            done?.Invoke(ActionResult.Success());
            return true;
        }
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
    protected override string RefusalSound => "lift_deny";

    /// <inheritdoc/>
    public override string HudText()
    {
        if (_captured is { } held)
        {
            return Loc.T("Lift (creation {0} captured)   {1} put the Lift down with it  {2} turn  {3} let go", held, Key(InputActions.Primary),
                Key(InputActions.Rotate), Key(InputActions.Secondary)) + (PlacementProblem is { } problem ? "\n" + problem : Status);
        }
        var lift = Own is { } own ? own.Creation is { } c ? Loc.T("level {0}, holding creation {1}", own.Level, c) : Loc.T("level {0}", own.Level) : Loc.T("not placed");
        return Loc.T("Lift ({0})   {1} place / capture  {2}/{3} raise/lower  {4} turn  hold {5} on the Lift: pick up  {6} on the Lift: blueprints", lift, Key(InputActions.Primary), Key(InputActions.LiftUp), Key(InputActions.LiftDown),
            Key(InputActions.Rotate), Key(InputActions.Secondary), Key(InputActions.Interact)) + Status;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Active || !ReadDeviceInput || Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            base._UnhandledInput(@event);
            return;
        }
        if (DrivesOwnLift && HandleArrows(@event))
            GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
            Place();
        else if (Pressed(@event, InputActions.Secondary))
        {
            // Aimed at the Lift (or what it holds) a hold picks it up, a click lets a capture go; aimed anywhere else a
            // click lets the capture go at once.
            if (!PressRemove())
                ReleaseCapture();
        }
        else if (Released(@event, InputActions.Secondary))
        {
            if (_removeFrom is null)
                return false;
            ReleaseRemove();
            ReleaseCapture();
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
            // Putting the Lift away lets a capture go, as right click does.
            ReleaseCapture();
            ShowTints();
        }
    }

    /// <inheritdoc/>
    public override Core.Ui.CrosshairState Crosshair => Aim switch
    {
        null or { Refusal: not null } => Core.Ui.CrosshairState.Default,
        { Creation: null } when PlacementProblem is not null => Core.Ui.CrosshairState.Default,
        _ => Core.Ui.CrosshairState.Valid,
    };

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
            return new Target(new Vec3(middle.X, ground, middle.Z), creation.Id, creation.Id == _captured ? null : Refusal(creation));
        }
        if (hit.Normal.Y < 0.7f)
            return null;
        var snapped = BuildAim.Snap(hit.Point.ToNumerics()).ToGodot();
        return new Target(new Vec3(snapped.X, hit.Point.Y, snapped.Z), null);
    }

    private float? GroundBelow(Vector3 from, Creations.CreationNode skip)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * Build.Settings!.Reach, World.PhysicsLayers.Queries);
        var exclude = new Godot.Collections.Array<Rid>([.. skip.Bodies.Select(b => b.GetRid()), Player.GetRid()]);
        if (Build.Lifts.TryGetValue(Player.PeerId, out var own))
            exclude.Add(own.GetRid());
        query.Exclude = exclude;
        var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? null : hit["position"].AsVector3().Y;
    }
}
