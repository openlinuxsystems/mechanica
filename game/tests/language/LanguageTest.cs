using Godot;
using Mechanica.Core.Net;
using Mechanica.Core.Saves;
using Mechanica.Core.Ui;
using Mechanica.Game.Editor;
using Mechanica.Game.Menus;
using Mechanica.Game.Player;
using Mechanica.Game.Settings;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// Languages (UI spec §16 Interface, Language). A language file dropped into the player's <c>lang</c> folder is
/// listed in Options, and picking it changes the UI at once. Then, in the pseudo-locale made from every
/// English key, every window is opened (main menu and each screen it opens, every Options tab, the mod tools,
/// and in a world the HUD, in-game menu, inventory, logbook, chat and compass) and every piece of text on it
/// must be pseudo text: anything still in English skipped the string tables. Run with <c>-- --collect</c> to
/// walk the same windows in English and write <c>data/lang/en.json</c> from what they show.
/// </summary>
public partial class LanguageTest : StagedTest
{
    private readonly List<string> _untranslated = [];
    private readonly SortedSet<string> _keys = new(StringComparer.Ordinal);
    private readonly bool _collect = OS.GetCmdlineUserArgs().Contains("--collect");
    private HashSet<string> _keyLabels = [];
    private MainMenu _menu = null!;
    private Main _main = null!;

    protected override string Tag => "language";

    protected override int StageTimeoutFrames => 1200;

