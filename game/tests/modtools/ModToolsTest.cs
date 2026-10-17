using System.Text.Json;
using Godot;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Remote;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The mod tools over the AI endpoint (AI support PR 6), host plus a client. An agent makes a mod in the
/// host's mods folder, writes a part with a mistake, finds it with <c>mods.validate</c>, fixes it and enables
/// the mod. <c>mods.reload</c> is refused while the client is joined; with reloads allowed it loads the new part
/// into the running world and drops the client, whose mods no longer match. The agent then places the new
/// part, and the Lua tools answer. Last the agent uses the editors (AI support PR 7): it makes a part with model
/// pieces that the editor's model builder bakes into a mesh, runs a Part Editor batch, reloads and places both.
/// Uses a copy of the fixture mod in <c>tests/remote/mods</c>.
/// </summary>
public partial class ModToolsTest : StagedTest
{
    private const string Token = "test-token";
    private const string Mod = "crates";
    private const string Part = """{ "id": "crate", "name": "Crate", "category": "block", "mass": -1, "durability": 3, "friction": 0.5, "buoyancy": 0.5, "flammable": false, "size": [1, 1, 1] }""";

    private readonly string _root = Path.Combine(OS.GetUserDataDir(), "test-modtools");
    private readonly RpcTestClient _agent = new();
    private readonly Dictionary<string, long> _ids = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;

    protected override string Tag => "modtools";

    protected override int StageTimeoutFrames => 600;

