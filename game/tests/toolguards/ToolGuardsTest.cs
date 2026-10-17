using Godot;
using Mechanica.Core.Data;
using Mechanica.Game.Editor;
using Mechanica.Game.Mods;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// The mod tools ask before anything destructive (UI spec §13 "Confirmation dialogs for destructive
/// actions"): the Part Editor before closing, starting or opening over unsaved changes, before running a
/// batch and before a new part over an existing file; recipe and sound event deletes; replacing a script
/// with a template; the Bot Behavior Editor before leaving or switching bots with unsaved changes and
/// before a new bot over an existing file; the Mod Manager before dropping an unapplied setup. Cancel
/// always leaves everything as it was.
/// </summary>
public partial class ToolGuardsTest : StagedTest
{
    private const string Mod = "guard_test";
    private string _modsRoot = "";
    private PartEditorScreen _parts = null!;
    private BotEditorScreen _bots = null!;
    private ModManagerScreen _manager = null!;
    private int _closed;
    private string _recipeFile = "";

    protected override string Tag => "toolguards";

    public override void _Ready()
    {
        _modsRoot = ProjectSettings.GlobalizePath("user://tests/guard_mods");
        if (Directory.Exists(_modsRoot))
            Directory.Delete(_modsRoot, recursive: true);
        Directory.CreateDirectory(Path.Combine(_modsRoot, Mod));
        File.WriteAllText(Path.Combine(_modsRoot, Mod, ModLoader.ManifestFileName),
            $$"""{ "id": "{{Mod}}", "name": "Guard test", "version": "1.0.0" }""");

        Add("the Part Editor asks before closing over an unsaved part", frame =>
        {
            if (frame == 0)
            {
                _parts = new PartEditorScreen { Name = "PartEditor", ModsRoot = _modsRoot };
                _parts.CloseRequested += () => _closed++;
                AddChild(_parts);
                return false;
            }
            Check(_parts.NewPart(Mod, "crate", "Crate", "block") is null && _parts.HasUnsavedChanges, "a new part is unsaved");
            Press(_parts, "Close");
            Check(Asked("Unsaved changes") && _closed == 0, "Close asks first");
            ConfirmDialog.Current!.Cancel();
            Check(_closed == 0 && _parts.Document?.Definition.Id == "crate", "Cancel keeps the part open");
            Press(_parts, "New…");
            Check(Asked("Unsaved changes"), "New over an unsaved part asks too");
            ConfirmDialog.Current!.Cancel();
            Press(_parts, "Open…");
            Check(Asked("Unsaved changes"), "and so does Open");
            ConfirmDialog.Current!.Cancel();
            Press(_parts, "Close");
            ConfirmDialog.Current!.Confirm();
            Check(_closed == 1, "Discard closes");
            _ = _parts.ExportAsync();
            return true;
        });
        Add("a saved part closes without asking", _ =>
        {
            if (_parts.HasUnsavedChanges)
                return false;
            Press(_parts, "Close");
            Check(ConfirmDialog.Current is null && _closed == 2, "nothing to lose, no question");
            return true;
        });
        Add("a new part over an existing file asks", _ =>
        {
            var dialog = _parts.GetChildren().OfType<ConfirmationDialog>().First(d => d.Title == "New part");
            var fields = Walk(dialog).OfType<LineEdit>().ToList();
            (fields[0].Text, fields[1].Text, fields[2].Text) = (Mod, "crate", "Crate again");
            dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
            Check(Asked("Part exists") && ConfirmDialog.Current!.Message.Contains("parts/crate.json"), $"it names the file ({ConfirmDialog.Current?.Message})");
            var before = _parts.Document;
            ConfirmDialog.Current!.Cancel();
            Check(_parts.Document == before, "Cancel keeps the open part");
            return true;
        });
        Add("running a batch asks first", _ =>
        {
            var batch = _parts.GetChildren().OfType<FileDialog>().First(d => d.Title == "Run batch");
            batch.EmitSignal(FileDialog.SignalName.FileSelected, Path.Combine(_modsRoot, Mod, "missing_batch.json"));
            Check(Asked("Run batch") && ConfirmDialog.Current!.Message.Contains("missing_batch.json"), "it names the batch");
            ConfirmDialog.Current!.Cancel();
            return true;
        });
        Add("deleting a recipe asks, and Cancel keeps the file", _ =>
        {
            var recipes = _parts.Recipes;
            _recipeFile = Path.Combine(_modsRoot, Mod, "recipes", "crate.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_recipeFile)!);
            File.WriteAllText(_recipeFile, $$"""
                { "id": "crate", "stations": ["bench"], "inputs": [{ "item": "{{Mod}}:crate", "count": 2 }], "outputs": [{ "item": "{{Mod}}:crate", "count": 1 }], "time": 1 }
                """);
            recipes.Reload();
            Check(recipes.Current?.Id == "crate", "the part's recipe is open");
            Check(recipes.AskDelete() is not null && Asked("Delete recipe"), "Delete asks");
            ConfirmDialog.Current!.Cancel();
            Check(File.Exists(_recipeFile), "Cancel keeps the file");
            recipes.AskDelete()!.Confirm();
            Check(!File.Exists(_recipeFile), "Delete removes it");
            return true;
        });
        Add("deleting a sound event asks", _ =>
        {
            var sounds = _parts.Sounds;
            sounds.SelectEvent(sounds.Bank!.Add("guard_beep"));
            var count = sounds.Bank.Events.Count;
            Check(sounds.AskDeleteEvent() is not null && Asked("Delete sound event") && ConfirmDialog.Current!.Message.Contains("guard_beep"), "Delete asks and names the event");
            ConfirmDialog.Current!.Cancel();
            Check(sounds.Bank.Events.Count == count, "Cancel keeps it");
            sounds.AskDeleteEvent()!.Confirm();
            Check(sounds.Bank.Events.Count == count - 1, "Delete removes it");
            return true;
        });
        Add("a template over code asks first", _ =>
        {
            var lua = _parts.Lua;
            lua.Code = "-- my code";
            Check(lua.AskNewFromTemplate() is not null && Asked("Replace script"), "replacing code asks");
            ConfirmDialog.Current!.Cancel();
            Check(lua.Code == "-- my code", "Cancel keeps the code");
            lua.Code = "";
            Check(lua.AskNewFromTemplate() is null && lua.Code.Length > 0, "an empty editor takes the template straight away");
            RemoveChild(_parts);
            _parts.QueueFree();
            return true;
        });
        Add("the Bot Behavior Editor asks before leaving or switching", frame =>
        {
            if (frame == 0)
            {
                _bots = new BotEditorScreen { Name = "BotEditor", ModsRoot = _modsRoot };
                _bots.CloseRequested += () => _closed++;
                AddChild(_bots);
                return false;
            }
            _closed = 0;
            Check(_bots.SelectMod(Mod) && _bots.New("melee", "biter") && _bots.HasUnsavedChanges, "a new bot is unsaved");
            Press(_bots, "Back");
            Check(Asked("Unsaved changes"), "Back asks first");
            ConfirmDialog.Current!.Cancel();
            Check(_closed == 0 && _bots.Document?.Id == "biter", "Cancel stays");
            Check(_bots.Export() is { Succeeded: true } && !_bots.HasUnsavedChanges, "export saves it");
            _bots.ScriptText = "-- edited";
            Check(_bots.HasUnsavedChanges, "an unsaved script counts");
            _bots.AskNew("melee", "other");
            Check(Asked("Unsaved changes"), "New over it asks");
            ConfirmDialog.Current!.Cancel();
            _bots.ScriptText = "";
            _bots.AskNew("melee", "biter");
            Check(Asked("Bot exists") && ConfirmDialog.Current!.Message.Contains("bots/biter.json"), "a new bot over an existing file asks");
            ConfirmDialog.Current!.Cancel();
            Press(_bots, "Back");
            Check(ConfirmDialog.Current is null && _closed == 1, "with nothing unsaved, Back just goes");
            RemoveChild(_bots);
            _bots.QueueFree();
            return true;
        });
        Add("the Mod Manager asks before dropping an unapplied setup", frame =>
        {
            if (frame == 0)
            {
                _manager = new ModManagerScreen
                {
                    ModsRoot = ProjectSettings.GlobalizePath("res://tests/mods/mods"),
                    ProfilePath = ProjectSettings.GlobalizePath("user://tests/guard_profile.json"),
                };
                if (File.Exists(_manager.ProfilePath))
                    File.Delete(_manager.ProfilePath);
                AddChild(_manager);
                return false;
            }
            var id = _manager.Rows.First(r => r.Id != "vanilla" && r.Enabled).Id;
            _manager.SetModEnabled(id, false);
            Check(_manager.HasUnsavedChanges, "turning a mod off is unsaved");
            Press(_manager, "Revert");
            Check(Asked("Unsaved changes"), "Revert asks");
            ConfirmDialog.Current!.Cancel();
            Check(_manager.HasUnsavedChanges, "Cancel keeps the change");
            Press(_manager, "Rescan");
            Check(Asked("Unsaved changes"), "Rescan asks");
            ConfirmDialog.Current!.Confirm();
            Check(!_manager.HasUnsavedChanges, "Discard drops it");
            RemoveChild(_manager);
            _manager.QueueFree();
            return true;
        });
    }

    private bool Asked(string title) => ConfirmDialog.Current is { } d && d.Window.Title == title;

    private void Press(Node root, string text)
    {
        var button = Walk(root).OfType<Button>().FirstOrDefault(b => b.Text == text);
        Check(button is not null, $"there is a {text} button");
        button?.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren())
        foreach (var n in Walk(child))
            yield return n;
    }
}
