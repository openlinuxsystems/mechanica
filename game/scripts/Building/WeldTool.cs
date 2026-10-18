using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Input;
using Mechanica.Game.Creations;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// The Weld Tool in the local player's hand (D7, UI spec §2, §8). The first click selects a part, and with it the
/// Creation to move; the part shows a blue outline. Aimed at another Creation, a see-through copy of the selected one
/// shows where it would go against the face under the crosshair (turned as when building, Q and T), a blue square marks
/// the weld plane, and a second click sends <c>Weld</c>: the host moves it there and merges the two, every touching face
/// rigid. Aimed at another part of the same Creation, the second click sends <c>WeldParts</c>, welding the two Bodies
/// together (locking a bearing, say). Holding left click on the first part and letting go on the second does the
/// same as two clicks. After a weld a blue seam glows on the faces it joined and fades. Right click drops the selection.
/// </summary>
public partial class WeldTool : HandTool
{
    private Node3D _preview = null!;
    private Node3D _outline = null!;
    private MeshInstance3D _plane = null!;
    private int? _previewOf;
    private (int Creation, int Part)? _outlineOf;
    private GridRotation _adjust;
    private Selection? _dragFrom;

    /// <summary>The selected part's outline.</summary>
    public static readonly StandardMaterial3D OutlineMaterial = new()
    {
        AlbedoColor = UiTokens.Blue with { A = 0.6f },
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Front,
        Grow = true,
        GrowAmount = 0.015f,
    };

    /// <summary>The weld plane square.</summary>
    public static readonly StandardMaterial3D PlaneMaterial = new()
    {
        AlbedoColor = UiTokens.Blue with { A = 0.45f },
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        NoDepthTest = true,
    };

    /// <summary>The part the weld starts from.</summary>
    /// <param name="Creation">Its Creation, which <c>Weld</c> moves.</param>
    /// <param name="Part">The part.</param>
    public sealed record Selection(int Creation, int Part);

    /// <summary>What the crosshair is on.</summary>
    /// <param name="Creation">The Creation under the crosshair.</param>
    /// <param name="Part">The part hit.</param>
    /// <param name="Offset">With a selection in another Creation: where its grid origin would land in this one's grid.</param>
    /// <param name="Rotation">And how its grid would turn.</param>
    /// <param name="Preview">And the world transform of its grid frame there.</param>
    /// <param name="Frame">The world transform of the hit Body's grid frame.</param>
    /// <param name="Cell">The cell hit, in that grid.</param>
    /// <param name="Face">The face of it hit.</param>
    public sealed record Target(Creation Creation, int? Part, GridCell Offset, GridRotation Rotation, Transform3D Preview, Transform3D Frame, GridCell Cell, GridCell Face);

    /// <summary>The faces a weld joined, as the seam shows them.</summary>
    /// <param name="Frame">World transform of the grid the faces are in.</param>
    /// <param name="Faces">The faces.</param>
    public sealed record Seam(Transform3D Frame, IReadOnlyList<SeamFace> Faces);

    /// <inheritdoc/>
    public override string ToolName => "Weld Tool";

    /// <inheritdoc/>
    public override CharacterProp Prop => CharacterProp.WeldTool;

    /// <inheritdoc/>
    protected override CharacterGesture GestureFor(InputEvent @event) =>
        Pressed(@event, Core.Input.InputActions.Primary) ? UseGesture : CharacterGesture.None;

    /// <inheritdoc/>
    protected override CharacterGesture UseGesture => CharacterGesture.Weld;

    /// <summary>The selected part, or null.</summary>
    public Selection? Selected { get; private set; }

    /// <summary>The current aim, updated every physics frame while in hand (and by <see cref="UpdateAim"/>).</summary>
    public Target? Aim { get; private set; }

    /// <summary>The blue outline round the selected part (visible while one is selected).</summary>
    public Node3D Outline => _outline;

