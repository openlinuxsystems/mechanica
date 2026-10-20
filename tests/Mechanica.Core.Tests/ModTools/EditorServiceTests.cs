using System.Text.Json;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.ModTools;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.ModTools;

public sealed class EditorServiceTests : IDisposable
{
    private static readonly string GameData = Path.Combine(TestMods.RepoRoot, "game", "data");

    private readonly TestMods _mods = new();
    private readonly FakeHost _host = new();
    private readonly EditorService _editor;

    public EditorServiceTests()
    {
        // The real vanilla, so templates, stations and items resolve as in the game.
        Directory.CreateSymbolicLink(Path.Combine(_mods.Root, "vanilla"), Path.Combine(TestMods.RepoRoot, "mods", "vanilla"));
        _mods.File("m", "mod.json", """{ "id": "m", "name": "m", "version": "1.0.0", "dependencies": [{ "id": "vanilla" }] }""");
        var workspace = new ModWorkspace(_mods.Root, Path.Combine(Path.GetDirectoryName(_mods.Root)!, ModProfile.FileName),
            source => source.Contains("if then") ? new ScriptProblem(1, 0, "unexpected symbol") : null);
        _editor = new EditorService(workspace, _host);
    }

    public void Dispose() => _mods.Dispose();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private string ModFile(string path) => Path.Combine(_mods.Root, "m", path);

    [Fact]
    public async Task APartGoesFromTemplateThroughEditsToALoadedPart()
    {
        var part = _editor.NewPart("m", "block", "crate", "Crate");
        Assert.Equal("Crate", part.Definition.GetProperty("name").GetString());
        Assert.True(part.Dirty);

        part = _editor.EditPart(part.Doc, "Heavier", null, Json("""{ "mass": 9, "visual": { "color": "#112233" } }"""), null);
        Assert.Equal(9, part.Definition.GetProperty("mass").GetDouble());
        Assert.Equal(0.8, part.Definition.GetProperty("visual").GetProperty("roughness").GetDouble());
        Assert.Equal("Heavier", part.Undo);
        Assert.Equal(2, _editor.UndoPart(part.Doc, redo: false).Definition.GetProperty("mass").GetDouble());
        Assert.Equal(9, _editor.UndoPart(part.Doc, redo: true).Definition.GetProperty("mass").GetDouble());
        Assert.Throws<ModToolException>(() => _editor.UndoPart(part.Doc, redo: true));
        Assert.Contains("place", _editor.SetPartSound(part.Doc, "place", "place_wood").Definition.GetProperty("sounds").GetRawText());
        Assert.NotEmpty(_editor.PartContacts(part.Doc));
        Assert.DoesNotContain(_editor.ValidatePart(part.Doc), d => d.Severity == "error");

        var export = await _editor.ExportPartAsync(part.Doc);

        Assert.True(export.Ok, string.Join("\n", export.Diagnostics.Select(d => d.Message)));
        Assert.Equal(("m:crate", "parts/crate.json"), (export.Id, export.File));
        Assert.False(_editor.GetPart(part.Doc).Dirty);
        Assert.Equal(9f, _mods.Load().FindPart("m:crate")!.Definition.Mass);
        Assert.Equal(0, _host.Bakes);
    }

    [Fact]
    public async Task ModelPiecesAreBakedByTheHost()
    {
        var part = _editor.NewPart("m", "block", "boxy", null);
        part = _editor.EditPart(part.Doc, null, null, null, Json("""{ "model": [{ "kind": "box", "size": [0.25, 0.25, 0.25] }, { "kind": "box", "size": [0.1, 0.1, 0.1], "position": [0, 0.2, 0] }] }"""));
        Assert.True(part.NeedsBake);

        var export = await _editor.ExportPartAsync(part.Doc);

        Assert.True(export.Ok, string.Join("\n", export.Diagnostics.Select(d => d.Message)));
        Assert.Equal(1, _host.Bakes);
        Assert.Equal(FakeHost.Glb, File.ReadAllBytes(ModFile(PartExporter.BakedMeshPath("parts/boxy.json"))));
        Assert.True(File.Exists(ModFile("editor/parts/boxy.json")));
    }

