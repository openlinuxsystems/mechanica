using Mechanica.Core.Data;
using Mechanica.Core.Sound;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Sound;

/// <summary>The sound events vanilla ships in <c>mods/vanilla/sounds</c> (chunk 16).</summary>
public class VanillaSoundsTests
{
    private static readonly string VanillaDir = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

    private static SoundRegistry Load() => SoundRegistry.Load(ModLoader.Load([VanillaDir]));

    [Fact]
    public void EveryCatalogSoundIsRegistered()
    {
        var registry = Load();

        Assert.Empty(registry.Diagnostics);
        var missing = CatalogSounds.Ids.Where(id => registry.Find(id) is null).ToList();
        Assert.True(missing.Count == 0, $"not registered: {string.Join(", ", missing)}");
        Assert.Equal(CatalogSounds.Ids.Select(id => $"vanilla:{id}").Order(StringComparer.Ordinal), registry.Ids);
    }

    [Fact]
    public void EveryClipExists()
    {
        foreach (var sound in Load().Events.Values)
        {
            Assert.NotEmpty(sound.Clips);
            Assert.All(sound.Clips, clip => Assert.True(File.Exists(clip) && new FileInfo(clip).Length > 100, $"{sound}: {clip}"));
        }
    }

    [Fact]
    public void LoopsFollowTheCatalogConventions()
    {
        var registry = Load();

        // CATALOG_IDS.md: loops end in _loop or are inherently loops.
        Assert.All(registry.Events.Values.Where(e => e.QualifiedId.EndsWith("_loop", StringComparison.Ordinal)), e => Assert.True(e.Loop, e.QualifiedId));
        foreach (var id in new[] { "engine_idle", "hover_hum", "music_explore", "wind_light", "bearing_spin_loop", "radio_loop_03" })
            Assert.True(registry.Find(id)!.Loop, id);
        foreach (var id in new[] { "place_wood", "smash_hit", "ui_click", "engine_start" })
            Assert.False(registry.Find(id)!.Loop, id);
        // Every parameterised sound is a loop, and the engines, rotors and thrusters can be driven.
        Assert.All(registry.Events.Values.Where(e => e.Parameters.Count > 0), e => Assert.True(e.Loop, e.QualifiedId));
        foreach (var (id, parameter) in new[] { ("engine_rev", "rpm"), ("bearing_spin_loop", "speed"), ("thruster_loop", "thrust"), ("rotor_loop", "speed") })
            Assert.NotNull(registry.Find(id)!.FindParameter(parameter));
    }

    [Fact]
    public void UiAndMusicAreNotPositional()
    {
        var registry = Load();

        Assert.All(registry.Events.Values.Where(e => e.Bus == SoundBus.Ui), e => Assert.False(e.Spatial, e.QualifiedId));
        Assert.False(registry.Find("music_menu")!.Spatial);
        Assert.True(registry.Find("radio_loop_01")!.Spatial, "a radio station plays from the radio");
        Assert.True(registry.Find("place_wood")!.Spatial);
    }

    [Fact]
    public void EveryCatalogMaterialHasItsSounds()
    {
        var registry = Load();
        foreach (var material in new[] { "wood", "metal", "concrete", "glass", "scrap", "plastic", "tile", "cardboard", "fabric" })
        {
            foreach (var key in PartSounds.MaterialKeys)
                Assert.NotNull(registry.Find(PartSounds.MaterialEvent(key, material)));
        }
    }

    [Fact]
    public void BankFilesAreInCanonicalForm()
    {
        // The Sound Event Binder writes banks with DataJson; the generated files must match its output exactly.
        foreach (var file in Directory.GetFiles(Path.Combine(VanillaDir, SoundRegistry.Folder), "*.json"))
        {
            var text = File.ReadAllText(file);
            var bank = DataJson.Read<SoundBankFile>(text)!;
            Assert.Equal(text, DataJson.Write(bank));
        }
    }
}
