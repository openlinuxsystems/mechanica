using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Remote;
using Mechanica.Core.Reports;
using Codes = Mechanica.Core.Remote.JsonRpcEndpoint.Codes;

namespace Mechanica.Core.Tests.Reports;

/// <summary>The <c>reports.*</c> methods over the endpoint: scopes, errors, describe and the reports event.</summary>
public sealed class ReportsModuleTests : IDisposable
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0, 4, 0, 0, 0, 2, 8, 6, 0, 0, 0];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mechanica-reports-rpc-" + Guid.NewGuid().ToString("N"));
    private readonly ActionDispatcher _dispatcher = new();
    private readonly ActionLog _log;
    private readonly RecentActivity _activity;
    private readonly RemoteEvents _events = new();
    private readonly JsonRpcEndpoint _endpoint;
    private Screenshot _screen = new(Png, null);

    public ReportsModuleTests()
    {
        _log = new ActionLog(_dispatcher, 60);
        _activity = new RecentActivity(_dispatcher, 60);
        var reporter = new Reporter(new ReportStore(_root, new DateTimeOffset(2026, 10, 17, 19, 0, 0, TimeSpan.Zero)), _activity,
            () => new ReportContext { Seed = 9 }, () => _screen);
        var access = new EndpointAccess().AddToken("watch", AgentScope.Observe).AddToken("root", AgentScope.Observe, AgentScope.Play, AgentScope.Admin);
        _endpoint = new JsonRpcEndpoint(_dispatcher, _log, 1, access, _events);
        _endpoint.AddModule(new ReportsModule(reporter, _events));
    }

    public void Dispose()
    {
        _activity.Dispose();
        _log.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private RemoteConnection Connect(string token)
    {
        var connection = _endpoint.Connect();
        Assert.Null(Code(Send(connection, "mechanica.auth", $$"""{ "token": "{{token}}" }""")));
        return connection;
    }

    private JsonElement Send(RemoteConnection connection, string method, string parameters = "{}")
    {
        var text = _endpoint.Handle(connection, $$"""{ "jsonrpc": "2.0", "id": 1, "method": "{{method}}", "params": {{parameters}} }""", 0);
        Assert.NotNull(text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static int? Code(JsonElement response) => response.TryGetProperty("error", out var e) ? e.GetProperty("code").GetInt32() : null;

    [Fact]
    public void AnObserverWritesAndReadsReports()
    {
        var watcher = Connect("watch");

        var note = Send(watcher, "reports.note", """{ "title": "Lift spot", "text": "By the spawn.", "tags": ["ux"] }""").GetProperty("result");
        var bug = Send(watcher, "reports.bug", """
            { "title": "Block sinks", "steps": ["Place a block"], "expected": "It rests", "actual": "It sinks", "severity": "critical", "author": "Grok" }
            """).GetProperty("result");

        Assert.Equal("20261017-190000/note-001", note.GetProperty("id").GetString());
        Assert.StartsWith("agent ", note.GetProperty("author").GetString());
        Assert.Equal(("critical", "Grok", "bug-001.png"), (bug.GetProperty("severity").GetString(), bug.GetProperty("author").GetString(), bug.GetProperty("screenshot").GetString()));
        var list = Send(watcher, "reports.list", """{ "kind": "bug" }""").GetProperty("result");
        Assert.Equal("20261017-190000/bug-001", Assert.Single(list.EnumerateArray()).GetProperty("id").GetString());
        var full = Send(watcher, "reports.get", """{ "id": "20261017-190000/bug-001", "screenshot": true }""").GetProperty("result");
        Assert.Equal(Png, Convert.FromBase64String(full.GetProperty("screenshot_png").GetString()!));
    }

    [Fact]
    public void BadReportsAnswerWithBadParams()
    {
        var watcher = Connect("watch");

        Assert.Equal(Codes.InvalidParams, Code(Send(watcher, "reports.note", """{ "title": "" }""")));
        Assert.Equal(Codes.InvalidParams, Code(Send(watcher, "reports.bug", """{ "title": "x", "steps": [], "expected": "a", "actual": "b" }""")));
        Assert.Equal(Codes.InvalidParams, Code(Send(watcher, "reports.bug", """{ "title": "x" }""")));
        Assert.Equal(Codes.InvalidParams, Code(Send(watcher, "reports.note", """{ "title": "x", "colour": "red" }""")));
        Assert.Equal(Codes.InvalidParams, Code(Send(watcher, "reports.get", """{ "id": "../../etc/passwd" }""")));
    }

    [Fact]
    public void DeletingNeedsAdmin()
    {
        var watcher = Connect("watch");
        var root = Connect("root");
        var id = Send(watcher, "reports.note", """{ "title": "Temporary" }""").GetProperty("result").GetProperty("id").GetString();

        Assert.Equal(Codes.Forbidden, Code(Send(watcher, "reports.delete", $$"""{ "id": "{{id}}" }""")));
        Assert.True(Send(root, "reports.delete", $$"""{ "id": "{{id}}" }""").GetProperty("result").GetBoolean());
        Assert.Equal(Codes.InvalidParams, Code(Send(root, "reports.delete", $$"""{ "id": "{{id}}" }""")));
    }

    [Fact]
    public void ScreenshotsAnswerWithThePictureOrWhyNot()
    {
        var watcher = Connect("watch");

        var shot = Send(watcher, "reports.screenshot").GetProperty("result");
        Assert.Equal((4, 2), (shot.GetProperty("width").GetInt32(), shot.GetProperty("height").GetInt32()));
        Assert.Equal(Png, Convert.FromBase64String(shot.GetProperty("png").GetString()!));

        _screen = new Screenshot(null, "The host runs headless.");
        var refused = Send(watcher, "reports.screenshot");
        Assert.Equal(Codes.Unavailable, Code(refused));
        Assert.Contains("headless", refused.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public void NewReportsArePushedOnTheReportsTopic()
    {
        var watcher = Connect("watch");
        Send(watcher, "mechanica.subscribe", """{ "topics": ["reports"] }""");

        Send(watcher, "reports.recommend", """{ "title": "Cheaper wheels", "text": "Too dear early on.", "priority": "high" }""");

        var pushed = JsonDocument.Parse(Assert.Single(_endpoint.DrainEvents(watcher))).RootElement;
        var data = pushed.GetProperty("params").GetProperty("data");
        Assert.Equal("reports", pushed.GetProperty("params").GetProperty("topic").GetString());
        Assert.Equal(("recommendation", "high"), (data.GetProperty("kind").GetString(), data.GetProperty("priority").GetString()));
    }

    [Fact]
    public void DescribeListsTheMethodsWithTheirScopes()
    {
        var methods = Send(Connect("watch"), "mechanica.describe").GetProperty("result").GetProperty("methods");
        var byName = methods.EnumerateArray().ToDictionary(m => m.GetProperty("name").GetString()!, m => m);

        foreach (var name in new[] { "reports.note", "reports.recommend", "reports.bug", "reports.list", "reports.get", "reports.delete", "reports.screenshot" })
            Assert.True(byName.ContainsKey(name), name);
        Assert.Equal("admin", byName["reports.delete"].GetProperty("scope").GetString());
        Assert.Equal("observe", byName["reports.bug"].GetProperty("scope").GetString());
    }
}