    [Fact]
    public void OpensExistingPartsAndRefusesBadInput()
    {
        _mods.Part("m", "a.json", TestMods.Block("a"));
        var part = _editor.OpenPart("m", "parts/a.json");

        Assert.Equal(("m", "parts/a.json", false), (part.Mod, part.File, part.Dirty));
        Assert.Throws<ModToolException>(() => _editor.OpenPart("m", "../vanilla/parts/x.json"));
        Assert.Throws<ModToolException>(() => _editor.OpenPart("m", "parts/missing.json"));
        Assert.Throws<ModToolException>(() => _editor.NewPart("vanilla", "block", "x", null));
        Assert.Throws<ModToolException>(() => _editor.NewPart("m", "nope", "x", null));
        Assert.Throws<ModToolException>(() => _editor.NewPart("m", "block", "Bad Id", null));
        Assert.Throws<ModToolException>(() => _editor.EditPart(part.Doc, null, null, Json("""{ "mass": "heavy" }"""), null));
        Assert.Throws<ModToolException>(() => _editor.EditPart(part.Doc, null, Json("{}"), Json("{}"), null));
        _editor.Close(part.Doc);
        Assert.Throws<ModToolException>(() => _editor.GetPart(part.Doc));
        Assert.Throws<ModToolException>(() => _editor.Close(part.Doc));
    }

    [Fact]
    public void ABotGoesFromTemplateThroughTreeEditsToAFile()
    {
        var bot = _editor.NewBot("m", "melee", "biter", "Biter");
        bot = _editor.EditBot(bot.Doc, Json("""{ "health": 80 }"""), null);
        Assert.Equal(80, bot.Bot.GetProperty("health").GetDouble());

        var (added, _) = _editor.EditBotTree(bot.Doc, "add", [], "wander", null, null, null);
        Assert.Equal([4], added);
        _editor.EditBotTree(bot.Doc, "set", [4], null, null, "radius", Json("3"));
        Assert.Equal([3], _editor.EditBotTree(bot.Doc, "move", [4], null, -1, null, null).Path);
        _editor.EditBotTree(bot.Doc, "remove", [3], null, null, null, null);
        Assert.Throws<ModToolException>(() => _editor.EditBotTree(bot.Doc, "remove", [9], null, null, null, null));
        Assert.Throws<ModToolException>(() => _editor.EditBotTree(bot.Doc, "explode", [], null, null, null, null));
        Assert.DoesNotContain(_editor.ValidateBot(bot.Doc), d => d.Severity == "error");

        Assert.Throws<ModToolException>(() => _editor.SaveBotScript(bot.Doc, "if then"));
        Assert.Equal("scripts/bots/biter.lua", _editor.SaveBotScript(bot.Doc, "-- biter\n"));
        Assert.Equal("-- biter\n", _editor.BotScript(bot.Doc));
        var export = _editor.ExportBot(bot.Doc);

        Assert.True(export.Ok, string.Join("\n", export.Diagnostics.Select(d => d.Message)));
        Assert.Equal("bots/biter.json", export.File);
        Assert.Equal(80, Core.Bots.BotBook.Load(_mods.Load()).FindBot("m:biter")!.Health);
    }

    [Fact]
    public void RaidTablesAreCheckedAgainstTheBots()
    {
        Assert.Null(_editor.Raids("m"));
        var bad = _editor.SaveRaids("m", Json("""{ "levels": [{ "level": 1, "min_value": 1, "budget": 1, "bots": [{ "bot": "m:nobody", "count": 1 }] }] }"""));
        Assert.False(bad.Ok);

        var good = _editor.SaveRaids("m", Json("""{ "levels": [{ "level": 1, "min_value": 1, "budget": 1, "bots": [{ "bot": "vanilla:scuttler_green", "count": 1 }] }] }"""));

        Assert.True(good.Ok, string.Join("\n", good.Diagnostics.Select(d => d.Message)));
        Assert.Equal(1, _editor.Raids("m")!.Value.GetProperty("levels").GetArrayLength());
    }

