using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Game.Creations;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Survival;

/// <summary>
/// Shows a pile of items lying on the ground (<see cref="ItemDrop"/>). A knocked-out player's pile is the knockout bag,
/// standing on the ground with a label of what it holds. Any other pile is one kind of item (the host lays one pile per
/// kind), shown as in Scrap Mechanic: the item's own model (<see cref="DropModels"/>; a block as the block), sized
/// <see cref="ItemSize"/> across, floating and slowly turning inside a glowing sphere with a beam of light rising
/// from it (<see cref="DropGlow"/>); the pick-up prompt names it. It has no collider; players pick it up by aiming at
/// the sphere or standing near it. A tossed pile (<see cref="ItemDrop.TossedFrom"/>) first flies from the player's hand
/// in a short tumbling arc to where it lies.
/// </summary>
public partial class DropPile : Node3D
{
    /// <summary>Seconds a tossed pile takes to fly from the hand to the ground.</summary>
    public const double TossSeconds = 0.45;

    /// <summary>How far across a loose item's model is drawn, in metres, whatever its own size (a seed packet, a chest).</summary>
    public const float ItemSize = 0.3f;

    private const float Size = 0.3f;
    private const float LabelGap = 0.12f;
    // How far above the higher end the arc's middle rises, in metres.
    private const float TossLift = 0.5f;
    // A loose item turns this fast (radians a second) and bobs this far up and down (metres) once every BobSeconds.
    private const float Spin = 0.9f;
    private const float Bob = 0.025f;
    private const float BobSeconds = 2.4f;

    private MeshInstance3D _box = null!;
    private Label3D _label = null!;
    private Node3D? _model;
    private string? _modelKey;
    // The model's middle and scale, so it can be turned about its middle.
    private Vector3 _centre;
    private float _scale = 1f;
    private bool _floating;
    private double _clock;
    private Node3D _glow = null!;
    private Tween? _toss;

    /// <summary>The pile shown.</summary>
    public ItemDrop? Drop { get; private set; }

    /// <summary>What the label reads (shown over a knockout bag only; a loose item's prompt names it).</summary>
    public string Text => _label.Text;

    /// <summary>The model standing in place of the crate (the knockout bag or the item's own), if any.</summary>
    public Node3D? Model => _model;

    /// <summary>The glowing sphere and beam round a loose item (hidden for a knockout bag).</summary>
    public Node3D Glow => _glow;

    /// <summary>True while a tossed pile is still flying to the ground.</summary>
    public bool Flying => _toss?.IsRunning() == true;

    /// <summary>A new, posed copy of a tool's model, or null when it has none (<see cref="ToolIconModels"/>).</summary>
    public Func<string, Node3D?>? ToolModel { get; init; }

    public override void _Ready()
    {
        _box = new MeshInstance3D { Name = "Box", Mesh = new BoxMesh { Size = new Vector3(Size, Size, Size) }, Position = new Vector3(0, Size / 2, 0) };
        AddChild(_box);
        _glow = new Node3D { Name = "Aura", Visible = false };
        var sphere = DropGlow.Sphere();
        sphere.Position = new Vector3(0, DropAim.Middle, 0);
        _glow.AddChild(sphere);
        _glow.AddChild(DropGlow.Beam());
        AddChild(_glow);
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
            Visible = false,
        };
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (!_floating || _model is null)
            return;
        _clock += delta;
        Pose();
    }

    /// <summary>
    /// Shows <paramref name="drop"/>, naming its items from <paramref name="catalog"/>. With <paramref name="assets"/>, a
    /// knocked-out player's pile is drawn as <paramref name="knockoutBag"/> (a <c>mod:path</c> model) and any other as
    /// its item.
    /// </summary>
    public void Show(ItemDrop drop, IPartCatalog catalog, PartAssets? assets = null, string? knockoutBag = null)
    {
        var bag = drop.KnockedOut && knockoutBag is not null;
        if (bag)
            ShowModel("bag:" + knockoutBag, floating: false, () => assets?.InstantiateScene(knockoutBag!, Identifiers.VanillaModId));
        else
        {
            var item = drop.Items.FirstOrDefault()?.Item;
            ShowModel(item, floating: true, () => item is null ? null : DropModels.Build(item, catalog, assets, ToolModel));
        }
        var landing = drop.Position.ToGodot();
        if (Drop is null)
            // Neighbouring items turn out of step.
            _clock = drop.Id * 0.77;
        if (drop.TossedFrom is { } from && Drop is null)
            Toss(from.ToGodot(), landing);
        else if (!Flying)
            Position = landing;
        Drop = drop;
        _label.Text = string.Join("\n", drop.Items.Select(s => $"{Loc.Mod(catalog.FindDefinition(s.Item)?.Name ?? s.Item)} x{s.Count}"));
        _label.Visible = drop.KnockedOut && !Flying;
        _glow.Visible = !drop.KnockedOut;
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
            _label.Visible = Drop?.KnockedOut == true;
        }));
    }

    // Puts the model `make` builds (once per key) in place of the crate, or the crate when it makes none: a loose item
    // (`floating`) sized ItemSize across and floating in the glow, the knockout bag standing on the ground as it is.
    private void ShowModel(string? key, bool floating, Func<Node3D?> make)
    {
        if (key == _modelKey)
            return;
        _modelKey = key;
        _model?.QueueFree();
        _model = key is null ? null : make();
        _floating = floating;
        _box.Visible = _model is null;
        _box.Position = new Vector3(0, floating ? DropAim.Middle : Size / 2, 0);
        _label.Position = new Vector3(0, Size + LabelGap, 0);
        if (_model is null)
            return;
        _model.Name = "Model";
        AddChild(_model);
        var bounds = _model.FindChildren("*", nameof(MeshInstance3D), owned: false).OfType<MeshInstance3D>()
            .Select(m => RelativeTo(m, _model) * m.GetAabb()).Aggregate((a, b) => a.Merge(b));
        _centre = bounds.GetCenter();
        if (floating)
        {
            var across = bounds.Size[(int)bounds.GetLongestAxisIndex()];
            _scale = across > 0f ? ItemSize / across : 1f;
            Pose();
            return;
        }
        // Stand it on the ground, whatever its origin.
        _model.Position = new Vector3(-_centre.X, -bounds.Position.Y, -_centre.Z);
        _label.Position = new Vector3(0, bounds.Size.Y + LabelGap, 0);
    }

    // A loose item's model, turned about its middle and bobbing at the middle of the glow.
    private void Pose()
    {
        var turn = new Basis(Vector3.Up, (float)(_clock * Spin)).Scaled(Vector3.One * _scale);
        var lift = DropAim.Middle + Bob * Mathf.Sin((float)(_clock * Mathf.Tau / BobSeconds));
        _model!.Transform = new Transform3D(turn, new Vector3(0, lift, 0) - turn * _centre);
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
