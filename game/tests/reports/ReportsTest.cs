using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Data;
using Mechanica.Core.Remote;
using Mechanica.Core.Reports;
using Mechanica.Game.Remote;
using Mechanica.Game.Ui;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// AI notes, recommendations and bug reports (AUDIT §4.5), host plus a client. An external agent builds on
/// the host over the endpoint and the host logs an error; the agent's bug report (<c>reports.bug</c>) then carries the game's context (both players, the
/// seed, the mods), the agent's calls as a replay slice that reads back as a recording, the host error,
/// and why a headless host has no screenshot. The AI Reports window lists the reports, filters them by
/// kind and session, shows one in full, copies its Markdown, opens its folder and deletes it after asking.
/// </summary>
public partial class ReportsTest : StagedTest
{
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private AiReport _bug = null!;
    private AiReport _note = null!;
    private AiReportsWindow _window = null!;
    private readonly RpcTestClient _agent = new();
    private readonly Dictionary<string, long> _ids = [];
    private string? _copied;
    private string? _opened;

    protected override string Tag => "reports";

    public override void _Ready()
    {
        var mods = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/remote/mods"));
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost(mods: mods);
        _client = _harness.StartClient("Client", mods: mods);

        Add("the client joins and the host opens the endpoint", _ =>
        {
            if (_host.Main.PlayerSync.Players.Count != 2 || _client.Main.PlayerSync.Players.Count != 2)
                return false;
            Check(_host.Main.Reports.Store.Root != AiReports.UserFolder, "a test scene never writes to the player's reports folder");
            Check(_host.Main.Ai.Listen(0) is null, $"the host listens on port {_host.Main.Ai.Port}");
            Check(_agent.Connect(_host.Main.Ai.Port) == Error.Ok, "the agent starts connecting");
            return true;
        });
        Add("the agent connects", _ => Pump() && _agent.IsOpen);
        Add("the agent builds and the host logs an error", frame =>
        {
            if (frame == 0)
            {
                _ids["sub"] = _agent.Call("mechanica.subscribe", new { topics = new[] { "reports" } });
                _ids["place"] = _agent.Call(BuildActions.PlacePart, new { part = "drive_test:block", cell = new[] { 0, 0, 0 }, position = new[] { 2, 0.75, 12 } });
                _ids["look"] = _agent.Call("ListCreations");
                GD.PushError("[test] the wheel fell through the ground");
            }
            if (!Pump() || Answer("look") is null)
                return false;
            Check(RpcTestClient.ErrorCode(Answer("place")!.Value) is null, $"the agent placed a block ({Answer("place")})");
            return true;
        });
        Add("the agent files a note and a bug", frame =>
        {
            if (frame == 0)
            {
                _ids["note"] = _agent.Call("reports.note", new { title = "The Lift is easy to find", text = "Right by the spawn.", tags = new[] { "ux" } });
                _ids["bug"] = _agent.Call("reports.bug", new
                {
                    title = "Block sinks", steps = new[] { "Place a block" }, expected = "It rests on the ground", actual = "It sinks",
                    severity = "high", tags = new[] { "physics" }, author = "Test agent",
                });
                _ids["bad"] = _agent.Call("reports.bug", new { title = "No steps", steps = Array.Empty<string>(), expected = "a", actual = "b" });
                _ids["shot"] = _agent.Call("reports.screenshot");
                _ids["list"] = _agent.Call("reports.list", new { session = "current" });
            }
            if (!Pump() || Answer("list") is not { } list)
                return false;
            Check(RpcTestClient.ErrorCode(Answer("bad")!.Value) == JsonRpcEndpoint.Codes.InvalidParams, "a bug without steps is refused");
            Check(RpcTestClient.ErrorCode(Answer("shot")!.Value) == JsonRpcEndpoint.Codes.Unavailable, "a headless host takes no screenshot");
            Check(RpcTestClient.Result(list).GetArrayLength() == 2, $"reports.list shows both ({list})");
            var store = _host.Main.Reports.Store;
            _note = store.Get(RpcTestClient.Result(Answer("note")!.Value).GetProperty("id").GetString()!)!;
            _bug = store.Get(RpcTestClient.Result(Answer("bug")!.Value).GetProperty("id").GetString()!)!;
            Check(_bug.Author == "Test agent" && _note.Author.StartsWith("agent ", StringComparison.Ordinal), $"authors ({_bug.Author}, {_note.Author})");
            Check(_agent.Events.Count(e => e.GetProperty("params").GetProperty("topic").GetString() == "reports") == 2, "both were pushed on the reports topic");
            return true;
        });
        Add("the bug carries the context, replay and host error", _ =>
        {
            var context = _bug.Context;
            Check(context.Players.Select(p => p.Name).Order().SequenceEqual(["Client", "Host"]), $"both players are in the context ({string.Join(", ", context.Players.Select(p => p.Name))})");
            Check(context.Seed == _harness.Seed && context.Mode == "creative", $"seed and mode ({context.Seed}, {context.Mode})");
            Check(context.Mods.Any(m => m.StartsWith("drive_test ", StringComparison.Ordinal)) && context.ContentFingerprint.Length > 0, "mods and fingerprint");
            Check(context.Tick > 0 && context.Headless && context.ProtocolVersion == JsonRpcEndpoint.ProtocolVersion, $"tick {context.Tick}, headless, protocol {context.ProtocolVersion}");
            Check(_bug.Screenshot is null && _bug.ScreenshotError?.Contains("headless") == true, $"no screenshot headless ({_bug.ScreenshotError})");
            Check(_bug.Messages!.Any(m => m.Source == "host" && m.Text.Contains("the wheel fell through the ground")), "the host's error is in the report");
            var store = _host.Main.Reports.Store;
            var replay = ActionRecording.FromJson(System.IO.File.ReadAllText(store.PathOf(_bug.Id, ".replay.json")));
            Check(replay.Actions.Select(a => a.Action).SequenceEqual([BuildActions.PlacePart]) && replay.Actions[0].Source == ActionSource.External,
                $"the replay slice holds the agent's PlacePart only ({string.Join(", ", replay.Actions.Select(a => a.Action))})");
            Check(System.IO.File.ReadAllText(store.PathOf(_bug.Id, ".md")).Contains("--seed 1234 --creative --replay"), "the Markdown says how to replay it");
            return true;
        });
        Add("the AI Reports window lists, shows, copies and opens", frame =>
        {
            if (frame == 0)
            {
                var layer = new CanvasLayer { Name = "Layer" };
                AddChild(layer);
                _window = new AiReportsWindow { Store = _host.Main.Reports.Store, Copy = text => _copied = text, OpenFolder = path => _opened = path };
                layer.AddChild(_window);
                return false;
            }
            Check(_window.Listed.Select(r => r.Id).Order().SequenceEqual(new[] { _bug.Id, _note.Id }.Order())
                && _window.Listed[0].Created >= _window.Listed[1].Created, $"newest first ({string.Join(", ", _window.Listed.Select(r => r.Id))})");
            Check(_window.Selected is null, "nothing is picked yet");
            _window.Show(_window.Listed.ToList().FindIndex(r => r.Id == _bug.Id));
            Check(_window.Selected?.Id == _bug.Id && _window.DetailText.Contains("Block sinks") && _window.DetailText.Contains("Steps to reproduce"), "the bug shows in full");
            Check(!_window.ShowsPicture, "a report without a screenshot shows no picture");
            _window.CopySelected();
            Check(_copied?.StartsWith("# Bug: Block sinks", StringComparison.Ordinal) == true, "Copy puts its Markdown on the clipboard");
            _window.OpenSelectedFolder();
            Check(_opened == _host.Main.Reports.Store.SessionDirectory, $"Open folder opens its session ({_opened})");
            _window.FilterKind(Array.FindIndex(AiReportsWindow.Kinds, k => k.Kind == ReportKind.Note));
            Check(_window.Listed.Select(r => r.Id).SequenceEqual([_note.Id]), "the kind filter shows notes only");
            _window.FilterKind(0);
            _window.FilterSession("20000101-000000");
            Check(_window.Listed.Count == 2, "an unknown session falls back to every session");
            _window.FilterSession(_host.Main.Reports.Store.Session);
            Check(_window.Listed.Count == 2, "this game's session lists both");
            return true;
        });
        Add("a new report appears in the open window", _ =>
        {
            _host.Main.Reports.Reporter.Recommend("Test agent", new RecommendParams("Cheaper blocks", "Blocks cost too much early on."));
            Check(_window.Listed.Count == 3 && _window.Listed[0].Kind == ReportKind.Recommendation, "the window refreshed by itself");
            return true;
        });
        Add("Delete asks, then removes the report and its files", frame =>
        {
            if (frame == 0)
            {
                _window.Show(_window.Listed.ToList().FindIndex(r => r.Id == _bug.Id));
                var dialog = _window.DeleteSelected();
                Check(dialog is not null && ConfirmDialog.Current == dialog, "Delete asks first");
                Check(_host.Main.Reports.Store.Get(_bug.Id) is not null, "nothing is deleted before the answer");
                dialog!.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
                return false;
            }
            var store = _host.Main.Reports.Store;
            Check(store.Get(_bug.Id) is null && !System.IO.File.Exists(store.PathOf(_bug.Id, ".replay.json")), "the bug and its replay are gone");
            Check(_window.Listed.Count == 2 && _window.Listed.All(r => r.Id != _bug.Id), "the list no longer shows it");
            Check(_window.Status.Contains("Deleted"), $"the status says so ({_window.Status})");
            return true;
        });
        Add("the client has its own, empty, reports folder", _ =>
        {
            Check(_client.Main.Reports.Store.List(new ReportFilter(Session: _client.Main.Reports.Store.Session)).Count == 0, "nothing was written on the client");
            System.IO.Directory.Delete(_host.Main.Reports.Store.Root, recursive: true);
            return true;
        });
    }

    private bool Pump()
    {
        _agent.Poll();
        return true;
    }

    private JsonElement? Answer(string name) => _agent.Response(_ids[name]);
}
