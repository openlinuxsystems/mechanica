using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.ModTools;
using Mechanica.Core.Remote;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.ModTools;

/// <summary>The editor.* methods driven through the endpoint, as an agent calls them.</summary>
public sealed class EditorModuleTests : IDisposable
{
    private readonly TestMods _mods = new();
    private readonly JsonRpcEndpoint _endpoint;
    private readonly RemoteConnection _admin;

    public EditorModuleTests()
    {
        Directory.CreateSymbolicLink(Path.Combine(_mods.Root, "vanilla"), Path.Combine(TestMods.RepoRoot, "mods", "vanilla"));
        _mods.File("m", "mod.json", """{ "id": "m", "name": "m", "version": "1.0.0", "dependencies": [{ "id": "vanilla" }] }""");
        var workspace = new ModWorkspace(_mods.Root, Path.Combine(Path.GetDirectoryName(_mods.Root)!, ModProfile.FileName),
            source => source.Contains("if then") ? new ScriptProblem(1, 3, "unexpected symbol") : null);
        var dispatcher = new ActionDispatcher();
        var access = new EndpointAccess().AddToken("admin", [.. EndpointAccess.All]).AddToken("play", AgentScope.Observe, AgentScope.Play);
        _endpoint = new JsonRpcEndpoint(dispatcher, new ActionLog(dispatcher, 60), 1, access, null);
        _endpoint.AddModule(new EditorModule(new EditorService(workspace, new EditorServiceTests.FakeHost())));
        _admin = Connect("admin");
    }

    public void Dispose() => _mods.Dispose();

    private RemoteConnection Connect(string token)
    {
        var connection = _endpoint.Connect();
        Assert.False(Call(connection, "mechanica.auth", JsonSerializer.Serialize(new { token })).TryGetProperty("error", out _));
        return connection;
    }

    private JsonElement Call(RemoteConnection connection, string method, string parameters = "{}")
    {
        var text = _endpoint.Handle(connection, $$"""{ "jsonrpc": "2.0", "id": 1, "method": "{{method}}", "params": {{parameters}} }""", 0);
        return JsonDocument.Parse(text!).RootElement.Clone();
    }

    private JsonElement Result(string method, object parameters)
    {
        var response = Call(_admin, method, JsonSerializer.Serialize(parameters));
        Assert.False(response.TryGetProperty("error", out var error), error.ToString());
        return response.GetProperty("result");
    }

    private int Error(string method, object parameters, RemoteConnection? connection = null) =>
        Call(connection ?? _admin, method, JsonSerializer.Serialize(parameters)).GetProperty("error").GetProperty("code").GetInt32();

    private static bool HasErrors(JsonElement diagnostics) =>
        diagnostics.EnumerateArray().Any(d => d.GetProperty("severity").GetString() == "error");

    [Fact]
    public void APartIsMadeEditedAndExportedWithDiagnosticsOnEveryWrite()
    {
        Assert.Contains("block", Result("editor.templates", new { }).GetProperty("parts").EnumerateArray().Select(t => t.GetString()));
        var made = Result("editor.part.new", new { mod = "m", template = "block", id = "crate", name = "Crate" });
        var doc = made.GetProperty("document").GetProperty("doc").GetString()!;
        Assert.False(HasErrors(made.GetProperty("diagnostics")));

        var broken = Result("editor.part.edit", new { doc, set = new { mass = -2 } });
        var problem = broken.GetProperty("diagnostics").EnumerateArray().Single(d => d.GetProperty("severity").GetString() == "error");
        Assert.Equal(("m", "$.mass"), (problem.GetProperty("mod").GetString(), problem.GetProperty("json_path").GetString()));
        Assert.False(HasErrors(Result("editor.part.undo", new { doc }).GetProperty("diagnostics")));
        Result("editor.part.edit", new { doc, label = "Heavier", set = new { mass = 9 } });

        var export = Result("editor.part.export", new { doc });

        Assert.True(export.GetProperty("ok").GetBoolean(), export.ToString());
        Assert.Equal("parts/crate.json", export.GetProperty("file").GetString());
        Assert.Equal(9f, _mods.Load().FindPart("m:crate")!.Definition.Mass);
        Assert.False(Result("editor.part.get", new { doc }).GetProperty("dirty").GetBoolean());
        Assert.NotEqual(0, Result("editor.part.contacts", new { doc }).GetArrayLength());
        Assert.Equal(JsonValueKind.Null, Result("editor.close", new { doc }).ValueKind);
        Assert.Equal(RpcErrors.Rejected, Error("editor.part.get", new { doc }));
    }

