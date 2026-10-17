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
/// Building by hand for the local player. Every physics frame while in hand it casts a ray from the
/// camera and works out where the selected part would go (<see cref="BuildAim"/>), showing a see-through
/// preview there. <see cref="Place"/> and <see cref="Remove"/> send <c>PlacePart</c>/<c>RemovePart</c>
/// through the Action API, so the host decides; nothing changes locally until it answers.
/// <para>
/// Aiming at a Creation attaches to it. Aiming at anything else starts a new Creation, its grid
/// aligned with the world and snapped to 0.25 m across the surface. Keys: left click places, right
/// click removes, Q turns the part about the face (Shift+Q back), T tips it over. The part comes from the
/// hotbar slot picked (<see cref="ToolBelt"/>).
/// </para>
/// </summary>
public partial class HammerTool : HandTool
{
    /// <summary>The see-through look of tool previews.</summary>
    public static readonly StandardMaterial3D PreviewMaterial = new()
    {
        AlbedoColor = UiTokens.Cyan with { A = 0.35f },
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = true,
    };

    private readonly List<string> _parts = [];
    private Node3D _preview = null!;
    private string? _previewPart;
    private GridRotation _adjust;

    /// <summary>What the ray currently points at.</summary>
    /// <param name="Creation">The Creation hit, or null for the ground and other world objects.</param>
    /// <param name="Cell">Origin cell for the selected part, in <see cref="Creation"/>'s grid or the new one's.</param>
    /// <param name="Rotation">Orientation for the selected part.</param>
    /// <param name="Position">For a new Creation, the world position of its grid origin.</param>
    /// <param name="HitPart">The part under the crosshair, if any (what <see cref="Remove"/> takes).</param>
    /// <param name="Preview">World transform of the selected part's centre where it would go.</param>
    public sealed record Target(Creation? Creation, GridCell Cell, GridRotation Rotation, Vec3? Position, int? HitPart, Transform3D Preview);

    /// <inheritdoc/>
    public override string ToolName => "Hammer";

    /// <inheritdoc/>
    public override CharacterProp Prop => CharacterProp.Hammer;

    /// <inheritdoc/>
    protected override CharacterGesture UseGesture => CharacterGesture.HammerSwing;

    /// <summary>Placeable parts to pick from, by qualified id.</summary>
    public IReadOnlyList<string> Parts => _parts;

    /// <summary>The part <see cref="Place"/> puts down, or null when no mod has a placeable part.</summary>
    public string? Selected { get; private set; }

    /// <summary>The player's turn and tip on top of the automatic orientation.</summary>
    public GridRotation Adjust => _adjust;

    /// <summary>The current aim, updated every physics frame (and by <see cref="UpdateAim"/>).</summary>
    public Target? Aim { get; private set; }

    public override void _Ready()
    {
        ReloadParts();
        _preview = new Node3D { Name = "Preview", TopLevel = true, Visible = false };
        AddChild(_preview);
    }

