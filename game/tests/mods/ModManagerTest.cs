using Godot;
using Mechanica.Core.Data;
using Mechanica.Game.Mods;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless Mod Manager test. Opens the screen on the fixture mods in <c>tests/mods/mods</c> (hidden
/// from the importer by a <c>.gdignore</c>) and checks load order, override clashes, declared
/// conflicts, enable/disable cascades, reordering, and that Apply saves a profile the loader reads
/// back the same way. Exits 0 on success, 1 on failure.
/// </summary>
public partial class ModManagerTest : Node
{
    private const string ProfilePath = "user://tests/mod_manager_profile.json";
    private bool _failed;

    public override void _Ready()
    {
        var profilePath = ProjectSettings.GlobalizePath(ProfilePath);
        if (File.Exists(profilePath))
            File.Delete(profilePath);

        var screen = Open(profilePath);
        Check(Ids(screen).SequenceEqual(["vanilla", "heavy", "lib", "cars", "light", "old_cars"]),
            $"mods are listed in load order ({string.Join(", ", Ids(screen))})");
        Check(Row(screen, "old_cars").Status.Contains("conflicts with 'cars'"), $"declared conflict is shown ({Row(screen, "old_cars").Status})");
        Check(Row(screen, "heavy").Status.Contains("replaced by later mods"), $"losing override is shown ({Row(screen, "heavy").Status})");
        Check(Mass(screen) == 1f, "the later override (light) wins");
        Check(screen.ProblemsText.Contains("both override 'vanilla:block'"), "the override clash is listed under problems");
        Check(!screen.HasUnsavedChanges, "a fresh setup has nothing to apply");

        Check(screen.MoveMod("heavy", 3) == 4 && Mass(screen) == 40f, "moving heavy below light makes it win");
        Check(screen.MoveMod("cars", -10) == 2, "cars cannot move above lib, which it needs");

        var changed = screen.SetModEnabled("lib", false);
        Check(changed.SequenceEqual(["lib", "cars"]), $"disabling lib disables cars too ({string.Join(", ", changed)})");
        Check(Row(screen, "cars").Status == "Disabled" && Row(screen, "old_cars").Status == "Loaded",
            "with cars disabled, old_cars loads");
        Check(screen.SetModEnabled("vanilla", false).Count == 0 && Row(screen, "vanilla").Enabled, "vanilla cannot be disabled");
        Check(screen.HasUnsavedChanges, "changes are pending until applied");

        screen.Apply();
        var diagnostics = new List<DataDiagnostic>();
        var saved = ModProfile.Load(profilePath, diagnostics);
        var reloaded = ModLoader.Load(screen.ModsRoot, saved);
        Check(saved is not null && diagnostics.Count == 0 && !screen.HasUnsavedChanges, "Apply saves a valid profile");
        Check(reloaded.Fingerprint == screen.Preview.Fingerprint, "the game loads exactly what the screen previewed");

        var reopened = Open(profilePath);
        Check(reopened.Rows.SequenceEqual(screen.Rows), "reopening the screen restores the saved setup");

        // New mod, Edit info and Check all content, on a copy of the fixture mods.
        var copy = ProjectSettings.GlobalizePath("user://tests/mod_manager_mods");
        if (Directory.Exists(copy))
            Directory.Delete(copy, recursive: true);
        CopyFolder(ProjectSettings.GlobalizePath("res://tests/mods/mods"), copy);
        var toolsProfile = ProjectSettings.GlobalizePath("user://tests/mod_manager_tools_profile.json");
        if (File.Exists(toolsProfile))
            File.Delete(toolsProfile);
        var tools = Open(toolsProfile, copy);
        Check(tools.CreateMod("Bad Id", "Bad") is not null, "a bad mod id is refused");
        Check(tools.CreateMod("tools_test", "Tools Test") is null && tools.SelectedId == "tools_test" && Row(tools, "tools_test").Status == "Loaded",
            $"New mod makes an empty mod that loads ({string.Join(", ", tools.Rows)})");
        Check(tools.EditInfo("tools_test", "Tools Test", "2.0.0", "A test mod", ["Ada"], ["vanilla", "lib"]).Count == 0, "Edit info saves");
        var manifest = ModLoader.ParseJson<ModManifest>(File.ReadAllText(Path.Combine(copy, "tools_test", "mod.json")), "mod.json", [])!;
        Check(manifest.Version == "2.0.0" && manifest.Authors![0] == "Ada" && manifest.Dependencies!.Select(d => d.Id).SequenceEqual(["vanilla", "lib"]),
            "the new info is in mod.json");
        Check(tools.EditInfo("tools_test", "Tools Test", "two", null, [], ["vanilla"]).Count > 0, "a bad version is refused");
        var report = tools.ValidateAll();
        Check(report.LoadedMods.Contains("tools_test") && tools.ProblemsText.Contains("error(s)") && tools.ProblemsText.Contains("[world]"),
            $"Check all content lists every kind of problem ({report.Errors} errors)");

        GD.Print($"[mods] {(_failed ? "FAILED" : "OK")}");
        StagedTest.QuitCleanly(this, _failed ? 1 : 0);
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(from))
            CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    private ModManagerScreen Open(string profilePath, string? modsRoot = null)
    {
        var screen = new ModManagerScreen
        {
            ModsRoot = modsRoot ?? ProjectSettings.GlobalizePath("res://tests/mods/mods"),
            ProfilePath = profilePath,
        };
        AddChild(screen);
        return screen;
    }

    private static IEnumerable<string> Ids(ModManagerScreen screen) => screen.Rows.Select(r => r.Id);

    private static (string Id, bool Enabled, string Status) Row(ModManagerScreen screen, string id) =>
        screen.Rows.First(r => r.Id == id);

    private static float? Mass(ModManagerScreen screen) => screen.Preview.FindDefinition("block")?.Mass;

    private void Check(bool ok, string what)
    {
        GD.Print($"[mods] {(ok ? "PASS" : "FAIL")}: {what}");
        _failed |= !ok;
    }
}
