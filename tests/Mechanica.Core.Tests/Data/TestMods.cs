using System.Text;
using System.Text.Json.Nodes;
using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Data;

/// <summary>Builds throwaway mod folders on disk for loader tests.</summary>
internal sealed class TestMods : IDisposable
{
    public TestMods() => Directory.CreateDirectory(Root);

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "mechanica-tests", Guid.NewGuid().ToString("N"), "mods");

    public static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "mods");

    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "Mechanica.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Mechanica.sln not found above the test output.");
        }
    }

    public TestMods Mod(string id, string version = "1.0.0", string dependencies = "[]", string conflicts = "[]")
    {
        File(id, "mod.json",
            $$"""{ "id": "{{id}}", "name": "{{id}}", "version": "{{version}}", "dependencies": {{dependencies}}, "conflicts": {{conflicts}} }""");
        return this;
    }

    public TestMods File(string mod, string relativePath, string content)
    {
        var path = Path.Combine(Root, mod, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
        return this;
    }

    public TestMods Part(string mod, string fileName, string json) => File(mod, Path.Combine("parts", fileName), json);

    public ModLoadResult Load() => ModLoader.LoadAll(Root);

    public ModLoadResult Load(ModProfile profile) => ModLoader.Load(Root, profile);

    public ModPackageManager Manager(ModProfile? profile = null) => ModPackageManager.Open(Root, profile);

    /// <summary>A profile listing <paramref name="ids"/> in order; a leading '-' disables the mod.</summary>
    public static ModProfile Profile(params string[] ids) => new()
    {
        Mods = [.. ids.Select(id => new ModProfileEntry { Id = id.TrimStart('-'), Enabled = !id.StartsWith('-') })],
    };

    /// <summary>A valid 1-cell block; <paramref name="overrides"/> is a JSON object whose members replace or add keys.</summary>
    public static string Block(string id = "test_block", string overrides = "{}")
    {
        var block = JsonNode.Parse($$"""
            {
              "id": "{{id}}", "name": "Test Block", "category": "block",
              "mass": 4, "durability": 3, "friction": 0.5, "buoyancy": 0.6, "flammable": true,
              "size": [1, 1, 1]
            }
            """)!.AsObject();
        foreach (var (key, value) in JsonNode.Parse(overrides)!.AsObject().ToList())
            block[key] = value?.DeepClone();
        return block.ToJsonString();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(Root)!, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
