using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Packages;
using Mechanica.Game.Mods;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless Mod Manager packaging test, driven through the screen's own buttons and dialogs. Copies the
/// fixture mods to a scratch folder, exports one with Export… (folder picker), installs the package into a
/// second mods folder with Install… (file picker, then the confirm dialog), checks a reinstall asks to
/// replace, that a tampered package is refused without a dialog, that a mod with errors is not exported,
/// and that Uninstall deletes the mod after confirming. Exits 0 on success, 1 on failure.
/// </summary>
public partial class ModPackagesTest : Node
{
    private bool _failed;

    public override void _Ready()
    {
        var scratch = ProjectSettings.GlobalizePath("user://tests/mod_packages");
        if (Directory.Exists(scratch))
            Directory.Delete(scratch, recursive: true);
        var source = Path.Combine(scratch, "source", "mods");
        var target = Path.Combine(scratch, "target", "mods");
        var packages = Path.Combine(scratch, "packages");
        Copy(ProjectSettings.GlobalizePath("res://tests/mods/mods"), source);
        Copy(ProjectSettings.GlobalizePath("res://tests/mods/mods/vanilla"), Path.Combine(target, "vanilla"));

        // Export lib from the source folder.
        var from = Open(source, Path.Combine(scratch, "source.json"), packages);
        Check(Button(from, "Export…").Disabled && Button(from, "Uninstall").Disabled, "Export and Uninstall wait for a selection");
        from.Select("vanilla");
        Check(!Button(from, "Export…").Disabled && Button(from, "Uninstall").Disabled, "vanilla can be exported but not uninstalled");
        from.Select("lib");
        Press(Button(from, "Export…"));
        Check(from.ExportDialog.Visible && from.ExportDialog.CurrentDir.TrimEnd('/') == packages.TrimEnd('/'),
            $"Export opens a folder picker in the packages folder ({from.ExportDialog.CurrentDir})");
        from.ExportDialog.EmitSignal(FileDialog.SignalName.DirSelected, packages);
        from.ExportDialog.Hide();
        var zip = Path.Combine(packages, "lib-2.0.0.zip");
        Check(File.Exists(zip) && from.PackageText.Contains("Exported lib 2.0.0"), $"choosing a folder writes lib-2.0.0.zip ({from.PackageText})");
        Check(ModPackages.Inspect(zip).IsValid, "the exported package verifies");

        // A mod with errors is not exported.
        File.WriteAllText(Path.Combine(source, "light", "overrides", "vanilla", "block.json"), "{ \"id\": \"block\", \"mass\": -1 }");
        from.ExportMod("light", packages);
        Check(!File.Exists(Path.Combine(packages, "light-1.0.0.zip")) && from.PackageText.Contains("was not exported"),
            $"a mod with errors is refused ({from.PackageText})");

        // Install it into the target folder through the file picker and the confirm dialog.
        var into = Open(target, Path.Combine(scratch, "target.json"), packages);
        Press(Button(into, "Install…"));
        Check(into.InstallDialog.Visible, "Install opens a file picker");
        into.InstallDialog.EmitSignal(FileDialog.SignalName.FileSelected, zip);
        into.InstallDialog.Hide();
        var ask = ConfirmDialog.Current;
        Check(ask is not null && ask.Message.Contains("Install Vehicle Library 2.0.0?") && ask.ConfirmButton.Text == "Install",
            $"installing asks first ({ask?.Message})");
        Check(!into.Rows.Any(r => r.Id == "lib"), "nothing is installed before confirming");
        Press(ask!.ConfirmButton);
        Check(into.Rows.Any(r => r.Id == "lib" && r.Enabled && r.Status == "Loaded") && Directory.Exists(Path.Combine(target, "lib")),
            $"confirming installs lib and lists it as loaded ({string.Join(", ", into.Rows)})");
        Check(into.PackageText.Contains("Installed Vehicle Library 2.0.0"), $"the install is reported ({into.PackageText})");
        Check(!into.HasUnsavedChanges, "installing leaves nothing to apply");
        Check(ModPackages.VerifyInstalled(Path.Combine(target, "lib")).Count == 0, "the installed files match the package");

        // Reinstalling asks to replace.
        into.InstallPackage(zip);
        ask = ConfirmDialog.Current;
        Check(ask is not null && ask.Message.Contains("already installed") && ask.ConfirmButton.Text == "Replace", $"a reinstall asks to replace ({ask?.Message})");
        Press(ask!.ConfirmButton);
        Check(into.PackageText.Contains("Installed"), "replacing installs again");

        // A tampered package is refused with its reasons and no dialog.
        var tampered = Path.Combine(packages, "tampered.zip");
        File.Copy(zip, tampered);
        using (var archive = System.IO.Compression.ZipFile.Open(tampered, System.IO.Compression.ZipArchiveMode.Update))
        {
            archive.GetEntry("lib/mod.json")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("lib/mod.json").Open());
            writer.Write("{ \"id\": \"lib\", \"name\": \"Evil\", \"version\": \"2.0.0\" }");
        }
        into.InstallPackage(tampered);
        Check(ConfirmDialog.Current is null && into.PackageText.Contains("cannot be installed") && into.PackageText.Contains(DiagnosticCodes.PackageHashMismatch),
            $"a tampered package is refused ({into.PackageText})");

        // Uninstall asks, then deletes.
        into.Select("lib");
        Press(Button(into, "Uninstall"));
        ask = ConfirmDialog.Current;
        Check(ask is not null && ask.Message.Contains("Delete Vehicle Library 2.0.0"), $"uninstall asks first ({ask?.Message})");
        Press(ask!.ConfirmButton);
        Check(!Directory.Exists(Path.Combine(target, "lib")) && !into.Rows.Any(r => r.Id == "lib") && into.PackageText.Contains("Uninstalled"),
            "confirming uninstalls lib");

        GD.Print($"[modpackages] {(_failed ? "FAILED" : "OK")}");
        StagedTest.QuitCleanly(this, _failed ? 1 : 0);
    }

    private ModManagerScreen Open(string modsRoot, string profilePath, string packages)
    {
        var screen = new ModManagerScreen { ModsRoot = modsRoot, ProfilePath = profilePath, PackagesFolder = packages };
        AddChild(screen);
        return screen;
    }

    private static Button Button(Node root, string text) => FindButton(root, text)
        ?? throw new InvalidOperationException($"no '{text}' button");

    private static Button? FindButton(Node node, string text)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Button b && b.Text == text)
                return b;
            if (FindButton(child, text) is { } found)
                return found;
        }
        return null;
    }

    // Presses a button the way a click does (an answered ConfirmDialog stops being Current at once).
    private static void Press(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(from))
            Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    private void Check(bool ok, string what)
    {
        GD.Print($"[modpackages] {(ok ? "PASS" : "FAIL")}: {what}");
        _failed |= !ok;
    }
}
