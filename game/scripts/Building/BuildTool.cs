using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// Building by hand for the local player: no tool, just the part picked on the hotbar (<see cref="ToolBelt"/>), held up
/// in the hand. Every physics frame while in hand it casts a ray from the
/// camera and works out where the selected part would go (<see cref="BuildAim"/>), showing a see-through
/// ghost there: blue when it would fit, red when it would not (checked here, the same way the host checks).
/// Requests go through the Action API, so the host decides; a placed part's ghost stays until the host answers
/// and fades out red if it refuses (<see cref="PendingGhost"/>).
/// <para>
/// Aiming at a Creation attaches to it. Aiming at anything else starts a new Creation, its grid
/// aligned with the world and snapped to 0.25 m across the surface. Left click places on release (UI spec §9).
/// Holding it still for a moment enters alignment mode: the ghost locks and arrows show the ways a drag can grow.
/// Moving while held drags out a grid of parts in one plane, up to 16 along each side, placed on release; Q steps
/// the plane (XY, XZ, YZ) while dragging. Holding right click on a part fills a ring round the crosshair and removes
/// it when full (0.4 to 0.9 s by material); letting go early cancels. Dragging with right click held marks the parts
/// of that body in the face's plane and removes them on release. Otherwise Q turns the part about the face
/// (Shift+Q back) and T tips it over. With an empty hotbar slot picked there is no part to place, and the hand only
/// removes parts (<see cref="Clear"/>). This right click is every tool's default (<see cref="HandTool.PickUp"/>), so
/// the gesture runs on while another tool or item is in hand.
/// </para>
/// </summary>
public partial class BuildTool : HandTool
{
    /// <summary>The see-through look of tool previews.</summary>
    public static readonly StandardMaterial3D PreviewMaterial = new()
    {
        AlbedoColor = UiTokens.Cyan with { A = 0.35f },
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = true,
    };

    /// <summary>The look of a ghost that would not fit, and of parts marked to come off.</summary>
    public static readonly StandardMaterial3D InvalidMaterial = new()
    {
        AlbedoColor = UiTokens.Error with { A = 0.4f },
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = true,
    };

    private readonly List<string> _parts = [];
    private readonly List<Node3D> _ghosts = [];
    private readonly List<MeshInstance3D> _marks = [];
    private readonly List<GridCell> _dragCells = [];
    private readonly List<int> _dragParts = [];
    private Node3D _preview = null!;
    private Node3D _arrows = null!;
    private string? _previewPart;
    private GridRotation _adjust;
    private ToolGesture _place = null!;
    private ToolGesture _remove = null!;
    private Target? _anchor;
    private int _plane = 1;
    private (CreationNode Node, int Part, int Body, GridCell Face, Transform3D Frame)? _removing;
    private bool _removed;
    private bool _previewValid;
    private bool _emptyHand;

    /// <summary>What the ray currently points at.</summary>
    /// <param name="Creation">The Creation hit, or null for the ground and other world objects.</param>
    /// <param name="Cell">Origin cell for the selected part, in <see cref="Creation"/>'s grid or the new one's.</param>
    /// <param name="Rotation">Orientation for the selected part.</param>
    /// <param name="Position">For a new Creation, the world position of its grid origin.</param>
    /// <param name="HitPart">The part under the crosshair, if any (what <see cref="Remove"/> takes).</param>
    /// <param name="Preview">World transform of the selected part's centre where it would go.</param>
    /// <param name="Face">The grid face the part goes on, in that grid.</param>
    /// <param name="Frame">World transform of that grid (the Body's, or the new Creation's).</param>
    public sealed record Target(Creation? Creation, GridCell Cell, GridRotation Rotation, Vec3? Position, int? HitPart, Transform3D Preview,
        GridCell Face = default, Transform3D Frame = default);

    /// <inheritdoc/>
    public override string ToolName => "Build";

    /// <inheritdoc/>
    public override CharacterProp Prop => Selected is null ? CharacterProp.None : PoseInHand.Prop;

    /// <inheritdoc/>
    protected override CharacterGesture UseGesture => CharacterGesture.Interact;

    /// <inheritdoc/>
    protected override CharacterGesture GestureFor(InputEvent @event) =>
        // An item that sets another part down on the ground (a Soil Bag) gestures as it is placed, not on the press.
        Selected is { } item && PartItems.GroundPartFor(Build.Catalog, item) is not null && Pressed(@event, InputActions.Primary)
            ? CharacterGesture.None
            : base.GestureFor(@event);

