using Mechanica.Core.Bots;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Net;
using Mechanica.Core.Quests;
using Mechanica.Core.Sound;
using Mechanica.Core.World;

namespace Mechanica.Core.ModTools;

/// <summary>A <see cref="DataDiagnostic"/> tagged with the content area that reported it.</summary>
/// <param name="Domain">
/// <c>mods</c> (manifests, parts, overrides), <c>sounds</c>, <c>world</c> (worldgen and POIs), <c>recipes</c>,
/// <c>bots</c>, <c>raids</c>, <c>quests</c>, <c>characters</c> or <c>scripts</c> (Lua compile errors).
/// </param>
/// <param name="Diagnostic">The problem.</param>
public sealed record ContentDiagnostic(string Domain, DataDiagnostic Diagnostic);

/// <summary>
/// Every kind of content the game reads from the enabled mods, loaded together the way a world starts:
/// parts, sounds, world generation, recipes, bots, the raid table, quests and characters. Its
/// <see cref="Manifest"/> is what peers compare on join, so the host and the mod tools agree on it.
/// </summary>
public sealed class GameContent
{
    private GameContent(ModLoadResult mods, SoundRegistry sounds, WorldGenData world, RecipeBook recipes, BotBook bots,
        RaidTable raids, QuestBook quests, CharacterBook characters)
    {
        Mods = mods;
        Sounds = sounds;
        World = world;
        Recipes = recipes;
        Bots = bots;
        Raids = raids;
        Quests = quests;
        Characters = characters;
        Manifest = ManifestOf(mods, world, recipes, bots, raids, quests, characters);
    }

    /// <summary>Mods and parts.</summary>
    public ModLoadResult Mods { get; }

    /// <summary>Sound events.</summary>
    public SoundRegistry Sounds { get; }

    /// <summary>World generation rules and POI prefabs.</summary>
    public WorldGenData World { get; }

    /// <summary>Crafting recipes.</summary>
    public RecipeBook Recipes { get; }

    /// <summary>Bot types.</summary>
    public BotBook Bots { get; }

    /// <summary>The raid table.</summary>
    public RaidTable Raids { get; }

    /// <summary>Quests and dialogue.</summary>
    public QuestBook Quests { get; }

    /// <summary>Player characters.</summary>
    public CharacterBook Characters { get; }

    /// <summary>The mods in load order and one fingerprint over all of the content, as checked on join.</summary>
    public ContentManifest Manifest { get; }

    /// <summary>Every problem every loader found, tagged by area, mods first.</summary>
    public IReadOnlyList<ContentDiagnostic> Diagnostics =>
    [
        .. Tag("mods", Mods.Diagnostics), .. Tag("sounds", Sounds.Diagnostics), .. Tag("world", World.Diagnostics),
        .. Tag("recipes", Recipes.Diagnostics), .. Tag("bots", Bots.Diagnostics), .. Tag("raids", Raids.Diagnostics),
        .. Tag("quests", Quests.Diagnostics), .. Tag("characters", Characters.Diagnostics), .. Tag("lang", LanguageDiagnostics),
    ];

    /// <summary>Problems with the mods' language files (<see cref="Ui.ModLanguages"/>), which never stop a mod loading.</summary>
    public IReadOnlyList<DataDiagnostic> LanguageDiagnostics { get; private init; } = [];

    /// <summary>Loads everything from already loaded mods.</summary>
    public static GameContent From(ModLoadResult mods)
    {
        var world = WorldGenData.Load(mods);
        var recipes = RecipeBook.Load(mods);
        var bots = BotBook.Load(mods);
        var languages = new List<DataDiagnostic>();
        Ui.ModLanguages.Read(mods.Mods.Select(m => m.Directory), languages);
        return new GameContent(mods, SoundRegistry.Load(mods), world, recipes, bots, RaidTable.Load(mods, bots),
            QuestBook.Load(mods, recipes, world), CharacterBook.Load(mods)) { LanguageDiagnostics = languages };
    }

    /// <summary>Loads everything from the mods <paramref name="profile"/> enables (null: every mod).</summary>
    public static GameContent Load(string modsRoot, ModProfile? profile) => From(ModLoader.Load(modsRoot, profile));

    /// <summary>
    /// The join manifest for content loaded piece by piece (the game's start-up path, where tests may replace
    /// single pieces): the mods plus one fingerprint over the mods, world, recipes, bots, raids, quests and characters.
    /// </summary>
    public static ContentManifest ManifestOf(ModLoadResult mods, WorldGenData world, RecipeBook recipes, BotBook bots,
        RaidTable raids, QuestBook quests, CharacterBook characters) =>
        ContentManifest.From(mods) with
        {
            Fingerprint = $"{mods.Fingerprint}+{world.Fingerprint}+{recipes.Fingerprint}+{bots.Fingerprint}+{raids.Fingerprint}+{quests.Fingerprint}+{characters.Fingerprint}",
        };

    private static IEnumerable<ContentDiagnostic> Tag(string domain, IEnumerable<DataDiagnostic> diagnostics) =>
        diagnostics.Select(d => new ContentDiagnostic(domain, d));
}