    /// <summary>Re-reads the placeable parts from the catalog, after mods are reloaded. Keeps the selection if it still exists.</summary>
    public void ReloadParts()
    {
        _parts.Clear();
        _parts.AddRange(Build.Catalog.Parts
            .Where(p => p.Value.Definition.IsPlaceable)
            .Select(p => p.Key)
            .Order(StringComparer.Ordinal));
        if (Selected is null || !_parts.Contains(Selected))
            Selected = _parts.FirstOrDefault();
        _previewPart = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Active)
            UpdateAim();
    }

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
            Place();
        else if (Pressed(@event, InputActions.Secondary))
            Remove();
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
    protected override void OnActiveChanged() => _preview.Visible = Active && Aim is not null;

    /// <inheritdoc/>
    public override Core.Ui.CrosshairState Crosshair => Aim is not null && Selected is not null ? Core.Ui.CrosshairState.Valid : Core.Ui.CrosshairState.Default;

    /// <summary>Selects a part by qualified id. Returns false when it is not placeable.</summary>
    public bool Select(string part)
    {
        if (!_parts.Contains(part))
            return false;
        Selected = part;
        return true;
    }

    /// <summary>Selects the next (or previous) placeable part.</summary>
    public void Cycle(int step)
    {
        if (_parts.Count == 0)
            return;
        var index = Selected is null ? 0 : _parts.IndexOf(Selected);
        Selected = _parts[((index + step) % _parts.Count + _parts.Count) % _parts.Count];
    }

    /// <summary>Turns the part a quarter turn about the face it goes on.</summary>
    public void Turn() => _adjust = BuildAim.Turn(_adjust);

    /// <summary>Turns the part a quarter turn the other way.</summary>
    public void TurnBack()
    {
        for (var i = 0; i < 3; i++)
            Turn();
    }

    /// <summary>Tips the part a quarter turn over, for wedges and other parts with a top.</summary>
    public void Tip() => _adjust = BuildAim.Tip(_adjust);

    /// <summary>Asks the host to place the selected part where the preview is. False when there is nothing to aim at.</summary>
    public bool Place(Action<ActionResult>? done = null)
    {
        if (Aim is not { } aim || Selected is null)
            return false;
        PlayFeedback("hammer_swing");
        Send(BuildActions.PlacePart, new PlacePartArgs(Selected, aim.Cell, aim.Rotation, aim.Creation?.Id, aim.Position), done);
        return true;
    }

    /// <summary>Asks the host to remove the part under the crosshair. False when no part is aimed at.</summary>
    public bool Remove(Action<ActionResult>? done = null)
    {
        if (Aim is not { Creation: { } creation, HitPart: { } part })
            return false;
        PlayFeedback("hammer_swing");
        Send(BuildActions.RemovePart, new RemovePartArgs(creation.Id, part), done);
        return true;
    }

    /// <summary>Casts the aim ray now and moves the preview.</summary>
    public void UpdateAim()
    {
        Aim = Selected is { } selected && Build.Catalog.FindDefinition(selected) is { } definition
            ? Cast(definition)
            : null;
        _preview.Visible = Active && Aim is not null;
        if (Aim is null)
            return;
        if (_previewPart != Selected)
            BuildPreview(Selected!, Build.Catalog.FindDefinition(Selected!)!);
        _preview.GlobalTransform = Aim.Preview;
    }

    private Target? Cast(PartDefinition definition)
    {
        if (CastRay() is not { } hit)
            return null;
        var (point, normal, from) = (hit.Point, hit.Normal, Eye);

        if (hit is { Body: { } body, Node: { } node })
        {
            var frame = body.GlobalTransform;
            var toGrid = frame.AffineInverse();
            var (cell, face) = BuildAim.FromHit((toGrid * point).ToNumerics(), (toGrid.Basis * normal).ToNumerics());
            var rotation = BuildAim.Orient(face, (toGrid.Basis * (from - point)).ToNumerics(), _adjust);
            var origin = BuildAim.Anchor(definition, rotation, cell + face, face);
            return new Target(node.Creation, origin, rotation, null, node.Creation.PartAt(cell)?.Id,
                frame * PartFrame(definition, origin, rotation));
        }

        // A new Creation: its grid follows the world axes, the first cell sits on the surface.
        var worldFace = BuildAim.NearestAxis(normal.ToNumerics());
        var start = point + worldFace.ToGodot() * (Core.MechanicaInfo.GridSize / 2);
        var snapped = BuildAim.Snap(start.ToNumerics()).ToGodot();
        var across = Vector3.One - worldFace.ToGodot().Abs();
        var position = snapped * across + start * worldFace.ToGodot().Abs();
        var turned = BuildAim.Orient(worldFace, (from - point).ToNumerics(), _adjust);
        var anchor = BuildAim.Anchor(definition, turned, GridCell.Zero, worldFace);
        return new Target(null, anchor, turned, position.ToNumerics().ToVec3(), null,
            new Transform3D(Basis.Identity, position) * PartFrame(definition, anchor, turned));
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
        if (Selected is null)
            return Loc.T("Hammer: no placeable parts are loaded");
        var name = Build.Catalog.FindDefinition(Selected)?.Name ?? Selected;
        return Loc.T("Hammer: {0}   {1} turn  {2} tip  {3} place  {4} remove", name,
            Key(InputActions.Rotate), Key(InputActions.Tip), Key(InputActions.Primary), Key(InputActions.Secondary)) + Status;
    }
}