    public override void _Ready()
    {
        _keyLabels = [.. Core.Input.InputActions.All.Select(a => GameInput.Label(a.Id))];
        if (_collect)
            Loc.Collect();
        Add("a language file dropped into the lang folder is listed", _ =>
        {
            DirAccess.MakeDirRecursiveAbsolute(Loc.UserFolder);
            Write("qps.json", PseudoLocale.For(Loc.English).ToJson());
            Write("broken.json", "{ \"code\": \"Not a code\", \"name\": \"Broken\" }");
            var languages = Loc.Refresh();
            Check(languages[0].Code == "en" && languages.Any(l => l is { Code: PseudoLocale.Code, Name: "Pseudo" }), $"English and Pseudo are listed ({string.Join(", ", languages.Select(l => l.Code))})");
            Check(languages.All(l => l.Name != "Broken"), "a file that does not read is left out");
            Check(Loc.English.Strings.Count > 100 || _collect, $"English has the game's strings ({Loc.English.Strings.Count})");
            return true;
        });
        Add("Options lists it and picking it changes the text at once", frame =>
        {
            if (frame == 0)
            {
                _menu = GD.Load<PackedScene>(MainMenu.MenuScene).Instantiate<MainMenu>();
                AddChild(_menu);
                return false;
            }
            if (frame == 1)
            {
                var options = _menu.OpenOptions();
                options.ShowTab(Array.IndexOf(OptionsScreen.Tabs, "Interface"));
                return false;
            }
            var options2 = Find<OptionsScreen>(_menu);
            var picker = Walk(options2).OfType<OptionButton>().First(o => o.Name == "Language");
            var items = Enumerable.Range(0, picker.ItemCount).Select(i => picker.GetItemText(i)).ToList();
            Check(items.Contains("Pseudo") && items[0] == "English", $"the Language list has English and Pseudo ({string.Join(", ", items)})");
            Check(!picker.CanAutoTranslate(), "language names show as written");
            if (_collect)
            {
                _menu.CloseAll();
                return true;
            }
            picker.Select(items.IndexOf("Pseudo"));
            picker.EmitSignal(OptionButton.SignalName.ItemSelected, items.IndexOf("Pseudo"));
            Check(GameSettings.Current.Interface.Language == PseudoLocale.Code && TranslationServer.GetLocale() == PseudoLocale.Code, $"picking it uses it ({GameSettings.Current.Interface.Language}, {TranslationServer.GetLocale()})");
            var back = Walk(_menu).OfType<Label>().First(l => l.Text == "Language");
            Check(PseudoLocale.IsPseudo(back.Atr(back.Text)), $"the screen's own text changes ({back.Atr(back.Text)})");
            _menu.CloseAll();
            return true;
        });
        Screen("the main menu", () => null);
        Screen("New game", () => _menu.OpenNewGame());
        Screen("Join game", () => _menu.OpenJoin());
        Screen("Options", () => _menu.OpenOptions(), tabs: true);
        Screen("Load game", () => _menu.OpenLoad());
        Screen("Help", () =>
        {
            _menu.Help.Open("playing.md");
            return _menu.Help;
        });
        Screen("the Bot Behavior Editor", () => _menu.OpenBotEditor(), tabs: true);
        Screen("the Test Sandbox", () => _menu.OpenSandbox(), tabs: true);
        Screen("the Mod Manager", () => _menu.OpenModManager());
        Add("the Part Editor", frame =>
        {
            if (frame == 0)
            {
                _menu.QueueFree();
                var mode = new PartEditorMode { Name = "PartEditor" };
                AddChild(mode);
                return false;
            }
            if (frame < 30)
                return false;
            var editor = GetNode<PartEditorMode>("PartEditor");
            if (frame == 30)
            {
                var error = editor.Editor.OpenPart(System.IO.Path.Combine(editor.ModsRoot, "vanilla", "parts", "wheel.json"));
                Check(error is null, $"a vanilla part opens ({error})");
                return false;
            }
            if (frame < 60)
                return false;
            Audit("the Part Editor", editor, tabs: true);
            foreach (var dialog in Walk(editor).OfType<AcceptDialog>().ToList())
            {
                dialog.PopupCentered();
                AuditTree($"the Part Editor's {dialog.Title} dialog", dialog);
                dialog.Hide();
            }
            editor.QueueFree();
            return true;
        });
        Add("a world starts", frame =>
        {
            if (frame == 0)
            {
                _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
                _main.Options = LaunchOptions.Offline with { Seed = 7 };
                AddChild(_main);
                return false;
            }
            return _main.LocalPlayer is not null && frame > 60;
        });
        World("the HUD", () => { });
        World("the in-game menu", () => _main.GetNode<PauseMenu>("PauseMenu").Open());
        World("Options in a world", () => _main.GetNode<PauseMenu>("PauseMenu").OpenOptions(), tabs: true);
        World("saves in a world", () => _main.GetNode<PauseMenu>("PauseMenu").OpenSaves());
        World("the inventory", () =>
        {
            _main.GetNode<PauseMenu>("PauseMenu").Close();
            _main.GetNode<Items.InventoryScreen>("InventoryScreen").Show(null);
        }, tabs: true);
        World("the logbook", () =>
        {
            _main.GetNode<Items.InventoryScreen>("InventoryScreen").Hide();
            _main.GetNode<Quests.QuestHud>("QuestHud").Open();
        });
        World("chat", () =>
        {
            _main.GetNode<Quests.QuestHud>("QuestHud").Close();
            var chat = _main.GetNode<Chat.ChatPanel>("ChatPanel");
            chat.Open();
            chat.Send("hello");
        });
        Add("a part's settings and the Creation Inspector", frame =>
        {
            if (frame == 0)
            {
                _main.GetNode<Chat.ChatPanel>("ChatPanel").Close();
                var at = _main.LocalPlayer!.GlobalPosition + new Vector3(0, 1, -3);
                _main.Actions.Request(Core.Actions.CreationActions.SpawnPart, new Core.Actions.SpawnPartArgs("vanilla:timer", [at.X, at.Y, at.Z]), r => Check(r.Ok, $"a timer spawns ({r.Message})"));
                return false;
            }
            if (_main.Creations.All.MaxBy(c => c.Id) is not { } creation)
                return false;
            _main.PartSettings!.Open(creation.Id, creation.Parts.First().Id);
            _main.Inspector.Open(creation.Id);
            return true;
        });
        World("the open part settings and inspector", () => { });
        World("a confirm dialog", () =>
        {
            _main.PartSettings!.Close();
            _main.Inspector.Close();
            var pause = _main.GetNode<PauseMenu>("PauseMenu");
            pause.OpenOptions();
            var options = Find<OptionsScreen>(pause);
            options.ShowTab(Array.IndexOf(OptionsScreen.Tabs, "Audio"));
            Walk(options).OfType<Button>().First(b => b.Text == "Reset to Defaults").EmitSignal(BaseButton.SignalName.Pressed);
        });
        Add("every window's text went through the string tables", _ =>
        {
            if (_collect)
            {
                _keys.UnionWith(Loc.Collected);
                // Keys the code looks up by value rather than as a literal.
                _keys.UnionWith(Compass.Points);
                _keys.UnionWith(Enum.GetNames<Net.NetSession.SessionState>());
                _keys.UnionWith(Enum.GetNames<Core.Survival.GameMode>());
                _keys.UnionWith(Walk(_main).OfType<Building.HandTool>().Select(t => t.ToolName));
                _keys.UnionWith(TutorialHints.Load().Hints.Select(h => h.Text));
                var now = DateTimeOffset.Now;
                foreach (var seconds in new[] { 0, 300, 4000 })
                    SaveSummary.FormatPlayTime(seconds, (key, _) => { _keys.Add(key); return key; });
                foreach (var minutes in new[] { 0, 1, 5, 61, 180, 1500, 4000 })
                    HomeScreen.Ago(now.AddMinutes(-minutes), now, (key, _) => { _keys.Add(key); return key; });
                foreach (var file in Directory.EnumerateFiles(ProjectSettings.GlobalizePath("res://scripts"), "*.cs", SearchOption.AllDirectories))
                    _keys.UnionWith(LocKeys.In(File.ReadAllText(file)));
                var keys = _keys.Where(k => k.Any(char.IsLetter)).ToDictionary(k => k, k => k, StringComparer.Ordinal);
                var path = ProjectSettings.GlobalizePath($"{Loc.BuiltIn}/en.json");
                File.WriteAllText(path, new Language(Core.Ui.Language.English, "English", keys).ToJson());
                GD.Print($"[language] wrote {keys.Count} keys to {path}");
                DirAccess.RemoveAbsolute($"{Loc.UserFolder}/qps.json");
                DirAccess.RemoveAbsolute($"{Loc.UserFolder}/broken.json");
                return true;
            }
            GameSettings.Apply(new());
            DirAccess.RemoveAbsolute($"{Loc.UserFolder}/qps.json");
            DirAccess.RemoveAbsolute($"{Loc.UserFolder}/broken.json");
            Loc.Refresh();
            foreach (var line in _untranslated.Distinct())
                GD.Print($"[language] untranslated: {line}");
            Check(_untranslated.Count == 0, $"no text skipped the tables ({_untranslated.Distinct().Count()} did)");
            return true;
        });
    }

