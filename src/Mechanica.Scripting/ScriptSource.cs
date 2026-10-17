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
}
