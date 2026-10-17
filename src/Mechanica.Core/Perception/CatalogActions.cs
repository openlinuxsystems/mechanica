using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Quests;

namespace Mechanica.Core.Perception;

/// <summary>The loaded content the catalog queries read. It follows mod reloads, so the game hands in its current books.</summary>
public interface IGameCatalog
{
    /// <summary>Loaded mods and their parts and items.</summary>
    ModLoadResult Mods { get; }

    /// <summary>Crafting recipes.</summary>
    RecipeBook Recipes { get; }

    /// <summary>Bot types.</summary>
    BotBook Bots { get; }

    /// <summary>Quests.</summary>
    QuestBook Quests { get; }

    /// <summary>Player characters.</summary>
    CharacterBook Characters { get; }
}

/// <summary>Arguments of <c>ListParts</c> and <c>ListItems</c>. Every filter is optional.</summary>
/// <param name="Category">Only this category.</param>
/// <param name="Mod">Only parts whose qualified id is in this mod.</param>
/// <param name="Search">Only ids or names that contain this text (any case).</param>
public sealed record ListPartsArgs(PartCategory? Category = null, string? Mod = null, string? Search = null);

/// <summary>One part or item in <c>ListParts</c> and <c>ListItems</c>.</summary>
/// <param name="Id">Qualified id (<c>mod_id:part_id</c>), as every action takes it.</param>
/// <param name="Name">Display name.</param>
/// <param name="Category">What kind of thing it is.</param>
/// <param name="Placeable">True for a part that can be built; false for an inventory-only item.</param>
/// <param name="StackSize">Most per inventory slot.</param>
/// <param name="Mass">Mass in kg.</param>
/// <param name="Size">Footprint in 0.25 m grid cells, for placeable parts.</param>
/// <param name="Durability">Durability rating (part health derives from it).</param>
/// <param name="Settings">Ids of the settings <c>ConfigurePart</c> can change; <c>GetPart</c> has their schemas.</param>
/// <param name="Description">Tooltip text.</param>
public sealed record PartSummary(
    string Id, string Name, PartCategory? Category, bool Placeable, int StackSize, float? Mass, GridSize? Size, int? Durability,
    IReadOnlyList<string> Settings, string? Description);

/// <summary>Arguments of <c>GetPart</c>.</summary>
/// <param name="Part">Qualified id (a bare id means vanilla).</param>
public sealed record GetPartArgs(string Part);

/// <summary>Value of <c>GetPart</c>.</summary>
/// <param name="Id">Qualified id.</param>
/// <param name="Mod">The mod whose definition is in use (another mod's for an override).</param>
/// <param name="Placeable">True for a buildable part.</param>
/// <param name="Definition">
/// The definition as loaded, in the part file's own schema (<c>docs/part-data-schema.md</c>): sockets, joints,
/// settings with their types, ranges, options and defaults (what <c>ConfigurePart</c> accepts), inventory, crafting,
/// seat, upgrades and properties.
/// </param>
/// <param name="MadeBy">Recipes that make it.</param>
/// <param name="UsedIn">Recipes that use it up.</param>
/// <param name="Crafts">Recipes a station part runs.</param>
public sealed record PartInfo(
    string Id, string Mod, bool Placeable, JsonElement Definition, IReadOnlyList<string> MadeBy, IReadOnlyList<string> UsedIn, IReadOnlyList<string> Crafts);

/// <summary>Arguments of <c>ListRecipes</c>. Every filter is optional.</summary>
/// <param name="Station">Only recipes this station part runs.</param>
/// <param name="Makes">Only recipes that make this item.</param>
/// <param name="Uses">Only recipes that use up this item.</param>
public sealed record ListRecipesArgs(string? Station = null, string? Makes = null, string? Uses = null);

/// <summary>One recipe in <c>ListRecipes</c>.</summary>
/// <param name="Id">Qualified id, as <c>Craft</c> takes it.</param>
/// <param name="Name">Display name.</param>
/// <param name="Stations">Station parts that run it.</param>
/// <param name="Inputs">Items used up per craft.</param>
/// <param name="Outputs">Items made per craft.</param>
/// <param name="Time">Seconds per craft at speed 1.</param>
public sealed record RecipeView(string Id, string Name, IReadOnlyList<string> Stations, IReadOnlyList<ItemAmount> Inputs, IReadOnlyList<ItemAmount> Outputs, float Time);