    /// <summary>The blue weld plane square (visible while a weld would join there).</summary>
    public MeshInstance3D Plane => _plane;

    /// <summary>The seam of the last weld the host accepted, or null.</summary>
    public Seam? LastSeam { get; private set; }

    /// <summary>The faces the next weld would join, as the plane square and the seam show them, or null.</summary>
    public Seam? Joining { get; private set; }

    /// <summary>True while left click is held after selecting: letting go on another part welds.</summary>
    public bool Dragging => _dragFrom is not null;

    public override void _Ready()
    {
        _preview = new Node3D { Name = "Preview", TopLevel = true, Visible = false };
        AddChild(_preview);
        _outline = new Node3D { Name = "Outline", TopLevel = true, Visible = false };
        AddChild(_outline);
        var size = Core.MechanicaInfo.GridSize;
        _plane = new MeshInstance3D
        {
            Name = "WeldPlane", TopLevel = true, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new PlaneMesh { Size = new Vector2(size, size), Orientation = PlaneMesh.OrientationEnum.Z, Material = PlaneMaterial },
        };
        AddChild(_plane);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Active)
            return;
        // A release the tool never heard (the mouse freed for a window) ends the drag without welding.
        if (ReadDeviceInput && Input.MouseMode != Input.MouseModeEnum.Captured)
            _dragFrom = null;
        UpdateAim();
    }

    /// <summary>Selects the part under the crosshair. False when no part is aimed at.</summary>
    public bool Select()
    {
        UpdateAim();
        if (Aim is not { Part: { } part } aim)
            return false;
        Selected = new Selection(aim.Creation.Id, part);
        _adjust = default;
        _previewOf = null;
        return true;
    }

    /// <summary>Drops the selection.</summary>
    public void Deselect()
    {
        Selected = null;
        _dragFrom = null;
    }

    /// <summary>Left click pressed: selects the aimed part (and starts a drag), or welds the selection to it.</summary>
    public void Press()
    {
        if (Selected is null)
        {
            if (Select())
                _dragFrom = Selected;
        }
        else
            Weld();
    }

    /// <summary>
    /// Left click let go: after a drag from the first part, welds it to the part now under the crosshair; let go on
    /// the same part, the selection stays for a second click. True when it welded.
    /// </summary>
    public bool Release(Action<ActionResult>? done = null)
    {
        if (_dragFrom is not { } from)
            return false;
        _dragFrom = null;
        UpdateAim();
        if (Selected != from || Aim is not { } aim || (aim.Creation.Id == from.Creation && aim.Part == from.Part))
            return false;
        return Weld(done);
    }

    /// <summary>Turns the moved Creation a quarter turn about the face.</summary>
    public void Turn() => _adjust = BuildAim.Turn(_adjust);

    /// <summary>Tips the moved Creation a quarter turn over.</summary>
    public void Tip() => _adjust = BuildAim.Tip(_adjust);

    /// <summary>
    /// Welds the selection to what the crosshair is on: <c>Weld</c> for another Creation, <c>WeldParts</c>
    /// for another part of the same one. False when there is nothing to weld.
    /// </summary>
    public bool Weld(Action<ActionResult>? done = null)
    {
        if (Selected is not { } selected || Aim is not { } aim)
            return false;
        if (aim.Creation.Id == selected.Creation && aim.Part is null)
            return false;
        PlayFeedback("weld_start");
        var seam = Joining;
        if (aim.Creation.Id == selected.Creation)
        {
            if (aim.Part is not { } part)
                return false;
            Send(ToolActions.WeldParts, new WeldPartsArgs(selected.Creation, selected.Part, part), Done(done, seam));
        }
        else
        {
            // The moved Creation shows where it goes until the host answers; a refusal fades it out red.
            var ghost = _preview.Visible ? PendingGhost.Leave(this, _preview) : null;
            var finish = Done(done, seam);
            Send(ToolActions.Weld, new WeldArgs(selected.Creation, aim.Creation.Id, aim.Offset, aim.Rotation), result =>
            {
                if (ghost is not null && IsInstanceValid(ghost))
                    ghost.Resolve(result, Build.Settings!.Placement.RollbackSeconds);
                finish(result);
            });
        }
        return true;
    }

    /// <summary>Casts the aim ray now and moves the preview, the outline and the weld plane square.</summary>
    public void UpdateAim()
    {
        if (Selected is { } s && (Build.Creations.Find(s.Creation) is not { } held || held.FindPart(s.Part) is null))
            Selected = null;
        Aim = Cast();
        UpdateOutline();
        Joining = Join();
        _plane.Visible = Active && Joining is { Faces.Count: > 0 };
        if (_plane.Visible)
            PlaceSquare(_plane, Joining!.Frame, Joining.Faces[0]);
        var source = Selected is { } sel && Aim is not null && Aim.Creation.Id != sel.Creation ? Build.Creations.Find(sel.Creation) : null;
        _preview.Visible = Active && source is not null;
        if (source is null)
            return;
        if (_previewOf != source.Id)
            BuildPreview(source);
        _preview.GlobalTransform = Aim!.Preview;
    }

    /// <summary>Puts a grid-face square at <paramref name="face"/>, just off the surface so it is not hidden in it.</summary>
    public static void PlaceSquare(MeshInstance3D square, Transform3D frame, SeamFace face)
    {
        var normal = face.Normal.AsVector.ToGodot();
        var basis = Mathf.Abs(normal.Y) > 0.5f ? new Basis(Vector3.Right, normal.Y > 0 ? -Mathf.Pi / 2 : Mathf.Pi / 2)
            : Basis.LookingAt(-normal, Vector3.Up);
        square.GlobalTransform = frame * new Transform3D(basis, face.CentreMetres.ToGodot() + normal * 0.004f);
    }

    /// <inheritdoc/>
    public override string HudText()
    {
        var state = Selected is { } s ? Loc.T("part {0} of creation {1} selected", s.Part, s.Creation) : Loc.T("nothing selected");
        return Loc.T("Weld Tool ({0})   {1} select / weld (or drag between parts)  {2} turn  {3} tip  {4} clear", state, Key(InputActions.Primary), Key(InputActions.Rotate), Key(InputActions.Tip), Key(InputActions.Secondary)) + Status;
    }

    /// <inheritdoc/>
    public override Core.Ui.CrosshairState Crosshair => Aim?.Part is null ? Core.Ui.CrosshairState.Default
        : Selected is null || Joining is { Faces.Count: > 0 } ? Core.Ui.CrosshairState.Valid : Core.Ui.CrosshairState.Default;

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
            Press();
        else if (Released(@event, InputActions.Primary))
            return Release();
        else if (Pressed(@event, InputActions.Secondary))
            Deselect();
        else if (Pressed(@event, InputActions.RotateBack))
        {
            for (var i = 0; i < 3; i++)
                Turn();
        }
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
        if (Active)
            return;
        _preview.Visible = _outline.Visible = _plane.Visible = false;
        _dragFrom = null;
    }

    private Action<ActionResult> Done(Action<ActionResult>? done, Seam? seam) => result =>
    {
        if (result.Ok)
        {
            Selected = null;
            if (seam is { Faces.Count: > 0 })
            {
                LastSeam = seam;
                AddChild(new WeldSeamGlow { Name = "Seam", Seam = seam, Seconds = Build.Settings!.Weld.SeamSeconds });
            }
        }
        done?.Invoke(result);
    };

    // The faces the weld would join: where the moved Creation would touch the target, or where the two parts touch.
    private Seam? Join()
    {
        if (Selected is not { } selected || Aim is not { Part: { } part } aim || Build.Creations.Find(selected.Creation) is not { } source)
            return null;
        if (aim.Creation.Id == selected.Creation)
        {
            if (part == selected.Part || Build.Creations.NodeOf(source.Id) is not { } node || node.Bodies.Count <= source.BodyIndexOf(selected.Part))
                return null;
            return new Seam(node.Bodies[source.BodyIndexOf(selected.Part)].GlobalTransform, WeldSeams.Between(source, selected.Part, part));
        }
        var moved = source.Parts.SelectMany(p => source.CellsOf(p.Id)).Select(c => aim.Rotation.Apply(c) + aim.Offset);
        var body = aim.Creation.BodyIndexOf(part);
        var target = aim.Creation.Parts.Where(p => aim.Creation.BodyIndexOf(p.Id) == body).SelectMany(p => aim.Creation.CellsOf(p.Id));
        return new Seam(aim.Frame, WeldSeams.Between(moved, target));
    }

    // The blue outline follows the selected part's Body.
    private void UpdateOutline()
    {
        if (!Active || Selected is not { } s || Build.Creations.NodeOf(s.Creation) is not { } node || node.Creation.FindPart(s.Part) is not { } part
            || node.Bodies.Count <= node.Creation.BodyIndexOf(s.Part))
        {
            _outline.Visible = false;
            return;
        }
        if (_outlineOf != (s.Creation, s.Part))
        {
            foreach (var child in _outline.GetChildren())
            {
                _outline.RemoveChild(child);
                child.QueueFree();
            }
            var definition = node.Creation.DefinitionOf(s.Part);
            var placed = new Transform3D(part.Rotation.ToBasis(), PartGeometry.CentreMetres(definition, part.Cell, part.Rotation).ToGodot());
            foreach (var mesh in Build.Assets.ModelFor(part.Part, definition).Meshes)
            {
                var instance = mesh.Instantiate();
                instance.Transform = placed * instance.Transform;
                instance.MaterialOverride = OutlineMaterial;
                instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                _outline.AddChild(instance);
            }
            _outlineOf = (s.Creation, s.Part);
        }
        _outline.GlobalTransform = node.Bodies[node.Creation.BodyIndexOf(s.Part)].GlobalTransform;
        _outline.Visible = true;
    }

    private Target? Cast()
    {
        if (CastRay() is not { Body: { } body, Node: { } node } hit)
            return null;
        var frame = body.GlobalTransform;
        var toGrid = frame.AffineInverse();
        var (cell, face) = BuildAim.FromHit((toGrid * hit.Point).ToNumerics(), (toGrid.Basis * hit.Normal).ToNumerics());
        var part = node.Creation.PartAt(cell)?.Id;
        if (Selected is not { } selected || selected.Creation == node.Creation.Id || Build.Creations.Find(selected.Creation) is not { } source)
            return new Target(node.Creation, part, GridCell.Zero, default, frame, frame, cell, face);
        var rotation = BuildAim.Orient(face, (toGrid.Basis * (Eye - hit.Point)).ToNumerics(), _adjust);
        var cells = source.Parts.SelectMany(p => source.CellsOf(p.Id)).Select(rotation.Apply).ToList();
        var offset = BuildAim.Anchor(cells, cell + face, face);
        return new Target(node.Creation, part, offset, rotation, frame * Pose.FromGrid(rotation, offset).ToTransform(), frame, cell, face);
    }

    private void BuildPreview(Creation source)
    {
        foreach (var child in _preview.GetChildren())
        {
            _preview.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var part in source.Parts)
        {
            var definition = source.DefinitionOf(part.Id);
            var placed = new Transform3D(part.Rotation.ToBasis(), PartGeometry.CentreMetres(definition, part.Cell, part.Rotation).ToGodot());
            foreach (var mesh in Build.Assets.ModelFor(part.Part, definition).Meshes)
            {
                var instance = mesh.Instantiate();
                instance.Transform = placed * instance.Transform;
                instance.MaterialOverride = BuildTool.PreviewMaterial;
                instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                _preview.AddChild(instance);
            }
        }
        _previewOf = source.Id;
    }
}
