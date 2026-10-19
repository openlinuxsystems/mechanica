using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// The Game rules reference in <c>docs/packaging-guide.md</c> lists every key a mod's <c>rules/&lt;name&gt;.patch.json</c>
/// may change: every leaf of the shipped <c>game/data/&lt;name&gt;.json</c> outside the keys <see cref="ModRules"/> locks,
/// written with dots (<c>fall.min_speed</c>) in that file's sub-section.
/// </summary>
public class GameRulesDocTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mechanica.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Mechanica.sln not found above the test binaries.");
    }

    public static TheoryData<string> RulesFiles()
    {
        var data = new TheoryData<string>();
        foreach (var name in ModRules.Files.Keys)
            data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(RulesFiles))]
    public void EveryPatchableRuleKeyIsInTheGameRulesReference(string name)
    {
        var section = Section(name);
        var locked = ModRules.Files[name];
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "game", "data", name + ".json")),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
        var keys = new List<string>();
        foreach (var (key, value) in json)
        {
            if (!locked.Contains(key))
                Leaves(key, value, keys);
        }
        Assert.NotEmpty(keys);
        var missing = keys.Where(k => !section.Contains($"`{k}`", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, $"docs/packaging-guide.md (Game rules reference, {name}.json) is missing: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(RulesFiles))]
    public void EveryLockedKeyIsNamedAsLocked(string name)
    {
        var section = Section(name);
        var locked = section[section.IndexOf("Locked:", StringComparison.Ordinal)..];
        var missing = ModRules.Files[name].Where(k => !locked.Contains($"`{k}`", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, $"docs/packaging-guide.md ({name}.json) does not list as locked: {string.Join(", ", missing)}");
    }

    // The text under "#### `name.json`" up to the next heading.
    private static string Section(string name)
    {
        var doc = File.ReadAllText(Path.Combine(Root, "docs", "packaging-guide.md"));
        var heading = $"#### `{name}.json`";
        var start = doc.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"docs/packaging-guide.md has no '{heading}' heading.");
        start += heading.Length;
        var end = doc.IndexOf("\n#", start, StringComparison.Ordinal);
        return end < 0 ? doc[start..] : doc[start..end];
    }

    // Objects are walked into; anything else (numbers, strings, lists) is one rule.
    private static void Leaves(string path, JsonNode? node, List<string> keys)
    {
        if (node is JsonObject obj && obj.Count > 0)
        {
            foreach (var (key, value) in obj)
                Leaves($"{path}.{key}", value, keys);
        }
        else
        {
            keys.Add(path);
        }
    }
}
