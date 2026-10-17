using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.ModTools;
using Mechanica.Core.Remote;

namespace Mechanica.Scripting.Editor;

/// <summary>Params of <c>mods.enable</c>.</summary>
/// <param name="Mod">Mod id.</param>
/// <param name="Enabled">False disables it (and the mods that depend on it).</param>
public sealed record ModEnableParams(string Mod, bool Enabled = true);

/// <summary>Params of <c>mods.move</c>.</summary>
/// <param name="Mod">Mod id.</param>
/// <param name="Index">Where in the load order it should go (0 is first); it stops at its dependencies.</param>
public sealed record ModMoveParams(string Mod, int Index);

/// <summary>Params of <c>mods.create</c>.</summary>
/// <param name="Mod">New mod id, lowercase snake_case; also its folder name.</param>
/// <param name="Name">Display name; defaults to the id.</param>
/// <param name="Version">Semantic version; defaults to 0.1.0.</param>
/// <param name="Description">One paragraph about the mod.</param>
/// <param name="Authors">Author names.</param>
/// <param name="Dependencies">Mod ids it needs; defaults to vanilla.</param>
public sealed record ModCreateParams(string Mod, string? Name = null, string? Version = null, string? Description = null,
    List<string>? Authors = null, List<string>? Dependencies = null);

/// <summary>Params of methods that take one mod.</summary>
/// <param name="Mod">Mod id.</param>
public sealed record ModParams(string Mod);

/// <summary>Params of <c>mods.validate</c>.</summary>
/// <param name="Mod">Only this mod's problems (it is enabled for the check); null checks everything enabled.</param>
public sealed record ModValidateParams(string? Mod = null);

/// <summary>Params of <c>mods.files</c>.</summary>
/// <param name="Mod">Mod id.</param>
/// <param name="Folder">Folder inside the mod; null lists the whole mod.</param>
public sealed record ModFilesParams(string Mod, string? Folder = null);

/// <summary>Params of <c>mods.read</c> and <c>mods.delete</c>.</summary>
/// <param name="Mod">Mod id.</param>
/// <param name="Path">Forward-slash path inside the mod, like <c>parts/wheel.json</c>.</param>
public sealed record ModFileParams(string Mod, string Path);

/// <summary>Params of <c>mods.write</c>.</summary>
/// <param name="Mod">Mod id.</param>
/// <param name="Path">Forward-slash path inside the mod.</param>
/// <param name="Text">Content of a text file (.json, .lua, .gltf, .md, .txt).</param>
/// <param name="Base64">Content of a binary file (.glb, .bin, .png, .ogg, .wav), base64.</param>
public sealed record ModWriteParams(string Mod, string Path, string? Text = null, string? Base64 = null);

/// <summary>Params of <c>mods.import</c>.</summary>
/// <param name="Mod">Mod id.</param>
/// <param name="Kind"><c>mesh</c>, <c>script</c>, <c>texture</c> or <c>sound</c>.</param>
/// <param name="FileName">The file's name, like <c>Big Wheel.glb</c>; it is cleaned to snake_case.</param>
/// <param name="Base64">The file, base64.</param>
/// <param name="Companions">A .gltf's buffers and images by relative path, base64.</param>
public sealed record ModImportParams(string Mod, string Kind, string FileName, string Base64, Dictionary<string, string>? Companions = null);

/// <summary>Params of <c>lua.check</c>.</summary>
/// <param name="Source">Script text to compile.</param>
/// <param name="Mod">Or: the mod of a script file.</param>
/// <param name="Path">And the file's path inside it.</param>
public sealed record LuaCheckParams(string? Source = null, string? Mod = null, string? Path = null);

/// <summary>Params of <c>lua.eval</c>.</summary>
/// <param name="Source">Lua to run; a single expression returns its value.</param>
public sealed record LuaEvalParams(string Source);

