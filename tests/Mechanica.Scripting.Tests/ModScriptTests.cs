using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;
using static Mechanica.Scripting.Tests.ScriptRig;

namespace Mechanica.Scripting.Tests;

// Mod scripts (mod.json "scripts"), events, actions.call and require.
public sealed class ModScriptTests : IDisposable
{
    private readonly ScriptRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private sealed record NoteArgs(string Text);

    // A state-changing action that records what it was given, and who asked.
    private List<(ActionCaller Caller, string Text)> RegisterNote()
    {
        var notes = new List<(ActionCaller, string)>();
        _rig.Dispatcher.Register<NoteArgs>("Note",
            (_, args) => args.Text.Length == 0 ? "text is empty" : null,
            (caller, args) =>
            {
                notes.Add((caller, args.Text));
                return JsonSerializer.SerializeToElement(new { count = notes.Count });
            });
        return notes;
    }

    [Fact]
    public void AModScriptRunsForTheWorldAndKeepsItsState()
    {
        _rig.Write("scripts/rules.lua", """
            function on_create() print("rules up") end
            function on_update(dt) state.seconds = (state.seconds or 0) + dt end
            """);
        _rig.ModScripts.Add("test:scripts/rules.lua");

        _rig.Runtime.Update(0.5f);
        _rig.Runtime.Update(0.5f);

        Assert.Empty(_rig.Errors);
        Assert.Contains("rules up", _rig.Prints);
        Assert.Equal(("test:scripts/rules.lua", (string?)null), Assert.Single(_rig.Runtime.ModScripts));
        var state = Assert.Single(_rig.Runtime.ExportModStates());
        Assert.Equal(1.0, state.Value["seconds"]!.GetValue<double>());

        _rig.Runtime.Clear();
        _rig.Runtime.Update(0.1f);
        Assert.True(_rig.Runtime.ImportModState("test:scripts/rules.lua", new JsonObject { ["seconds"] = 40 }));
        _rig.Runtime.Update(1f);
        Assert.Equal(41.0, _rig.Runtime.ExportModStates()["test:scripts/rules.lua"]["seconds"]!.GetValue<double>());
        Assert.False(_rig.Runtime.ImportModState("test:scripts/nothing.lua", []));
    }

    [Fact]
    public void ModScriptsHearTheEventsTheyListenTo()
    {
        _rig.Write("scripts/greeter.lua", """
            events.listen("players")
            function on_event(topic, data) print(topic .. " " .. data.kind .. " " .. data.name) end
            """);
        _rig.ModScripts.Add("test:scripts/greeter.lua");
        _rig.Runtime.Update(0.1f);

        Assert.True(_rig.Runtime.Listens("players"));
        Assert.False(_rig.Runtime.Listens("chat"));
        _rig.Runtime.RaiseEvent("players", JsonSerializer.SerializeToElement(new { kind = "joined", peer = 2, name = "Ada" }));
        _rig.Runtime.RaiseEvent("chat", JsonSerializer.SerializeToElement(new { text = "unheard" }));
        Assert.DoesNotContain(_rig.Prints, p => p.StartsWith("players", StringComparison.Ordinal));
        _rig.Runtime.Update(0.1f);

        Assert.Empty(_rig.Errors);
        Assert.Equal(["players joined Ada"], _rig.Prints);
    }

    [Fact]
    public void PartsListenToo_AndBadTopicsAreErrors()
    {
        _rig.Write("scripts/thruster.lua", """
            function on_create() events.listen("raid") end
            function on_event(topic, data) state.level = data.level end
            """);
        _rig.Write("scripts/bad.lua", """events.listen("spaceships")""");
        _rig.ModScripts.Add("test:scripts/bad.lua");
        var creation = _rig.AddCreation(1, Thruster);
        _rig.Runtime.Update(0.1f);

        _rig.Runtime.RaiseEvent("raid", JsonSerializer.SerializeToElement(new { level = 3 }));
        _rig.Runtime.Update(0.1f);

        Assert.Equal(3.0, _rig.Runtime.ExportState(1, PartOf(creation, Thruster))!["level"]!.GetValue<double>());
        Assert.Contains(_rig.Errors, e => e.Script == "test:scripts/bad.lua" && e.Text.Contains("no event topic 'spaceships'"));
        Assert.Equal("test:scripts/bad.lua", _rig.Runtime.ModScripts.Single(s => s.Fault is not null).Script);
    }