    /// <summary>How items look in hand (a Soil Bag is carried in both arms); null holds every part as a plain item.</summary>
    public Core.Characters.CharacterSettings? Characters { get; init; }

    private Core.Characters.ItemPose PoseInHand => Selected is { } item && Characters is { } characters
        ? characters.PoseFor(item, Build.Catalog.FindDefinition(item)?.Category is { } c ? System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(c.ToString()) : null)
        : Core.Characters.ItemPose.Default;

    /// <summary>Placeable parts to pick from, by qualified id.</summary>
    public IReadOnlyList<string> Parts => _parts;

    /// <summary>The item picked in hand, or null with an empty hand (or when no mod has a placeable part).</summary>
    public string? Selected { get; private set; }

    /// <summary>
    /// The part <see cref="Place"/> puts down: the selected item's ground part (<see cref="PartDefinition.GroundPart"/>:
    /// a soil mound for a Soil Bag) when it aims at the ground and Force build is not held, otherwise the item itself.
    /// It stays put while a placing gesture is held or dragged.
    /// </summary>
    public string? Placing { get; private set; }

    /// <summary>Holds Force build down in tests (true) or lets go (false); null reads the key.</summary>
    public bool? ForceBuild { get; set; }

    private bool ForceHeld => ForceBuild ?? (ReadDeviceInput && Input.IsActionPressed(InputActions.ForceBuild));

    /// <summary>The player's turn and tip on top of the automatic orientation.</summary>
    public GridRotation Adjust => _adjust;

    /// <summary>The current aim, updated every physics frame (and by <see cref="UpdateAim"/>).</summary>
    public Target? Aim { get; private set; }

    /// <summary>Where the left button's gesture is: pressed, aligning (held still) or dragging.</summary>
    public GesturePhase PlacePhase => _place.Phase;

    /// <summary>Where the right button's gesture is: pressed or held (timed removal), or dragging.</summary>
    public GesturePhase RemovePhase => _remove.Phase;

    /// <summary>The normal axis of the plane a drag spreads in (0 = X, 1 = Y, 2 = Z).</summary>
    public int Plane => _plane;

    /// <summary>The origin cells a placing drag covers now (empty when not dragging).</summary>
    public IReadOnlyList<GridCell> DragCells => _dragCells;

    /// <summary>The parts a removing drag has marked.</summary>
    public IReadOnlyList<int> DragParts => _dragParts;

    /// <summary>True when the ghost would fit where it is (the local check; the host has the last word).</summary>
    public bool PreviewValid => _previewValid;

    /// <summary>Why the ghost would not fit, or null.</summary>
    public string? PreviewProblem { get; private set; }

    /// <summary>The arrows alignment mode shows (hidden otherwise).</summary>
    public Node3D Arrows => _arrows;

    /// <summary>Whether the player has one of a part to place; null builds with anything (Creative).</summary>
    public Func<string, bool>? CanAfford { get; set; }

    /// <summary>The time the gestures run on, in seconds.</summary>
    public static double Now => Time.GetTicksUsec() / 1e6;

    private PlacementSettings Timing => Build.Settings!.Placement;

    public override void _Ready()
    {
        ReloadParts();
        _preview = new Node3D { Name = "Preview", TopLevel = true, Visible = false };
        AddChild(_preview);
        _arrows = new Node3D { Name = "Arrows", TopLevel = true, Visible = false };
        AddChild(_arrows);
        _place = new ToolGesture(Timing.AlignHoldSeconds, Timing.DragSeconds);
        _remove = new ToolGesture(Timing.AlignHoldSeconds, Timing.DragSeconds);
        HudRoot.FocusLost += CancelGestures;
    }

    public override void _ExitTree() => HudRoot.FocusLost -= CancelGestures;

