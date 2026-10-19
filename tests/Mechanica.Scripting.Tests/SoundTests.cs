using System.Numerics;
using Mechanica.Core.Tests.Survival;
using static Mechanica.Scripting.Tests.ScriptRig;

namespace Mechanica.Scripting.Tests;

public sealed class SoundTests : IDisposable
{
    private readonly ScriptRig _rig = new(new ScriptSettings { MaxLoopsPerPart = 2 });
    private readonly FakeSound _sound = new();

    public SoundTests() => _rig.Runtime.Sound = _sound;

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void OneShotsPlayAtThePartOrAPointInThePartsMod()
    {
        _rig.Write("scripts/thruster.lua", """
            function on_create()
              state.a = sound.play("thruster_ignite")
              state.b = sound.play("vanilla:thump", vec3(1, 2, 3))
              local ok, why = sound.play("missing")
              state.c, state.why = ok, why
            end
            """);
        var creation = _rig.AddCreation(1, Thruster);
        _rig.Runtime.Update(0.1f);

        Assert.Empty(_rig.Errors);
        var state = _rig.Runtime.ExportState(1, PartOf(creation, Thruster))!;
        Assert.True(state["a"]!.GetValue<bool>());
        Assert.True(state["b"]!.GetValue<bool>());
        Assert.False(state["c"]!.GetValue<bool>());
        Assert.Equal("there is no sound event 'missing'", state["why"]!.GetValue<string>());
        Assert.Equal(("thruster_ignite", "test"), (_sound.Played[0].Sound, _sound.Played[0].Mod));
        Assert.Equal(PartKinematics.Of(creation, PartOf(creation, Thruster)).Position, _sound.Played[0].At);
        Assert.Equal(new Vector3(1, 2, 3), _sound.Played[1].At);
        Assert.Single(_rig.Messages, m => m.Kind == ScriptMessageKind.Warning);
    }

    [Fact]
    public void LoopsFollowThePartAndOnlyItsOwnHandlesWork()
    {
        _rig.Write("scripts/thruster.lua", """
            function on_create()
              state.hum = sound.start("hum", {speed = 0.5})
              state.second = sound.start("hum")
              state.third, state.why = sound.start("hum")
              state.set = sound.set_parameter(state.hum, "speed", 0.9)
              state.foreign = sound.stop(12345)
              state.stopped = sound.stop(state.second)
            end
            """);
        var creation = _rig.AddCreation(1, Thruster);
        _rig.Runtime.Update(0.1f);

        Assert.Empty(_rig.Errors);
        var state = _rig.Runtime.ExportState(1, PartOf(creation, Thruster))!;
        var hum = (int)state["hum"]!.GetValue<double>();
        Assert.Equal((1, PartOf(creation, Thruster)), (_sound.Loops[hum].Creation, _sound.Loops[hum].Part));
        Assert.Equal(0.5f, _sound.Started[0]!["speed"]);
        Assert.Null(_sound.Started[1]);
        Assert.Null(state["third"]);
        Assert.Equal("a part may run at most 2 sound loops", state["why"]!.GetValue<string>());
        Assert.True(state["set"]!.GetValue<bool>());
        Assert.Equal(0.9f, _sound.Loops[hum].Parameters["speed"]);
        Assert.False(state["foreign"]!.GetValue<bool>());
        Assert.True(state["stopped"]!.GetValue<bool>());
        Assert.Equal([hum], _sound.Loops.Keys);
    }

    [Fact]
    public void LoopsSurviveAHotReloadAndEndWithThePartOrAnError()
    {
        _rig.Write("scripts/thruster.lua", """function on_create() state.hum = sound.start("hum") end""");
        _rig.AddCreation(1, Thruster);
        _rig.AddCreation(2, Thruster);
        _rig.Runtime.Update(0.1f);
        Assert.Equal(2, _sound.Loops.Count);

        _rig.Write("scripts/thruster.lua", """
            function on_reload() state.kept = sound.set_parameter(state.hum, "speed", 1) end
            function on_update(dt) if part.creation() == 2 then error("boom") end end
            """);
        _rig.Runtime.CheckForChanges();
        Assert.True(_rig.Runtime.ExportState(1, PartOf(_rig.World.Creations[1], Thruster))!["kept"]!.GetValue<bool>());
        Assert.Equal(2, _sound.Loops.Count);

        _rig.Runtime.Update(0.1f);
        Assert.Single(_sound.Loops);

        _rig.World.Creations.Remove(1);
        _rig.Runtime.Update(0.1f);
        Assert.Empty(_sound.Loops);
    }

    [Fact]
    public void AnItemPlaysOneShotsAtItsHoldersEyesButRunsNoLoops()
    {
        using var items = new ItemRig();
        items.Runtime.Sound = _sound;
        items.Write("scripts/horn.lua", """
            function on_use(target)
              local loop, why = sound.start("hum")
              return { played = sound.play("honk"), loop = loop, why = why }
            end
            """);
        var horn = items.AddItem("horn", "scripts/horn.lua");
        items.F.Pockets(SurvivalFixture.Me).Add(horn, 1);

        var result = items.Used(SurvivalFixture.Me, items.F.SlotOf(SurvivalFixture.Me, horn));

        Assert.True(result["played"]!.GetValue<bool>());
        Assert.Null(result["loop"]);
        Assert.Equal("sound loops only work in part scripts; items and mod scripts play one-shots", result["why"]!.GetValue<string>());
        Assert.Equal(("honk", "test", new Vector3(0, 1.6f, 3)), Assert.Single(_sound.Played));
        Assert.Empty(_sound.Loops);
    }

    private sealed class FakeSound : IScriptSound
    {
        private int _next = 1;

        public List<(string Sound, string Mod, Vector3 At)> Played { get; } = [];

        public List<IReadOnlyDictionary<string, float>?> Started { get; } = [];

        public Dictionary<int, (int Creation, int Part, Dictionary<string, float> Parameters)> Loops { get; } = [];

        public string? Play(string sound, string ownerModId, Vector3 position)
        {
            if (sound == "missing")
                return $"there is no sound event '{sound}'";
            Played.Add((sound, ownerModId, position));
            return null;
        }

        public int StartLoop(string sound, string ownerModId, int creation, int part, IReadOnlyDictionary<string, float>? parameters, out string? error)
        {
            error = null;
            Started.Add(parameters is null ? null : new Dictionary<string, float>(parameters));
            Loops[_next] = (creation, part, new Dictionary<string, float>(parameters ?? new Dictionary<string, float>()));
            return _next++;
        }

        public string? SetLoopParameter(int handle, string name, float value)
        {
            if (!Loops.TryGetValue(handle, out var loop))
                return "the loop has stopped";
            loop.Parameters[name] = value;
            return null;
        }

        public void StopLoop(int handle) => Loops.Remove(handle);
    }
}
