using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Perception;
using Mechanica.Core.Quests;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.Perception;

public class CatalogActionsTests
{
    private static readonly ActionCaller Agent = new(1, ActionSource.External);

    private static readonly Lazy<Content> Vanilla = new(() => new Content(ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"))));

    private readonly ActionDispatcher _dispatcher = new();

    public CatalogActionsTests() => new CatalogActions(Vanilla.Value).Register(_dispatcher);

    private JsonElement Call(string action, string args = "{}")
    {
        var result = _dispatcher.Dispatch(Agent, action, args);
        Assert.True(result.Ok, result.ToString());
        Assert.True(_dispatcher.IsReadOnly(action), action);
        return result.Value!.Value;
    }

    private T Call<T>(string action, string args = "{}") => Call(action, args).Deserialize<T>(DataJson.ReadOptions)!;

    [Fact]
    public void ListPartsHasOnlyBuildablePartsAndListItemsHasEverything()
    {
        var mods = Vanilla.Value.Mods;
        var parts = Call<List<PartSummary>>(CatalogActions.ListParts);
        var items = Call<List<PartSummary>>(CatalogActions.ListItems);

        Assert.All(parts, p => Assert.True(p.Placeable, p.Id));
        Assert.Equal(mods.Parts.Count, items.Count);
        Assert.Contains(items, i => !i.Placeable);
        Assert.Equal(items.Select(i => i.Id).Order(StringComparer.Ordinal), items.Select(i => i.Id));
        Assert.All(items, i => Assert.StartsWith("vanilla:", i.Id));
    }

    [Fact]
    public void ListPartsFilters()
    {
        var blocks = Call<List<PartSummary>>(CatalogActions.ListParts, """{ "category": "block" }""");
        Assert.NotEmpty(blocks);
        Assert.All(blocks, b => Assert.Equal(PartCategory.Block, b.Category));

        var found = Call<List<PartSummary>>(CatalogActions.ListItems, """{ "search": "SCRAP" }""");
        Assert.NotEmpty(found);
        Assert.All(found, f => Assert.True(f.Id.Contains("scrap") || f.Name.Contains("Scrap", StringComparison.OrdinalIgnoreCase), f.Id));

        Assert.Empty(Call<List<PartSummary>>(CatalogActions.ListItems, """{ "mod": "nope" }"""));
        Assert.Equal(ActionErrors.BadArgs, _dispatcher.Dispatch(Agent, CatalogActions.ListParts, """{ "category": "spaceship" }""").Code);
    }

    [Fact]
    public void GetPartGivesTheDefinitionWithSettingSchemasAndRecipes()
    {
        var mods = Vanilla.Value.Mods;
        var configurable = mods.Parts.Values.First(p => p.Definition.Settings is { Count: > 0 });

        var info = Call<PartInfo>(CatalogActions.GetPart, $$"""{ "part": "{{configurable.QualifiedId}}" }""");

        Assert.Equal((configurable.QualifiedId, configurable.ModId), (info.Id, info.Mod));
        var settings = info.Definition.GetProperty("settings");
        Assert.Equal(configurable.Definition.Settings!.Count, settings.GetArrayLength());
        Assert.Equal(configurable.Definition.Settings[0].Id, settings[0].GetProperty("id").GetString());
        Assert.True(settings[0].TryGetProperty("type", out _));

        var recipe = Vanilla.Value.Recipes.All[0];
        var made = Call<PartInfo>(CatalogActions.GetPart, $$"""{ "part": "{{recipe.Outputs[0].Item}}" }""");
        Assert.Contains(recipe.QualifiedId, made.MadeBy);
        var station = Call<PartInfo>(CatalogActions.GetPart, $$"""{ "part": "{{recipe.Stations[0]}}" }""");
        Assert.Contains(recipe.QualifiedId, station.Crafts);
        var input = Call<PartInfo>(CatalogActions.GetPart, $$"""{ "part": "{{recipe.Inputs[0].Item}}" }""");
        Assert.Contains(recipe.QualifiedId, input.UsedIn);

        Assert.Contains("no part or item", _dispatcher.Dispatch(Agent, CatalogActions.GetPart, """{ "part": "vanilla:nope" }""").Message);
    }

    [Fact]
    public void ListRecipesFiltersByStationAndItems()
    {
        var book = Vanilla.Value.Recipes;
        var all = Call<List<RecipeView>>(CatalogActions.ListRecipes);
        Assert.Equal(book.All.Select(r => r.QualifiedId), all.Select(r => r.Id));

        var recipe = book.All[0];
        var atStation = Call<List<RecipeView>>(CatalogActions.ListRecipes, $$"""{ "station": "{{recipe.Stations[0]}}" }""");
        Assert.Contains(atStation, r => r.Id == recipe.QualifiedId);
        Assert.All(atStation, r => Assert.Contains(recipe.Stations[0], r.Stations));

        var making = Call<List<RecipeView>>(CatalogActions.ListRecipes, $$"""{ "makes": "{{recipe.Outputs[0].Item}}" }""");
        Assert.All(making, r => Assert.Contains(r.Outputs, o => o.Item == recipe.Outputs[0].Item));
        var using_ = Call<List<RecipeView>>(CatalogActions.ListRecipes, $$"""{ "uses": "{{recipe.Inputs[0].Item}}" }""");
        Assert.Contains(using_, r => r.Id == recipe.QualifiedId);
    }

    [Fact]
    public void ListBotTypesHasStatsAttacksAndBehaviour()
    {
        var bots = Call<List<BotTypeView>>(CatalogActions.ListBotTypes);

        Assert.Equal(Vanilla.Value.Bots.All.Select(b => b.QualifiedId), bots.Select(b => b.Id));
        Assert.All(bots, b => Assert.True(b.Health > 0, b.Id));
        Assert.Contains(bots, b => b.Attacks.Count > 0);
        Assert.Contains(bots, b => b.Behavior is { ValueKind: JsonValueKind.Object });
    }

    [Fact]
    public void GetQuestBookHasStepsDialogueAndChoiceIds()
    {
        var value = Call(CatalogActions.GetQuestBook);
        var quests = value.Deserialize<List<QuestView>>(DataJson.ReadOptions)!;

        Assert.Equal(Vanilla.Value.Quests.All.Select(q => q.QualifiedId), quests.Select(q => q.Id));
        Assert.All(quests, q => Assert.NotEmpty(q.Steps));
        var choose = quests.SelectMany(q => q.Steps).First(s => s.Kind == QuestObjectiveKind.Choose);
        Assert.NotEmpty(choose.Choices);
        Assert.All(choose.Choices, c => Assert.False(string.IsNullOrEmpty(c.Id)));
        Assert.Contains("\"kind\":\"choose\"", value.GetRawText());
    }

    [Fact]
    public void ListCharactersNamesEveryCharacter()
    {
        var characters = Call<List<CharacterView>>(CatalogActions.ListCharacters);

        Assert.Equal(Vanilla.Value.Characters.All.Select(c => c.QualifiedId), characters.Select(c => c.Id));
        Assert.NotEmpty(characters);
    }

    private sealed class Content : IGameCatalog
    {
        public Content(ModLoadResult mods)
        {
            Mods = mods;
            Recipes = RecipeBook.Load(mods);
            Bots = BotBook.Load(mods);
            Quests = QuestBook.Load(mods, Recipes, WorldGenData.Load(mods));
            Characters = CharacterBook.Load(mods);
        }

        public ModLoadResult Mods { get; }

        public RecipeBook Recipes { get; }

        public BotBook Bots { get; }

        public QuestBook Quests { get; }

        public CharacterBook Characters { get; }
    }
}
