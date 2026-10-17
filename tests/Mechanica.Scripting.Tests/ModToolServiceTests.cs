using Mechanica.Core.ModTools;
using Mechanica.Scripting.Editor;

namespace Mechanica.Scripting.Tests;

public sealed class ModToolServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mechanica-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeHost _host = new();
    private readonly ModToolService _tools;

    public ModToolServiceTests()
    {
        var mods = Path.Combine(_root, "mods");
        Directory.CreateDirectory(Path.Combine(mods, "m", "scripts"));
        File.WriteAllText(Path.Combine(mods, "m", "mod.json"), """{ "id": "m", "name": "m", "version": "1.0.0" }""");
        File.WriteAllText(Path.Combine(mods, "m", "scripts", "bad.lua"), "local x = = 1");
        _tools = new ModToolService(new ModWorkspace(mods, Path.Combine(_root, "mods.json"), LuaSyntax.Problem), _host,
            new ScriptSettings { InstructionBudget = 10_000 });
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ReloadIsRefusedWhileClientsAreJoinedUnlessAllowed()
    {
        _host.ClientCount = 1;
        Assert.Contains("--ai-allow-reload-with-clients", Assert.Throws<ModToolException>(_tools.Reload).Message);
        Assert.Equal(0, _host.Reloads);

        _host.AllowReloadWithClients = true;
        Assert.Equal([2], _tools.Reload().DroppedPeers);

        _host.ClientCount = 0;
        _host.AllowReloadWithClients = false;
        _tools.Reload();
        Assert.Equal(2, _host.Reloads);
    }

    [Fact]
    public void ChecksScriptsFromTextOrModFiles()
    {
        Assert.True(_tools.Check("return 1", null, null).Ok);
        var bad = _tools.Check(null, "m", "scripts/bad.lua");
        Assert.False(bad.Ok);
        Assert.Equal(1, bad.Line);
        Assert.Throws<ModToolException>(() => _tools.Check("x", "m", "scripts/bad.lua"));
        Assert.Throws<ModToolException>(() => _tools.Check(null, "m", "mod.json"));
    }

    [Fact]
    public void ValidateIncludesScriptErrors()
    {
        var report = _tools.Workspace.Validate("m");

        Assert.Contains(report.Diagnostics, d => d is { Domain: "scripts", Mod: "m", Line: 1 });
    }

    [Fact]
    public void MessagesMergeBothConsolesAndFilter()
    {
        _host.PartMessages.AddRange([
            new ScriptMessage(ScriptMessageKind.Print, "m:scripts/a.lua", 1, 2, "hello", 5),
            new ScriptMessage(ScriptMessageKind.Error, "m:scripts/a.lua", 1, 2, "boom", 9),
        ]);
        _host.BotMessages.Add(new ScriptMessage(ScriptMessageKind.Warning, "m:scripts/bot.lua", null, 7, "refused", 7));

        Assert.Equal(["hello", "refused", "boom"], _tools.Messages(null, null, null, null).Select(m => m.Text));
        Assert.Equal(["bots", "parts"], _tools.Messages(5, null, null, null).Select(m => m.Source));
        Assert.Equal(["boom"], _tools.Messages(null, ["error"], null, null).Select(m => m.Text));
        Assert.Equal(["refused"], _tools.Messages(null, null, "m:scripts/bot.lua", null).Select(m => m.Text));
        Assert.Equal(["boom"], _tools.Messages(null, null, null, 1).Select(m => m.Text));
        Assert.Throws<ModToolException>(() => _tools.Messages(null, ["shout"], null, null));
        Assert.Throws<ModToolException>(() => _tools.Messages(null, null, null, 0));
    }

    [Fact]
    public void ImportsBase64Assets()
    {
        Assert.Equal("textures/icon.png", _tools.Import("m", "texture", "Icon.png", Convert.ToBase64String([1]), null));
        Assert.Throws<ModToolException>(() => _tools.Import("m", "1", "Icon.png", "AA==", null));
        Assert.Throws<ModToolException>(() => _tools.Import("m", "video", "Icon.png", "AA==", null));
        Assert.Throws<ModToolException>(() => _tools.Import("m", "texture", "Icon.png", "!!", null));
    }

    [Fact]
    public void EvalRunsInTheSandbox()
    {
        Assert.Equal(6, _tools.Eval("2 * 3").Values[0]!.GetValue<double>());
    }

    private sealed class FakeHost : IModToolsHost
    {
        public int ClientCount { get; set; }

        public bool AllowReloadWithClients { get; set; }

        public int Reloads { get; private set; }

        public List<ScriptMessage> PartMessages { get; } = [];

        public List<ScriptMessage> BotMessages { get; } = [];

        IReadOnlyList<ScriptMessage> IModToolsHost.PartMessages => PartMessages;

        IReadOnlyList<ScriptMessage> IModToolsHost.BotMessages => BotMessages;

        public ModReloadResult Reload()
        {
            Reloads++;
            return new ModReloadResult("f", ["m"], 0, 0, [], ClientCount > 0 ? [2] : []);
        }
    }
}
