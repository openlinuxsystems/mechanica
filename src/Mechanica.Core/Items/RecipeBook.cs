using System.Security.Cryptography;
using System.Text;
using Mechanica.Core.Data;

namespace Mechanica.Core.Items;

/// <summary>A recipe that loaded cleanly, with every id qualified.</summary>
/// <param name="QualifiedId">Game-wide id, <c>mod_id:recipe_id</c>.</param>
/// <param name="Name">Display name (the recipe's own, else its first output's).</param>
/// <param name="Stations">Qualified station part ids.</param>
/// <param name="Inputs">Items used up, qualified.</param>
/// <param name="Outputs">Items made, qualified.</param>
/// <param name="Time">Seconds at speed 1.</param>
/// <param name="Definition">The data as written.</param>
public sealed record Recipe(
    string QualifiedId,
    string Name,
    IReadOnlyList<string> Stations,
    IReadOnlyList<ItemAmount> Inputs,
    IReadOnlyList<ItemAmount> Outputs,
    float Time,
    RecipeDefinition Definition)
{
    /// <summary>True when the station part <paramref name="stationId"/> can run it.</summary>
    public bool RunsAt(string stationId) => Stations.Contains(Identifiers.Qualify(stationId));

    /// <summary>The inputs of <paramref name="count"/> crafts.</summary>
    public IEnumerable<ItemAmount> InputsFor(int count) => Inputs.Select(i => i with { Count = i.Count * count });
}

/// <summary>
/// Every recipe of the enabled mods (chunk 18), read from each mod's <c>recipes/**/*.json</c> in load
/// order and checked against the loaded parts: stations must be parts with <c>crafting</c>, items must
/// exist. A file with errors is left out and <see cref="Diagnostics"/> says why. Host and clients compare
/// <see cref="Fingerprint"/> on join.
/// </summary>
public sealed class RecipeBook
{
    /// <summary>Where a mod keeps its recipes.</summary>
    public const string Folder = "recipes";

    private readonly Dictionary<string, Recipe> _recipes;

