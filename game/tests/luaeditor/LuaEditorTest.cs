using Godot;
using Mechanica.Game.Editor;
using Mechanica.Scripting;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Part Editor's Script tab (chunk 8), end to end with the Test Sandbox and a local client (D10).
/// A block gets a script from the "empty" template, saved and exported, and runs on the sandbox's host
/// only. Back in the editor, a broken edit is saved with its error line marked while the sandbox keeps the
/// old code; the fix hot reloads into the running part with its <c>state</c> kept, and its output shows in
/// the console. Exits 0 on success.
/// </summary>
public partial class LuaEditorTest : StagedTest
{
    private const string Mod = "lua_editor_test";
    private const string Part = $"{Mod}:beacon";
    private const string Script = $"{Mod}:scripts/beacon.lua";

    private PartEditorMode _mode = null!;
    private SandboxScreen _sandbox = null!;
    private SandboxScreen.Instance _client = null!;
    private Task<Core.Editor.ExportResult?>? _export;
    private double _countBeforeReload;

    protected override string Tag => "luaeditor";

    protected override int StageTimeoutFrames => 900;

    private LuaPanel Lua => _mode.Editor.Lua;

    private ScriptRuntime Runtime => _sandbox.Host.Main.Scripts.Runtime;

    public override void _Ready()
    {
        var modsRoot = ProjectSettings.GlobalizePath("user://tests/lua_editor_mods");
        if (Directory.Exists(modsRoot))
            Directory.Delete(modsRoot, recursive: true);
        Directory.CreateDirectory(modsRoot);
        _mode = new PartEditorMode { Name = "Mode", ModsRoot = modsRoot };
        AddChild(_mode);

        Add("a block gets a script from a template and exports", frame =>
        {
            if (frame == 0)
            {
                Check(Lua.Templates.Select(t => t.Id).SequenceEqual(["empty", "bearing_motor", "distance_sensor", "thruster", "wing"]),
                    $"the templates are listed ({string.Join(", ", Lua.Templates.Select(t => t.Id))})");
                Check(Lua.NewFromTemplate("empty") is not null, "a template needs an open part");
                Check(_mode.Editor.NewPart(Mod, "beacon", "Beacon", "block") is null, "a block starts");
                Check(Lua.ScriptKey is null && Lua.Save() is { Written: false }, "a part without a script has nothing to save");
                Check(Lua.NewFromTemplate("empty") is null && _mode.Editor.Document!.Definition.Script == "scripts/beacon.lua"
                    && Lua.ScriptKey == Script && Lua.Code.Contains("Beacon", StringComparison.Ordinal),
                    $"the empty template names the part and gives it scripts/beacon.lua ({Lua.ScriptKey})");
                Lua.Code = "";
                Lua.InsertApi(LuaApi.Entries.ToList().FindIndex(e => e.Name == "on_create"));
                Check(Lua.Code.StartsWith("function on_create()\n", StringComparison.Ordinal) && Lua.Code.Contains("end"),
                    $"inserting a hook from the API list writes an empty function ({Lua.Code})");
                Lua.Code = """
                    -- Beacon v1: counts its steps.
                    function on_create() state.count = 0; log("beacon v1") end
                    function on_update(dt) state.count = state.count + 1 end
                    """;
                Check(Lua.IsDirty, "the edit is unsaved");
                var saved = Lua.Save();
                Check(saved is { Written: true, Error: null, Reload: null } && File.Exists(Lua.ScriptFile) && !Lua.IsDirty,
                    $"saving writes the script ({saved.Message})");
                _export = _mode.Editor.ExportAsync();
            }
            if (_export is not { IsCompleted: true })
                return false;
            Check(_export.Result is { Succeeded: true }, $"export succeeds ({string.Join("; ", _export.Result?.Diagnostics.Select(d => d.ToString()) ?? [])})");
            _mode.Test(Part);
            _sandbox = _mode.Sandbox!;
            return true;
        });
        Add("the sandbox's host runs the script", frame =>
            frame >= 30 && Runtime.Instances is [{ Script: Script, Version: 1, Fault: null }] && Count() >= 10);
        Add("a local client joins and runs no scripts", frame =>
        {
            if (frame == 0)
                _client = _sandbox.AddLocalClient()!;
            if (_client?.Main.Creations.All.Count() != 1)
                return false;
            Check(_client.Main.Scripts.Runtime.Instances.Count == 0, "the client runs no scripts");
            return true;
        });
        Add("a broken edit is saved and marked, and the sandbox keeps the old code", frame =>
        {
            if (frame == 0)
            {
                _sandbox.EmitBack();
                Lua.Code = """
                    -- Beacon v2
                    function on_reload() state.reloaded = true; log("beacon v2" end
                    function on_update(dt) state.count = state.count + 1 end
                    """;
                var saved = Lua.Save();
                Check(saved is { Written: true, Error.Line: 2, Reload: null } && Lua.ErrorLine == 2,
                    $"the error is on line 2 ({saved.Message})");
                Check(Lua.StatusText.Contains("keeps the old code", StringComparison.Ordinal), $"the status says so ({Lua.StatusText})");
                Check(Runtime.Instances is [{ Version: 1, Fault: null }], "the sandbox still runs version 1");
            }
            return frame >= 2;
        });
        Add("the fix hot reloads into the running part", frame =>
        {
            if (frame == 0)
            {
                _countBeforeReload = Count();
                Lua.Code = Lua.Code.Replace("\"beacon v2\" end", "\"beacon v2\") end", StringComparison.Ordinal);
                var saved = Lua.Save();
                Check(saved is { Written: true, Error: null, Reload: { Ok: true, Instances: 1 } } && Lua.ErrorLine == 0,
                    $"the fixed script reloads on the sandbox's part ({saved.Message})");
                var state = Runtime.ExportState(Runtime.Instances[0].Creation, Runtime.Instances[0].Part)!;
                Check(Runtime.Instances is [{ Version: 2, Fault: null }] && state["reloaded"]?.GetValue<bool>() == true
                    && state["count"]!.GetValue<double>() >= _countBeforeReload, $"version 2 runs with its state kept ({state.ToJsonString()})");
            }
            if (frame < 2)
                return false;
            Check(Lua.ConsoleText.Contains("beacon v1", StringComparison.Ordinal) && Lua.ConsoleText.Contains("beacon v2", StringComparison.Ordinal),
                $"the console shows the script's output ({Lua.ConsoleText})");
            return true;
        });
        Add("reopening the part loads its script", _ =>
        {
            var file = Path.Combine(modsRoot, Mod, "parts", "beacon.json");
            Check(_mode.Editor.OpenPart(file) is null && Lua.ScriptKey == Script && Lua.Code.Contains("beacon v2", StringComparison.Ordinal) && !Lua.IsDirty,
                "the saved script comes back");
            return true;
        });
    }

    private double Count()
    {
        var info = Runtime.Instances.FirstOrDefault();
        return info is null ? 0 : Runtime.ExportState(info.Creation, info.Part)?["count"]?.GetValue<double>() ?? 0;
    }
}
