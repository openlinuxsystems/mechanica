using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mechanica.Core.Tests.Remote;

/// <summary>
/// The hand-written AI guides (<c>docs/ai/*.md</c>, <c>llms.txt</c>) only link to files that exist and only
/// name methods the generated <c>docs/ai/describe.json</c> lists, so an AI fed them never calls something
/// that isn't there.
/// </summary>
public partial class AiDocsTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mechanica.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Mechanica.sln not found above the test binaries.");
    }

    private static IEnumerable<string> Guides() =>
        Directory.EnumerateFiles(Path.Combine(Root, "docs", "ai"), "*.md")
            .Where(p => Path.GetFileName(p) != "reference.md")
            .Append(Path.Combine(Root, "llms.txt"))
            .Order(StringComparer.Ordinal);

    [Fact]
    public void LinksPointAtFilesThatExist()
    {
        var broken = new List<string>();
        foreach (var guide in Guides())
        {
            foreach (Match link in Link().Matches(File.ReadAllText(guide)))
            {
                var target = link.Groups[1].Value.Split('#')[0];
                if (target.Length == 0 || target.Contains("://", StringComparison.Ordinal))
                    continue;
                if (!Path.Exists(Path.Combine(Path.GetDirectoryName(guide)!, target)))
                    broken.Add($"{Path.GetFileName(guide)} -> {target}");
            }
        }
        Assert.Empty(broken);
    }

    [Fact]
    public void GuidesNameOnlyMethodsTheGameHas()
    {
        using var describe = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "docs", "ai", "describe.json")));
        var names = describe.RootElement.GetProperty("methods").EnumerateArray().Select(m => m.GetProperty("name").GetString()).ToHashSet();
        var unknown = new List<string>();
        foreach (var guide in Guides())
        {
            foreach (Match code in MethodName().Matches(File.ReadAllText(guide)))
            {
                var name = code.Groups[1].Value;
                if (!names.Contains(name))
                    unknown.Add($"{Path.GetFileName(guide)}: {name}");
            }
        }
        Assert.Empty(unknown);
    }

    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    // `GetPlayer`, `GetPlayer {...}`, `mechanica.join`, `package.inspect {...}`: a method name opening a code span.
    [GeneratedRegex(@"`((?:[A-Z][a-z]+){2,}|(?:mechanica|package|mods|lua|editor|reports|world)\.[a-z][a-z_.]*[a-z])[` ]")]
    private static partial Regex MethodName();
}
