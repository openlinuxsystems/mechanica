using System.Text.Json.Nodes;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Editor;

public class BotDocumentTests : IDisposable
{
    private const string Template = """
        {
          // a test template
          "id": "new_bot", "name": "New Bot", "health": 50, "speed": 4, "raid_cost": 1,
          "attacks": [ { "id": "hit", "kind": "melee", "damage": 10, "range": 1.5, "interval": 1 } ],
        }
        """;

    private readonly TestMods _mods = new();

    public BotDocumentTests() => _mods.Mod("botmod");

    private string ModDir => Path.Combine(_mods.Root, "botmod");

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_mods.Root)!, recursive: true);

    private BotDocument New(string id = "biter") => BotDocument.New(ModDir, Template, id, "Biter");

    [Fact]
    public void A_tree_is_built_moved_and_edited_by_path_with_undo()
    {
        var doc = New();
        Assert.Null(doc.Tree);
        Assert.Null(doc.AddNode([0], "selector")); // no tree yet: only a root can go in
        Assert.Equal([], doc.AddNode([], "selector"));
        Assert.Equal([0], doc.AddNode([], "chase"));
        Assert.Equal([1], doc.AddNode([], "wander"));
        Assert.Null(doc.AddNode([1], "wait")); // a leaf takes no children
        Assert.Equal([2], doc.AddNode([], "cooldown"));
        Assert.Equal([2, 0], doc.AddNode([2], "attack"));
        Assert.Null(doc.AddNode([2], "stop")); // a decorator takes one

        // Built-in parameters start at their defaults.
        Assert.Equal(8, doc.Node([1])!["radius"]!.GetValue<double>());
        Assert.True(doc.SetParameter([2, 0], "attack", JsonValue.Create("hit")));
        Assert.False(doc.SetParameter([2, 0], "type", JsonValue.Create("x")));
        Assert.Equal([1], doc.MoveNode([2], -1));
        Assert.Equal("cooldown", doc.Node([1])!["type"]!.GetValue<string>());
        Assert.Equal("hit", doc.Node([1, 0])!["attack"]!.GetValue<string>());
        Assert.Null(doc.MoveNode([0], -1));

        Assert.True(doc.RemoveNode([1]));
        Assert.Equal(["chase", "wander"], ((JsonArray)doc.Tree!["children"]!).Select(n => n!["type"]!.GetValue<string>()));
        Assert.True(doc.Undo());
        Assert.Equal("cooldown", doc.Node([1])!["type"]!.GetValue<string>());
        Assert.True(doc.Redo());
        Assert.Equal("wander", doc.Node([1])!["type"]!.GetValue<string>());
        Assert.True(doc.RemoveNode([]));
        Assert.Null(doc.Tree);
    }

    [Fact]
    public void Export_writes_a_bot_the_loader_reads_and_refuses_errors()
    {
        var doc = New();
        doc.AddNode([], "sequence");
        doc.AddNode([], "find_player");
        doc.AddNode([], "attack");
        doc.SetParameter([1], "attack", JsonValue.Create("hit"));
        doc.Set("health", JsonValue.Create(-5));
        var refused = BotExporter.Export(doc, _mods.Load());
        Assert.False(refused.Succeeded);
        Assert.Contains(refused.Diagnostics, d => d.JsonPath == "$.health");
        Assert.False(File.Exists(Path.Combine(ModDir, "bots/biter.json")));

        doc.Set("health", JsonValue.Create(70));
        var result = BotExporter.Export(doc, _mods.Load());
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.False(doc.IsDirty);
        Assert.Equal("botmod:biter", result.QualifiedId);
        var book = BotBook.Load(_mods.Load());
        Assert.Empty(book.Diagnostics);
        Assert.Equal(70, book.FindBot("botmod:biter")!.Health);

        // Reopened, it is the same bot, and a second bot can't take its id.
        var reopened = BotDocument.Open(ModDir, "bots/biter.json", [])!;
        Assert.Equal(doc.Json.ToJsonString(), reopened.Json.ToJsonString());
        Assert.False(BotExporter.Export(New(), _mods.Load()).Succeeded);
        Assert.Equal(["bots/biter.json"], BotDocument.Files(ModDir));
    }

    [Fact]
    public void A_script_is_saved_beside_the_bot_and_read_back()
    {
        var doc = New();
        Assert.Null(BotExporter.ReadScript(doc));
        Assert.Equal("scripts/bots/biter.lua", BotExporter.SaveScript(doc, "function bite(node) return true end\r\n"));
        Assert.Equal("scripts/bots/biter.lua", doc.Json["script"]!.GetValue<string>());
        Assert.Equal("function bite(node) return true end\n", BotExporter.ReadScript(doc));
        Assert.True(BotExporter.Export(doc, _mods.Load()).Succeeded);
    }

    [Fact]
    public void A_batch_makes_bots_from_templates_and_earlier_bots_then_the_raid_table()
    {
        var json = """
            {
              "format_version": 1,
              "bots": [
                { "file": "bots/biter.json", "template": "melee", "set": { "name": "Biter", "behavior": { "type": "wander" } } },
                { "file": "bots/big_biter.json", "base": "biter", "set": { "name": "Big Biter", "health": 200, "raid_cost": 3 } }
              ],
              "raids": { "levels": [ { "level": 1, "min_value": 5, "budget": 4, "bots": [ { "bot": "biter", "count": 1 }, { "bot": "big_biter", "count": 1 } ] } ] }
            }
            """;
        var diagnostics = new List<DataDiagnostic>();
        var batch = BotBatch.Read(json, "batch.json", diagnostics)!;
        Assert.True(BotBatch.IsBotBatch(json));
        Assert.False(BotBatch.IsBotBatch("""{ "parts": [] }"""));
        var results = BotBatch.Run(batch, ModDir, name => name == "melee" ? Template : null, "batch.json", diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(3, results.Count(r => r.Succeeded));
        var content = _mods.Load();
        var book = BotBook.Load(content);
        Assert.Equal((200f, 4f), (book.FindBot("botmod:big_biter")!.Health, book.FindBot("botmod:big_biter")!.Speed));
        Assert.Equal("wander", book.FindBot("botmod:big_biter")!.Definition.Behavior!["type"]!.GetValue<string>());
        Assert.Equal(4, RaidTable.Load(content, book).Levels.Single().MixCost);
    }

    [Theory]
    [InlineData("""{ "bots": [ { "file": "parts/x.json", "template": "melee" } ] }""", "$.bots[0].file")]
    [InlineData("""{ "bots": [ { "file": "bots/x.json" } ] }""", "$.bots[0]")]
    [InlineData("""{ "bots": [ { "file": "bots/x.json", "base": "y" } ] }""", "$.bots[0].base")]
    [InlineData("""{ "bots": [ { "file": "bots/x.json", "template": "nope" } ] }""", "$.bots[0].template")]
    [InlineData("""{ "bots": [ { "file": "bots/x.json", "template": "melee", "set": { "id": "z" } } ] }""", "$.bots[0].set.id")]
    [InlineData("""{ "bots": [] }""", "$.bots")]
    public void Batch_errors_name_the_entry_and_write_nothing(string json, string path)
    {
        var diagnostics = new List<DataDiagnostic>();
        var batch = BotBatch.Read(json, "batch.json", diagnostics)!;
        Assert.Empty(BotBatch.Run(batch, ModDir, name => name == "melee" ? Template : null, "batch.json", diagnostics));
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.JsonPath == path);
        Assert.False(Directory.Exists(Path.Combine(ModDir, "bots")));
    }
}