    [Fact]
    public void RecipesSaveListAndDelete()
    {
        var recipe = Json("""{ "id": "plank", "stations": ["fabricator"], "inputs": [{ "item": "scrap_metal_block", "count": 1 }], "outputs": [{ "item": "antenna", "count": 2 }], "time": 1 }""");

        var saved = _editor.SaveRecipe("m", recipe, null);

        Assert.True(saved.Ok, string.Join("\n", saved.Diagnostics.Select(d => d.Message)));
        Assert.Equal("recipes/plank.json", Assert.Single(_editor.Recipes("m")).File);
        Assert.False(_editor.SaveRecipe("m", Json("""{ "id": "bad", "stations": ["nowhere"], "inputs": [], "outputs": [] }"""), null).Ok);
        _editor.DeleteRecipe("m", "recipes/plank.json");
        Assert.Empty(_editor.Recipes("m"));
        Assert.Throws<ModToolException>(() => _editor.DeleteRecipe("m", "parts/plank.json"));
    }

    [Fact]
    public void SoundBanksSaveAndReadBack()
    {
        _mods.File("m", "sounds/clank.ogg", "ogg");

        var saved = _editor.SaveSoundBank("m", null, Json("""[{ "id": "clank", "clips": ["sounds/clank.ogg"], "volume_db": -3 }]"""));

        Assert.True(saved.Ok, string.Join("\n", saved.Diagnostics.Select(d => d.Message)));
        Assert.Equal(["sounds/m.json"], _editor.SoundBanks("m"));
        Assert.Equal("clank", _editor.SoundBank("m", null).GetProperty("events")[0].GetProperty("id").GetString());
        Assert.False(_editor.SaveSoundBank("m", null, Json("""[{ "id": "gone", "clips": ["sounds/missing.ogg"] }]""")).Ok);
        Assert.Throws<ModToolException>(() => _editor.SoundBank("m", "parts/x.json"));
    }

    [Fact]
    public async Task BatchesRunThroughTheHost()
    {
        _mods.File("m", "editor/batches/b.json", "{}");

        var report = await _editor.RunBatchAsync("m", "editor/batches/b.json");

        Assert.Equal(("parts", true), (report.Kind, report.Ok));
        Assert.Equal(ModFile("editor/batches/b.json"), _host.LastBatch);
        Assert.Equal("parts/x.json", Assert.Single(report.Exports).File);
        await Assert.ThrowsAsync<ModToolException>(() => _editor.RunBatchAsync("m", "editor/batches/none.json"));
    }

    internal sealed class FakeHost : IEditorHost
    {
        public static readonly byte[] Glb = [0x67, 0x6c, 0x54, 0x46];

        public int Bakes { get; private set; }

        public string? LastBatch { get; private set; }

        public IReadOnlyList<string> PartTemplates => ["block"];

        public IReadOnlyList<string> BotTemplates => ["melee"];

        public string? PartTemplate(string name) => Read("part_templates", name);

        public string? BotTemplate(string name) => Read("bot_templates", name);

        public Task<byte[]?> BakeAsync(PartDocument document)
        {
            Bakes++;
            return Task.FromResult<byte[]?>(Glb);
        }

        public Task<(string Kind, bool Ok, IReadOnlyList<ExportResult> Exports, IReadOnlyList<DataDiagnostic> Diagnostics)> RunBatchAsync(string batchFile)
        {
            LastBatch = batchFile;
            var dir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(batchFile)))!;
            return Task.FromResult<(string, bool, IReadOnlyList<ExportResult>, IReadOnlyList<DataDiagnostic>)>(
                ("parts", true, [new ExportResult(true, Path.Combine(dir, "parts", "x.json"), "m:x", [])], []));
        }

        private static string? Read(string folder, string name)
        {
            var path = Path.Combine(GameData, folder, $"{name}.json");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }
}
