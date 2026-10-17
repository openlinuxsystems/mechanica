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

        Add("every tool fits the screen at 75% and 150% UI scale", frame =>
        {
            float[] scales = [0.75f, 1.5f];
            if (frame == 0)
            {
                AddChild(new PartEditorScreen { Name = "FitParts", ModsRoot = _modsRoot });
                AddChild(new BotEditorScreen { Name = "FitBots", ModsRoot = _modsRoot });
                AddChild(new ModManagerScreen { Name = "FitMods", ModsRoot = ProjectSettings.GlobalizePath("res://tests/mods/mods"), ProfilePath = ProjectSettings.GlobalizePath("user://tests/guard_fit.json") });
            }
            if (frame % 4 == 0 && frame / 4 < scales.Length)
                UiRoot.UiScale = scales[frame / 4];
            if (frame % 4 != 3)
                return false;
            var screen = GetViewport().GetVisibleRect();
            foreach (var name in new[] { "FitParts", "FitBots", "FitMods" })
            {
                var window = Walk(GetNode(name)).OfType<ToolWindow>().First();
                var rect = window.GetGlobalRect();
                Check(screen.Grow(1).Encloses(rect), $"{name} fits at {UiRoot.UiScale:P0} ({rect} in {screen})");
            }
            if (frame / 4 < scales.Length - 1)
                return false;
            UiRoot.UiScale = 1f;
            foreach (var name in new[] { "FitParts", "FitBots", "FitMods" })
            {
                var node = GetNode(name);
                RemoveChild(node);
                node.QueueFree();
            }
            return true;
        });
        Add("the Part Editor asks before closing over an unsaved part", frame =>
        {
            if (frame == 0)
            {
                _parts = new PartEditorScreen { Name = "PartEditor", ModsRoot = _modsRoot };
                _parts.CloseRequested += () => _closed++;
                AddChild(_parts);
                return false;
            }
            CheckTooltips(_parts, "the Part Editor");
            var export = _parts.Window.Actions.GetChildren().OfType<Button>().LastOrDefault();
            Check(_parts.Window.Title == "Part Editor" && export is { Text: "Export", Disabled: true } && export.ThemeTypeVariation == "PrimaryButton",
                "the editor has a title bar, and Export is its primary action, off with no part open");
            Check(_parts.NewPart(Mod, "crate", "Crate", "block") is null && _parts.HasUnsavedChanges, "a new part is unsaved");
            Check(export is { Disabled: false }, "Export turns on with a part open");
            _parts.Window.CloseButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(Asked("Unsaved changes") && _closed == 0, "the X asks first");
            ConfirmDialog.Current!.Cancel();
            Esc();
            Check(Asked("Unsaved changes") && _closed == 0, "so does Esc");
            Esc();
            Check(ConfirmDialog.Current is null && _closed == 0, "and Esc again cancels the question, not the editor");
            Check(_closed == 0 && _parts.Document?.Definition.Id == "crate", "Cancel keeps the part open");
            Press(_parts, "New…");
            Check(Asked("Unsaved changes"), "New over an unsaved part asks too");
            ConfirmDialog.Current!.Cancel();
            Press(_parts, "Open…");
            Check(Asked("Unsaved changes"), "and so does Open");
            ConfirmDialog.Current!.Cancel();
            _parts.Window.CloseButton.EmitSignal(BaseButton.SignalName.Pressed);
            ConfirmDialog.Current!.Confirm();
            Check(_closed == 1, "Discard closes");
            _ = _parts.ExportAsync();
            return true;
        });
        Add("a saved part closes without asking", _ =>
        {
            if (_parts.HasUnsavedChanges)
                return false;
            _parts.Window.CloseButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(ConfirmDialog.Current is null && _closed == 2, "nothing to lose, no question");
            return true;
        });
        Add("Esc and the Handbook key close the help", frame =>
        {
            var help = _parts.GetChildren().OfType<HelpWindow>().Single();
            if (frame == 0)
            {
                _parts.OpenHelp("getting-started.md");
                return false;
            }
            if (frame == 2)
            {
                Check(help.Visible, "the help is open");
                help.Scroll = 300;
                Esc();
                return false;
            }
            if (frame == 4)
            {
                Check(!help.Visible && _closed == 2, "Esc closes the help and leaves the editor open");
                _parts.OpenHelp("getting-started.md");
                return false;
            }
            if (frame < 7)
                return false;
            Check(help.Scroll > 0, $"the page opens where it was left ({help.Scroll})");
            help.Hide();
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
            _parts.Tabs.CurrentTab = 1;
            RemoveChild(_parts);
            _parts.QueueFree();
            return true;
        });
        Add("the Part Editor opens where it was left", frame =>
        {
            if (frame == 0)
            {
                _parts = new PartEditorScreen { Name = "PartEditor2", ModsRoot = _modsRoot };
                AddChild(_parts);
                return false;
            }
            Check(_parts.Document?.Definition.Id == "crate" && _parts.Tabs.CurrentTab == 1, $"the part and tab are back ({_parts.Document?.Definition.Id}, tab {_parts.Tabs.CurrentTab})");
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
            CheckTooltips(_bots, "the Bot Behavior Editor");
            Check(_bots.SelectMod(Mod) && _bots.New("melee", "biter") && _bots.HasUnsavedChanges, "a new bot is unsaved");
            _bots.Window.CloseButton.EmitSignal(BaseButton.SignalName.Pressed);
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
            _bots.Window.CloseButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(ConfirmDialog.Current is null && _closed == 1, "with nothing unsaved, Back just goes");
            _bots.Tabs.CurrentTab = 2;
            RemoveChild(_bots);
            _bots.QueueFree();
            return true;
        });
        Add("the Bot Behavior Editor opens where it was left", frame =>
        {
            if (frame == 0)
            {
                _bots = new BotEditorScreen { Name = "BotEditor2", ModsRoot = _modsRoot };
                AddChild(_bots);
                return false;
            }
            Check(_bots.Document?.Id == "biter" && _bots.Tabs.CurrentTab == 2, $"the bot and tab are back ({_bots.Document?.Id}, tab {_bots.Tabs.CurrentTab})");
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
            CheckTooltips(_manager, "the Mod Manager");
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

    // Every button says what it does (UI spec §13: tooltips with a name and a short description), and is a
    // big enough target: 32 px at 1080p is 21 px on the 1280×720 layout.
    private void CheckTooltips(Node root, string tool)
    {
        var buttons = Walk(root).OfType<BaseButton>().Where(b => b is not OptionButton && b.GetParent() is not ColorPicker).ToList();
        var bare = buttons.Where(b => string.IsNullOrWhiteSpace(b.TooltipText)).Select(b => b is Button { Text: var t } && t.Length > 0 ? t : b.Name.ToString()).ToList();
        Check(bare.Count == 0, $"every button in {tool} has a tooltip ({string.Join(", ", bare)})");
        var small = buttons.Where(b => b.IsVisibleInTree()).Where(b => b.Size.X < 21 || b.Size.Y < 21).Select(b => $"{(b as Button)?.Text ?? b.Name} {b.Size}").ToList();
        Check(small.Count == 0, $"every button in {tool} is at least 32 px at 1080p ({string.Join(", ", small)})");
    }

    private void Esc() => GetViewport().PushInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Escape, Keycode = Key.Escape });

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