    public override void _Ready()
    {
        var mods = Path.Combine(_root, "mods");
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        CopyFolder(ProjectSettings.GlobalizePath("res://tests/remote/mods/drive_test"), Path.Combine(mods, "drive_test"));
        var loaded = ModLoader.LoadAll(mods);

        _harness = new NetTestHarness
        {
            Name = "Harness",
            Setup = main =>
            {
                main.ModsFolder = mods;
                main.ModProfileFile = Path.Combine(_root, "mods.json");
            },
        };
        AddChild(_harness);
        _host = _harness.StartHost(mods: loaded);
        _client = _harness.StartClient("Client", mods: loaded);

        Add("the client joins and the agent connects", frame =>
        {
            if (_host.Main.PlayerSync.Players.Count != 2 || _client.Main.PlayerSync.Players.Count != 2)
                return false;
            if (!_agent.IsOpen)
            {
                if (frame == 0 || _host.Main.Ai.Port == 0)
                {
                    Check(!loaded.HasErrors, $"fixture mod loads ({string.Join("; ", loaded.Diagnostics)})");
                    var access = new EndpointAccess().AddToken(Token, [.. EndpointAccess.All]);
                    Check(_host.Main.Ai.Listen(0, "127.0.0.1", access) is null, "the host listens");
                    Check(_agent.Connect(_host.Main.Ai.Port) == Error.Ok, "the agent starts connecting");
                }
                _agent.Poll();
                return false;
            }
            return true;
        });
        Add("the agent makes a mod and writes a part with a mistake", frame =>
        {
            if (frame == 0)
            {
                _ids["auth"] = _agent.Call("mechanica.auth", new { token = Token });
                _ids["create"] = _agent.Call("mods.create", new { mod = Mod, name = "Crates", dependencies = new[] { "drive_test" } });
                _ids["write"] = _agent.Call("mods.write", new { mod = Mod, path = "parts/crate.json", text = Part });
                _ids["escape"] = _agent.Call("mods.read", new { mod = Mod, path = "../drive_test/mod.json" });
                _ids["validate"] = _agent.Call("mods.validate", new { mod = Mod });
            }
            if (Answer("validate") is not { } validate)
                return false;
            Check(RpcTestClient.ErrorCode(Answer("auth")!.Value) is null, "the agent authenticates");
            Check(RpcTestClient.ErrorCode(Answer("create")!.Value) is null, $"mods.create ({Answer("create")})");
            Check(RpcTestClient.Result(Answer("write")!.Value).GetProperty("check").GetProperty("ok").GetBoolean(), "the part is valid JSON");
            Check(RpcTestClient.ErrorCode(Answer("escape")!.Value) == JsonRpcEndpoint.Codes.Rejected, "a path out of the mod is refused");
            Check(File.Exists(Path.Combine(_root, "mods", Mod, "parts", "crate.json")), "the file is in the host's mods folder");
            var report = RpcTestClient.Result(validate);
            var diagnostic = report.GetProperty("diagnostics")[0];
            Check(!report.GetProperty("ok").GetBoolean() && diagnostic.GetProperty("json_path").GetString() == "$.mass"
                && diagnostic.GetProperty("file").GetString()!.EndsWith($"{Mod}/parts/crate.json"),
                $"mods.validate finds the bad mass ({report})");
            return true;
        });
        Add("the agent fixes the part and enables the mod", frame =>
        {
            if (frame == 0)
            {
                _ids["fix"] = _agent.Call("mods.write", new { mod = Mod, path = "parts/crate.json", text = Part.Replace("-1", "8") });
                _ids["enable"] = _agent.Call("mods.enable", new { mod = Mod });
                _ids["revalidate"] = _agent.Call("mods.validate");
                _ids["refused"] = _agent.Call("mods.reload");
            }
            if (Answer("refused") is not { } refused)
                return false;
            Check(RpcTestClient.ErrorCode(Answer("fix")!.Value) is null, "the fix is written");
            Check(RpcTestClient.ErrorCode(Answer("enable")!.Value) is null, "the mod is enabled");
            var report = RpcTestClient.Result(Answer("revalidate")!.Value);
            // The fixture has no world generation rules, so the world check always complains; the mods must be clean.
            Check(report.GetProperty("diagnostics").EnumerateArray().All(d => d.GetProperty("domain").GetString() == "world")
                && report.GetProperty("loaded_mods").EnumerateArray().Any(m => m.GetString() == Mod), $"the mods validate ({report})");
            Check(RpcTestClient.ErrorCode(refused) == JsonRpcEndpoint.Codes.Rejected
                && refused.GetProperty("error").GetProperty("message").GetString()!.Contains("--ai-allow-reload-with-clients"),
                $"a reload is refused while a client is joined ({refused})");
            Check(_host.Main.Mods!.Parts.ContainsKey($"{Mod}:crate") is false, "and nothing changed");
            return true;
        });
        Add("with reloads allowed, the reload drops the client", frame =>
        {
            if (frame == 0)
            {
                _host.Main.Options = _host.Main.Options! with { AiAllowReloadWithClients = true };
                _ids["reload"] = _agent.Call("mods.reload");
            }
            if (Answer("reload") is not { } reload)
                return false;
            var result = RpcTestClient.Result(reload);
            Check(result.GetProperty("loaded_mods").EnumerateArray().Any(m => m.GetString() == Mod), $"the new mod is loaded ({result})");
            Check(result.GetProperty("errors").GetInt32() == 0, "without errors");
            Check(result.GetProperty("dropped_peers").GetArrayLength() == 1, "the client is dropped");
            Check(_host.Main.Mods!.Parts.ContainsKey($"{Mod}:crate"), "the host knows the new part");
            Check(result.GetProperty("fingerprint").GetString() == _host.Main.Content!.Fingerprint, "the fingerprint is the running one");
            return true;
        });
        Add("the client leaves", _ =>
        {
            _agent.Poll();
            return _client.Main.Session.State != Mechanica.Game.Net.NetSession.SessionState.Connected && _host.Main.Session.ReadyPeers.Count == 0;
        });
        Add("the agent places the new part and the Lua tools answer", frame =>
        {
            if (frame == 0)
            {
                _ids["place"] = _agent.Call(BuildActions.PlacePart, new PlacePartArgs($"{Mod}:crate", new GridCell(0, 0, 0), Position: new(2f, 1f, 12f)));
                _ids["eval"] = _agent.Call("lua.eval", new { source = "print('hi') return 6 * 7" });
                _ids["check"] = _agent.Call("lua.check", new { source = "if then" });
                _ids["messages"] = _agent.Call("lua.messages", new { limit = 10 });
            }
            if (Answer("messages") is not { } messages)
                return false;
            Check(RpcTestClient.ErrorCode(Answer("place")!.Value) is null, $"PlacePart with the new part ({Answer("place")})");
            var eval = RpcTestClient.Result(Answer("eval")!.Value);
            Check(eval.GetProperty("ok").GetBoolean() && eval.GetProperty("values")[0].GetDouble() == 42
                && eval.GetProperty("output")[0].GetString() == "hi", $"lua.eval runs ({eval})");
            var check = RpcTestClient.Result(Answer("check")!.Value);
            Check(!check.GetProperty("ok").GetBoolean() && check.GetProperty("line").GetInt32() == 1 && check.GetProperty("column").GetInt32() == 3, $"lua.check finds the error ({check})");
            Check(RpcTestClient.Result(messages).ValueKind == JsonValueKind.Array, "lua.messages answers");
            return true;
        });
        Add("the agent makes a part with model pieces in the Part Editor", frame =>
        {
            if (frame == 0)
            {
                _ids["templates"] = _agent.Call("editor.templates");
                _ids["new"] = _agent.Call("editor.part.new", new { mod = Mod, template = "block", id = "lamp", name = "Lamp" });
            }
            if (Answer("new") is not { } made)
                return false;
            var templates = RpcTestClient.Result(Answer("templates")!.Value);
            Check(templates.GetProperty("parts").GetArrayLength() > 0 && templates.GetProperty("bots").GetArrayLength() > 0,
                $"the editors' templates are listed ({templates})");
            var doc = RpcTestClient.Result(made).GetProperty("document").GetProperty("doc").GetString()!;
            _ids["edit"] = _agent.Call("editor.part.edit", new
            {
                doc,
                label = "Shape",
                source = JsonDocument.Parse("""{ "model": [{ "kind": "box", "size": [0.25, 0.1, 0.25], "position": [0, -0.075, 0] }, { "kind": "sphere", "radius": 0.08, "position": [0, 0.04, 0] }] }""").RootElement,
            });
            _ids["export"] = _agent.Call("editor.part.export", new { doc });
            return true;
        });
        Add("the export bakes the mesh and writes the part", _ =>
        {
            if (Answer("export") is not { } export)
                return false;
            var edit = RpcTestClient.Result(Answer("edit")!.Value);
            Check(edit.GetProperty("document").GetProperty("needs_bake").GetBoolean(), "the model pieces need a bake");
            Check(edit.GetProperty("diagnostics").EnumerateArray().All(d => d.GetProperty("severity").GetString() != "error"), $"and are valid ({edit})");
            var result = RpcTestClient.Result(export);
            Check(result.GetProperty("ok").GetBoolean() && result.GetProperty("file").GetString() == "parts/lamp.json", $"editor.part.export ({result})");
            var dir = Path.Combine(_root, "mods", Mod);
            Check(File.Exists(Path.Combine(dir, "meshes", "lamp.glb")) && new FileInfo(Path.Combine(dir, "meshes", "lamp.glb")).Length > 100,
                "the baked mesh is in the mod");
            Check(File.Exists(Path.Combine(dir, "editor", "parts", "lamp.json")), "so are the model pieces");
            return true;
        });
        Add("the agent runs a Part Editor batch", frame =>
        {
            if (frame == 0)
            {
                _ids["batch-write"] = _agent.Call("mods.write", new { mod = Mod, path = "editor/batches/planks.json", text = """{ "format_version": 1, "parts": [{ "file": "parts/plank.json", "template": "block", "set": { "name": "Plank", "size": [2, 1, 1] } }] }""" });
                _ids["batch"] = _agent.Call("editor.batch.run", new { mod = Mod, path = "editor/batches/planks.json" });
            }
            if (Answer("batch") is not { } batch)
                return false;
            var report = RpcTestClient.Result(batch);
            Check(report.GetProperty("kind").GetString() == "parts" && report.GetProperty("ok").GetBoolean(), $"editor.batch.run ({report})");
            Check(File.Exists(Path.Combine(_root, "mods", Mod, "parts", "plank.json")), "the batch wrote its part");
            _ids["reload-2"] = _agent.Call("mods.reload");
            return true;
        });
        Add("the agent reloads", _ =>
        {
            if (Answer("reload-2") is not { } reload)
                return false;
            Check(RpcTestClient.ErrorCode(reload) is null, $"mods.reload with nobody joined ({reload})");
            return true;
        });
        Add("the agent places both new parts", frame =>
        {
            if (frame == 0)
            {
                _ids["place-lamp"] = _agent.Call(BuildActions.PlacePart, new PlacePartArgs($"{Mod}:lamp", new GridCell(0, 0, 0), Position: new(-4f, 1f, 12f)));
                _ids["place-plank"] = _agent.Call(BuildActions.PlacePart, new PlacePartArgs($"{Mod}:plank", new GridCell(0, 0, 0), Position: new(6f, 1f, 12f)));
            }
            if (Answer("place-plank") is not { } plank)
                return false;
            Check(RpcTestClient.ErrorCode(Answer("place-lamp")!.Value) is null, $"the baked part is placed ({Answer("place-lamp")})");
            Check(RpcTestClient.ErrorCode(plank) is null, $"the batch part is placed ({plank})");
            return true;
        });
    }

    public override void _ExitTree()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private JsonElement? Answer(string name)
    {
        _agent.Poll();
        return _ids.TryGetValue(name, out var id) ? _agent.Response(id) : null;
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
