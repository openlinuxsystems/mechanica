using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Sound;
using Mechanica.Game.Editor;
using Mechanica.Game.Mods;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless Sound Editor test on a copy of the Recipe Editor's fixture mods: vanilla is never the mod to edit, a mod
/// with no sounds starts its default bank, a clip imported makes an event, the bank saves and the game registers it,
/// and a second bank file opens from the list. Exits 0 on success, 1 on failure.
/// </summary>
public partial class SoundEditorTest : Node
{
    private bool _failed;

    public override void _Ready()
    {
        var mods = ProjectSettings.GlobalizePath("user://tests/sound_editor_mods");
        if (Directory.Exists(mods))
            Directory.Delete(mods, recursive: true);
        CopyFolder(ProjectSettings.GlobalizePath("res://tests/recipeeditor/mods"), mods);
        var profile = ProjectSettings.GlobalizePath("user://tests/sound_editor_profile.json");
        if (File.Exists(profile))
            File.Delete(profile);

        var screen = new SoundEditorScreen { ModsRoot = mods, ProfilePath = profile };
        AddChild(screen);
        var binder = screen.Binder;
        Check(screen.Targets.SequenceEqual(["cars"]) && screen.TargetMod == "cars" && !screen.SetTarget("vanilla"), "cars is the mod to edit; vanilla never is");
        Check(screen.Files.SequenceEqual(["sounds/cars.json"]) && binder.Bank is { Events.Count: 0, RelativeFile: "sounds/cars.json" },
            "a mod with no sounds starts its default bank");
        Check(binder.GetNode<Control>("PartSounds").Visible == false, "there are no part bindings without a part");

        var clip = Path.Combine(ModPaths.ModsRoot, "vanilla", "sounds", "joints", "bearing_spin_loop.ogg");
        Check(binder.NewEventFromClip(clip) is null && binder.Bank!.Events.Single() is { Id: "bearing_spin_loop" }, "importing a clip makes an event");
        var bank = binder.Bank!;
        bank.Set(0, bank.Events[0] with { Id = "engine_hum", Loop = true });
        Check(binder.SaveBank() is { Succeeded: true } && File.Exists(Path.Combine(mods, "cars", "sounds", "cars.json")), $"the bank saves ({binder.BankProblemsText})");
        Check(SoundRegistry.Load(ModLoader.LoadAll(mods)).Find("cars:engine_hum") is { Loop: true }, "the game registers the event");

        File.WriteAllText(Path.Combine(mods, "cars", "sounds", "alarms.json"), "{ \"events\": [] }");
        screen.Reload();
        Check(screen.Files.SequenceEqual(["sounds/alarms.json", "sounds/cars.json"]), $"every bank file is listed ({string.Join(", ", screen.Files)})");
        Check(screen.OpenFile("sounds/cars.json") is null && binder.Bank is { Events.Count: 1, RelativeFile: "sounds/cars.json" }, "a bank file opens from the list");
        Check(screen.OpenFile("sounds/../escape.json") is not null && screen.LastError is not null, "a path outside sounds/ is refused");

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
        GD.Print($"[soundeditor] {(ok ? "PASS" : "FAIL")}: {what}");
        _failed |= !ok;
    }
}
