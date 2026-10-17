using Godot;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using Mechanica.Game.Editor;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Bot Behavior Editor (chunk 21, D9) in a scratch mods folder: a bot starts from the melee template,
/// its behaviour tree is rebuilt node by node, a broken script is refused with its line and a good one is
/// saved, the bot exports and loads exactly as authored, undo works, a raid table using the bot is
/// previewed and saved, and Help opens the Bot Tutorial. Exits 0 on success.
/// </summary>
public partial class BotEditorTest : StagedTest
{
    private const string Mod = "bot_editor_test";

    private BotEditorScreen _editor = null!;
    private string _modsRoot = "";

    protected override string Tag => "boteditor";

    public override void _Ready()
    {
        _modsRoot = ProjectSettings.GlobalizePath("user://tests/bot_editor_mods");
        if (Directory.Exists(_modsRoot))
            Directory.Delete(_modsRoot, recursive: true);
        Directory.CreateDirectory(Path.Combine(_modsRoot, Mod));
        File.WriteAllText(Path.Combine(_modsRoot, Mod, ModLoader.ManifestFileName),
            $$"""{ "id": "{{Mod}}", "name": "Bot editor test", "version": "1.0.0" }""");
        _editor = new BotEditorScreen { Name = "BotEditor", ModsRoot = _modsRoot };
        AddChild(_editor);

        Add("a bot starts from a template", _ =>
        {
            Check(_editor.Templates.Keys.SequenceEqual(["melee", "passive", "ranged"]), $"the templates are listed ({string.Join(", ", _editor.Templates.Keys)})");
            Check(_editor.SelectMod(Mod) && _editor.ModDirectory!.EndsWith(Mod, StringComparison.Ordinal), "the scratch mod is picked");
            Check(!_editor.New("melee", "Not An Id"), "a bad id is refused");
            Check(_editor.New("melee", "biter") && _editor.Document!.Id == "biter" && _editor.Document.Tree is not null,
                "the melee template brings stats, an attack and a tree");
            return true;
        });
        Add("the behaviour tree is rebuilt node by node", _ =>
        {
            _editor.SelectNode([]);
            Check(_editor.RemoveNode() && _editor.Document!.Tree is null, "the template's tree is removed");
            Check(_editor.AddNode("selector") && _editor.Selected.Count == 0, "a selector is the new root");
            Check(_editor.AddNode("sequence") && _editor.Selected.SequenceEqual([0]), "a sequence goes under it");
            Check(_editor.AddNode("find_player") && _editor.Selected.SequenceEqual([0, 0]), "find_player goes in the sequence");
            Check(!_editor.AddNode("wait"), "a leaf takes no children");
            _editor.SelectNode([0]);
            Check(_editor.AddNode("attack") && _editor.SetParameter("attack", "hit"), "an attack node uses the bot's attack");
            _editor.SelectNode([]);
            Check(_editor.AddNode("wander") && _editor.SetParameter("radius", "5"), "wander is the fallback, radius 5");
            Check(_editor.MoveNode(-1) && _editor.Selected.SequenceEqual([0]), "wander moves up");
            Check(_editor.MoveNode(1) && _editor.Selected.SequenceEqual([1]), "and back down");
            _editor.SelectNode([1]);
            Check(_editor.AddNode("bite_hard") is false, "a script leaf is a leaf too");
            _editor.SelectNode([0]);
            Check(_editor.AddNode("bite_hard"), "a leaf from the bot's script can be added by name");
            return true;
        });
        Add("a broken script is refused and a good one saved", _ =>
        {
            _editor.ScriptText = "function bite_hard(node)\n  return \n";
            Check(!_editor.SaveScript() && _editor.StatusText.Contains("line", StringComparison.Ordinal), $"the syntax error is shown ({_editor.StatusText})");
            Check(!File.Exists(Path.Combine(_modsRoot, Mod, "scripts/bots/biter.lua")), "nothing is written");
            _editor.ScriptText = "function bite_hard(node)\n  bot.sound(\"scuttler_attack\")\n  return true\nend\n";
            Check(_editor.SaveScript() && File.Exists(Path.Combine(_modsRoot, Mod, "scripts/bots/biter.lua")), $"the script is saved ({_editor.StatusText})");
            return true;
        });
        Add("the bot exports and loads as authored", _ =>
        {
            var result = _editor.Export();
            Check(result is { Succeeded: true }, $"export succeeds ({_editor.StatusText})");
            var book = BotBook.Load(ModLoader.LoadAll(_modsRoot));
            var bot = book.FindBot($"{Mod}:biter");
            Check(book.Diagnostics.Count == 0 && bot is not null, $"the bot loads ({string.Join("; ", book.Diagnostics)})");
            var tree = bot!.Definition.Behavior!;
            Check(tree["type"]!.GetValue<string>() == "selector" && tree["children"]!.AsArray().Count == 2, "the root selector has two children");
            Check(tree["children"]![0]!["children"]!.AsArray().Select(n => n!["type"]!.GetValue<string>()).SequenceEqual(["find_player", "attack", "bite_hard"]),
                "the sequence holds find_player, attack and the script leaf");
            Check(tree["children"]![1]!["radius"]!.GetValue<double>() == 5, "wander's radius is 5");
            Check(bot.Script == $"{Mod}:scripts/bots/biter.lua", $"the bot points at its script ({bot.Script})");
            Check(_editor.Document!.Undo() && _editor.Document.IsDirty, "undo takes back the last edit");
            Check(_editor.Document.Redo() && !_editor.Document.IsDirty, "redo puts it back");
            return true;
        });
        Add("a raid table using the bot is previewed and saved", _ =>
        {
            Check(_editor.SetRaidsJson("""{ "levels": [ { "level": 1, "min_value": 5, "budget": 3, "bots": [ { "bot": "biter", "count": 3 } ] } ] }""") is null,
                "the raid table reads");
            var saved = _editor.SaveRaids();
            Check(saved is { Succeeded: true }, $"it saves ({_editor.StatusText})");
            var content = ModLoader.LoadAll(_modsRoot);
            var table = RaidTable.Load(content, BotBook.Load(content));
            Check(table.Diagnostics.Count == 0 && table.Plan(10, 2) is { Level: 1, Budget: 4 } plan && plan.Bots.All(b => b == $"{Mod}:biter"),
                $"two players get four biters ({string.Join("; ", table.Diagnostics)})");
            Check(_editor.SetRaidsJson("""{ "levels": [ { "level": 1, "budget": 3, "bots": [] } ] }""") is null
                && _editor.SaveRaids() is { Succeeded: false }, "a broken table is not saved");
            return true;
        });
        Add("Help opens the Bot Tutorial", _ =>
        {
            _editor.OpenHelp();
            Check(_editor.Help.Current == BotEditorScreen.HelpFile && _editor.Help.Text.Contains("behaviour tree", StringComparison.OrdinalIgnoreCase),
                $"the tutorial is shown ({_editor.Help.Current})");
            return true;
        });
    }
}
