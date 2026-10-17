using Godot;
using Mechanica.Core.Data;

namespace Mechanica.Game.Mods;

/// <summary>
/// Where mods and the player's mod profile live (D6). Exported builds read <c>mods/</c> next to the
/// executable; running from the source tree reads the repo's <c>mods/</c>. The profile is
/// <c>user://mods.json</c>, so reinstalling or updating a mod never resets it.
/// </summary>
public static class ModPaths
{
    /// <summary>The mods folder for this run.</summary>
    public static string ModsRoot => OS.HasFeature("template")
        ? Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "mods")
        : Path.GetFullPath(ProjectSettings.GlobalizePath("res://../mods"));

    /// <summary>Absolute path of the player's <see cref="ModProfile"/>.</summary>
    public static string ProfilePath => ProjectSettings.GlobalizePath($"user://{ModProfile.FileName}");

    /// <summary>Where the Mod Manager writes exported packages by default (<c>user://mod_packages</c>).</summary>
    public static string PackagesRoot => ProjectSettings.GlobalizePath("user://mod_packages");

    /// <summary>Loads the content this run uses: the mods the saved profile enables, in its load order.</summary>
    public static ModLoadResult LoadContent() => ModLoader.LoadWithProfile(ModsRoot, ProfilePath);
}
