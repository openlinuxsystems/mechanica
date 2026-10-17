using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;

namespace Mechanica.Game.Survival;

/// <summary>
/// Shows a pile of items lying on the ground (<see cref="ItemDrop"/>): a small crate in the first item's
/// colour with a label of what it holds. It has no collider; players pick it up by standing near it.
/// </summary>
public partial class DropPile : Node3D
{
    private const float Size = 0.3f;

    private MeshInstance3D _box = null!;
    private Label3D _label = null!;

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
            Name = "Label",
            Position = new Vector3(0, Size + 0.25f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.004f,
            FontSize = 32,
            OutlineSize = 8,
        };
        AddChild(_label);
    }

    /// <summary>Shows <paramref name="drop"/>, naming its items from <paramref name="catalog"/>.</summary>
    public void Show(ItemDrop drop, IPartCatalog catalog)
    {
        Drop = drop;
        Position = drop.Position.ToGodot();
        _label.Text = string.Join("\n", drop.Items.Select(s => $"{catalog.FindDefinition(s.Item)?.Name ?? s.Item} x{s.Count}"));
        var color = drop.Items.Select(s => catalog.FindDefinition(s.Item)?.Visual?.Color).FirstOrDefault(c => c is not null);
        _box.MaterialOverride = new StandardMaterial3D { AlbedoColor = color is null ? new Color(0.6f, 0.5f, 0.35f) : Color.FromHtml(color) };
    }
}
