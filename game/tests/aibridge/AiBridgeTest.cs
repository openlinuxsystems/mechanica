using System.Diagnostics;
using System.Text.Json;
using Godot;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The AI connection bridges in <c>tools/ai/bridge</c> against a real host with a client (D10). The host
/// opens its endpoint with a token; <c>tools/ai/bridge/test/e2e.mjs</c> (Node) then drives the MCP server
/// the way Claude does and the OpenAI-compatible bridge against a scripted model server, with native tool
/// calls and with tools described in the prompt, as a local Grok needs. Each joins as a player, walks, and
/// leaves. The script writes its own checks to a file; this scene keeps the game running meanwhile, checks
/// the client saw every AI player, and that all of them are gone afterwards.
/// </summary>
public partial class AiBridgeTest : StagedTest
{
    private const string Token = "bridge-token";
    private static readonly string[] AgentNames = ["Mcp (AI)", "Grok (AI)", "Grok2 (AI)"];

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Process? _node;
    private string _resultPath = "";
    private readonly HashSet<string> _seenByClient = [];

    protected override string Tag => "aibridge";

    // The script starts three Node processes in turn; a stage may take a while of real time.
    protected override int StageTimeoutFrames => 60 * 90;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins and the host opens the endpoint with a token", _ =>
        {
            if (_host.Main.PlayerSync.Players.Count != 2 || _client.Main.PlayerSync.Players.Count != 2)
                return false;
            Check(_host.Main.Ai.Listen(0, "127.0.0.1", Token) is null && _host.Main.Ai.Port > 0, $"the host listens on port {_host.Main.Ai.Port}");
            return true;
        });
        Add("the bridges run against the game", _ =>
        {
            if (_node is null)
                return !Check(StartNode(), "Node 22+ runs the bridge script (node on PATH)");
            Watch();
            return _node.HasExited;
        });
        Add("every bridge check passed", _ =>
        {
            if (_node is null)
                return true;
            Check(_node.ExitCode == 0, $"the script exits with 0 ({_node.ExitCode})");
            if (!Check(File.Exists(_resultPath), "the script wrote its results"))
                return true;
            foreach (var name in AgentNames)
                Check(_seenByClient.Contains(name), $"the client saw {name}");
            using var result = JsonDocument.Parse(File.ReadAllText(_resultPath));
            var failed = new List<string>();
            foreach (var check in result.RootElement.GetProperty("checks").EnumerateArray())
            {
                var what = check.GetProperty("what").GetString()!;
                GD.Print($"[aibridge] script {(check.GetProperty("ok").GetBoolean() ? "PASS" : "FAIL")}: {what}");
                if (!check.GetProperty("ok").GetBoolean())
                    failed.Add(what);
            }
            Check(result.RootElement.GetProperty("ok").GetBoolean() && failed.Count == 0,
                $"every bridge check passed{(failed.Count > 0 ? ": failed " + string.Join("; ", failed) : "")}");
            return true;
        });
        Add("the AI players and connections are gone", _ =>
        {
            Watch();
            return _host.Main.Ai.ConnectionCount == 0 && _host.Main.AgentHost.Agents.All.Count == 0
                && _host.Main.PlayerSync.Players.Count == 2 && _client.Main.PlayerSync.Players.Count == 2;
        });
    }

    public override void _ExitTree()
    {
        if (_node is { HasExited: false })
            _node.Kill(entireProcessTree: true);
        _node?.Dispose();
    }

    private bool StartNode()
    {
        var repo = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
        _resultPath = Path.Combine(Path.GetTempPath(), $"mechanica-aibridge-{System.Environment.ProcessId}.json");
        File.Delete(_resultPath);
        var start = new ProcessStartInfo("node") { UseShellExecute = false };
        foreach (var arg in new[]
        {
            Path.Combine(repo, "tools", "ai", "bridge", "test", "e2e.mjs"),
            "--game", $"ws://127.0.0.1:{_host.Main.Ai.Port}", "--token", Token, "--out", _resultPath,
        })
            start.ArgumentList.Add(arg);
        try
        {
            _node = Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            GD.PrintErr($"[aibridge] cannot start node: {e.Message}");
        }
        return _node is not null;
    }

    private void Watch()
    {
        foreach (var player in _client.Main.PlayerSync.Players.Values)
            _seenByClient.Add(player.DisplayName);
    }
}
