using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Input;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Building;

/// <summary>
/// The Weld Tool in the local player's hand (D7). The first click selects a part, and with it the
/// Creation to move. Aimed at another Creation, a see-through copy of the selected one shows where it
/// would go against the face under the crosshair (turned like the Hammer, R and T), and a second click
/// sends <c>Weld</c>: the host moves it there and merges the two, every touching face rigid. Aimed at
/// another part of the same Creation, the second click sends <c>WeldParts</c>, welding the two Bodies
/// together (locking a bearing, say). Right click drops the selection.
/// </summary>
public partial class WeldTool : HandTool
{
    private Node3D _preview = null!;
    private int? _previewOf;
    private GridRotation _adjust;

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
    public sealed record Target(Creation Creation, int? Part, GridCell Offset, GridRotation Rotation, Transform3D Preview);

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

    public override void _Ready()
    {
        _preview = new Node3D { Name = "Preview", TopLevel = true, Visible = false };
        AddChild(_preview);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Active)
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
    public void Deselect() => Selected = null;

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
        if (aim.Creation.Id == selected.Creation)
        {
            if (aim.Part is not { } part)
                return false;
            Send(ToolActions.WeldParts, new WeldPartsArgs(selected.Creation, selected.Part, part), Done(done));
        }
        else
        {
            Send(ToolActions.Weld, new WeldArgs(selected.Creation, aim.Creation.Id, aim.Offset, aim.Rotation), Done(done));
        }
        return true;
    }

    /// <summary>Casts the aim ray now and moves the preview.</summary>
    public void UpdateAim()
    {
        if (Selected is { } s && Build.Creations.Find(s.Creation) is null)
            Selected = null;
        Aim = Cast();
        var source = Selected is { } sel && Aim is not null && Aim.Creation.Id != sel.Creation ? Build.Creations.Find(sel.Creation) : null;
        _preview.Visible = Active && source is not null;
        if (source is null)
            return;
        if (_previewOf != source.Id)
            BuildPreview(source);
        _preview.GlobalTransform = Aim!.Preview;
    }

    /// <inheritdoc/>
    public override string HudText()
    {
        var state = Selected is { } s ? $"part {s.Part} of creation {s.Creation} selected" : "nothing selected";
        return $"Weld Tool ({state})   {Key(InputActions.Primary)} select / weld  {Key(InputActions.Rotate)} turn  {Key(InputActions.Tip)} tip  {Key(InputActions.Secondary)} clear{Status}";
    }

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
        {
            if (Selected is null)
                Select();
            else
                Weld();
        }
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
        if (!Active)
            _preview.Visible = false;
    }

    private Action<ActionResult> Done(Action<ActionResult>? done) => result =>
    {
        if (result.Ok)
            Selected = null;
        done?.Invoke(result);
    };

    private Target? Cast()
    {
        if (CastRay() is not { Body: { } body, Node: { } node } hit)
            return null;
        var frame = body.GlobalTransform;
        var toGrid = frame.AffineInverse();
        var (cell, face) = BuildAim.FromHit((toGrid * hit.Point).ToNumerics(), (toGrid.Basis * hit.Normal).ToNumerics());
        var part = node.Creation.PartAt(cell)?.Id;
        if (Selected is not { } selected || selected.Creation == node.Creation.Id || Build.Creations.Find(selected.Creation) is not { } source)
            return new Target(node.Creation, part, GridCell.Zero, default, frame);
        var rotation = BuildAim.Orient(face, (toGrid.Basis * (Eye - hit.Point)).ToNumerics(), _adjust);
        var cells = source.Parts.SelectMany(p => source.CellsOf(p.Id)).Select(rotation.Apply).ToList();
        var offset = BuildAim.Anchor(cells, cell + face, face);
        return new Target(node.Creation, part, offset, rotation, frame * Pose.FromGrid(rotation, offset).ToTransform());
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
                instance.MaterialOverride = HammerTool.PreviewMaterial;
                instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                _preview.AddChild(instance);
            }
        }
        _previewOf = source.Id;
    }
}