    private void Screen(string name, Func<Node?> open, bool tabs = false) => Add(name, frame =>
    {
        if (frame == 0)
        {
            _menu.CloseAll();
            return false;
        }
        if (frame == 1)
        {
            open();
            return false;
        }
        if (frame < 30)
            return false;
        Audit(name, _menu, tabs);
        return true;
    });

    private void World(string name, Action open, bool tabs = false) => Add(name, frame =>
    {
        if (frame == 0)
        {
            open();
            return false;
        }
        if (frame < 30)
            return false;
        Audit(name, _main, tabs);
        return true;
    });

    private void Write(string file, string text)
    {
        using var f = Godot.FileAccess.Open($"{Loc.UserFolder}/{file}", Godot.FileAccess.ModeFlags.Write);
        f.StoreString(text);
    }

    // Every text under root that shows (or would, on another tab) must be pseudo text.
    private void Audit(string where, Node root, bool tabs)
    {
        foreach (var options in Walk(root).OfType<OptionsScreen>().ToList())
        {
            for (var i = 0; i < OptionsScreen.Tabs.Length; i++)
            {
                options.ShowTab(i);
                AuditTree($"{where}, {OptionsScreen.Tabs[i]}", root);
            }
        }
        foreach (var tab in Walk(root).OfType<TabContainer>().ToList())
        {
            for (var i = 0; tabs && i < tab.GetTabCount(); i++)
            {
                tab.CurrentTab = i;
                AuditTree(where, root);
            }
        }
        AuditTree(where, root);
    }

