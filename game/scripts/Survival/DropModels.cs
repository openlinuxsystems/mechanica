using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Survival;

/// <summary>How an item is drawn lying on the ground (<see cref="DropModels.Resolve"/>).</summary>
public enum DropLook
{
    /// <summary>No item by that id: drawn as a plain crate.</summary>
    None,

    /// <summary>The item's own model (its <c>mesh</c>; a block's is the block with its texture).</summary>
    Model,

    /// <summary>A tool's hand prop, as its inventory picture shows it (<see cref="Ui.ToolIconModels"/>).</summary>
    Tool,

    /// <summary>The item has no model: a box of its size in its colour.</summary>
    Placeholder,
}

/// <summary>
/// The models items show as when they lie on the ground (<see cref="DropPile"/>): the item's own model, a block as the
/// block itself, a tool as its hand prop. Every vanilla item resolves to one of the first two (game/tests/drops).
/// </summary>
public static class DropModels
{
    /// <summary>How <paramref name="item"/> is drawn on the ground.</summary>
    public static DropLook Resolve(string item, IPartCatalog catalog, PartAssets? assets, Func<string, Node3D?>? toolModel)
    {
        if (HotbarTools.IsTool(item) && toolModel is not null)
            return DropLook.Tool;
        var qualified = Identifiers.Qualify(item);
        if (catalog.FindDefinition(qualified) is not { } definition)
            return DropLook.None;
        return assets?.ModelFor(qualified, definition) is { IsPlaceholder: false, Meshes.Count: > 0 } ? DropLook.Model : DropLook.Placeholder;
    }

    /// <summary>A new copy of <paramref name="item"/>'s model as it lies on the ground, or null when it has none of its own.</summary>
    public static Node3D? Build(string item, IPartCatalog catalog, PartAssets? assets, Func<string, Node3D?>? toolModel)
    {
        switch (Resolve(item, catalog, assets, toolModel))
        {
            case DropLook.Tool:
                return toolModel!(Identifiers.Qualify(item));
            case DropLook.Model:
                var qualified = Identifiers.Qualify(item);
                var node = new Node3D();
                foreach (var mesh in assets!.ModelFor(qualified, catalog.FindDefinition(qualified)!).Meshes)
                    node.AddChild(mesh.Instantiate());
                return node;
            default:
                return null;
        }
    }
}
