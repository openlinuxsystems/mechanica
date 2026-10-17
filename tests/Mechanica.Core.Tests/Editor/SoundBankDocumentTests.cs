using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Sound;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.Tests.Sound;

namespace Mechanica.Core.Tests.Editor;

public sealed class SoundBankDocumentTests : IDisposable
{
    private readonly TestMods _mods = new();

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_mods.Root)!, recursive: true);

    private string ModDir(string id) => Path.Combine(_mods.Root, id);

    [Fact]
    public void ANewBankStartsEmptyAndSavesCanonicalJsonTheLoaderReads()
    {
        _mods.Mod("vanilla").Mod("my_mod").Clip("my_mod", "sounds/hum.ogg");
        var doc = SoundBankDocument.Open(ModDir("my_mod"), null, [])!;
        Assert.Equal("sounds/my_mod.json", doc.RelativeFile);
        Assert.Empty(doc.Events);
        Assert.False(doc.IsDirty);

        var changes = 0;
        doc.Changed += () => changes++;
        var i = doc.Add("hum", "sounds/hum.ogg");
        doc.Set(i, doc.Events[i] with { Loop = true, Parameters = [new SoundParameter { Name = "speed", Pitch = [1, 2] }] });
        Assert.Equal(2, changes);
        Assert.True(doc.IsDirty);

        var result = doc.Save();
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics));
        Assert.False(doc.IsDirty);
        var text = File.ReadAllText(doc.FilePath);
        Assert.Equal(DataJson.Write(doc.Bank), text);
        Assert.Contains("\"loop\": true", text);

        var registry = SoundRegistry.Load(_mods.Load());
        Assert.Empty(registry.Diagnostics);
        Assert.True(registry.Find("hum", "my_mod")!.Loop);

        var reopened = SoundBankDocument.Open(ModDir("my_mod"), null, [])!;
        Assert.Equal(["hum"], reopened.Events.Select(e => e.Id));
        Assert.Equal(["sounds/my_mod.json"], SoundBankDocument.Files(ModDir("my_mod")));
    }

    [Fact]
    public void ASaveWithErrorsWritesNothing()
    {
        _mods.Mod("my_mod");
        var doc = SoundBankDocument.Open(ModDir("my_mod"), null, [])!;
        doc.Add("no_clip");
        doc.Add("Bad Id", "sounds/missing.ogg");

        var result = doc.Save();

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.JsonPath == "$.events[0].clips");
        Assert.Contains(result.Diagnostics, d => d.JsonPath == "$.events[1].id");
        Assert.False(File.Exists(doc.FilePath));
        Assert.True(doc.IsDirty);
        Assert.Null(doc.Preview(0));
    }

    [Fact]
    public void AddedIdsAreMadeUniqueAndEventsCanBeRemoved()
    {
        _mods.Mod("my_mod");
        var doc = SoundBankDocument.Open(ModDir("my_mod"), null, [])!;
        doc.Add("new_sound");
        doc.Add("new_sound");
        doc.Add("new_sound");
        Assert.Equal(["new_sound", "new_sound_2", "new_sound_3"], doc.Events.Select(e => e.Id));
        doc.Remove(1);
        Assert.Equal(1, doc.IndexOf("new_sound_3"));
        Assert.Equal(-1, doc.IndexOf("new_sound_2"));
    }

    [Fact]
    public void PathsOutsideTheSoundsFolderAreRefused()
    {
        _mods.Mod("my_mod");
        foreach (var bad in new[] { "parts/x.json", "sounds/../x.json", "sounds/a.txt" })
        {
            var diagnostics = new List<DataDiagnostic>();
            Assert.Null(SoundBankDocument.Open(ModDir("my_mod"), bad, diagnostics));
            Assert.Single(diagnostics);
        }
    }

    [Fact]
    public void AnUnreadableBankIsReported()
    {
        _mods.Mod("my_mod").File("my_mod", "sounds/broken.json", "{ not json");
        var diagnostics = new List<DataDiagnostic>();
        Assert.Null(SoundBankDocument.Open(ModDir("my_mod"), "sounds/broken.json", diagnostics));
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void PreviewResolvesClipsInThisAndOtherMods()
    {
        _mods.Mod("vanilla").Clip("vanilla", "sounds/v.ogg").Mod("my_mod", dependencies: """[{ "id": "vanilla" }]""").Clip("my_mod", "sounds/a.wav");
        var doc = SoundBankDocument.Open(ModDir("my_mod"), null, [])!;
        doc.Add("pair", "sounds/a.wav");
        doc.Set(0, doc.Events[0] with { Clips = ["sounds/a.wav", "vanilla:sounds/v.ogg"], PitchVariation = 0.2f });

        var preview = doc.Preview(0)!;

        Assert.Equal("my_mod:pair", preview.QualifiedId);
        Assert.Equal([Path.Combine(ModDir("my_mod"), "sounds", "a.wav"), Path.Combine(ModDir("vanilla"), "sounds", "v.ogg")], preview.Clips);
        Assert.Equal(0.2f, preview.PitchVariation);
    }

    [Fact]
    public void ShortIdsAreBareWhenTheyResolveToTheSameEvent()
    {
        _mods.Mod("vanilla").Clip("vanilla", "sounds/a.ogg").Bank("vanilla", "place_wood", "smash_hit")
            .Mod("my_mod").Clip("my_mod", "sounds/a.ogg").Bank("my_mod", "smash_hit", "buzz");
        var registry = SoundRegistry.Load(_mods.Load());

        Assert.Equal("place_wood", registry.ShortId("vanilla:place_wood", "my_mod"));
        Assert.Equal("vanilla:smash_hit", registry.ShortId("vanilla:smash_hit", "my_mod"));
        Assert.Equal("smash_hit", registry.ShortId("my_mod:smash_hit", "my_mod"));
        Assert.Equal("my_mod:buzz", registry.ShortId("my_mod:buzz", "other"));
    }

    [Fact]
    public void PartDocumentBindsAndUnbindsSounds()
    {
        var doc = PartDocument.New(ModDir("my_mod"), new PartDefinition { Category = PartCategory.Block }, "plank", "Plank");

        doc.SetSound("place", "place_wood");
        doc.SetSound("start", "my_mod:buzz");
        Assert.Equal(new Dictionary<string, string> { ["place"] = "place_wood", ["start"] = "my_mod:buzz" }, doc.Definition.Sounds);
        Assert.Equal("Bind start sound", doc.UndoLabel);

        doc.SetSound("start", "my_mod:buzz");
        Assert.Equal("Bind start sound", doc.UndoLabel);
        doc.SetSound("place", null);
        doc.SetSound("start", "");
        Assert.Null(doc.Definition.Sounds);
        Assert.True(doc.Undo());
        Assert.Equal(["start"], doc.Definition.Sounds!.Keys);
    }
}