    [Fact]
    public void ModScriptsCallActions_PartsOnlyRead()
    {
        var notes = RegisterNote();
        _rig.Write("scripts/rules.lua", """
            function on_create()
              local ok, value = actions.call("Note", { text = "hello" })
              state.count = value.count
              local refused, why = actions.call("Note", { text = "" })
              state.why = why
              state.ok = refused
              state.moving = pcall(actions.call, "ApplyForce", {})
              local listed, creations = actions.call("ListCreations")
              state.listed = listed
            end
            """);
        _rig.Write("scripts/thruster.lua", """
            function on_create()
              state.read = actions.call("ListCreations")
              local ok, err = pcall(actions.call, "Note", { text = "from a part" })
              state.err = err
            end
            """);
        _rig.ModScripts.Add("test:scripts/rules.lua");
        var creation = _rig.AddCreation(1, Thruster);

        _rig.Runtime.Update(0.1f);

        Assert.Empty(_rig.Errors);
        var (caller, text) = Assert.Single(notes);
        Assert.Equal(("hello", ActionSource.Script), (text, caller.Source));
        var rules = _rig.Runtime.ExportModStates()["test:scripts/rules.lua"];
        Assert.Equal(1.0, rules["count"]!.GetValue<double>());
        Assert.Equal("text is empty", rules["why"]!.GetValue<string>());
        Assert.False(rules["ok"]!.GetValue<bool>());
        Assert.False(rules["moving"]!.GetValue<bool>());
        Assert.True(rules["listed"]!.GetValue<bool>());
        var part = _rig.Runtime.ExportState(1, PartOf(creation, Thruster))!;
        Assert.True(part["read"]!.GetValue<bool>());
        Assert.Contains("only mod scripts", part["err"]!.GetValue<string>());
    }

    [Fact]
    public void RequireLoadsAModuleOncePerMod()
    {
        _rig.Write("scripts/lib/maths.lua", """
            print("loading maths")
            local M = {}
            function M.double(x) return x * 2 end
            return M
            """);
        _rig.Write("scripts/shared.lua", "return { greeting = 'hi' }", mod: "lib");
        _rig.Write("scripts/secret.lua", "return 1", mod: "other");
        _rig.Write("scripts/a.lua", """
            local maths = require("scripts/lib/maths.lua")
            local shared = require("lib:scripts/shared.lua")
            state.value = maths.double(21)
            state.greeting = shared.greeting
            state.blocked = pcall(require, "other:scripts/secret.lua")
            """);
        _rig.Write("scripts/b.lua", """state.same = require("scripts/lib/maths.lua").double(1)""");
        _rig.ModScripts.AddRange(["test:scripts/a.lua", "test:scripts/b.lua"]);

        _rig.Runtime.Update(0.1f);

        Assert.Empty(_rig.Errors);
        var states = _rig.Runtime.ExportModStates();
        Assert.Equal(42.0, states["test:scripts/a.lua"]["value"]!.GetValue<double>());
        Assert.Equal("hi", states["test:scripts/a.lua"]["greeting"]!.GetValue<string>());
        Assert.False(states["test:scripts/a.lua"]["blocked"]!.GetValue<bool>());
        Assert.Equal(2.0, states["test:scripts/b.lua"]["same"]!.GetValue<double>());
        Assert.Single(_rig.Prints, "loading maths");
    }

    [Fact]
    public void ARequireLoopIsAnErrorNotAHang()
    {
        _rig.Write("scripts/x.lua", """require("scripts/y.lua")""");
        _rig.Write("scripts/y.lua", """require("scripts/x.lua")""");
        _rig.ModScripts.Add("test:scripts/x.lua");

        _rig.Runtime.Update(0.1f);

        Assert.Contains(_rig.Errors, e => e.Text.Contains("requires itself"));
    }

