using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Game.Editor;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless Recipe Editor test on a copy of the fixture mods in <c>tests/recipeeditor/mods</c>: every mod's recipes
/// are listed, vanilla is never the mod to edit, another mod's recipe is read-only until it is overridden or copied,
/// an override wins in the list and in what the game loads, a bad recipe is not saved, and Delete asks first.
/// Exits 0 on success, 1 on failure.
/// </summary>
public partial class RecipeEditorTest : Node
{
    private bool _failed;

    public override void _Ready()
    {
        var mods = ProjectSettings.GlobalizePath("user://tests/recipe_editor_mods");
        if (Directory.Exists(mods))
            Directory.Delete(mods, recursive: true);
        CopyFolder(ProjectSettings.GlobalizePath("res://tests/recipeeditor/mods"), mods);
        var profile = ProjectSettings.GlobalizePath("user://tests/recipe_editor_profile.json");
        if (File.Exists(profile))
            File.Delete(profile);
        var fixture = ModLoader.LoadAll(mods);
        Check(fixture.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error), $"the fixture mods load ({string.Join("; ", fixture.Diagnostics)})");

        var screen = new RecipeEditorScreen { ModsRoot = mods, ProfilePath = profile };
        AddChild(screen);
        Check(screen.Entries.Select(e => e.QualifiedId).SequenceEqual(["vanilla:wheel", "cars:car_wheel"]),
            $"every mod's recipes are listed in load order ({string.Join(", ", screen.Entries.Select(e => e.QualifiedId))})");
        Check(screen.TargetMod == "cars" && !screen.SetTarget("vanilla"), "cars is the mod to edit; vanilla never is");

        screen.Filter("scrap");
        Check(screen.Shown.Count == 2, "the filter looks at inputs");
        screen.Filter("car_");
        Check(screen.Shown.Count == 1 && screen.Shown[0].QualifiedId == "cars:car_wheel", "and at ids");
        screen.Filter("");

        Check(screen.Select("vanilla", "recipes/wheel.json") && screen.Current is null, "vanilla's recipe shows read-only");
        Check(screen.OverrideSelected() is null && screen.OverrideOfCurrent == "vanilla" && screen.IsDirty, "Override starts cars' version of it");
        screen.Edit(r => r with { Inputs = [new RecipeItem { Item = "scrap_metal", Count = 1 }] });
        var saved = screen.Save();
        var overridePath = Path.Combine(mods, "cars", "overrides", "vanilla", "recipes", "wheel.json");
        Check(saved is { Succeeded: true } && File.Exists(overridePath) && screen.CurrentFile == "overrides/vanilla/recipes/wheel.json" && !screen.IsDirty,
            $"the override saves under overrides/vanilla/recipes ({screen.ProblemsText})");
        var book = RecipeBook.Load(ModLoader.LoadAll(mods));
        Check(book.Find("vanilla:wheel")?.Inputs.Single().Count == 1, "the game loads the override");
        Check(screen.Select("vanilla", "recipes/wheel.json") && screen.OverrideSelected() is null && screen.CurrentFile == "overrides/vanilla/recipes/wheel.json",
            "overriding it again opens the override cars already has");

        screen.Select("vanilla", "recipes/wheel.json");
        Check(screen.CopySelected() is null && screen.Current?.Id == "wheel" && screen.OverrideOfCurrent is null, "Copy to mod starts a new recipe from it");
        screen.Edit(r => r with { Id = "spare_wheel", Inputs = [new RecipeItem { Item = "no_such_item", Count = 1 }] });
        Check(screen.Save() is { Succeeded: false } && screen.ProblemsText.Contains("not saved") && !File.Exists(Path.Combine(mods, "cars", "recipes", "spare_wheel.json")),
            $"a bad recipe is not saved ({screen.ProblemsText})");
        screen.Edit(r => r with { Inputs = [new RecipeItem { Item = "scrap_metal", Count = 6 }] });
        Check(screen.Save() is { Succeeded: true } && File.Exists(Path.Combine(mods, "cars", "recipes", "spare_wheel.json")), "fixed, it saves");

        Check(screen.Select("cars", "recipes/car_wheel.json") && screen.Current?.Id == "car_wheel", "cars' own recipe opens for editing");
        Check(screen.NewRecipe() is null && screen.Current?.Id == "car_wheel_2" && screen.Current.Outputs![0].Item == "cars:car_wheel",
            "New recipe makes the picked recipe's output with a free id");

        Check(screen.Select("cars", "recipes/spare_wheel.json"), "the copy is listed");
        var ask = screen.AskDelete();
        Check(ask is not null && ConfirmDialog.Current == ask, "Delete asks first");
        ask?.QueueFree();
        Check(screen.DeleteCurrent() && !File.Exists(Path.Combine(mods, "cars", "recipes", "spare_wheel.json")), "and deletes the file");
        Check(screen.Entries.Count == 3, $"the list follows ({screen.Entries.Count})");

        GD.Print($"[recipeeditor] {(_failed ? "FAILED" : "OK")}");
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

    private void Check(bool ok, string what)
    {
        GD.Print($"[recipeeditor] {(ok ? "PASS" : "FAIL")}: {what}");
        _failed |= !ok;
    }
}
