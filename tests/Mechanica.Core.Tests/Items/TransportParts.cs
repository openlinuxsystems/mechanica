using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Items;

/// <summary>
/// Item transport parts for tests (parts map E1), every one 1 x 1 x 1: a Vacuum Pipe and corner, chests whose port takes
/// in, sends out or both, a pump with an intake, a station with an input and an output port, and a block. Shared with the
/// scripting tests.
/// </summary>
internal static class TransportParts
{
    internal const string Pipe = "test:pipe";
    internal const string Corner = "test:corner";
    internal const string Chest = "test:chest";
    internal const string InChest = "test:in_chest";
    internal const string OutChest = "test:out_chest";
    internal const string Pump = "test:pump";
    internal const string Station = "test:station";
    internal const string Block = "test:block";

    // Local Z turned to world +X (so a pipe's ends face along X).
    internal static readonly GridRotation AlongX = GridRotation.FromAxes(new GridCell(0, 0, -1), new GridCell(0, 1, 0));

    /// <summary>The transport parts used here, every one 1 x 1 x 1.</summary>
    internal static PartCatalog Catalog(PartCatalog? into = null)
    {
        var catalog = into ?? new PartCatalog();
        static ItemPortDefinition Port(string id, float x, float y, float z, ItemPortMode mode = ItemPortMode.Both) =>
            new() { Id = id, Position = new Vec3(x * 0.125f, y * 0.125f, z * 0.125f), Normal = new Vec3(x, y, z), Mode = mode };
        return catalog
            .Add(Pipe, Placeable("pipe") with { Conduit = true, IoPorts = [Port("back", 0, 0, -1), Port("front", 0, 0, 1)] })
            .Add(Corner, Placeable("corner") with { Conduit = true, IoPorts = [Port("back", 0, 0, -1), Port("side", 1, 0, 0)] })
            .Add(Chest, Placeable("chest") with { Inventory = new InventoryDefinition { Slots = 4 }, IoPorts = [Port("port", 0, 0, 1)] })
            .Add(InChest, Placeable("in_chest") with { Inventory = new InventoryDefinition { Slots = 4 }, IoPorts = [Port("port", 0, 0, 1, ItemPortMode.In)] })
            .Add(OutChest, Placeable("out_chest") with { Inventory = new InventoryDefinition { Slots = 4 }, IoPorts = [Port("port", 0, 0, 1, ItemPortMode.Out)] })
            .Add(Pump, Placeable("pump") with
            {
                IoPorts = [Port("port", 0, 0, -1)],
                Intake = new IntakeDefinition { Position = new Vec3(0, 0, 0.125f), Normal = new Vec3(0, 0, 1), Reach = 2, Throw = 1 },
            })
            .Add(Station, Placeable("station") with
            {
                Inventory = new InventoryDefinition { Slots = 2 },
                Crafting = new CraftingDefinition(),
                IoPorts = [Port("input", -1, 0, 0, ItemPortMode.In), Port("output", 1, 0, 0, ItemPortMode.Out)],
            })
            .Add(Block, Placeable("block"));
    }

    internal static PartDefinition Placeable(string id) => new()
    {
        Id = id,
        Name = char.ToUpperInvariant(id[0]) + id[1..],
        Category = PartCategory.Interactive,
        Mass = 4.5f,
        Durability = 3,
        Flammable = false,
        Size = GridSize.One,
    };
}
