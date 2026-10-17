using System.Text.Json;
using Godot;
using Mechanica.Core.Remote;

namespace Mechanica.Game.Tests;

/// <summary>
/// Keeps the AI docs in step with the game. A host's AI endpoint answers <c>mechanica.describe</c>, and the
/// method reference (<c>docs/ai/reference.md</c>) and schema file (<c>docs/ai/describe.json</c>) rendered
/// from it must match the checked-in copies, so adding, renaming or re-documenting an action or endpoint
/// method without regenerating them fails here. Run with <c>-- --write</c> (or <c>tools/ai/gen-docs.sh</c>)
/// to rewrite them.
/// </summary>
public partial class AiDocsTest : StagedTest
{
    private readonly bool _write = OS.GetCmdlineUserArgs().Contains("--write");
    private NetTestHarness _harness = null!;

    protected override string Tag => "aidocs";

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        var host = _harness.StartHost();

        Add("the reference matches the game's mechanica.describe", _ =>
        {
            var rpc = host.Main.Ai.CreateEndpoint(new EndpointAccess());
            var connection = rpc.Connect();
            var answer = rpc.Handle(connection, """{ "jsonrpc": "2.0", "id": 1, "method": "mechanica.describe" }""", 0);
            using var document = JsonDocument.Parse(answer!);
            if (!Check(document.RootElement.TryGetProperty("result", out var describe), $"mechanica.describe answers ({answer?[..Math.Min(200, answer.Length)]})"))
                return true;
            Check(describe.GetProperty("methods").GetArrayLength() > 100, $"it lists the game's methods ({describe.GetProperty("methods").GetArrayLength()})");
            var folder = Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "docs", "ai");
            var files = new Dictionary<string, string>
            {
                ["reference.md"] = AiReference.Markdown(describe),
                ["describe.json"] = AiReference.Json(describe),
            };
            foreach (var (name, text) in files)
            {
                var path = Path.GetFullPath(Path.Combine(folder, name));
                if (_write)
                {
                    File.WriteAllText(path, text);
                    GD.Print($"[aidocs] wrote {path}");
                    continue;
                }
                var current = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";
                Check(current == text, current == text ? $"docs/ai/{name} is up to date"
                    : current.Length == 0 ? $"docs/ai/{name} exists (run tools/ai/gen-docs.sh)"
                    : $"docs/ai/{name} is up to date (run tools/ai/gen-docs.sh); the game now says: {FirstDifference(current, text)}");
            }
            return true;
        });
    }

    private static string FirstDifference(string a, string b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i])
            i++;
        var end = b.IndexOf('\n', Math.Min(i, b.Length));
        var startB = Math.Max(0, b.LastIndexOf('\n', Math.Max(0, Math.Min(i, b.Length) - 1)) + 1);
        return $"'{b[startB..(end < 0 ? b.Length : end)]}'";
    }
}
