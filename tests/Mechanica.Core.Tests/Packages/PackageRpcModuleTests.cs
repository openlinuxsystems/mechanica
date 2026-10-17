using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Packages;
using Mechanica.Core.Remote;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Packages;

/// <summary>The package.* endpoint methods, called through a real <see cref="JsonRpcEndpoint"/>.</summary>
public class PackageRpcModuleTests : IDisposable
{
    private readonly TestMods _mods = new();
    private readonly string _packages;
    private readonly RemoteEvents _events = new();
    private readonly JsonRpcEndpoint _endpoint;
    private readonly RemoteConnection _connection;

    public PackageRpcModuleTests()
    {
        _mods.Mod("vanilla", "0.1.0")
            .Mod("wheels", "1.2.0", dependencies: """[{ "id": "vanilla" }]""")
            .Part("wheels", "wheel.json", TestMods.Block("wheel"))
            .Mod("cars", dependencies: """[{ "id": "wheels" }]""");
        _packages = Path.Combine(Path.GetDirectoryName(_mods.Root)!, "packages");
        var dispatcher = new ActionDispatcher();
        _endpoint = new JsonRpcEndpoint(dispatcher, new ActionLog(dispatcher, 60), 1, new EndpointAccess(), _events);
        _endpoint.AddModule(new PackageRpcModule(_mods.Root, _packages, _events));
        _connection = _endpoint.Connect();
    }

    public void Dispose() => _mods.Dispose();

    private JsonElement Call(string method, string parameters = "{}")
    {
        var text = _endpoint.Handle(_connection, $$"""{ "jsonrpc": "2.0", "id": 1, "method": "{{method}}", "params": {{parameters}} }""", 0);
        return JsonDocument.Parse(text!).RootElement.Clone();
    }

    private static JsonElement Result(JsonElement response) =>
        response.TryGetProperty("result", out var r) ? r : throw new Xunit.Sdk.XunitException(response.ToString());

    [Fact]
    public void ExportListInspectInstallVerifyAndUninstall()
    {
        var exported = Result(Call("package.export", """{ "id": "wheels" }"""));
        Assert.True(exported.GetProperty("ok").GetBoolean());
        Assert.Equal("wheels-1.2.0.zip", exported.GetProperty("file").GetString());

        var list = Result(Call("package.list"));
        Assert.Equal("wheels-1.2.0.zip", Assert.Single(list.GetProperty("packages").EnumerateArray()).GetProperty("file").GetString());

        var inspected = Result(Call("package.inspect", """{ "file": "wheels-1.2.0.zip" }"""));
        Assert.True(inspected.GetProperty("valid").GetBoolean());
        Assert.Equal("reinstall", inspected.GetProperty("change").GetString());
        Assert.Equal("wheels", inspected.GetProperty("manifest").GetProperty("id").GetString());

        var refused = Result(Call("package.install", """{ "file": "wheels-1.2.0.zip" }"""));
        Assert.False(refused.GetProperty("ok").GetBoolean());
        Assert.True(refused.GetProperty("needs_replace").GetBoolean());

        _endpoint.Handle(_connection, """{ "jsonrpc": "2.0", "id": 2, "method": "mechanica.subscribe", "params": { "topics": ["mods"] } }""", 0);
        var installed = Result(Call("package.install", """{ "file": "wheels-1.2.0.zip", "replace": true }"""));
        Assert.True(installed.GetProperty("ok").GetBoolean());
        Assert.Contains("\"installed\"", Assert.Single(_endpoint.DrainEvents(_connection)));

        var verified = Result(Call("package.verify", """{ "id": "wheels" }"""));
        Assert.True(verified.GetProperty("from_package").GetBoolean() && verified.GetProperty("unchanged").GetBoolean());

        var removed = Result(Call("package.uninstall", """{ "id": "wheels" }"""));
        Assert.True(removed.GetProperty("ok").GetBoolean());
        Assert.Equal("cars", Assert.Single(removed.GetProperty("dependents").EnumerateArray()).GetString());
        Assert.False(Directory.Exists(Path.Combine(_mods.Root, "wheels")));
    }

    [Theory]
    [InlineData("../wheels-1.2.0.zip")]
    [InlineData("/etc/passwd")]
    [InlineData("sub/x.zip")]
    [InlineData("notes.txt")]
    public void OnlyFileNamesInThePackagesFolderAreAccepted(string file)
    {
        var error = Call("package.inspect", $$"""{ "file": {{JsonSerializer.Serialize(file)}} }""").GetProperty("error");

        Assert.Equal(RpcErrors.BadParams, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public void MissingFilesAndModsAreRejected()
    {
        Assert.Equal(RpcErrors.Rejected, Call("package.install", """{ "file": "nope-1.0.0.zip" }""").GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(RpcErrors.Rejected, Call("package.verify", """{ "id": "nope" }""").GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(RpcErrors.BadParams, Call("package.uninstall", """{ "id": "../x" }""").GetProperty("error").GetProperty("code").GetInt32());
        Assert.False(Result(Call("package.uninstall", """{ "id": "vanilla" }""")).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void PackageMethodsNeedTheAdminScope()
    {
        var access = new EndpointAccess().AddToken("play", [AgentScope.Observe, AgentScope.Play]);
        var dispatcher = new ActionDispatcher();
        var endpoint = new JsonRpcEndpoint(dispatcher, new ActionLog(dispatcher, 60), 1, access, _events);
        endpoint.AddModule(new PackageRpcModule(_mods.Root, _packages));
        var connection = endpoint.Connect();
        endpoint.Handle(connection, """{ "jsonrpc": "2.0", "id": 1, "method": "mechanica.auth", "params": { "token": "play" } }""", 0);

        var answer = JsonDocument.Parse(endpoint.Handle(connection, """{ "jsonrpc": "2.0", "id": 2, "method": "package.list", "params": {} }""", 0)!).RootElement;

        Assert.Equal(RpcErrors.Forbidden, answer.GetProperty("error").GetProperty("code").GetInt32());
    }
}
