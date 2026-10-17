using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.ModTools;
using Mechanica.Core.Remote;
using Mechanica.Scripting.Editor;

namespace Mechanica.Scripting.Tests;

/// <summary>The mods.* and lua.* methods driven through the endpoint, as an agent calls them.</summary>
public sealed class ModToolsModuleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mechanica-tests", Guid.NewGuid().ToString("N"));
    private readonly RemoteEvents _events = new();
    private readonly JsonRpcEndpoint _endpoint;
    private readonly RemoteConnection _admin;
    private readonly ReloadHost _host = new();

    public ModToolsModuleTests()
    {
        var mods = Path.Combine(_root, "mods");
        Directory.CreateDirectory(Path.Combine(mods, "vanilla"));
        File.WriteAllText(Path.Combine(mods, "vanilla", "mod.json"), """{ "id": "vanilla", "name": "Mechanica", "version": "0.1.0" }""");
        var dispatcher = new ActionDispatcher();
        var access = new EndpointAccess().AddToken("admin", [.. EndpointAccess.All]).AddToken("watch", AgentScope.Observe);
        _endpoint = new JsonRpcEndpoint(dispatcher, new ActionLog(dispatcher, 60), 1, access, _events);
        var tools = new ModToolService(new ModWorkspace(mods, Path.Combine(_root, "mods.json"), LuaSyntax.Problem), _host,
            new ScriptSettings { InstructionBudget = 10_000 });
        _endpoint.AddModule(new ModToolsModule(tools, _events));
        _admin = Connect("admin");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private RemoteConnection Connect(string token)
    {
        var connection = _endpoint.Connect();
        Assert.False(Call(connection, "mechanica.auth", $$"""{ "token": "{{token}}" }""").TryGetProperty("error", out _));
        return connection;
    }

    private JsonElement Call(RemoteConnection connection, string method, string parameters = "{}")
    {
        var text = _endpoint.Handle(connection, $$"""{ "jsonrpc": "2.0", "id": 1, "method": "{{method}}", "params": {{parameters}} }""", 0);
        return JsonDocument.Parse(text!).RootElement.Clone();
    }

    private JsonElement Result(string method, string parameters = "{}")
    {
        var response = Call(_admin, method, parameters);
        Assert.False(response.TryGetProperty("error", out var error), error.ToString());
        return response.GetProperty("result");
    }

    private int Error(string method, string parameters = "{}", RemoteConnection? connection = null) =>
        Call(connection ?? _admin, method, parameters).GetProperty("error").GetProperty("code").GetInt32();

    [Fact]
    public void AnAgentMakesAModWritesAPartValidatesAndReloads()
    {
        Result("mods.create", """{ "mod": "wheels", "name": "Wheels" }""");
        var write = Result("mods.write", """{ "mod": "wheels", "path": "parts/crate.json", "text": "{ \"id\": \"crate\", \"name\": \"Crate\", \"category\": \"block\", \"mass\": -1, \"durability\": 3, \"friction\": 0.5, \"buoyancy\": 0.5, \"flammable\": true, \"size\": [1, 1, 1] }" }""");
        Assert.True(write.GetProperty("check").GetProperty("ok").GetBoolean());

        var report = Result("mods.validate", """{ "mod": "wheels" }""");
        Assert.False(report.GetProperty("ok").GetBoolean());
        var diagnostic = report.GetProperty("diagnostics")[0];
        Assert.Equal(("wheels", "$.mass"), (diagnostic.GetProperty("mod").GetString(), diagnostic.GetProperty("json_path").GetString()));

        var text = Result("mods.read", """{ "mod": "wheels", "path": "parts/crate.json" }""").GetProperty("text").GetString()!;
        Result("mods.write", JsonSerializer.Serialize(new { mod = "wheels", path = "parts/crate.json", text = text.Replace("-1", "4") }));
        Assert.True(Result("mods.validate", """{ "mod": "wheels" }""").GetProperty("ok").GetBoolean());
        Assert.Equal("wheels", Result("mods.list").GetProperty("mods").EnumerateArray().Last().GetProperty("id").GetString());

        Assert.Equal("f", Result("mods.reload").GetProperty("fingerprint").GetString());
        _host.ClientCount = 1;
        Assert.Equal(RpcErrors.Rejected, Error("mods.reload"));
    }

    [Fact]
    public void QuickChecksCatchBrokenJsonAndLua()
    {
        Result("mods.create", """{ "mod": "m" }""");

        var json = Result("mods.write", """{ "mod": "m", "path": "parts/a.json", "text": "{ oops" }""").GetProperty("check");
        var lua = Result("mods.write", """{ "mod": "m", "path": "scripts/a.lua", "text": "if then" }""").GetProperty("check");

        Assert.False(json.GetProperty("ok").GetBoolean());
        Assert.False(lua.GetProperty("ok").GetBoolean());
        Assert.False(Result("lua.check", """{ "mod": "m", "path": "scripts/a.lua" }""").GetProperty("ok").GetBoolean());
        Assert.Equal(["scripts/a.lua", "parts/a.json", "mod.json"],
            Result("mods.files", """{ "mod": "m" }""").EnumerateArray().Select(f => f.GetProperty("path").GetString()).Reverse());
    }

    [Fact]
    public void RefusalsAreRejectedErrorsAndBadParamsAreBadParams()
    {
        Assert.Equal(RpcErrors.Rejected, Error("mods.read", """{ "mod": "vanilla", "path": "../../etc/passwd.txt" }"""));
        Assert.Equal(RpcErrors.Rejected, Error("mods.write", """{ "mod": "vanilla", "path": "parts/x.json", "text": "{}" }"""));
        Assert.Equal(RpcErrors.BadParams, Error("mods.read", """{ "mod": "vanilla" }"""));
        Assert.Equal(RpcErrors.BadParams, Error("mods.read", """{ "mod": "vanilla", "path": "mod.json", "extra": 1 }"""));
    }

    [Fact]
    public void ModToolsNeedAdminButLuaChecksOnlyObserve()
    {
        var watcher = Connect("watch");

        Assert.Equal(RpcErrors.Forbidden, Error("mods.list", connection: watcher));
        Assert.Equal(RpcErrors.Forbidden, Error("lua.eval", """{ "source": "1" }""", watcher));
        Assert.True(Call(watcher, "lua.check", """{ "source": "return 1" }""").GetProperty("result").GetProperty("ok").GetBoolean());
        Assert.Equal(0, Call(watcher, "lua.messages").GetProperty("result").GetArrayLength());
    }

    [Fact]
    public void EvalAndImportWorkOverTheWire()
    {
        Result("mods.create", """{ "mod": "m" }""");

        Assert.Equal(7, Result("lua.eval", """{ "source": "3 + 4" }""").GetProperty("values")[0].GetDouble());
        Assert.Equal("sounds/beep.ogg", Result("mods.import", """{ "mod": "m", "kind": "sound", "file_name": "Beep.ogg", "base64": "AAEC" }""").GetProperty("path").GetString());
    }

    [Fact]
    public void LargeFilesFitInOneCall()
    {
        Result("mods.create", """{ "mod": "m" }""");
        var big = Convert.ToBase64String(new byte[ModFiles.MaxFileBytes]);

        Assert.Equal(ModFiles.MaxFileBytes, Result("mods.write", JsonSerializer.Serialize(new { mod = "m", path = "meshes/big.glb", base64 = big })).GetProperty("bytes").GetInt32());
    }

    [Fact]
    public void ValidateAndReloadArePushedOnTheModsTopic()
    {
        Call(_admin, "mechanica.subscribe", """{ "topics": ["mods"] }""");

        Result("mods.validate");
        Result("mods.reload");

        var kinds = _endpoint.DrainEvents(_admin).Select(n => JsonDocument.Parse(n).RootElement.GetProperty("params").GetProperty("data").GetProperty("kind").GetString());
        Assert.Equal(["validated", "reloaded"], kinds);
    }

    private sealed class ReloadHost : IModToolsHost
    {
        public int ClientCount { get; set; }

        public bool AllowReloadWithClients => false;

        public IReadOnlyList<ScriptMessage> PartMessages => [];

        public IReadOnlyList<ScriptMessage> BotMessages => [];

        public ModReloadResult Reload() => new("f", ["vanilla"], 0, 0, [], []);
    }
}