/// <summary>Params of <c>lua.messages</c>.</summary>
/// <param name="SinceTick">Only lines written after this tick.</param>
/// <param name="Kinds">Only these kinds: print, warning, error.</param>
/// <param name="Script">Only this script (<c>mod:scripts/x.lua</c>).</param>
/// <param name="Limit">Newest lines to return, 1 to 500; default 100.</param>
public sealed record LuaMessagesParams(long? SinceTick = null, List<string>? Kinds = null, string? Script = null, int? Limit = null);

/// <summary>
/// The mod workspace as endpoint methods (AI support PR 6): <c>mods.*</c> for the Mod Manager, files, import,
/// validation and reload, and <c>lua.*</c> for compile checks, the script consoles and a sandboxed eval. Every
/// method that changes or runs something needs the admin scope; the read-only Lua tools need observe.
/// </summary>
public sealed class ModToolsModule(ModToolService tools, RemoteEvents? events = null) : IRpcModule
{
    private ModWorkspace Workspace => tools.Workspace;

    /// <inheritdoc />
    public IEnumerable<RpcMethodInfo> Methods =>
    [
        RpcMethod.Create<NoArgs>("mods.list", AgentScope.Admin,
            "Installed mods in load order: id, name, version, enabled, can_disable, loaded, dependencies; plus profile entries that are not installed (missing) and folders whose mod.json is broken.",
            (_, _) => Run(() => Workspace.List())),
        RpcMethod.Create<ModEnableParams>("mods.enable", AgentScope.Admin,
            "Enables or disables a mod like the Mod Manager (enabling pulls in its dependencies, disabling its dependents) and saves the profile. Takes effect on mods.reload. Returns the mods that changed.",
            (_, a) => Run(() => new { changed = Workspace.SetEnabled(a.Mod, a.Enabled) })),
        RpcMethod.Create<ModMoveParams>("mods.move", AgentScope.Admin,
            "Moves a mod in the load order (it stops at its dependencies and dependents) and saves the profile. Returns the index it ended up at.",
            (_, a) => Run(() => new { index = Workspace.Move(a.Mod, a.Index) })),
        RpcMethod.Create<NoArgs>("mods.conflicts", AgentScope.Admin,
            "Conflicts between the enabled mods: incompatible (not loaded) and override_clash (two mods override one part; the later wins).",
            (_, _) => Run(() => Workspace.Conflicts())),
        RpcMethod.Create<ModCreateParams>("mods.create", AgentScope.Admin,
            "Creates a new mod folder with mod.json and empty parts/ and scripts/. It depends on vanilla unless dependencies says otherwise.",
            (_, a) => Run(() => new { manifest = Workspace.Create(a.Mod, a.Name, a.Version, a.Description, a.Authors, a.Dependencies) })),
        RpcMethod.Create<ModFilesParams>("mods.files", AgentScope.Admin,
            "Lists the files of a mod (or one folder of it): path, size and modified time.",
            (_, a) => Run(() => Workspace.Files.List(a.Mod, a.Folder))),
        RpcMethod.Create<ModFileParams>("mods.read", AgentScope.Admin,
            "Reads a file of a mod: text for .json, .lua, .gltf, .md and .txt, base64 for the rest. Up to 2 MiB.",
            (_, a) => Run(() => Workspace.Files.Read(a.Mod, a.Path))),
        RpcMethod.Create<ModWriteParams>("mods.write", AgentScope.Admin,
            "Writes a file of a mod (text or base64, up to 2 MiB), creating folders. Paths stay inside the mod; vanilla only changes under editor/. Returns the bytes written and a quick check: JSON syntax for .json, a compile for .lua. Run mods.validate for the full check.",
            (_, a) => Run(() => new { bytes = Workspace.Files.Write(a.Mod, a.Path, a.Text, a.Base64), check = QuickCheck(a.Path, a.Text) })),
        RpcMethod.Create<ModFileParams>("mods.delete", AgentScope.Admin,
            "Deletes a file of a mod, and folders it leaves empty. mod.json can't be deleted.",
            (_, a) => Run(() =>
            {
                Workspace.Files.Delete(a.Mod, a.Path);
                return (object?)null;
            })),
        RpcMethod.Create<ModImportParams>("mods.import", AgentScope.Admin,
            "Imports an asset like the Part Editor: into meshes/, scripts/, textures/ or sounds/ under a clean snake_case name, reusing an identical file. A .gltf brings its buffers and images in companions. Returns the path to write in data.",
            (_, a) => Run(() => new { path = tools.Import(a.Mod, a.Kind, a.FileName, a.Base64, a.Companions) })),
        RpcMethod.Create<ModValidateParams>("mods.validate", AgentScope.Admin,
            "Loads all content as a world would (parts, sounds, world generation, recipes, bots, raids, quests, characters) and compiles every Lua script. Returns ok, error and warning counts, the mods that would load, the content fingerprint and every diagnostic (domain, severity, code, mod, file, json_path, line, message). A mod with part errors is left out, so its recipes and bots are checked once its parts load.",
            (_, a) => Run(() => Publish("validated", Workspace.Validate(a.Mod)))),
        RpcMethod.Create<NoArgs>("mods.reload", AgentScope.Admin,
            "Reloads the enabled mods into the running world: parts, sounds, part scripts and bot types. World generation, recipes, raids, quests and characters change only in a new world (listed in next_world). Refused while clients are joined unless the host runs with --ai-allow-reload-with-clients, which drops clients whose mods no longer match (dropped_peers).",
            (_, _) => Run(() => Publish("reloaded", tools.Reload()))),
        RpcMethod.Create<LuaCheckParams>("lua.check", AgentScope.Observe,
            "Compiles a script (source text, or mod and path of a file) the way the host does, without running it. Returns ok, or the line, column and message of the first error.",
            (_, a) => Run(() => tools.Check(a.Source, a.Mod, a.Path))),
        RpcMethod.Create<LuaMessagesParams>("lua.messages", AgentScope.Observe,
            "The newest lines of the part and bot script consoles, oldest first: source (parts or bots), kind (print, warning, error), script, creation, part, text, tick. Subscribe to the messages topic to have them pushed instead.",
            (_, a) => Run(() => tools.Messages(a.SinceTick, a.Kinds, a.Script, a.Limit))),
        RpcMethod.Create<LuaEvalParams>("lua.eval", AgentScope.Admin,
            "Runs Lua once in a throwaway sandbox with what part scripts start with (safe core libraries and vec3, no io, files or world) under the host's instruction budget. A single expression returns its value. Returns ok, values, printed output and error.",
            (_, a) => Run(() => tools.Eval(a.Source))),
    ];

    private object? QuickCheck(string path, string? text)
    {
        if (text is null)
            return null;
        if (path.EndsWith(".lua", StringComparison.Ordinal))
            return tools.Check(text, null, null);
        if (!path.EndsWith(".json", StringComparison.Ordinal))
            return null;
        var diagnostics = new List<DataDiagnostic>();
        ModLoader.ParseJson<JsonElementHolder>(text, path, diagnostics);
        return diagnostics.FirstOrDefault() is { } d
            ? new ScriptCheckResult(false, d.Line, null, $"{d.JsonPath}: {d.Message}")
            : new ScriptCheckResult(true, null, null, null);
    }

    private T Publish<T>(string kind, T result) where T : class
    {
        events?.Publish("mods", () => new { kind, result });
        return result;
    }

    private static JsonElement? Run<T>(Func<T> call)
    {
        try
        {
            return call() is { } value ? RpcMethod.Value(value) : null;
        }
        catch (ModToolException e)
        {
            throw new RpcException(RpcErrors.Rejected, e.Message);
        }
    }

    // Any JSON object parses into this, so only the syntax is checked.
    private sealed class JsonElementHolder
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, JsonElement>? Values { get; init; }
    }
}
