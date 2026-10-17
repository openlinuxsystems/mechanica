using Mechanica.Core.Data;
using Mechanica.Core.ModTools;
using Mechanica.Scripting;
using Mechanica.Scripting.Editor;
using Mechanica.Game.Net;

namespace Mechanica.Game.Remote;

/// <summary>
/// The running game behind the mod tools' endpoint methods: reloads the host's mods into the world the way the
/// Test Sandbox does, drops clients whose mods no longer match, and reads the part and bot script consoles.
/// </summary>
public sealed class ModToolsHost(Main main, string modsRoot, string profilePath) : IModToolsHost
{
    /// <inheritdoc />
    public int ClientCount => main.Session.State == NetSession.SessionState.Hosting ? main.Session.ReadyPeers.Count : 0;

    /// <inheritdoc />
    public bool AllowReloadWithClients => main.Options?.AiAllowReloadWithClients == true;

    /// <inheritdoc />
    public IReadOnlyList<ScriptMessage> PartMessages => main.Scripts.Runtime.Messages;

    /// <inheritdoc />
    public IReadOnlyList<ScriptMessage> BotMessages => main.Bots.Runtime?.Messages ?? [];

    /// <inheritdoc />
    public ModReloadResult Reload()
    {
        var content = GameContent.Load(modsRoot, ModProfile.Load(profilePath, []));
        main.ReloadMods(content.Mods);
        // These are read once when a world starts; a changed copy waits for the next world.
        var nextWorld = new List<string>();
        if (content.World.Fingerprint != main.WorldGen?.Fingerprint)
            nextWorld.Add("world");
        if (content.Recipes.Fingerprint != main.Recipes?.Fingerprint)
            nextWorld.Add("recipes");
        if (content.Raids.Fingerprint != main.RaidTable?.Fingerprint)
            nextWorld.Add("raids");
        if (content.Quests.Fingerprint != main.QuestBook?.Fingerprint)
            nextWorld.Add("quests");
        if (content.Characters.Fingerprint != main.CharacterBook?.Fingerprint)
            nextWorld.Add("characters");
        var dropped = main.Session.State == NetSession.SessionState.Hosting ? main.Session.RecheckContent() : [];
        var diagnostics = content.Mods.Diagnostics.Concat(content.Sounds.Diagnostics).Concat(content.Bots.Diagnostics).ToList();
        var errors = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        return new ModReloadResult(main.Content!.Fingerprint, [.. content.Mods.Mods.Select(m => m.Id)], errors, diagnostics.Count - errors,
            nextWorld, dropped);
    }
}