/// <summary>One bot type in <c>ListBotTypes</c>.</summary>
/// <param name="Id">Qualified id, as <c>SpawnBot</c> takes it.</param>
/// <param name="Mod">The mod that defines it.</param>
/// <param name="Name">Display name.</param>
/// <param name="Health">Full health.</param>
/// <param name="Speed">Top speed on land, m/s.</param>
/// <param name="WaterSpeed">Top speed in water, m/s.</param>
/// <param name="Sight">How far it notices things, m.</param>
/// <param name="Radius">Body radius, m.</param>
/// <param name="Height">Body height, m.</param>
/// <param name="MeleeImmune">Melee hits do nothing.</param>
/// <param name="Passive">Never raids, never attacks first.</param>
/// <param name="RaidCost">Cost in a raid's budget, or null when it never raids.</param>
/// <param name="Attacks">Its attacks.</param>
/// <param name="Drops">What it may drop.</param>
/// <param name="Script">Qualified Lua script, or null.</param>
/// <param name="Behavior">Its behaviour tree as written, or null.</param>
public sealed record BotTypeView(
    string Id, string Mod, string Name, float Health, float Speed, float WaterSpeed, float Sight, float Radius, float Height,
    bool MeleeImmune, bool Passive, int? RaidCost, IReadOnlyList<BotAttack> Attacks, IReadOnlyList<BotDrop> Drops, string? Script,
    JsonElement? Behavior);

/// <summary>One quest in <c>GetQuestBook</c>.</summary>
/// <param name="Id">Qualified id, as <c>ChooseQuestOption</c> takes it.</param>
/// <param name="Name">Display name.</param>
/// <param name="Summary">Quest log line.</param>
/// <param name="Requires">Quests that must be done first.</param>
/// <param name="Steps">Its steps in order: objective, target, count, dialogue and the choice ids of a choose step.</param>
/// <param name="Items">Item rewards.</param>
/// <param name="Reveal">POIs revealed on completion.</param>
/// <param name="Flags">Story flags set on completion.</param>
/// <param name="Outro">Lines said on completion.</param>
public sealed record QuestView(
    string Id, string Name, string Summary, IReadOnlyList<string> Requires, IReadOnlyList<QuestStepInfo> Steps, IReadOnlyList<ItemAmount> Items,
    IReadOnlyList<string> Reveal, IReadOnlyDictionary<string, string> Flags, IReadOnlyList<DialogueLine> Outro);

/// <summary>One player character in <c>ListCharacters</c>.</summary>
/// <param name="Id">Qualified id, as <c>ChooseCharacter</c> takes it.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">One line about it.</param>
public sealed record CharacterView(string Id, string Name, string Description);

/// <summary>
/// Read-only catalog queries (AI support PR 3), so an agent never has to guess an id:
/// <c>ListParts</c> (buildable parts), <c>ListItems</c> (everything that fits in an inventory), <c>GetPart</c> (one
/// definition with its setting schemas and recipes), <c>ListRecipes</c>, <c>ListBotTypes</c>, <c>GetQuestBook</c>
/// (text, objectives, dialogue and choice ids) and <c>ListCharacters</c>. They read the content loaded now, so they
/// follow mod reloads.
/// </summary>
public sealed class CatalogActions(IGameCatalog catalog)
{
    /// <summary>Action name: buildable parts.</summary>
    public const string ListParts = "ListParts";

    /// <summary>Action name: every item.</summary>
    public const string ListItems = "ListItems";

    /// <summary>Action name: one part or item.</summary>
    public const string GetPart = "GetPart";

    /// <summary>Action name: recipes.</summary>
    public const string ListRecipes = "ListRecipes";

    /// <summary>Action name: bot types.</summary>
    public const string ListBotTypes = "ListBotTypes";

    /// <summary>Action name: quests.</summary>
    public const string GetQuestBook = "GetQuestBook";

    /// <summary>Action name: player characters.</summary>
    public const string ListCharacters = "ListCharacters";

