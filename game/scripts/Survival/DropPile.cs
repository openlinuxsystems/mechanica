using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Survival;

/// <summary>
/// Shows a pile of items lying on the ground (<see cref="ItemDrop"/>): the item itself when it is one item with a
/// model of its own (a bucket put down), otherwise a small crate in the first item's colour, with a label of what it
/// holds. It has no collider; players pick it up by standing near it.
/// </summary>
public partial class DropPile : Node3D
{
    private const float Size = 0.3f;

    private MeshInstance3D _box = null!;
    private Label3D _label = null!;
    private Node3D? _model;
    private string? _modelItem;

    /// <summary>The pile shown.</summary>
    public ItemDrop? Drop { get; private set; }

    /// <summary>What the label reads.</summary>
    public string Text => _label.Text;

    public override void _Ready()
    {
        _box = new MeshInstance3D { Name = "Box", Mesh = new BoxMesh { Size = new Vector3(Size, Size, Size) }, Position = new Vector3(0, Size / 2, 0) };
        AddChild(_box);
        _label = new Label3D
        {
            // Names are the mods' own text.
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
            Name = "Label",
            Position = new Vector3(0, Size + 0.25f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.004f,
            FontSize = 32,
            OutlineSize = 8,
        };
        AddChild(_label);
    }

    /// <summary>
    /// Shows <paramref name="drop"/>, naming its items from <paramref name="catalog"/>; with <paramref name="assets"/>, a
    /// single item that has a mesh is drawn as itself.
    /// </summary>
    public void Show(ItemDrop drop, IPartCatalog catalog, PartAssets? assets = null)
    {
        Drop = drop;
        ShowModel(drop.Items is [{ Count: 1 } only] ? only.Item : null, catalog, assets);
        Position = drop.Position.ToGodot();
        _label.Text = string.Join("\n", drop.Items.Select(s => $"{catalog.FindDefinition(s.Item)?.Name ?? s.Item} x{s.Count}"));
        var color = drop.Items.Select(s => catalog.FindDefinition(s.Item)?.Visual?.Color).FirstOrDefault(c => c is not null);
        _box.MaterialOverride = new StandardMaterial3D { AlbedoColor = color is null ? new Color(0.6f, 0.5f, 0.35f) : Color.FromHtml(color) };
    }

    // The item's own model standing on the ground in place of the crate, or the crate when it has none.
    private void ShowModel(string? item, IPartCatalog catalog, PartAssets? assets)
    {
        if (item == _modelItem)
            return;
        _modelItem = item;
        _model?.QueueFree();
        _model = null;
        if (item is not null && assets is not null && catalog.FindDefinition(item) is { Mesh: not null } definition
            && assets.ModelFor(Identifiers.Qualify(item), definition) is { IsPlaceholder: false, Meshes.Count: > 0 } model)
        {
            _model = new Node3D { Name = "Model" };
            foreach (var mesh in model.Meshes)
                _model.AddChild(mesh.Instantiate());
            AddChild(_model);
            // Stand it on the ground, whatever its origin.
            var bounds = _model.GetChildren().OfType<MeshInstance3D>().Select(m => m.Transform * m.GetAabb()).Aggregate((a, b) => a.Merge(b));
            _model.Position = new Vector3(-bounds.GetCenter().X, -bounds.Position.Y, -bounds.GetCenter().Z);
            _label.Position = new Vector3(0, bounds.Size.Y + 0.25f, 0);
        }
        else
            _label.Position = new Vector3(0, Size + 0.25f, 0);
        _box.Visible = _model is null;
    }
}
