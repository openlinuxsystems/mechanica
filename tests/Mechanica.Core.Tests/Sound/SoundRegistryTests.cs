using Mechanica.Core.Data;
using Mechanica.Core.Sound;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Sound;

public sealed class SoundRegistryTests : IDisposable
{
    private readonly TestMods _mods = new();

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_mods.Root)!, recursive: true);

    [Fact]
    public void LoadsEventsWithDefaultsAndQualifiedIds()
    {
        _mods.Mod("vanilla").Clip("vanilla", "sounds/a.ogg").Clip("vanilla", "sounds/b.wav")
            .File("vanilla", "sounds/tools.json", """
                {
                  // comments and trailing commas are fine
                  "events": [
                    { "id": "smash_hit", "clips": ["sounds/a.ogg", "sounds/b.wav"], "pitch_variation": 0.1, },
                    { "id": "engine_idle", "clips": ["sounds/a.ogg"], "loop": true, "bus": "sfx",
                      "parameters": [{ "name": "load", "pitch": [1, 1.5] }] },
                    { "id": "ui_click", "clips": ["sounds/b.wav"], "bus": "ui" },
                  ],
                }
                """);

        var registry = SoundRegistry.Load(_mods.Load());

        Assert.Empty(registry.Diagnostics);
        Assert.Equal(["vanilla:engine_idle", "vanilla:smash_hit", "vanilla:ui_click"], registry.Ids);
        var hit = registry.Find("smash_hit")!;
        Assert.Equal(2, hit.Clips.Count);
        Assert.Equal(Path.Combine(_mods.Root, "vanilla", "sounds", "b.wav"), hit.Clips[1]);
        Assert.Equal((SoundBus.Sfx, false, 0f, 1f, 0.1f, true), (hit.Bus, hit.Loop, hit.VolumeDb, hit.Pitch, hit.PitchVariation, hit.Spatial));
        Assert.Equal((SoundEvent.DefaultMinDistance, SoundEvent.DefaultMaxDistance, SoundRolloff.Inverse, SoundEvent.DefaultMaxInstances, 0f),
            (hit.MinDistance, hit.MaxDistance, hit.Rolloff, hit.MaxInstances, hit.Cooldown));
        Assert.True(registry.Find("vanilla:engine_idle")!.Loop);
        Assert.False(registry.Find("ui_click")!.Spatial);
    }

    [Fact]
    public void BareIdsMeanTheOwnersEventThenVanillas()
    {
        _mods.Mod("vanilla").Clip("vanilla", "sounds/a.ogg").Bank("vanilla", "place_wood", "horn_honk");
        _mods.Mod("cars", dependencies: """[{ "id": "vanilla" }]""").Clip("cars", "sounds/a.ogg").Bank("cars", "horn_honk");

        var registry = SoundRegistry.Load(_mods.Load());

        Assert.Empty(registry.Diagnostics);
        Assert.Equal("cars:horn_honk", registry.Find("horn_honk", "cars")!.QualifiedId);
        Assert.Equal("vanilla:place_wood", registry.Find("place_wood", "cars")!.QualifiedId);
        Assert.Equal("vanilla:horn_honk", registry.Find("horn_honk")!.QualifiedId);
        Assert.Equal("vanilla:horn_honk", registry.Find("vanilla:horn_honk", "cars")!.QualifiedId);
        Assert.Null(registry.Find("cars:place_wood"));
        Assert.Null(registry.Find(""));
    }

    [Fact]
    public void AModCanReplaceAnotherModsEvent()
    {
        _mods.Mod("vanilla").Clip("vanilla", "sounds/a.ogg").Bank("vanilla", "place_wood");
        _mods.Mod("loud", dependencies: """[{ "id": "vanilla" }]""").Clip("loud", "sounds/bang.wav")
            .File("loud", "sounds/override.json", """{ "events": [{ "id": "vanilla:place_wood", "clips": ["sounds/bang.wav", "vanilla:sounds/a.ogg"], "volume_db": 6 }] }""");

        var registry = SoundRegistry.Load(_mods.Load());

        Assert.Empty(registry.Diagnostics);
        var wood = registry.Find("place_wood")!;
        Assert.Equal(("vanilla:place_wood", "loud", 6f), (wood.QualifiedId, wood.ModId, wood.VolumeDb));
        Assert.Equal(Path.Combine(_mods.Root, "vanilla", "sounds", "a.ogg"), wood.Clips[1]);
        Assert.Single(registry.Events);
    }

    [Fact]
    public void TwoModsReplacingOneEventWarnAndTheLaterWins()
    {
        _mods.Mod("vanilla").Clip("vanilla", "sounds/a.ogg").Bank("vanilla", "place_wood");
        foreach (var mod in new[] { "aa", "bb" })
        {
            _mods.Mod(mod, dependencies: """[{ "id": "vanilla" }]""").Clip(mod, "sounds/a.ogg")
                .File(mod, "sounds/x.json", """{ "events": [{ "id": "vanilla:place_wood", "clips": ["sounds/a.ogg"] }] }""");
        }

        var registry = SoundRegistry.Load(_mods.Load());

        var warning = Assert.Single(registry.Diagnostics);
        Assert.Equal((DiagnosticSeverity.Warning, DiagnosticCodes.SoundOverrideConflict), (warning.Severity, warning.Code));
        Assert.Equal("bb", registry.Find("place_wood")!.ModId);
    }

    [Theory]
    [InlineData("""{ "events": [{ "id": "vanilla:place_wood", "clips": ["sounds/a.ogg"] }] }""", "must be in this mod's dependencies")]
    [InlineData("""{ "events": [{ "id": "other:place_wood", "clips": ["sounds/a.ogg"] }] }""", "must be in this mod's dependencies")]
    public void ReplacingNeedsTheDependency(string bank, string message)
    {
        _mods.Mod("vanilla").Clip("vanilla", "sounds/a.ogg").Bank("vanilla", "place_wood");
        _mods.Mod("loud").Clip("loud", "sounds/a.ogg").File("loud", "sounds/x.json", bank);

        var registry = SoundRegistry.Load(_mods.Load());

        Assert.Contains(registry.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains(message));
        Assert.Equal("vanilla", registry.Find("place_wood")!.ModId);
    }

    [Fact]
    public void ReplacingAnEventThatDoesNotExistIsAnError()
    {
        _mods.Mod("vanilla").Clip("vanilla", "sounds/a.ogg").Bank("vanilla", "place_wood");
        _mods.Mod("loud", dependencies: """[{ "id": "vanilla" }]""").Clip("loud", "sounds/a.ogg")
            .File("loud", "sounds/x.json", """{ "events": [{ "id": "vanilla:place_jelly", "clips": ["sounds/a.ogg"] }] }""");

        var registry = SoundRegistry.Load(_mods.Load());

        Assert.Contains(registry.Diagnostics, d => d.JsonPath == "$.events[0].id" && d.Message.Contains("does not have"));
        Assert.Null(registry.Find("place_jelly"));
    }

    [Theory]
    [InlineData("""{ "clips": ["sounds/a.ogg"] }""", "$.events[0].id", "id is required")]
    [InlineData("""{ "id": "Place Wood", "clips": ["sounds/a.ogg"] }""", "$.events[0].id", "snake_case")]
    [InlineData("""{ "id": "x" }""", "$.events[0].clips", "at least one")]
    [InlineData("""{ "id": "x", "clips": [] }""", "$.events[0].clips", "at least one")]
    [InlineData("""{ "id": "x", "clips": ["sounds/missing.ogg"] }""", "$.events[0].clips[0]", "was not found")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.mp3"] }""", "$.events[0].clips[0]", ".ogg or .wav")]
    [InlineData("""{ "id": "x", "clips": ["../a.ogg"] }""", "$.events[0].clips[0]", "cannot leave the mod folder")]
    [InlineData("""{ "id": "x", "clips": ["res://a.ogg"] }""", "$.events[0].clips[0]", "URL or engine path")]
    [InlineData("""{ "id": "x", "clips": ["other:sounds/a.ogg"] }""", "$.events[0].clips[0]", "not in this mod's dependencies")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "volume_db": 40 }""", "$.events[0].volume_db", "between -80 and 24")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "pitch": 0 }""", "$.events[0].pitch", "between 0.01 and 16")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "pitch_variation": 1 }""", "$.events[0].pitch_variation", "less than 1")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "volume_variation_db": -1 }""", "$.events[0].volume_variation_db", "between 0 and 24")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "min_distance": 0 }""", "$.events[0].min_distance", "greater than 0")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "min_distance": 50 }""", "$.events[0].max_distance", "greater than min_distance")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "max_instances": 0 }""", "$.events[0].max_instances", "1 to 64")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "cooldown": -1 }""", "$.events[0].cooldown", "0 to 60")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "rolloff": "cubic" }""", "$.events[0].rolloff", "")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "loop": true, "parameters": [{ "pitch": [1, 2] }] }""", "$.events[0].parameters[0].name", "name is required")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "loop": true, "parameters": [{ "name": "a", "pitch": [1, 2] }, { "name": "a", "pitch": [1, 2] }] }""", "$.events[0].parameters[1].name", "listed twice")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "loop": true, "parameters": [{ "name": "a", "min": 1, "max": 1, "pitch": [1, 2] }] }""", "$.events[0].parameters[0]", "greater than min")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "loop": true, "parameters": [{ "name": "a", "default": 2, "pitch": [1, 2] }] }""", "$.events[0].parameters[0].default", "between min")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "loop": true, "parameters": [{ "name": "a", "pitch": [1] }] }""", "$.events[0].parameters[0].pitch", "[low, high]")]
    [InlineData("""{ "id": "x", "clips": ["sounds/a.ogg"], "loop": true, "parameters": [{ "name": "a", "volume_db": [0, 99] }] }""", "$.events[0].parameters[0].volume_db", "[low, high]")]
    public void BadEventsAreReportedWithTheirPath(string json, string path, string message)
    {
        _mods.Mod("vanilla").Clip("vanilla", "sounds/a.ogg").Clip("vanilla", "sounds/a.mp3")
            .File("vanilla", "sounds/bad.json", $$"""{ "events": [{{json}}] }""")
            .Bank("vanilla", "good");

        var registry = SoundRegistry.Load(_mods.Load());

        var error = registry.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("vanilla/sounds/bad.json", error.File);
        Assert.Equal(path, error.JsonPath);
        Assert.Contains(message, error.Message);
        // The bad file is left out; the other one still loads.
        Assert.Equal(["vanilla:good"], registry.Ids);
    }

    [Fact]
    public void DuplicateIdsAreCaughtAcrossFiles()
    {
        _mods.Mod("vanilla").Clip("vanilla", "sounds/a.ogg").BankAt("vanilla", "x", "sounds/one.json").BankAt("vanilla", "x", "sounds/two.json");

        var registry = SoundRegistry.Load(_mods.Load());

        var error = registry.Diagnostics.First();
        Assert.Equal((DiagnosticCodes.SoundDuplicate, "vanilla/sounds/two.json"), (error.Code, error.File));
        Assert.Single(registry.Ids);
    }

    [Fact]
    public void WarningsDoNotStopAnEventLoading()
    {
        _mods.Mod("vanilla").Clip("vanilla", "sounds/a.ogg").File("vanilla", "sounds/w.json", """
            { "events": [
              { "id": "x", "clips": ["sounds/a.ogg"], "volum_db": 3, "parameters": [{ "name": "speed", "pitch": [1, 2] }] },
              { "id": "y", "clips": ["sounds/a.ogg"], "spatial": false, "max_distance": 10 }
            ] }
            """);

        var registry = SoundRegistry.Load(_mods.Load());

        Assert.Equal(["vanilla:x", "vanilla:y"], registry.Ids);
        Assert.All(registry.Diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        Assert.Contains(registry.Diagnostics, d => d.Message.Contains("Did you mean 'volume_db'?"));
        Assert.Contains(registry.Diagnostics, d => d.Message.Contains("meant for loops"));
        Assert.Contains(registry.Diagnostics, d => d.Message.Contains("not spatial"));
    }

    [Fact]
    public void PartsNamingUnknownSoundsGetWarnings()
    {
        _mods.Mod("vanilla").Clip("vanilla", "sounds/a.ogg").Bank("vanilla", "place_wood", "break_wood", "impact_wood", "engine_start")
            .Part("vanilla", "engine.json", TestMods.Block("engine", """{ "sound_material": "wood", "sounds": { "start": "engine_start", "stop": "engine_stopp", "impact": "impact_wood" } }"""))
            .Part("vanilla", "jelly.json", TestMods.Block("jelly", """{ "sound_material": "jelly" }"""));

        var registry = SoundRegistry.Load(_mods.Load());

        Assert.All(registry.Diagnostics, d => Assert.Equal((DiagnosticSeverity.Warning, DiagnosticCodes.SoundUnknown), (d.Severity, d.Code)));
        Assert.Equal(
        [
            ("vanilla/parts/engine.json", "$.sounds.stop", "engine_stopp"),
            ("vanilla/parts/jelly.json", "$.sound_material", "place_jelly"),
            ("vanilla/parts/jelly.json", "$.sound_material", "break_jelly"),
            ("vanilla/parts/jelly.json", "$.sound_material", "impact_jelly"),
        ], registry.Diagnostics.Select(d => (d.File, d.JsonPath, d.Message.Split('\'')[1])));
    }

    [Fact]
    public void ValidateChecksABankBeforeItIsSaved()
    {
        _mods.Mod("vanilla").Mod("cars", dependencies: """[{ "id": "vanilla" }]""").Clip("vanilla", "sounds/a.ogg").Clip("cars", "sounds/horn.ogg");
        var bank = new SoundBankFile
        {
            Events =
            [
                new SoundEventDefinition { Id = "horn", Clips = ["sounds/horn.ogg", "vanilla:sounds/a.ogg"] },
                new SoundEventDefinition { Id = "horn", Clips = ["sounds/nope.ogg"] },
            ],
        };

        var diagnostics = SoundRegistry.Validate(bank, Path.Combine(_mods.Root, "cars"), "sounds/cars.json");

        Assert.Equal(["$.events[1].id", "$.events[1].clips[0]"], diagnostics.Select(d => d.JsonPath));
        Assert.All(diagnostics, d => Assert.Equal("sounds/cars.json", d.File));
    }

    [Fact]
    public void ModsWithoutSoundsGiveAnEmptyRegistry()
    {
        _mods.Mod("vanilla");

        var registry = SoundRegistry.Load(_mods.Load());

        Assert.Empty(registry.Events);
        Assert.Empty(registry.Diagnostics);
        Assert.Empty(SoundRegistry.Empty.Events);
    }
}

internal static class SoundTestMods
{
    /// <summary>Writes a placeholder audio file; the loader only checks that clips exist.</summary>
    public static TestMods Clip(this TestMods mods, string mod, string path) => mods.File(mod, path, "not really audio");

    /// <summary>Writes a bank of minimal events that all play <c>sounds/a.ogg</c>.</summary>
    public static TestMods Bank(this TestMods mods, string mod, params string[] ids) => mods.Write(mod, ids, "sounds/bank.json");

    /// <summary>Writes a bank of minimal events to <paramref name="file"/>.</summary>
    public static TestMods BankAt(this TestMods mods, string mod, string id, string file) => mods.Write(mod, [id], file);

    private static TestMods Write(this TestMods mods, string mod, string[] ids, string file) =>
        mods.File(mod, file, $$"""{ "events": [{{string.Join(", ", ids.Select(id => $$"""{ "id": "{{id}}", "clips": ["sounds/a.ogg"] }"""))}}] }""");
}
