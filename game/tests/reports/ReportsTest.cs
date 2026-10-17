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
/// the host and the host logs an error; a bug report then carries the game's context (both players, the
/// seed, the mods), the agent's calls as a replay slice that reads back as a recording, the host error,
/// and why a headless host has no screenshot. The AI Reports window lists the reports, filters them by
/// kind and session, shows one in full, copies its Markdown, opens its folder and deletes it after asking.
/// </summary>
public partial class ReportsTest : StagedTest
{
    private static readonly ActionCaller Agent = new(1, ActionSource.External);

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private AiReport _bug = null!;
    private AiReport _note = null!;
    private AiReportsWindow _window = null!;
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

        Add("the client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.PlayerSync.Players.Count == 2);
        Add("the agent builds and the host logs an error", frame =>
        {
            if (frame == 0)
            {
                Check(_host.Main.Reports.Store.Root != AiReports.UserFolder, "a test scene never writes to the player's reports folder");
                var placed = _host.Main.Actions.Dispatcher.Dispatch(Agent, BuildActions.PlacePart,
                    $$"""{"part": "drive_test:block", "cell": [0, 0, 0], "position": [2, 0.75, 12]}""");
                Check(placed.Ok, $"the agent placed a block ({placed})");
                _host.Main.Actions.Dispatcher.Dispatch(Agent, "ListCreations", "{}");
                GD.PushError("[test] the wheel fell through the ground");
                return false;
            }
            return frame > 2;
        });
        Add("a bug report carries the context, replay and host error", _ =>
        {
            var reporter = _host.Main.Reports.Reporter;
            _note = reporter.Note("Test agent", new NoteParams("The Lift is easy to find", "Right by the spawn.", ["ux"]));
            _bug = reporter.Bug("Test agent", new BugParams("Block sinks", ["Place a block"], "It rests on the ground", "It sinks", BugSeverity.High, Tags: ["physics"]));
            var context = _bug.Context;
            Check(context.Players.Select(p => p.Name).Order().SequenceEqual(["Client", "Host"]), $"both players are in the context ({string.Join(", ", context.Players.Select(p => p.Name))})");
            Check(context.Seed == _harness.Seed && context.Mode == "creative", $"seed and mode ({context.Seed}, {context.Mode})");
            Check(context.Mods.Any(m => m.StartsWith("drive_test ", StringComparison.Ordinal)) && context.ContentFingerprint.Length > 0, "mods and fingerprint");
            Check(context.Tick > 0 && context.Headless, $"tick {context.Tick}, headless");
            Check(_bug.Screenshot is null && _bug.ScreenshotError?.Contains("headless") == true, $"no screenshot headless ({_bug.ScreenshotError})");
            Check(_bug.Messages!.Any(m => m.Source == "host" && m.Text.Contains("the wheel fell through the ground")), "the host's error is in the report");
            var store = _host.Main.Reports.Store;
            var replay = ActionRecording.FromJson(System.IO.File.ReadAllText(store.PathOf(_bug.Id, ".replay.json")));
            Check(replay.Actions.Select(a => a.Action).SequenceEqual([BuildActions.PlacePart]), $"the replay slice holds the agent's PlacePart only ({string.Join(", ", replay.Actions.Select(a => a.Action))})");
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
            Check(_window.Listed.Select(r => r.Id).SequenceEqual([_bug.Id, _note.Id]), $"newest first ({string.Join(", ", _window.Listed.Select(r => r.Id))})");
            Check(_window.Selected is null, "nothing is picked yet");
            _window.Show(0);
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
}
