using Mechanica.Core.Data;
using Mechanica.Core.ModTools;

namespace Mechanica.Scripting.Editor;

/// <summary>What the game reports after <see cref="IModToolsHost.Reload"/>.</summary>
/// <param name="Fingerprint">The join fingerprint of the content now running.</param>
/// <param name="LoadedMods">Mods now loaded, in load order.</param>
/// <param name="Errors">Errors the reload found (the mods with them were left out).</param>
/// <param name="Warnings">Warnings the reload found.</param>
/// <param name="NextWorld">
/// Content areas whose files changed but which only take effect in a new world: <c>world</c>, <c>recipes</c>,
/// <c>raids</c>, <c>quests</c>, <c>characters</c>.
/// </param>
/// <param name="DroppedPeers">Clients dropped because their content no longer matched.</param>
public sealed record ModReloadResult(string Fingerprint, IReadOnlyList<string> LoadedMods, int Errors, int Warnings,
    IReadOnlyList<string> NextWorld, IReadOnlyList<int> DroppedPeers);

/// <summary>The running game behind the mod tools: the host's reload, its clients and its script consoles.</summary>
public interface IModToolsHost
{
    /// <summary>Clients joined to this host.</summary>
    int ClientCount { get; }

    /// <summary>True when the host was started with <c>--ai-allow-reload-with-clients</c>.</summary>
    bool AllowReloadWithClients { get; }

    /// <summary>
    /// Loads the enabled mods again and switches the world to them (parts, sounds, part scripts, bot types),
    /// then drops clients whose content no longer matches.
    /// </summary>
    ModReloadResult Reload();

    /// <summary>Recent part script messages, oldest first.</summary>
    IReadOnlyList<ScriptMessage> PartMessages { get; }

    /// <summary>Recent bot script messages, oldest first.</summary>
    IReadOnlyList<ScriptMessage> BotMessages { get; }
}

/// <summary>One script console line as the endpoint sends it.</summary>
/// <param name="Source"><c>parts</c> or <c>bots</c>.</param>
/// <param name="Kind"><c>print</c>, <c>warning</c> or <c>error</c>.</param>
/// <param name="Script">Qualified script path.</param>
/// <param name="Creation">Creation the line came from, if any.</param>
/// <param name="Part">Part (or bot) the line came from, if any.</param>
/// <param name="Text">The line.</param>
/// <param name="Tick">Runtime tick it was written on.</param>
public sealed record ScriptMessageInfo(string Source, string Kind, string Script, int? Creation, int? Part, string Text, long Tick);

/// <summary>The result of checking one script.</summary>
/// <param name="Ok">True when it compiles.</param>
/// <param name="Line">1-based line of the error.</param>
/// <param name="Column">0-based column of the error.</param>
/// <param name="Message">The error.</param>
public sealed record ScriptCheckResult(bool Ok, int? Line, int? Column, string? Message);

/// <summary>
/// Everything the mod workspace endpoint methods do (<c>mods.*</c> and <c>lua.*</c>), over a
/// <see cref="ModWorkspace"/>, the Lua sandbox and the running game. Each method takes and returns plain
/// records, so the endpoint module only maps names to them.
/// </summary>
public sealed class ModToolService(ModWorkspace workspace, IModToolsHost host, ScriptSettings scriptSettings)
{
    /// <summary>Most console lines <see cref="Messages"/> returns.</summary>
    public const int MaxMessages = 500;

    /// <summary>The workspace.</summary>
    public ModWorkspace Workspace { get; } = workspace;

    /// <summary>
    /// Reloads the mods into the running world. Refused while clients are joined, since their content would no
    /// longer match, unless the host allows it (then mismatched clients are dropped).
    /// </summary>
    public ModReloadResult Reload()
    {
        if (host.ClientCount > 0 && !host.AllowReloadWithClients)
            throw new ModToolException(
                $"{host.ClientCount} client(s) are joined and would no longer match the host's mods. Wait until they leave, "
                + "or start the host with --ai-allow-reload-with-clients to drop clients whose mods differ.");
        return host.Reload();
    }

    /// <summary>Compiles a script given as text, or a file of a mod.</summary>
    public ScriptCheckResult Check(string? source, string? modId, string? path)
    {
        if ((source is null) == (modId is null || path is null))
            throw new ModToolException("Give either source, or mod and path.");
        if (source is null)
        {
            if (!path!.EndsWith(".lua", StringComparison.Ordinal))
                throw new ModToolException($"'{path}' is not a .lua file.");
            source = Workspace.Files.Read(modId!, path).Text!;
        }
        return LuaSyntax.Problem(source) is { } problem
            ? new ScriptCheckResult(false, problem.Line, problem.Column, problem.Message)
            : new ScriptCheckResult(true, null, null, null);
    }

    /// <summary>Runs Lua in a throwaway sandbox (no world access) under the host's instruction budget.</summary>
    public LuaEvalResult Eval(string source) => LuaSandbox.Eval(source, scriptSettings);

    /// <summary>
    /// The newest script console lines, oldest first: part scripts and bot scripts, optionally only after a tick,
    /// of some kinds, or of one script.
    /// </summary>
    public IReadOnlyList<ScriptMessageInfo> Messages(long? sinceTick, IReadOnlyList<string>? kinds, string? script, int? limit)
    {
        var count = limit ?? 100;
        if (count is < 1 or > MaxMessages)
            throw new ModToolException($"limit must be 1 to {MaxMessages}.");
        var wanted = kinds?.ToHashSet(StringComparer.Ordinal);
        if (wanted?.FirstOrDefault(k => k is not ("print" or "warning" or "error")) is { } bad)
            throw new ModToolException($"'{bad}' is not a message kind (print, warning, error).");
        var lines = host.PartMessages.Select(m => Info("parts", m)).Concat(host.BotMessages.Select(m => Info("bots", m)))
            .Where(m => (sinceTick is null || m.Tick > sinceTick) && (wanted is null || wanted.Contains(m.Kind)) && (script is null || m.Script == script))
            .OrderBy(m => m.Tick)
            .ToList();
        return lines[Math.Max(0, lines.Count - count)..];
    }

    /// <summary>Imports an asset sent as base64.</summary>
    public string Import(string modId, string kind, string fileName, string base64, IReadOnlyDictionary<string, string>? companions)
    {
        if (!Enum.TryParse<AssetKind>(kind, ignoreCase: true, out var assetKind) || !Enum.IsDefined(assetKind) || char.IsDigit(kind[0]))
            throw new ModToolException($"'{kind}' is not an asset kind (mesh, script, texture, sound).");
        return Workspace.Import(modId, assetKind, fileName, Decode(base64, fileName),
            companions?.ToDictionary(c => c.Key, c => Decode(c.Value, c.Key)));
    }

    private static byte[] Decode(string base64, string name)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            return bytes.Length <= ModFiles.MaxFileBytes ? bytes : throw new ModToolException($"{name} is larger than {ModFiles.MaxFileBytes} bytes.");
        }
        catch (FormatException)
        {
            throw new ModToolException($"{name} is not valid base64.");
        }
    }

    private static ScriptMessageInfo Info(string source, ScriptMessage m) =>
        new(source, m.Kind.ToString().ToLowerInvariant(), m.Script, m.Creation, m.Part, m.Text, m.Tick);
}