    private RecipeBook(IEnumerable<Recipe> recipes, IReadOnlyList<DataDiagnostic> diagnostics)
    {
        _recipes = recipes.ToDictionary(r => r.QualifiedId, StringComparer.Ordinal);
        All = [.. _recipes.Values.OrderBy(r => r.QualifiedId, StringComparer.Ordinal)];
        Diagnostics = diagnostics;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var recipe in All)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(recipe.QualifiedId));
            hash.AppendData(Encoding.UTF8.GetBytes(DataJson.Write(recipe.Definition)));
        }
        Fingerprint = Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>A book with no recipes.</summary>
    public static RecipeBook Empty { get; } = new([], []);

    /// <summary>Every recipe, ordered by qualified id.</summary>
    public IReadOnlyList<Recipe> All { get; }

    /// <summary>Every problem found while loading.</summary>
    public IReadOnlyList<DataDiagnostic> Diagnostics { get; }

    /// <summary>True when any diagnostic is an error.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>SHA-256 (hex) of every recipe's id and canonical form.</summary>
    public string Fingerprint { get; }

    /// <summary>Finds a recipe; a bare id means vanilla.</summary>
    public Recipe? Find(string id) => _recipes.GetValueOrDefault(Identifiers.Qualify(id));

    /// <summary>The recipes a station part can run, ordered by id.</summary>
    public IEnumerable<Recipe> ForStation(string stationId) => All.Where(r => r.RunsAt(stationId));

    /// <summary>The recipes that make <paramref name="itemId"/>, ordered by id.</summary>
    public IEnumerable<Recipe> Making(string itemId)
    {
        var item = Identifiers.Qualify(itemId);
        return All.Where(r => r.Outputs.Any(o => o.Item == item));
    }

    /// <summary>
    /// Loads the recipes of the mods in <paramref name="content"/>. Each mod's own <c>recipes/</c> come
    /// first, then its <c>overrides/&lt;mod_id&gt;/recipes/</c>, which replace that mod's recipes of the
    /// same id, so the override of the mod latest in load order wins.
    /// </summary>
    public static RecipeBook Load(ModLoadResult content)
    {
        var diagnostics = new List<DataDiagnostic>();
        var recipes = new Dictionary<string, Recipe>(StringComparer.Ordinal);
        // Which mod's override replaced each recipe, to warn when two unrelated mods replace one.
        var overriddenBy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mod in content.Mods)
        {
            foreach (var (path, relative) in Files(mod.Directory, Path.Combine(mod.Directory, Folder)))
            {
                var sink = new DiagnosticSink($"{mod.Id}/{relative}", diagnostics);
                if (Read(path, sink) is not { } definition)
                    continue;
                var recipe = Check(definition, mod.Id, content, sink);
                if (recipe is not null && recipes.ContainsKey(recipe.QualifiedId))
                    sink.Error(DiagnosticCodes.DuplicateRecipe, "$.id", $"recipe '{recipe.QualifiedId}' is defined twice.");
                else if (recipe is not null && sink.ErrorCount == 0)
                    recipes.Add(recipe.QualifiedId, recipe);
            }
            LoadOverrides(mod, content, recipes, overriddenBy, diagnostics);
        }
        return new RecipeBook(recipes.Values, diagnostics);
    }

    // The mod loader has already refused overrides/<target>/ unless the target is a dependency, so it is
    // loaded and its recipes are in the table.
    private static void LoadOverrides(LoadedMod mod, ModLoadResult content, Dictionary<string, Recipe> recipes,
        Dictionary<string, string> overriddenBy, List<DataDiagnostic> diagnostics)
    {
        var overridesDir = Path.Combine(mod.Directory, ModLoader.OverridesFolder);
        if (!Directory.Exists(overridesDir))
            return;
        var own = new HashSet<string>(StringComparer.Ordinal);
        foreach (var targetDir in Directory.GetDirectories(overridesDir).Order(StringComparer.Ordinal))
        {
            var targetId = Path.GetFileName(targetDir);
            foreach (var (path, relative) in Files(mod.Directory, Path.Combine(targetDir, ModLoader.RecipeOverridesFolder)))
            {
                var sink = new DiagnosticSink($"{mod.Id}/{relative}", diagnostics);
                if (Read(path, sink) is not { } definition)
                    continue;
                var recipe = Check(definition, targetId, content, sink);
                if (recipe is null || sink.ErrorCount > 0)
                    continue;
                var id = recipe.QualifiedId;
                if (!recipes.ContainsKey(id))
                {
                    sink.Error(DiagnosticCodes.RecipeOverrideUnknown, "$.id", $"mod '{targetId}' has no recipe '{definition.Id}' to override.");
                    continue;
                }
                if (!own.Add(id))
                {
                    sink.Error(DiagnosticCodes.DuplicateRecipe, "$.id", $"'{id}' is overridden twice in this mod.");
                    continue;
                }
                if (overriddenBy.TryGetValue(id, out var previous) && !DependsOn(mod.Id, previous, content))
                    sink.Warning(DiagnosticCodes.RecipeOverrideConflict, "$.id",
                        $"mods '{previous}' and '{mod.Id}' both override recipe '{id}'; '{mod.Id}' loads later and wins. "
                        + "Change the load order in the Mod Manager to pick the other.");
                overriddenBy[id] = mod.Id;
                recipes[id] = recipe;
            }
        }
    }

    // True when modId depends on otherId, directly or through other mods.
    private static bool DependsOn(string modId, string otherId, ModLoadResult content)
    {
        var mods = content.Mods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>([modId]);
        while (stack.TryPop(out var id))
        {
            foreach (var dep in mods.GetValueOrDefault(id)?.Manifest.Dependencies ?? [])
            {
                if (dep.Id == otherId)
                    return true;
                if (seen.Add(dep.Id!))
                    stack.Push(dep.Id!);
            }
        }
        return false;
    }

    // Every *.json under folder, with its mod-relative path, sorted so all machines read files in the same order.
    private static IEnumerable<(string Path, string Relative)> Files(string modDir, string folder) => Directory.Exists(folder)
        ? Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories)
            .Select(f => (Path: f, Relative: Path.GetRelativePath(modDir, f).Replace('\\', '/')))
            .OrderBy(f => f.Relative, StringComparer.Ordinal)
        : [];

    /// <summary>Builds a book from recipes in code (tests, tools). Throws on any error.</summary>
    /// <exception cref="FormatException">A recipe has errors.</exception>
    public static RecipeBook From(IPartCatalog catalog, params (string ModId, RecipeDefinition Recipe)[] recipes)
    {
        var diagnostics = new List<DataDiagnostic>();
        var list = new List<Recipe>();
        foreach (var (modId, definition) in recipes)
        {
            var sink = new DiagnosticSink($"{modId}:{definition.Id}", diagnostics);
            if (Check(definition, modId, catalog, sink) is { } recipe && sink.ErrorCount == 0)
                list.Add(recipe);
        }
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            throw new FormatException(string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return new RecipeBook(list, diagnostics);
    }

    /// <summary>
    /// Checks one recipe as the loader would, as if it were in mod <paramref name="modId"/>. The editor's
    /// recipe panel checks with this before it saves.
    /// </summary>
    public static IReadOnlyList<DataDiagnostic> Validate(RecipeDefinition recipe, string modId, IPartCatalog catalog, string displayFile)
    {
        var diagnostics = new List<DataDiagnostic>();
        Check(recipe, modId, catalog, new DiagnosticSink(displayFile, diagnostics));
        return diagnostics;
    }

    private static Recipe? Check(RecipeDefinition r, string modId, IPartCatalog catalog, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.RecipeInvalid;
        var errors = sink.ErrorCount;
        if (r.Id is null)
            sink.Error(code, "$.id", "id is required.");
        else if (!Identifiers.IsValid(r.Id))
            sink.Error(code, "$.id", $"id '{r.Id}' must be lowercase snake_case (a letter, then letters, digits or '_').");
        if (r.Name is not null && string.IsNullOrWhiteSpace(r.Name))
            sink.Error(code, "$.name", "name must not be blank; leave it out to use the output's name.");

        var stations = new List<string>();
        if (r.Stations is not { Count: > 0 })
            sink.Error(code, "$.stations", "stations (part ids of crafting stations) needs at least one entry.");
        for (var i = 0; i < (r.Stations?.Count ?? 0); i++)
        {
            var path = $"$.stations[{i}]";
            var id = r.Stations![i];
            if (!PartValidator.IsItemRef(id))
            {
                sink.Error(code, path, $"'{id}' must be a part id or 'mod_id:part_id'.");
                continue;
            }
            var qualified = Identifiers.Qualify(id);
            var station = catalog.FindDefinition(qualified);
            if (station is null)
                sink.Error(code, path, $"there is no part '{qualified}'.");
            else if (station.Crafting is null)
                sink.Error(code, path, $"'{qualified}' is not a crafting station (it has no \"crafting\").");
            else if (stations.Contains(qualified))
                sink.Error(code, path, $"'{qualified}' is listed twice.");
            else
                stations.Add(qualified);
        }

        var inputs = Items(r.Inputs, "inputs", catalog, sink);
        var outputs = Items(r.Outputs, "outputs", catalog, sink);
        if (r.Time is not { } time)
            sink.Error(code, "$.time", "time (seconds, 0 or more) is required.");
        else if (!(time >= 0 && float.IsFinite(time)))
            sink.Error(code, "$.time", "time must be 0 or more seconds.");
        KnownKeys.WarnUnknown(r.Extra, "$", sink, typeof(RecipeDefinition));

        if (sink.ErrorCount > errors)
            return null;
        var name = r.Name ?? catalog.FindDefinition(outputs[0].Item)?.Name ?? outputs[0].Item;
        return new Recipe($"{modId}:{r.Id}", name, stations, inputs, outputs, r.Time!.Value, r);
    }

    private static List<ItemAmount> Items(List<RecipeItem>? items, string key, IPartCatalog catalog, DiagnosticSink sink)
    {
        const string code = DiagnosticCodes.RecipeInvalid;
        var list = new List<ItemAmount>();
        if (items is not { Count: > 0 })
        {
            sink.Error(code, $"$.{key}", $"{key} needs at least one item.");
            return list;
        }
        for (var i = 0; i < items.Count; i++)
        {
            var path = $"$.{key}[{i}]";
            var item = items[i];
            KnownKeys.WarnUnknown(item.Extra, path, sink, typeof(RecipeItem));
            if (!PartValidator.IsItemRef(item.Item))
            {
                sink.Error(code, $"{path}.item", item.Item is null ? "item is required." : $"'{item.Item}' must be an item id or 'mod_id:item_id'.");
                continue;
            }
            var qualified = Identifiers.Qualify(item.Item);
            var errors = sink.ErrorCount;
            if (catalog.FindDefinition(qualified) is null)
                sink.Error(code, $"{path}.item", $"there is no item '{qualified}'.");
            else if (list.Any(a => a.Item == qualified))
                sink.Error(code, $"{path}.item", $"'{qualified}' is listed twice; add the counts together.");
            if (item.Count is not { } count)
                sink.Error(code, $"{path}.count", "count (1 or more) is required.");
            else if (count < 1)
                sink.Error(code, $"{path}.count", $"count must be 1 or more (got {count}).");
            else if (sink.ErrorCount == errors)
                list.Add(new ItemAmount(qualified, count));
        }
        return list;
    }

    private static RecipeDefinition? Read(string path, DiagnosticSink sink)
    {
        try
        {
            return ModLoader.ParseJson<RecipeDefinition>(File.ReadAllText(path), sink);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            sink.Error(DiagnosticCodes.FileUnreadable, null, $"could not read file: {e.Message}");
            return null;
        }
    }
}
