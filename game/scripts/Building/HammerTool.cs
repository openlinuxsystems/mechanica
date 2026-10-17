using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Building;

/// <summary>
/// Building by hand for the local player. Every physics frame it casts a ray from the camera and
/// works out where the selected part would go (<see cref="BuildAim"/>), showing a see-through preview
/// there. <see cref="Place"/> and <see cref="Remove"/> send <c>PlacePart</c>/<c>RemovePart</c> through the
/// Action API, so the host decides; nothing changes locally until it answers.
/// <para>
/// Aiming at a Creation attaches to it. Aiming at anything else starts a new Creation, its grid
/// aligned with the world and snapped to 0.25 m across the surface. Keys: left click places, right
/// click removes, Q/E pick the part, R turns it about the face, T tips it over.
/// </para>
/// </summary>
public partial class HammerTool : Node
{
    private static readonly StandardMaterial3D PreviewMaterial = new()
    {
        AlbedoColor = new Color(0.4f, 0.8f, 1f, 0.35f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = true,
    };

    private readonly List<string> _parts = [];
    private Node3D _preview = null!;
    private string? _previewPart;
    private Label? _hud;
    private GridRotation _adjust;

    /// <summary>What the ray currently points at.</summary>
    /// <param name="Creation">The Creation hit, or null for the ground and other world objects.</param>
    /// <param name="Cell">Origin cell for the selected part, in <see cref="Creation"/>'s grid or the new one's.</param>
    /// <param name="Rotation">Orientation for the selected part.</param>
    /// <param name="Position">For a new Creation, the world position of its grid origin.</param>
    /// <param name="HitPart">The part under the crosshair, if any (what <see cref="Remove"/> takes).</param>
    /// <param name="Preview">World transform of the selected part's centre where it would go.</param>
    public sealed record Target(Creation? Creation, GridCell Cell, GridRotation Rotation, Vec3? Position, int? HitPart, Transform3D Preview);

    /// <summary>The player whose camera aims. Set before adding to the tree.</summary>
    public PlayerController Player { get; init; } = null!;

    /// <summary>Where requests go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>Settings and content. Set before adding to the tree.</summary>
    public BuildSync Build { get; init; } = null!;

    /// <summary>False in automated tests: keys and mouse do nothing and no HUD is shown.</summary>
    public bool ReadDeviceInput { get; init; } = true;

    /// <summary>Placeable parts to pick from, by qualified id.</summary>
    public IReadOnlyList<string> Parts => _parts;

    /// <summary>The part <see cref="Place"/> puts down, or null when no mod has a placeable part.</summary>
    public string? Selected { get; private set; }

    /// <summary>The player's turn and tip on top of the automatic orientation.</summary>
    public GridRotation Adjust => _adjust;

    /// <summary>The current aim, updated every physics frame (and by <see cref="UpdateAim"/>).</summary>
    public Target? Aim { get; private set; }

    /// <summary>The host's answer to the last request, or null while waiting or before any.</summary>
    public ActionResult? LastResult { get; private set; }

    public override void _Ready()
    {
        _parts.AddRange(Build.Catalog.Parts
            .Where(p => p.Value.Definition.IsPlaceable)
            .Select(p => p.Key)
            .Order(StringComparer.Ordinal));
        Selected = _parts.FirstOrDefault();
        _preview = new Node3D { Name = "Preview", TopLevel = true, Visible = false };
        AddChild(_preview);
        if (ReadDeviceInput)
        {
            var layer = new CanvasLayer { Name = "Hud" };
            // Bottom left, clear of the connect panel at the top.
            _hud = new Label { Name = "HammerLabel", GrowVertical = Control.GrowDirection.Begin };
            layer.AddChild(_hud);
            _hud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.KeepSize, 16);
            var crosshair = new Label { Name = "Crosshair", Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            layer.AddChild(crosshair);
            crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
            AddChild(layer);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdateAim();
        if (_hud is not null)
            _hud.Text = HudText();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!ReadDeviceInput || Input.MouseMode != Input.MouseModeEnum.Captured)
            return;
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                Place();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                Remove();
                break;
            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Q }:
                Cycle(-1);
                break;
            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.E }:
                Cycle(1);
                break;
            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.R }:
                Turn();
                break;
            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.T }:
                Tip();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

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

    /// <summary>Tips the part a quarter turn over, for wedges and other parts with a top.</summary>
    public void Tip() => _adjust = BuildAim.Tip(_adjust);

    /// <summary>Asks the host to place the selected part where the preview is. False when there is nothing to aim at.</summary>
    public bool Place(Action<ActionResult>? done = null)
    {
        if (Aim is not { } aim || Selected is null)
            return false;
        LastResult = null;
        Actions.Request(BuildActions.PlacePart,
            new PlacePartArgs(Selected, aim.Cell, aim.Rotation, aim.Creation?.Id, aim.Position),
            result => Answered(result, done));
        return true;
    }

    /// <summary>Asks the host to remove the part under the crosshair. False when no part is aimed at.</summary>
    public bool Remove(Action<ActionResult>? done = null)
    {
        if (Aim is not { Creation: { } creation, HitPart: { } part })
            return false;
        LastResult = null;
        Actions.Request(BuildActions.RemovePart, new RemovePartArgs(creation.Id, part), result => Answered(result, done));
        return true;
    }

    /// <summary>Casts the aim ray now and moves the preview.</summary>
    public void UpdateAim()
    {
        Aim = Selected is { } selected && Build.Catalog.FindDefinition(selected) is { } definition
            ? Cast(definition)
            : null;
        _preview.Visible = Aim is not null;
        if (Aim is null)
            return;
        if (_previewPart != Selected)
            BuildPreview(Selected!, Build.Catalog.FindDefinition(Selected!)!);
        _preview.GlobalTransform = Aim.Preview;
    }

    private Target? Cast(PartDefinition definition)
    {
        var camera = Player.Camera.GlobalTransform;
        var from = camera.Origin;
        var to = from - camera.Basis.Z * Build.Settings!.Reach;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = [Player.GetRid()];
        var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return null;
        var point = hit["position"].AsVector3();
        var normal = hit["normal"].AsVector3();

        if (hit["collider"].AsGodotObject() is CreationBody body && body.GetParent() is CreationNode node)
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

    private void Answered(ActionResult result, Action<ActionResult>? done)
    {
        LastResult = result;
        if (!result.Ok && ReadDeviceInput)
            GD.Print($"[build] {result.Message}");
        done?.Invoke(result);
    }

    private string HudText()
    {
        if (Selected is null)
            return "Hammer: no placeable parts are loaded";
        var name = Build.Catalog.FindDefinition(Selected)?.Name ?? Selected;
        var status = LastResult is { Ok: false } failed ? $"\n{failed.Message}" : "";
        return $"Hammer: {name}   [Q/E] part  [R] turn  [T] tip  [LMB] place  [RMB] remove{status}";
    }
}