    [Fact]
    public void AModScriptReloadsInPlaceAndStopsWhenItsModGoes()
    {
        _rig.Write("scripts/rules.lua", """function on_update(dt) state.n = (state.n or 0) + 1 end""");
        _rig.ModScripts.Add("test:scripts/rules.lua");
        _rig.Runtime.Update(0.1f);
        _rig.Write("scripts/rules.lua", """
            function on_reload() print("reloaded") end
            function on_destroy() print("bye") end
            function on_update(dt) state.n = state.n + 10 end
            """);

        Assert.True(Assert.Single(_rig.Runtime.CheckForChanges()).Ok);
        _rig.Runtime.Update(0.1f);
        Assert.Equal(11.0, _rig.Runtime.ExportModStates()["test:scripts/rules.lua"]["n"]!.GetValue<double>());

        _rig.ModScripts.Clear();
        _rig.Runtime.Source = _rig.Runtime.Source;
        _rig.Runtime.Update(0.1f);
        Assert.Empty(_rig.Runtime.ModScripts);
        Assert.Equal(["reloaded", "bye"], _rig.Prints);
    }

    [Fact]
    public void AModScriptActivatingAPartOfItsOwnModWorks()
    {
        // The mod script's hook is running when the part's on_activate runs in the same Lua state.
        _rig.Write("scripts/thruster.lua", """function on_activate(peer) state.used = (state.used or 0) + 1 end""");
        _rig.Write("scripts/rules.lua", """
            events.listen("chat")
            function on_event(topic, data) state.ok, state.why = actions.call("Nudge", { creation = 1 }) end
            """);
        var creation = _rig.AddCreation(1, Thruster);
        var part = PartOf(creation, Thruster);
        _rig.Dispatcher.Register<NudgeArgs>("Nudge", (_, _) => null, (_, a) =>
        {
            var result = _rig.Runtime.Invoke(a.Creation, part, ScriptRuntime.Hooks.OnActivate, 1);
            return result.Ok ? null : throw new InvalidOperationException(result.Error);
        });
        _rig.ModScripts.Add("test:scripts/rules.lua");
        _rig.Runtime.Update(0.1f);

        _rig.Runtime.RaiseEvent("chat", JsonSerializer.SerializeToElement(new { text = "go" }));
        _rig.Runtime.Update(0.1f);

        Assert.Empty(_rig.Errors);
        Assert.Equal(1.0, _rig.Runtime.ExportState(1, part)!["used"]!.GetValue<double>());
    }

    private sealed record NudgeArgs(int Creation);
}

// Mod scripts and require resolved against real mod folders.
public sealed class ModScriptSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mechanica-modsource-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void WorldScriptsFollowLoadOrderAndRequireStaysInsideDependencies()
    {
        Write("base/mod.json", """{ "id": "base", "name": "Base", "version": "1.0.0", "scripts": ["scripts/base.lua"] }""");
        Write("base/scripts/base.lua", "");
        Write("base/scripts/lib.lua", "");
        Write("stranger/mod.json", """{ "id": "stranger", "name": "Stranger", "version": "1.0.0" }""");
        Write("stranger/scripts/lib.lua", "");
        Write("addon/mod.json", """{ "id": "addon", "name": "Addon", "version": "1.0.0", "dependencies": [{ "id": "base" }], "scripts": ["scripts/a.lua", "base:scripts/lib.lua"] }""");
        Write("addon/scripts/a.lua", "");
        var mods = Mechanica.Core.Data.ModLoader.LoadAll(_root);
        Assert.False(mods.HasErrors, string.Join("\n", mods.Diagnostics));
        IScriptSource source = new ModScriptSource(mods);

        Assert.Equal(["base:scripts/base.lua", "addon:scripts/a.lua", "base:scripts/lib.lua"], source.WorldScripts.Select(s => s.Key));
        Assert.Equal("base:scripts/lib.lua", source.FindModule("addon", "base:scripts/lib.lua")?.Key);
        Assert.Equal("addon:scripts/a.lua", source.FindModule("addon", "scripts/a.lua")?.Key);
        Assert.Null(source.FindModule("addon", "stranger:scripts/lib.lua"));
        Assert.Null(source.FindModule("addon", "scripts/missing.lua"));
        Assert.Null(source.FindModule("addon", "../base/scripts/lib.lua"));
    }
}
