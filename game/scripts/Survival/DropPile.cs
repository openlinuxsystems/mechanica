using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Game.Creations;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Survival;

/// <summary>
/// Shows a pile of items lying on the ground (<see cref="ItemDrop"/>): the knockout bag when a knocked-out player
/// dropped it, the item itself when it is one item with a model of its own (a bucket put down), otherwise a small
/// crate in the first item's colour, with a label of what it holds. It has no collider; players pick it up by
/// standing near it. A tossed pile (<see cref="ItemDrop.TossedFrom"/>) first flies from the player's hand in a short
/// tumbling arc to where it lies.
/// </summary>
public partial class DropPile : Node3D
{
    /// <summary>Seconds a tossed pile takes to fly from the hand to the ground.</summary>
    public const double TossSeconds = 0.45;

    private const float Size = 0.3f;
    // A single item's own model smaller than this across (a seed packet) is scaled up to it, so it can be seen and
    // picked up in the grass.
    private const float ItemSize = 0.3f;
    private const float LabelGap = 0.12f;
    // How far above the higher end the arc's middle rises, in metres.
    private const float TossLift = 0.5f;

    private MeshInstance3D _box = null!;
    private Label3D _label = null!;
    private Node3D? _model;
    private string? _modelKey;
    private Tween? _toss;

    /// <summary>The pile shown.</summary>
    public ItemDrop? Drop { get; private set; }

    /// <summary>What the label reads.</summary>
    public string Text => _label.Text;

    /// <summary>The model standing in place of the crate (the knockout bag or the item's own), if any.</summary>
    public Node3D? Model => _model;

    /// <summary>True while a tossed pile is still flying to the ground.</summary>
    public bool Flying => _toss?.IsRunning() == true;

    public override void _Ready()
    {
        _box = new MeshInstance3D { Name = "Box", Mesh = new BoxMesh { Size = new Vector3(Size, Size, Size) }, Position = new Vector3(0, Size / 2, 0) };
        AddChild(_box);
        _label = new Label3D
        {
            // Names are the mods' own text, in a mod's translation when it has one.
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
            Name = "Label",
            Position = new Vector3(0, Size + LabelGap, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            // A long list grows upwards, clear of the pile.
            VerticalAlignment = VerticalAlignment.Bottom,
            PixelSize = 0.004f,
            FontSize = 32,
            OutlineSize = 8,
        };
        AddChild(_label);
    }

    /// <summary>
    /// Shows <paramref name="drop"/>, naming its items from <paramref name="catalog"/>. With <paramref name="assets"/>, a
    /// knocked-out player's pile is drawn as <paramref name="knockoutBag"/> (a <c>mod:path</c> model) and a single
    /// item that has a mesh as itself.
    /// </summary>
    public void Show(ItemDrop drop, IPartCatalog catalog, PartAssets? assets = null, string? knockoutBag = null)
    {
        if (drop.KnockedOut && knockoutBag is not null)
            ShowModel("bag:" + knockoutBag, () => assets?.InstantiateScene(knockoutBag, Identifiers.VanillaModId));
        else
            ShowItem(drop.Items is [{ Count: 1 } only] ? only.Item : null, catalog, assets);
        var landing = drop.Position.ToGodot();
        if (drop.TossedFrom is { } from && Drop is null)
            Toss(from.ToGodot(), landing);
        else if (!Flying)
            Position = landing;
        Drop = drop;
        _label.Text = string.Join("\n", drop.Items.Select(s => $"{Loc.Mod(catalog.FindDefinition(s.Item)?.Name ?? s.Item)} x{s.Count}"));
        var color = drop.Items.Select(s => catalog.FindDefinition(s.Item)?.Visual?.Color).FirstOrDefault(c => c is not null);
        _box.MaterialOverride = new StandardMaterial3D { AlbedoColor = color is null ? new Color(0.6f, 0.5f, 0.35f) : Color.FromHtml(color) };
    }

    // Flies the pile from `from` to `to` along an arc that rises TossLift over the higher end, tumbling one turn, with
    // the label hidden until it lands.
    private void Toss(Vector3 from, Vector3 to)
    {
        var top = Mathf.Max(from.Y, to.Y) + TossLift;
        var middle = new Vector3((from.X + to.X) / 2, 2 * top - (from.Y + to.Y) / 2, (from.Z + to.Z) / 2);
        var spin = new Vector3(to.X - from.X, 0, to.Z - from.Z).Cross(Vector3.Up).Normalized();
        Position = from;
        _label.Visible = false;
        _toss?.Kill();
        _toss = CreateTween();
        _toss.TweenMethod(Callable.From<float>(t =>
        {
            // A quadratic Bézier through the middle point peaks at `top` halfway.
            Position = (1 - t) * (1 - t) * from + 2 * (1 - t) * t * middle + t * t * to;
            Basis = spin.IsZeroApprox() ? Basis.Identity : new Basis(spin, -Mathf.Tau * t);
        }), 0f, 1f, TossSeconds);
        _toss.TweenCallback(Callable.From(() =>
        {
            Position = Drop?.Position.ToGodot() ?? to;
            Basis = Basis.Identity;
            _label.Visible = true;
        }));
    }

    // The item's own model standing on the ground in place of the crate, or the crate when it has none.
    private void ShowItem(string? item, IPartCatalog catalog, PartAssets? assets) => ShowModel(item, minSize: ItemSize, make: () =>
    {
        if (item is null || assets is null || catalog.FindDefinition(item) is not { Mesh: not null } definition
            || assets.ModelFor(Identifiers.Qualify(item), definition) is not { IsPlaceholder: false, Meshes.Count: > 0 } model)
            return null;
        var node = new Node3D();
        foreach (var mesh in model.Meshes)
            node.AddChild(mesh.Instantiate());
        return node;
    });

    // Puts the model `make` builds (once per key) on the ground in place of the crate, or the crate when it makes none.
    private void ShowModel(string? key, Func<Node3D?> make, float minSize = 0f)
    {
        if (key == _modelKey)
            return;
        _modelKey = key;
        _model?.QueueFree();
        _model = key is null ? null : make();
        if (_model is not null)
        {
            _model.Name = "Model";
            AddChild(_model);
            // Stand it on the ground, whatever its origin.
            var bounds = _model.FindChildren("*", nameof(MeshInstance3D), owned: false).OfType<MeshInstance3D>()
                .Select(m => RelativeTo(m, _model) * m.GetAabb()).Aggregate((a, b) => a.Merge(b));
            var across = bounds.Size[(int)bounds.GetLongestAxisIndex()];
            if (across > 0f && across < minSize)
            {
                var scale = minSize / across;
                _model.Scale = Vector3.One * scale;
                bounds = new Aabb(bounds.Position * scale, bounds.Size * scale);
            }
            _model.Position = new Vector3(-bounds.GetCenter().X, -bounds.Position.Y, -bounds.GetCenter().Z);
            _label.Position = new Vector3(0, bounds.Size.Y + LabelGap, 0);
        }
        else
            _label.Position = new Vector3(0, Size + LabelGap, 0);
        _box.Visible = _model is null;
    }

    // A node's transform in the space of one of its ancestors.
    private static Transform3D RelativeTo(Node3D node, Node3D ancestor)
    {
        var transform = Transform3D.Identity;
        for (Node? n = node; n is Node3D n3 && n != ancestor; n = n.GetParent())
            transform = n3.Transform * transform;
        return transform;
    }
}
