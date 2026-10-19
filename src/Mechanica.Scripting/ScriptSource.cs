using Mechanica.Core.Data;

namespace Mechanica.Scripting;

/// <summary>A part's Lua file on disk.</summary>
/// <param name="Key">Qualified path, <c>mod_id:scripts/name.lua</c>: what errors and the editor show.</param>
/// <param name="ModId">The mod that owns the file. Scripts of one mod share a Lua state; mods are kept apart.</param>
/// <param name="FullPath">Absolute file path.</param>
public sealed record ScriptFile(string Key, string ModId, string FullPath);

/// <summary>Finds the script of a part type.</summary>
public interface IScriptSource
{
    /// <summary>The script of part type <paramref name="partId"/>, or null when it has none (or it can't be resolved).</summary>
    ScriptFile? FindScript(string partId);

    /// <summary>The mod scripts that run for the whole world (<c>mod.json</c> <c>scripts</c>), in mod load order.</summary>
    IReadOnlyList<ScriptFile> WorldScripts => [];

    /// <summary>
    /// The file <c>require(path)</c> loads for a script of <paramref name="modId"/>: a path in that mod, or
    /// <c>other_mod:path</c> in one of its dependencies. Null when there is no such script or the mod may not use it.
    /// </summary>
    ScriptFile? FindModule(string modId, string path) => null;
}

/// <summary>Resolves part scripts against loaded mods (D6: paths are relative to the mod root, or <c>other_mod:path</c>).</summary>
public sealed class ModScriptSource(ModLoadResult mods) : IScriptSource
{
    /// <inheritdoc/>
    public ScriptFile? FindScript(string partId)
    {
        if (mods.FindPart(partId) is not { Definition.Script: { } path } part)
            return null;
        if (!AssetPath.TryParse(path, part.ModId, AssetKind.Script, out var asset, out _))
            return null;
        var mod = mods.Mods.FirstOrDefault(m => m.Id == asset.ModId);
        return mod is null ? null : new ScriptFile(asset.ToString(), asset.ModId, asset.Resolve(mod.Directory));
    }

    /// <inheritdoc/>
    public IReadOnlyList<ScriptFile> WorldScripts =>
        [.. mods.Mods.SelectMany(m => (m.Manifest.Scripts ?? []).Select(path => Resolve(m, path))).OfType<ScriptFile>()];

    /// <inheritdoc/>
    public ScriptFile? FindModule(string modId, string path)
    {
        if (mods.Mods.FirstOrDefault(m => m.Id == modId) is not { } mod || Resolve(mod, path) is not { } file)
            return null;
        // A mod reaches into itself and the mods it depends on, which always load before it.
        return file.ModId == modId || (mod.Manifest.Dependencies ?? []).Any(d => d.Id == file.ModId) ? file : null;
    }

    // A script path as a mod writes it, in that mod; null when it names no script file there.
    private ScriptFile? Resolve(LoadedMod mod, string path)
    {
        if (!AssetPath.TryParse(path, mod.Id, AssetKind.Script, out var asset, out _))
            return null;
        var owner = mods.Mods.FirstOrDefault(m => m.Id == asset.ModId);
        var full = owner is null ? null : asset.Resolve(owner.Directory);
        return full is not null && File.Exists(full) ? new ScriptFile(asset.ToString(), asset.ModId, full) : null;
    }
}
