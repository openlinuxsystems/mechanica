using Godot;
using Mechanica.Core.Data;

namespace Mechanica.Game.Mods;

/// <summary>
/// Where mods and the player's mod profile live (D6). Exported builds read <c>mods/</c> next to the
/// executable (inside the app bundle's <c>Contents/Resources</c> on macOS); running from the source tree reads the repo's <c>mods/</c>. The profile is
/// <c>user://mods.json</c>, so reinstalling or updating a mod never resets it.
/// </summary>
public static class ModPaths
{
    /// <summary>The mods folder for this run.</summary>
    public static string ModsRoot => ShippedFolder("mods");

    /// <summary>
    /// A folder the release ships beside the game (<c>mods</c>, <c>docs</c>): next to the executable, or in
    /// <c>Contents/Resources</c> of the macOS app bundle; the repo's folder when running from the source tree.
    /// </summary>
    public static string ShippedFolder(string name)
    {
        if (!OS.HasFeature("template"))
            return Path.GetFullPath(ProjectSettings.GlobalizePath($"res://../{name}"));
        var executableFolder = Path.GetDirectoryName(OS.GetExecutablePath())!;
        return OS.HasFeature("macos")
            ? Path.GetFullPath(Path.Combine(executableFolder, "..", "Resources", name))
            : Path.Combine(executableFolder, name);
    }

    /// <summary>Absolute path of the player's <see cref="ModProfile"/>.</summary>
    public static string ProfilePath => ProjectSettings.GlobalizePath($"user://{ModProfile.FileName}");

    /// <summary>Loads the content this run uses: the mods the saved profile enables, in its load order.</summary>
    public static ModLoadResult LoadContent() => ModLoader.LoadWithProfile(ModsRoot, ProfilePath);
}