    /// <summary>Registers all seven queries.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<ListPartsArgs>(ListParts, (_, _) => null, (_, a) => Value(Parts(a, placeableOnly: true)), readOnly: true);
        dispatcher.Register<ListPartsArgs>(ListItems, (_, _) => null, (_, a) => Value(Parts(a, placeableOnly: false)), readOnly: true);
        dispatcher.Register<GetPartArgs>(GetPart, (_, a) => catalog.Mods.FindPart(a.Part) is null ? $"There is no part or item '{a.Part}'." : null,
            (_, a) => Value(Describe(a.Part)), readOnly: true);
        dispatcher.Register<ListRecipesArgs>(ListRecipes, (_, _) => null, (_, a) => Value(Recipes(a)), readOnly: true);
        dispatcher.Register<NoArgs>(ListBotTypes, (_, _) => null, (_, _) => Value(BotTypes()), readOnly: true);
        dispatcher.Register<NoArgs>(GetQuestBook, (_, _) => null, (_, _) => Value(Quests()), readOnly: true);
        dispatcher.Register<NoArgs>(ListCharacters, (_, _) => null,
            (_, _) => Value(catalog.Characters.All.Select(c => new CharacterView(c.QualifiedId, c.Name, c.Description)).ToList()), readOnly: true);
    }

    /// <summary>Parts (or every item), ordered by id, as <c>ListParts</c> and <c>ListItems</c> return them.</summary>
    public IReadOnlyList<PartSummary> Parts(ListPartsArgs args, bool placeableOnly) => [.. catalog.Mods.Parts.Values
        .Where(p => !placeableOnly || p.Definition.IsPlaceable)
        .Where(p => args.Category is not { } category || p.Definition.Category == category)
        .Where(p => args.Mod is not { } mod || p.QualifiedId.StartsWith(mod + ":", StringComparison.Ordinal))
        .Where(p => args.Search is not { } text || p.QualifiedId.Contains(text, StringComparison.OrdinalIgnoreCase)
            || (p.Definition.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false))
        .OrderBy(p => p.QualifiedId, StringComparer.Ordinal)
        .Select(p => Summarize(p.QualifiedId, p.Definition))];

    private static PartSummary Summarize(string id, PartDefinition d) => new(id, d.Name ?? id, d.Category, d.IsPlaceable, d.StackSize ?? 1, d.Mass,
        d.IsPlaceable ? d.Size : null, d.Durability, [.. (d.Settings ?? []).Select(s => s.Id!)], d.Description);

    /// <summary>One part or item, as <c>GetPart</c> returns it.</summary>
    public PartInfo Describe(string id)
    {
        var part = catalog.Mods.FindPart(id)!;
        var qualified = part.QualifiedId;
        using var definition = JsonDocument.Parse(DataJson.Write(part.Definition));
        var recipes = catalog.Recipes.All;
        return new PartInfo(qualified, part.ModId, part.Definition.IsPlaceable, definition.RootElement.Clone(),
            [.. recipes.Where(r => r.Outputs.Any(o => o.Item == qualified)).Select(r => r.QualifiedId)],
            [.. recipes.Where(r => r.Inputs.Any(i => i.Item == qualified)).Select(r => r.QualifiedId)],
            [.. recipes.Where(r => r.RunsAt(qualified)).Select(r => r.QualifiedId)]);
    }

    /// <summary>Recipes, in load order, as <c>ListRecipes</c> returns them.</summary>
    public IReadOnlyList<RecipeView> Recipes(ListRecipesArgs args) => [.. catalog.Recipes.All
        .Where(r => args.Station is not { } station || r.RunsAt(station))
        .Where(r => args.Makes is not { } item || r.Outputs.Any(o => o.Item == Identifiers.Qualify(item)))
        .Where(r => args.Uses is not { } used || r.Inputs.Any(i => i.Item == Identifiers.Qualify(used)))
        .Select(r => new RecipeView(r.QualifiedId, r.Name, r.Stations, r.Inputs, r.Outputs, r.Time))];

    /// <summary>Bot types, in load order, as <c>ListBotTypes</c> returns them.</summary>
    public IReadOnlyList<BotTypeView> BotTypes() => [.. catalog.Bots.All.Select(b => new BotTypeView(
        b.QualifiedId, b.ModId, b.Name, b.Health, b.Speed, b.WaterSpeed, b.Sight, b.Radius, b.Height, b.MeleeImmune, b.Passive, b.RaidCost,
        b.Attacks, b.Drops, b.Script, b.Behavior is { } tree ? JsonSerializer.SerializeToElement(tree) : null))];

    /// <summary>Quests, in load order, as <c>GetQuestBook</c> returns them.</summary>
    public IReadOnlyList<QuestView> Quests() => [.. catalog.Quests.All.Select(q => new QuestView(
        q.QualifiedId, q.Name, q.Summary, q.Requires, q.Steps, q.Items, q.Reveal, q.Flags, q.Outro))];

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);
}
