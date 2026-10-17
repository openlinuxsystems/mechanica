using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Items;

/// <summary>Items, containers and stations for inventory tests, built in code.</summary>
internal static class ItemParts
{
    public const string Scrap = "test:scrap";
    public const string Wood = "test:wood";
    public const string Kit = "test:kit";
    public const string Fuel = "test:fuel";
    public const string Key = "test:key";
    public const string Chest = "test:chest";
    public const string Tank = "test:tank";
    public const string Bench = "test:bench";
    public const string Block = "test:block";

    public static PartCatalog Catalog() => new PartCatalog()
        .Add(Scrap, Item("scrap", "Scrap", 50))
        .Add(Wood, Item("wood", "Wood", 50))
        .Add(Kit, Item("kit", "Kit", 20))
        .Add(Fuel, Item("fuel", "Fuel", 20))
        .Add(Key, Item("key", "Key", null))
        .Add(Block, Placeable("block", "Block", PartCategory.Block, null, null))
        .Add(Chest, Placeable("chest", "Chest", PartCategory.Container, new InventoryDefinition { Slots = 4 }, null))
        .Add(Tank, Placeable("tank", "Tank", PartCategory.Container, new InventoryDefinition { Slots = 1, Accepts = ["test:fuel"] }, null))
        .Add(Bench, Placeable("bench", "Bench", PartCategory.Station, new InventoryDefinition { Slots = 2 },
            new CraftingDefinition { Speed = 2, QueueSize = 2 }) with
        {
            Sounds = new() { ["start"] = "bench_start", ["work"] = "bench_work", ["complete"] = "bench_done" },
        });

    /// <summary>Bench recipes: 2 scrap + 1 wood make a chest in 4 s; 1 scrap makes 10 blocks in 1 s.</summary>
    public static RecipeBook Recipes(IPartCatalog catalog) => RecipeBook.From(catalog,
        ("test", new RecipeDefinition
        {
            Id = "chest",
            Stations = [Bench],
            Inputs = [new RecipeItem { Item = Scrap, Count = 2 }, new RecipeItem { Item = Wood, Count = 1 }],
            Outputs = [new RecipeItem { Item = Chest, Count = 1 }],
            Time = 4,
        }),
        ("test", new RecipeDefinition
        {
            Id = "blocks",
            Stations = [Bench],
            Inputs = [new RecipeItem { Item = Scrap, Count = 1 }],
            Outputs = [new RecipeItem { Item = Block, Count = 10 }],
            Time = 1,
        }));

    private static PartDefinition Item(string id, string name, int? stack) =>
        new() { Id = id, Name = name, Category = PartCategory.Resource, StackSize = stack };

    private static PartDefinition Placeable(string id, string name, PartCategory category, InventoryDefinition? inventory, CraftingDefinition? crafting) => new()
    {
        Id = id,
        Name = name,
        Category = category,
        Mass = 5,
        Durability = 3,
        Flammable = false,
        Size = GridSize.One,
        StackSize = 10,
        Inventory = inventory,
        Crafting = crafting,
    };
}