    [Fact]
    public void ABotIsBuiltNodeByNodeAndExported()
    {
        var doc = Result("editor.bot.new", new { mod = "m", template = "melee", id = "biter" }).GetProperty("document").GetProperty("doc").GetString()!;

        var added = Result("editor.bot.tree", new { doc, op = "add", path = Array.Empty<int>(), type = "wander" });
        Assert.Equal(4, added.GetProperty("path")[0].GetInt32());
        Assert.False(HasErrors(added.GetProperty("diagnostics")));
        Assert.Equal(RpcErrors.Rejected, Error("editor.bot.script_save", new { doc, code = "if then" }));
        Assert.Equal("scripts/bots/biter.lua", Result("editor.bot.script_save", new { doc, code = "-- biter\n" }).GetProperty("script").GetString());
        Assert.Equal("-- biter\n", Result("editor.bot.script", new { doc }).GetString());
        Result("editor.bot.edit", new { doc, set = new { health = 70 } });

        var export = Result("editor.bot.export", new { doc });

        Assert.True(export.GetProperty("ok").GetBoolean(), export.ToString());
        Assert.Equal(70, Core.Bots.BotBook.Load(_mods.Load()).FindBot("m:biter")!.Health);
    }

    [Fact]
    public void RecipesRaidsAndSoundBanksSaveWithDiagnostics()
    {
        var recipe = Result("editor.recipes.save", new { mod = "m", recipe = JsonDocument.Parse("""{ "id": "plank", "stations": ["fabricator"], "inputs": [{ "item": "scrap_metal", "count": 1 }], "outputs": [{ "item": "antenna", "count": 2 }], "time": 1 }""").RootElement });
        Assert.True(recipe.GetProperty("ok").GetBoolean(), recipe.ToString());
        Assert.Equal("recipes/plank.json", Result("editor.recipes.list", new { mod = "m" })[0].GetProperty("file").GetString());

        var raids = Result("editor.raids.save", new { mod = "m", raids = JsonDocument.Parse("""{ "levels": [{ "level": 1, "min_value": 1, "budget": 1, "bots": [{ "bot": "m:nobody", "count": 1 }] }] }""").RootElement });
        Assert.False(raids.GetProperty("ok").GetBoolean());
        Assert.True(HasErrors(raids.GetProperty("diagnostics")));
        Assert.Equal(JsonValueKind.Null, Result("editor.raids.get", new { mod = "m" }).ValueKind);

        _mods.File("m", "sounds/clank.ogg", "ogg");
        var bank = Result("editor.sounds.save", new { mod = "m", events = JsonDocument.Parse("""[{ "id": "clank", "clips": ["sounds/clank.ogg"] }]""").RootElement });
        Assert.True(bank.GetProperty("ok").GetBoolean(), bank.ToString());
        Assert.Equal("sounds/m.json", Result("editor.sounds.list", new { mod = "m" })[0].GetString());
    }

    [Fact]
    public void BatchesRunAndVanillaIsOnlyChangedThroughThem()
    {
        _mods.File("m", "editor/batches/b.json", "{}");

        Assert.Equal("parts", Result("editor.batch.run", new { mod = "m", path = "editor/batches/b.json" }).GetProperty("kind").GetString());
        Assert.Equal(RpcErrors.Rejected, Error("editor.part.new", new { mod = "vanilla", template = "block", id = "x" }));
        Assert.Equal(RpcErrors.Rejected, Error("editor.batch.run", new { mod = "m", path = "../vanilla/mod.json" }));
        Assert.Equal(RpcErrors.BadParams, Error("editor.part.new", new { mod = "m" }));
    }

    [Fact]
    public void EditorMethodsNeedAdmin()
    {
        var player = Connect("play");

        Assert.Equal(RpcErrors.Forbidden, Error("editor.templates", new { }, player));
        Assert.Equal(RpcErrors.Forbidden, Error("editor.part.new", new { mod = "m", template = "block", id = "x" }, player));
    }
}