    private void AuditTree(string where, Node root)
    {
        foreach (var node in Walk(root))
        {
            // Godot's own file browser keeps its own (English) text.
            if (node is CanvasItem { } item && !item.IsVisibleInTree() || node is Node3D { } spatial && !spatial.IsVisibleInTree() || InHiddenWindow(node)
                || node.GetParent() is { } parent && Ancestors(parent).Any(a => a is FileDialog))
                continue;
            foreach (var text in Texts(node))
            {
                if (!text.Any(char.IsLetter) || !node.CanAutoTranslate() || _keyLabels.Contains(text) || NotText().IsMatch(text))
                    continue;
                if (_collect)
                {
                    if (Loc.Built(text))
                        continue;
                    _keys.Add(text);
                    continue;
                }
                var shown = node.Atr(text);
                if (!PseudoLocale.IsPseudo(shown))
                    _untranslated.Add($"{where}: {Path(root, node)} \"{shown}\"");
            }
        }
    }

    private static IEnumerable<Node> Ancestors(Node node)
    {
        for (var n = node; n is not null; n = n.GetParent())
            yield return n;
    }

    private static bool InHiddenWindow(Node node)
    {
        for (var n = node; n is not null; n = n.GetParent())
        {
            if (n is Window { Visible: false })
                return true;
        }
        return false;
    }

    private static IEnumerable<string> Texts(Node node)
    {
        if (node is Control { TooltipText.Length: > 0 } control)
            yield return control.TooltipText;
        switch (node)
        {
            case OptionButton option:
                for (var i = 0; i < option.ItemCount; i++)
                    yield return option.GetItemText(i);
                break;
            case Button button:
                yield return button.Text;
                break;
            case Label label:
                yield return label.Text;
                break;
            case RichTextLabel rich:
                yield return rich.Text;
                break;
            case LineEdit line:
                yield return line.PlaceholderText;
                break;
            case TextEdit edit:
                yield return edit.PlaceholderText;
                break;
            case ItemList list:
                for (var i = 0; i < list.ItemCount; i++)
                    yield return list.GetItemText(i);
                break;
            case TabContainer tabs:
                for (var i = 0; i < tabs.GetTabCount(); i++)
                    yield return tabs.GetTabTitle(i);
                break;
            case TabBar bar:
                for (var i = 0; i < bar.TabCount; i++)
                    yield return bar.GetTabTitle(i);
                break;
            case Label3D label3D:
                yield return label3D.Text;
                break;
            case Window window:
                yield return window.Title;
                break;
        }
    }

    // Paths, resolutions and colour codes (Godot's own file and colour pickers show them).
    [System.Text.RegularExpressions.GeneratedRegex(@"^(/|[A-Za-z]:\\|#[0-9a-fA-F]{6,8}$|\d+x\d+$)")]
    private static partial System.Text.RegularExpressions.Regex NotText();

    private static string Path(Node root, Node node) => root.IsAncestorOf(node) ? root.GetPathTo(node).ToString() : node.Name.ToString();

    private static T Find<T>(Node root) where T : Node => Walk(root).OfType<T>().First();

    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren(includeInternal: false))
        foreach (var n in Walk(child))
            yield return n;
    }
}