    /// <summary>
    /// Re-reads the placeable parts from the catalog, after mods are reloaded. Keeps the selection if it still exists,
    /// and an empty hand empty.
    /// </summary>
    public void ReloadParts()
    {
        _parts.Clear();
        _parts.AddRange(Build.Catalog.Parts
            .Where(p => p.Value.Definition.IsPlaceable && p.Value.Definition.IsItem)
            .Select(p => p.Key)
            .Order(StringComparer.Ordinal));
        if (!_emptyHand && (Selected is null || !_parts.Contains(Selected)))
            Selected = _parts.FirstOrDefault();
        _previewPart = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        // A release the tool never heard (the mouse freed for a window, focus lost) ends nothing.
        if (ReadDeviceInput && Input.MouseMode != Input.MouseModeEnum.Captured && (_place.Down || _remove.Down))
            CancelGestures();
        if (!Active)
        {
            // Another tool's right click picking a part up (HandTool.PickUp).
            UpdateRemoving();
            return;
        }
        UpdateAim();
        UpdatePlacing();
        UpdateRemoving();
    }

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
            PressPlace();
        else if (GameInput.Released(@event, InputActions.Primary))
            return ReleasePlace();
        else if (Pressed(@event, InputActions.Secondary))
            PressRemove();
        else if (GameInput.Released(@event, InputActions.Secondary))
            return ReleaseRemove();
        else if (Pressed(@event, InputActions.RotateBack))
            TurnBack();
        else if (Pressed(@event, InputActions.Rotate))
            Turn();
        else if (Pressed(@event, InputActions.Tip))
            Tip();
        else
            return false;
        return true;
    }

    /// <inheritdoc/>
    protected override void OnActiveChanged()
    {
        if (!Active)
            CancelGestures();
        _preview.Visible = Active && Aim is not null;
    }

    /// <inheritdoc/>
    public override Core.Ui.CrosshairState Crosshair => Aim is not null && Selected is not null && _previewValid ? Core.Ui.CrosshairState.Valid : Core.Ui.CrosshairState.Default;

    /// <inheritdoc/>
    public override float? Progress => _removing is { } r && _remove.Phase is GesturePhase.Pressed or GesturePhase.Holding && !_removed
        ? (float)Math.Min(1, _remove.Held / Math.Max(1e-6, RemoveSecondsOf(r.Node.Creation, r.Part)))
        : null;

    /// <summary>Selects a part by qualified id. Returns false when it is not placeable.</summary>
    public bool Select(string part)
    {
        if (!_parts.Contains(part))
            return false;
        Selected = part;
        _emptyHand = false;
        return true;
    }

    /// <summary>Empties the hand: nothing to place, and right click still removes parts.</summary>
    public void Clear()
    {
        CancelGestures();
        Selected = null;
        _emptyHand = true;
        _previewPart = null;
    }

    /// <summary>Selects the next (or previous) placeable part.</summary>
    public void Cycle(int step)
    {
        if (_parts.Count == 0)
            return;
        var index = Selected is null ? 0 : _parts.IndexOf(Selected);
        Selected = _parts[((index + step) % _parts.Count + _parts.Count) % _parts.Count];
        _emptyHand = false;
    }

    /// <summary>Turns the part a quarter turn about the face it goes on; while a drag is held, Q steps its plane instead.</summary>
    public void Turn()
    {
        if (_place.Phase is GesturePhase.Holding or GesturePhase.Dragging)
            CyclePlane();
        else
            _adjust = BuildAim.Turn(_adjust);
    }

    /// <summary>Turns the part a quarter turn the other way.</summary>
    public void TurnBack()
    {
        if (_place.Phase is GesturePhase.Holding or GesturePhase.Dragging)
        {
            CyclePlane();
            CyclePlane();
            return;
        }
        for (var i = 0; i < 3; i++)
            _adjust = BuildAim.Turn(_adjust);
    }

    /// <summary>Tips the part a quarter turn over, for wedges and other parts with a top. Ground-only parts (soil) never tip.</summary>
    public void Tip()
    {
        if (Placing is { } part && Build.Catalog.FindDefinition(part)?.GroundOnly == true)
            return;
        _adjust = BuildAim.Tip(_adjust);
    }

    /// <summary>Steps the drag plane: XY, XZ, YZ (UI spec §8).</summary>
    public void CyclePlane() => _plane = BuildDrag.NextPlane(_plane);

    /// <summary>Starts the left button's gesture on the ghost: a click, an alignment hold or a drag.</summary>
    public void PressPlace()
    {
        if (Aim is not { } aim || Selected is null)
            return;
        _anchor = aim;
        _plane = BuildDrag.AxisOf(aim.Face);
        _place.Press(Now, aim.Cell);
    }

    /// <summary>
    /// Ends the left button's gesture: a click or hold places one part where the ghost is (if it fits; a soft error
    /// sound if not), a drag places the whole grid. False when no gesture was under way.
    /// </summary>
    public bool ReleasePlace(Action<ActionResult>? done = null)
    {
        UpdatePlacing();
        var end = _place.Release(Now);
        var anchor = _anchor;
        _anchor = null;
        _arrows.Visible = false;
        if (end is null)
            return false;
        if (end == GestureEnd.Drag && anchor is not null && _dragCells.Count > 1)
        {
            PlaceMany(anchor, [.. _dragCells], done);
        }
        else if (end == GestureEnd.Hold && anchor is not null)
        {
            if (Check(anchor) is null)
                PlaceAt(anchor, done);
            else
                Sounds?.PlayUi("ui_error");
        }
        else if (!_previewValid)
        {
            Sounds?.PlayUi("ui_error");
        }
        else
        {
            Place(done);
        }
        _dragCells.Clear();
        ShowGhosts();
        return true;
    }

    /// <summary>
    /// Starts the right button's gesture on the part under the crosshair: held, it picks it up; dragged, an area. Any
    /// tool without a right click of its own starts it too (<see cref="HandTool.PickUp"/>). False when no part is aimed at.
    /// </summary>
    public bool PressRemove()
    {
        if (CastRay() is not { Body: { } body, Node: { } node } hit || PartHit(hit) is not var (_, part))
            return false;
        var frame = body.GlobalTransform;
        var toGrid = frame.AffineInverse();
        var (cell, face) = BuildAim.FromHit((toGrid * hit.Point).ToNumerics(), (toGrid.Basis * hit.Normal).ToNumerics());
        _removing = (node, part, node.Creation.BodyIndexOf(part), face, frame);
        _removed = false;
        _remove.Press(Now, cell);
        return true;
    }

    /// <summary>
    /// Ends the right button's gesture: a drag removes the parts it marked; anything else that had not filled the
    /// ring cancels. False when no gesture was under way.
    /// </summary>
    public bool ReleaseRemove(Action<ActionResult>? done = null)
    {
        UpdateRemoving();
        var end = _remove.Release(Now);
        var removing = _removing;
        _removing = null;
        if (end is null)
            return false;
        if (end == GestureEnd.Drag && removing is { } r && _dragParts.Count > 0)
        {
            Send(BuildActions.RemoveParts, new RemovePartsArgs(r.Node.Creation.Id, [.. _dragParts]), done);
        }
        _dragParts.Clear();
        ShowMarks();
        return true;
    }

    /// <summary>Drops the right button's gesture without acting.</summary>
    public void CancelRemove()
    {
        if (_remove is null || !_remove.Down && _removing is null)
            return;
        _remove.Cancel();
        _removing = null;
        _dragParts.Clear();
        ShowMarks();
    }

    /// <summary>Drops both gestures without acting.</summary>
    public void CancelGestures()
    {
        _place.Cancel();
        _remove.Cancel();
        _anchor = null;
        _removing = null;
        _dragCells.Clear();
        _dragParts.Clear();
        if (_arrows is not null)
        {
            _arrows.Visible = false;
            ShowGhosts();
            ShowMarks();
        }
    }

    /// <summary>Asks the host to place the selected part where the preview is. False when there is nothing to aim at.</summary>
    public bool Place(Action<ActionResult>? done = null) => Aim is { } aim && PlaceAt(aim, done);

    private bool PlaceAt(Target aim, Action<ActionResult>? done)
    {
        if (Placing is not { } part)
            return false;
        var ghost = PendingGhost.Leave(this, _preview);
        ghost.GlobalTransform = aim.Preview;
        PlayPlaceGesture();
        Send(BuildActions.PlacePart, new PlacePartArgs(part, aim.Cell, aim.Rotation, aim.Creation?.Id, aim.Position), result =>
        {
            if (IsInstanceValid(ghost))
                ghost.Resolve(result, Timing.RollbackSeconds);
            done?.Invoke(result);
        });
        return true;
    }

    private void PlaceMany(Target anchor, IReadOnlyList<GridCell> cells, Action<ActionResult>? done)
    {
        if (Placing is not { } part || Build.Catalog.FindDefinition(part) is not { } definition)
            return;
        var ghosts = cells.Select(cell =>
        {
            var ghost = PendingGhost.Leave(this, _preview);
            ghost.GlobalTransform = anchor.Frame * PartFrame(definition, cell, anchor.Rotation);
            return ghost;
        }).ToList();
        PlayPlaceGesture();
        Send(BuildActions.PlaceParts, new PlacePartsArgs(part, cells, anchor.Rotation, anchor.Creation?.Id, anchor.Position), result =>
        {
            foreach (var ghost in ghosts.Where(IsInstanceValid))
                ghost.Resolve(result, Timing.RollbackSeconds);
            done?.Invoke(result);
        });
    }

    // An item that sets another part down gestures as it does: its own use gesture (the Soil Bag tipped out) for the ground
    // part, the plain use gesture for the item itself (the bag put down with Force build).
    private void PlayPlaceGesture()
    {
        if (Selected is not { } item || PartItems.GroundPartFor(Build.Catalog, item) is null)
            return;
        var gesture = Placing != item && PoseInHand.Gesture != CharacterGesture.None ? PoseInHand.Gesture : UseGesture;
        Player.Gesture(gesture);
    }

    /// <summary>Asks the host to remove the part under the crosshair at once. False when no part is aimed at.</summary>
    public bool Remove(Action<ActionResult>? done = null)
    {
        if (Aim is not { Creation: { } creation, HitPart: { } part })
            return false;
        Send(BuildActions.RemovePart, new RemovePartArgs(creation.Id, part), done);
        return true;
    }

    /// <summary>Seconds of holding right click that remove a part: its material's time (§9).</summary>
    public double RemoveSecondsOf(Creation creation, int part) =>
        creation.FindPart(part) is null ? Timing.RemoveSeconds : Timing.RemoveSecondsFor(creation.DefinitionOf(part).SoundMaterial);

    /// <summary>Casts the aim ray now and moves the preview.</summary>
    public void UpdateAim()
    {
        if (_place.Phase is not (GesturePhase.Holding or GesturePhase.Dragging))
            Placing = PlacingFor(Selected);
        Aim = Placing is { } placing && Build.Catalog.FindDefinition(placing) is { } definition
            ? Cast(definition)
            : null;
        // Aligning and dragging lock the ghost to the cell the button went down on.
        var shown = _place.Phase is GesturePhase.Holding or GesturePhase.Dragging ? _anchor : Aim;
        _preview.Visible = Active && shown is not null && _dragCells.Count <= 1;
        if (shown is null)
        {
            _previewValid = false;
            PreviewProblem = null;
            return;
        }
        if (_previewPart != Placing)
            BuildPreview(Placing!, Build.Catalog.FindDefinition(Placing!)!);
        _preview.GlobalTransform = shown.Preview;
        PreviewProblem = Check(shown);
        _previewValid = PreviewProblem is null;
        SetPreviewMaterial(_preview, _previewValid ? PreviewMaterial : InvalidMaterial);
    }

    // The same checks the host makes, so the ghost turns red where a placement would be refused.
    private string? Check(Target aim)
    {
        if (Placing is not { } part || Build.Catalog.FindDefinition(part) is not { } definition)
            return "no part";
        if (Eye.DistanceTo(aim.Preview.Origin) > Build.Settings!.Reach)
            return Loc.T("Too far away");
        if (Selected is { } item && CanAfford?.Invoke(item) == false)
            return Loc.T("You have none left");
        Pose pose;
        if (aim.Creation is { } creation)
        {
            if (creation.Parts.Count >= Build.Settings.MaxPartsPerCreation)
                return Loc.T("This creation has the most parts allowed");
            if (!creation.CanPlacePart(part, aim.Cell, aim.Rotation, out _, out var error))
                return error;
            pose = aim.Frame.ToPose();
        }
        else
        {
            pose = new Pose(aim.Position!.Value.ToNumerics(), System.Numerics.Quaternion.Identity);
        }
        if (BuildAim.GroundOnlyProblem(definition, aim.Creation, aim.Rotation, pose,
                () => Build.RestsOnWorld(aim.Creation, definition, aim.Cell, aim.Rotation, pose)) is not null)
            return Loc.T("{0} goes only flat on the ground", definition.Name ?? part);
        var centre = pose.Apply(PartGeometry.CentreMetres(definition, aim.Cell, aim.Rotation).ToNumerics());
        if (BuildAim.SlopeProblem(definition, () => Build.GroundSlope(centre, BuildAim.SlopeReach(definition))) is not null)
            return Loc.T("The ground is too steep for {0}", definition.Name ?? part);
        return Build.CheckClear(aim.Creation, definition, aim.Cell, aim.Rotation, pose);
    }

    // Alignment shows arrows; a drag grows the grid from the anchor towards where the aim meets the plane.
    private void UpdatePlacing()
    {
        if (!_place.Down || _anchor is not { } anchor || Placing is not { } part || Build.Catalog.FindDefinition(part) is not { } definition)
            return;
        // On the plane of the surface the ghost sits on, the aim meets that surface; Q's other planes cut through the ghost.
        var cell = _plane == BuildDrag.AxisOf(anchor.Face) ? AimOnPlane(anchor.Frame, anchor.Cell, _plane, -anchor.Face) : AimOnPlane(anchor.Frame, anchor.Cell, _plane);
        var phase = _place.Update(Now, cell);
        _dragCells.Clear();
        if (phase == GesturePhase.Dragging)
            _dragCells.AddRange(BuildDrag.Cells(anchor.Cell, _place.Current, _plane, BuildDrag.StepOf(definition, anchor.Rotation), Timing.DragMaxPerAxis));
        ShowGhosts();
        _arrows.Visible = phase == GesturePhase.Holding;
        if (_arrows.Visible)
            ShowArrows(anchor);
    }

    // A held right button fills the ring and removes the part once; a drag marks the parts of its body in the plane.
    private void UpdateRemoving()
    {
        if (!_remove.Down || _removing is not { } r)
            return;
        if (!IsInstanceValid(r.Node) || r.Node.Creation.FindPart(r.Part) is not { } start)
        {
            _remove.Cancel();
            _removing = null;
            ShowMarks();
            return;
        }
        var axis = BuildDrag.AxisOf(r.Face);
        var cell = AimOnPlane(r.Frame, _remove.Start, axis, r.Face);
        var phase = _remove.Update(Now, cell);
        _dragParts.Clear();
        if (phase == GesturePhase.Dragging)
        {
            var creation = r.Node.Creation;
            foreach (var at in BuildDrag.Cells(_remove.Start, _remove.Current, axis, new GridSize(1, 1, 1), Timing.DragMaxPerAxis))
            {
                if (creation.PartAt(at) is { } p && !_dragParts.Contains(p.Id) && creation.BodyIndexOf(p.Id) == r.Body)
                    _dragParts.Add(p.Id);
            }
        }
        else if (!_removed && _remove.Held >= RemoveSecondsOf(r.Node.Creation, r.Part))
        {
            // One removal per press: hold again for the next part (no queue).
            _removed = true;
            Send(BuildActions.RemovePart, new RemovePartArgs(r.Node.Creation.Id, start.Id), null);
        }
        ShowMarks();
    }

    // The cell where the aim ray crosses the plane through cell with normal axis (or cell's face side), in the grid at frame.
    private GridCell? AimOnPlane(Transform3D frame, GridCell cell, int axis, GridCell? face = null)
    {
        var toGrid = frame.AffineInverse();
        var size = Core.MechanicaInfo.GridSize;
        // BuildDrag counts cell c as [c, c + 1); the grid frame's cells are centred on whole numbers (BuildAim.CellAt).
        var origin = (toGrid * Eye) / size + new Vector3(0.5f, 0.5f, 0.5f);
        var direction = (toGrid.Basis * -Player.Camera.GlobalBasis.Z).Normalized();
        return face is { } side
            ? BuildDrag.OnFace(origin.ToNumerics(), direction.ToNumerics(), cell, side)
            : BuildDrag.OnPlane(origin.ToNumerics(), direction.ToNumerics(), cell, axis);
    }

    private void ShowGhosts()
    {
        if (_ghosts.Count > 0 && _ghosts[0].GetMeta("part", "").AsString() != (Placing ?? ""))
        {
            foreach (var ghost in _ghosts)
                ghost.QueueFree();
            _ghosts.Clear();
        }
        var count = _dragCells.Count > 1 ? _dragCells.Count : 0;
        while (_ghosts.Count < count)
        {
            var ghost = new Node3D { Name = "DragGhost", TopLevel = true };
            ghost.SetMeta("part", Placing ?? "");
            foreach (var mesh in _preview.GetChildren().OfType<MeshInstance3D>())
                ghost.AddChild(new MeshInstance3D { Mesh = mesh.Mesh, Transform = mesh.Transform, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            AddChild(ghost);
            _ghosts.Add(ghost);
        }
        var definition = Placing is { } part ? Build.Catalog.FindDefinition(part) : null;
        for (var i = 0; i < _ghosts.Count; i++)
        {
            var ghost = _ghosts[i];
            ghost.Visible = i < count && _anchor is not null && definition is not null;
            if (!ghost.Visible)
                continue;
            var cell = _dragCells[i];
            var target = _anchor! with { Cell = cell, Preview = _anchor.Frame * PartFrame(definition!, cell, _anchor.Rotation) };
            ghost.GlobalTransform = target.Preview;
            // The first is checked fully; the rest attach to the ones before, so only room and reach count.
            var fits = i == 0 ? Check(target) is null : Eye.DistanceTo(target.Preview.Origin) <= Build.Settings!.Reach
                && Build.CheckClear(target.Creation, definition!, cell, target.Rotation, target.Creation is null ? new Pose(target.Position!.Value.ToNumerics(), System.Numerics.Quaternion.Identity) : target.Frame.ToPose()) is null;
            SetPreviewMaterial(ghost, fits ? PreviewMaterial : InvalidMaterial);
        }
    }

    private void ShowMarks()
    {
        var creation = _removing?.Node is { } node && IsInstanceValid(node) ? node.Creation : null;
        while (_marks.Count < _dragParts.Count)
        {
            var mark = new MeshInstance3D { Name = "RemoveMark", TopLevel = true, Mesh = new BoxMesh { Size = Vector3.One * Core.MechanicaInfo.GridSize * 1.04f }, MaterialOverride = InvalidMaterial };
            AddChild(mark);
            _marks.Add(mark);
        }
        for (var i = 0; i < _marks.Count; i++)
        {
            var mark = _marks[i];
            mark.Visible = creation is not null && i < _dragParts.Count && creation.FindPart(_dragParts[i]) is not null;
            if (!mark.Visible)
                continue;
            var part = creation!.FindPart(_dragParts[i])!;
            var definition = creation.DefinitionOf(part.Id);
            var size = BuildDrag.StepOf(definition, part.Rotation);
            // A box mesh rebuilds on every size set, and this runs every tick of a drag.
            var box = new Vector3(size.X, size.Y, size.Z) * Core.MechanicaInfo.GridSize * 1.04f;
            if (((BoxMesh)mark.Mesh).Size != box)
                ((BoxMesh)mark.Mesh).Size = box;
            mark.GlobalTransform = _removing!.Value.Frame * new Transform3D(Basis.Identity, PartGeometry.CentreMetres(definition, part.Cell, part.Rotation).ToGodot());
        }
    }

    // Four arrows round the locked ghost, along the plane's axes.
    private void ShowArrows(Target anchor)
    {
        if (_arrows.GetChildCount() == 0)
        {
            var material = new StandardMaterial3D { AlbedoColor = UiTokens.Cyan, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
            for (var i = 0; i < 4; i++)
                _arrows.AddChild(new MeshInstance3D { Name = $"Arrow{i}", Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.06f, Height = 0.16f }, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        var directions = BuildDrag.Directions(_plane);
        _arrows.GlobalTransform = new Transform3D(anchor.Frame.Basis.Orthonormalized(), anchor.Preview.Origin);
        for (var i = 0; i < 4; i++)
        {
            var arrow = _arrows.GetChild<MeshInstance3D>(i);
            var d = directions[i].AsVector.ToNumerics().ToGodot();
            // A cylinder points up its Y axis; turn it to point along d, just outside the part.
            arrow.Transform = new Transform3D(new Basis(new Quaternion(Vector3.Up, d)), d * 0.32f);
        }
    }

    private static void SetPreviewMaterial(Node3D preview, Material material)
    {
        foreach (var child in preview.GetChildren())
        {
            if (child is MeshInstance3D mesh && mesh.MaterialOverride != material)
                mesh.MaterialOverride = material;
        }
    }

    // The item itself, or its ground part when it aims at the ground (the world's own terrain) without Force build.
    private string? PlacingFor(string? item)
    {
        if (item is null || PartItems.GroundPartFor(Build.Catalog, item) is not { } ground || Build.Catalog.FindDefinition(ground) is null || ForceHeld)
            return item;
        return CastRay() is { Collider: StaticBody3D { Name: var name } and not LiftBody } && name == World.WorldStreamer.GroundName ? ground : item;
    }

    private Target? Cast(PartDefinition definition)
    {
        if (CastRay() is not { } hit)
            return null;
        var (point, normal, from) = (hit.Point, hit.Normal, Eye);
        var adjust = BuildAim.Upright(definition, _adjust);

        if (hit is { Body: { } body, Node: { } node })
        {
            var frame = body.GlobalTransform;
            var toGrid = frame.AffineInverse();
            var (cell, face) = BuildAim.FromHit((toGrid * point).ToNumerics(), (toGrid.Basis * normal).ToNumerics());
            var rotation = BuildAim.Orient(face, (toGrid.Basis * (from - point)).ToNumerics(), adjust);
            var origin = BuildAim.Anchor(definition, rotation, cell + face, face);
            return new Target(node.Creation, origin, rotation, null, node.Creation.PartAt(cell)?.Id,
                frame * PartFrame(definition, origin, rotation), face, frame);
        }

        // A new Creation: its grid follows the world axes, the first cell sits on the surface.
        var worldFace = BuildAim.NearestAxis(normal.ToNumerics());
        var start = point + worldFace.ToGodot() * (Core.MechanicaInfo.GridSize / 2);
        var snapped = BuildAim.Snap(start.ToNumerics()).ToGodot();
        var across = Vector3.One - worldFace.ToGodot().Abs();
        var position = snapped * across + start * worldFace.ToGodot().Abs();
        var turned = BuildAim.Orient(worldFace, (from - point).ToNumerics(), adjust);
        var anchor = BuildAim.Anchor(definition, turned, GridCell.Zero, worldFace);
        return new Target(null, anchor, turned, position.ToNumerics().ToVec3(), null,
            new Transform3D(Basis.Identity, position) * PartFrame(definition, anchor, turned), worldFace, new Transform3D(Basis.Identity, position));
    }

    private static Transform3D PartFrame(PartDefinition definition, GridCell origin, GridRotation rotation) =>
        new(rotation.ToBasis(), PartGeometry.CentreMetres(definition, origin, rotation).ToGodot());

    private void BuildPreview(string part, PartDefinition definition)
    {
        foreach (var child in _preview.GetChildren())
        {
            _preview.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var mesh in Build.Assets.ModelFor(part, definition).Meshes)
        {
            var instance = mesh.Instantiate();
            instance.MaterialOverride = PreviewMaterial;
            instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            _preview.AddChild(instance);
        }
        _previewPart = part;
    }

    /// <inheritdoc/>
    public override string HudText()
    {
        if (_remove.Phase == GesturePhase.Dragging)
            return Loc.T("Pick up {0} parts   let go to pick them up", _dragParts.Count);
        if (Selected is null)
            return (_parts.Count == 0 && !_emptyHand ? Loc.T("No placeable parts are loaded") : Loc.T("Empty hand   {0}", PickUpHint)) + Status;
        var name = Build.Catalog.FindDefinition(Selected)?.Name ?? Selected;
        if (_place.Phase == GesturePhase.Dragging)
            return Loc.T("{0} × {1} on the {2} plane   {3} next plane  let go to place", name, _dragCells.Count, BuildDrag.PlaneName(_plane), Key(InputActions.Rotate));
        if (_place.Phase == GesturePhase.Holding)
            return Loc.T("{0}   drag to place a grid on the {1} plane   {2} next plane", name, BuildDrag.PlaneName(_plane), Key(InputActions.Rotate));
        var problem = !_previewValid && PreviewProblem is { Length: > 0 } p && Aim is not null ? $"\n{p}" : "";
        if (PartItems.GroundPartFor(Build.Catalog, Selected) is { } ground && Placing == ground)
            return Loc.T("{0}   {1} turn  {2} spill  hold {3} set the bag down  hold {4} on a part to pick it up", name,
                Key(InputActions.Rotate), Key(InputActions.Primary), Key(InputActions.ForceBuild), Key(InputActions.Secondary)) + (Status.Length > 0 ? Status : problem);
        if (Build.Catalog.FindDefinition(Placing ?? Selected)?.GroundOnly == true)
            return Loc.T("{0}   {1} turn  {2} place (hold to drag)  hold {3} on a part to pick it up", name,
                Key(InputActions.Rotate), Key(InputActions.Primary), Key(InputActions.Secondary)) + (Status.Length > 0 ? Status : problem);
        return Loc.T("{0}   {1} turn  {2} tip  {3} place (hold to drag)  hold {4} on a part to pick it up", name,
            Key(InputActions.Rotate), Key(InputActions.Tip), Key(InputActions.Primary), Key(InputActions.Secondary)) + (Status.Length > 0 ? Status : problem);
    }
}
