using Godot;
using Mechanica.Core.Net;
using Mechanica.Game.Editor;
using Mechanica.Game.Menus;

namespace Mechanica.Game.Tests;

/// <summary>
/// Every screen fits the smallest window the UI is laid out for (UI spec §11: 1280×720, scaled up from there). The main
/// menu and each screen it opens (every Options tab), the mod tools (every tab), and in a world the HUD, in-game menu,
/// Options, saves, inventory (every tab), the Fabricator screen, its Storage drawer and upgrade dialog, logbook, chat,
/// blueprint menu, colour picker, character choice, a part's settings, the Creation Inspector and a confirm dialog are opened in turn,
/// and nothing that shows may reach past the edge of the window, a screen meant to fill the window must fill it, and
/// wrapped text must not be squeezed into a sliver. With <c>-- --shots</c> and a folder, and a real display (for example
/// <c>xvfb-run</c> with <c>--rendering-driver opengl3</c>) each screen is also saved as a picture.
/// </summary>
public partial class FitTest : StagedTest
{
    private readonly List<string> _overflows = [];
    private MainMenu _menu = null!;
    private Main _main = null!;
    private (int Creation, int Part) _fabricator;
    private List<(string Name, Action Show)> _views = [];
    private int _shot;

    // With -- --shots <folder> and a real display, each screen measured is also saved as a picture to look at.
    private static string? Shots =>
        OS.GetCmdlineUserArgs() is var args && Array.IndexOf(args, "--shots") is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;

    /// <summary>The UI scale to check at (Options, Interface); the scene for 150% sets it.</summary>
    [Export]
    public float Scale { get; set; } = 1f;

    protected override string Tag => Scale == 1f ? "fit" : $"fit-{Scale * 100:0}";

    protected override int StageTimeoutFrames => 1200;

    public override void _Ready()
    {
        Add("the window is the design size", frame =>
        {
            if (frame == 0)
            {
                Ui.UiRoot.UiScale = Scale;
                return false;
            }
            var size = GetViewport().GetVisibleRect().Size;
            Check((size - new Vector2(1280, 720) / Scale).Length() < 2, $"1280×720 at {Scale * 100:0}% ({size})");
            _menu = GD.Load<PackedScene>(MainMenu.MenuScene).Instantiate<MainMenu>();
            AddChild(_menu);
            return true;
        });
        Screen("the main menu", () => { });
        Screen("New Game", () => _menu.OpenNewGame());
        Screen("Join Game", () => _menu.OpenJoin());
        Screen("Options", () => _menu.OpenOptions());
        Screen("Load Game", () => _menu.OpenLoad());
        Screen("Credits", () => _menu.OpenCredits());
        Screen("Help", () => _menu.Help.Open("playing.md"));
        Screen("the Bot Behavior Editor", () => _menu.OpenBotEditor());
        Screen("the Recipe Editor", () => _menu.OpenRecipeEditor());
        Screen("the Test Sandbox", () => _menu.OpenSandbox());
        Screen("the Mod Manager", () => _menu.OpenModManager());
        Add("the Part Editor", frame =>
        {
            if (frame == 0)
            {
                _menu.QueueFree();
                AddChild(new PartEditorMode { Name = "PartEditor" });
                return false;
            }
            var editor = GetNode<PartEditorMode>("PartEditor");
            if (frame == 30)
                Check(editor.Editor.OpenPart(System.IO.Path.Combine(editor.ModsRoot, "vanilla", "parts", "fabricator.json")) is null, "a vanilla part opens");
            if (frame < 60 || !Fits("the Part Editor", editor, frame - 60))
                return false;
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
        World("the character choice", () => { });
        World("the HUD", () => _main.Characters.Window?.Confirm());
        World("the in-game menu", () => Pause.Open());
        World("Options in a world", () => Pause.OpenOptions());
        World("saves in a world", () => Pause.OpenSaves());
        World("the inventory", () =>
        {
            Pause.Close();
            Inventory.Show(null);
        });
        Add("a Fabricator", frame =>
        {
            if (frame == 0)
            {
                Inventory.Hide();
                var at = _main.LocalPlayer!.GlobalPosition + new Vector3(1, 1, -3);
                _main.Actions.Request(Core.Actions.CreationActions.SpawnPart, new Core.Actions.SpawnPartArgs("vanilla:fabricator", [at.X, at.Y, at.Z]), r => Check(r.Ok, $"a Fabricator spawns ({r.Message})"));
                return false;
            }
            if (_main.Creations.All.FirstOrDefault(c => c.Parts.Any(p => p.Part == "vanilla:fabricator")) is not { } creation)
                return false;
            _fabricator = (creation.Id, creation.Parts.First(p => p.Part == "vanilla:fabricator").Id);
            return true;
        });
        World("the Fabricator screen", () => Inventory.Show(Core.Items.InventoryRef.OfPart(_fabricator.Creation, _fabricator.Part)));
        World("the Fabricator's Storage drawer", () => Inventory.ToggleStorage());
        World("the upgrade dialog", () =>
        {
            Inventory.ToggleStorage();
            Inventory.Hide();
            _main.Upgrades!.Open(_fabricator);
        });
        World("the logbook", () =>
        {
            _main.Upgrades!.Close();
            _main.GetNode<Quests.QuestHud>("QuestHud").Open();
        });
        World("chat", () =>
        {
            _main.GetNode<Quests.QuestHud>("QuestHud").Close();
            var chat = _main.GetNode<Chat.ChatPanel>("ChatPanel");
            chat.Open();
            chat.Send("hello");
        });
        World("the Lift's blueprint menu", () =>
        {
            _main.GetNode<Chat.ChatPanel>("ChatPanel").Close();
            _main.Blueprints!.Open();
        });
        World("the Paint Tool's colour picker", () =>
        {
            _main.Blueprints!.Close();
            _main.Tools!.Paint.Picker.Open(0);
        });
        World("a part's settings and the Creation Inspector", () =>
        {
            _main.Tools!.Paint.Picker.Close();
            _main.PartSettings!.Open(_fabricator.Creation, _fabricator.Part);
            _main.Inspector.Open(_fabricator.Creation);
        });
        Add("an engine and a controller", frame =>
        {
            if (frame == 0)
            {
                _main.PartSettings!.Close();
                _main.Inspector.Close();
                foreach (var (part, x) in new[] { ("vanilla:gas_engine", -2f), ("vanilla:controller", 3f) })
                {
                    var at = _main.LocalPlayer!.GlobalPosition + new Vector3(x, 1, -3);
                    _main.Actions.Request(Core.Actions.CreationActions.SpawnPart, new Core.Actions.SpawnPartArgs(part, [at.X, at.Y, at.Z]), r => Check(r.Ok, $"{part} spawns ({r.Message})"));
                }
                return false;
            }
            return Spawned("vanilla:gas_engine") is not null && Spawned("vanilla:controller") is not null;
        });
        World("an engine's menu", () => _main.PartSettings!.Open(Spawned("vanilla:gas_engine")!.Value.Creation, Spawned("vanilla:gas_engine")!.Value.Part));
        World("a controller's menu", () => _main.PartSettings!.Open(Spawned("vanilla:controller")!.Value.Creation, Spawned("vanilla:controller")!.Value.Part));
        World("a confirm dialog", () =>
        {
            _main.PartSettings!.Close();
            _main.Inspector.Close();
            Pause.OpenOptions();
            var options = Walk(Pause).OfType<OptionsScreen>().First();
            options.ShowTab(Array.IndexOf(OptionsScreen.Tabs, "Audio"));
            Walk(options).OfType<Button>().First(b => b.Text == "Reset to Defaults").EmitSignal(BaseButton.SignalName.Pressed);
        });
        Add("every screen fits", _ =>
        {
            foreach (var line in _overflows.Distinct())
                GD.Print($"[{Tag}] does not fit: {line}");
            Check(_overflows.Count == 0, $"nothing reaches past the window ({_overflows.Distinct().Count()} did)");
            Ui.UiRoot.UiScale = 1f;
            return true;
        });
    }

    // The first part of a type in the world.
    private (int Creation, int Part)? Spawned(string type) =>
        _main.Creations.All.FirstOrDefault(c => c.Parts.Any(p => p.Part == type)) is { } creation
            ? (creation.Id, creation.Parts.First(p => p.Part == type).Id)
            : null;

    private PauseMenu Pause => _main.GetNode<PauseMenu>("PauseMenu");

    private Items.InventoryScreen Inventory => _main.GetNode<Items.InventoryScreen>("InventoryScreen");

    private void Screen(string name, Action open) => Add(name, frame =>
    {
        if (frame == 0)
        {
            _menu.CloseAll();
            return false;
        }
        if (frame == 1)
            open();
        return frame >= 30 && Fits(name, _menu, frame - 30);
    });

    private void World(string name, Action open) => Add(name, frame =>
    {
        if (frame == 0)
            open();
        // Pictures render a few a second, so screenshots wait for them.
        var settle = Shots is null ? 30 : 240;
        return frame >= settle && Fits(name, _main, frame - settle);
    });

    // Measures what shows, then every Options tab and every tab of each tab container, a few frames apart so each is
    // laid out first. True when all have been measured.
    private bool Fits(string where, Node root, int frame)
    {
        const int Gap = 5;
        if (frame == 0)
        {
            _views = [("", () => { })];
            foreach (var options in Walk(root).OfType<OptionsScreen>().Where(o => o.IsVisibleInTree()))
            {
                for (var i = 0; i < OptionsScreen.Tabs.Length; i++)
                {
                    var tab = i;
                    _views.Add((OptionsScreen.Tabs[i], () => options.ShowTab(tab)));
                }
            }
            foreach (var tabs in Walk(root).OfType<TabContainer>().Where(t => t.IsVisibleInTree()))
            {
                for (var i = 0; i < tabs.GetTabCount(); i++)
                {
                    var tab = i;
                    _views.Add((tabs.GetTabTitle(i), () => tabs.CurrentTab = tab));
                }
            }
        }
        if (frame % Gap != 0)
            return false;
        var view = frame / Gap;
        if (view > 0)
        {
            var (name, _) = _views[view - 1];
            Measure(name.Length == 0 ? where : $"{where}, {name}", root);
        }
        if (view >= _views.Count)
            return true;
        _views[view].Show();
        return false;
    }

    // Compares each shown control with the window. What sits in a scroll area or a clipping panel can be scrolled to or
    // is cut off at its edge, so only that area itself has to fit.
    private void Measure(string where, Node root)
    {
        if (Shots is { } folder && DisplayServer.GetName() != "headless")
        {
            System.IO.Directory.CreateDirectory(folder);
            var file = string.Concat(where.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder, $"{Tag}_{_shot++:00}_{file}.png"));
        }
        var screen = GetViewport().GetVisibleRect();
        foreach (var control in Walk(root).OfType<Control>())
        {
            if (!control.IsVisibleInTree() || Hidden(control))
                continue;
            // A screen meant to fill the window (anchored on all four sides) that came out a fraction of it.
            if (control.AnchorLeft == 0 && control.AnchorTop == 0 && control.AnchorRight == 1 && control.AnchorBottom == 1 && control.GetParent() is not Container)
            {
                var room = control.GetParent() is Control parent ? parent.Size : screen.Size;
                if (control.Size.X < room.X / 2 || control.Size.Y < room.Y / 2)
                    _overflows.Add($"{where}: {root.GetPathTo(control)} should fill {room.X:0}×{room.Y:0} but is {control.Size.X:0}×{control.Size.Y:0}");
            }
            // Wrapped text squeezed into a sliver, a word or a few letters a line.
            if (control is Label { AutowrapMode: not TextServer.AutowrapMode.Off } label && label.Text.Length > 12 && label.Size.X is > 0 and < 80)
                _overflows.Add($"{where}: {root.GetPathTo(control)} wraps \"{label.Text}\" in {label.Size.X:0} px");
            if (Clipped(control) || control.Size.X < 1 || control.Size.Y < 1)
                continue;
            var rect = Rect(control);
            if (rect.Position.X < screen.Position.X - 1 || rect.Position.Y < screen.Position.Y - 1 || rect.End.X > screen.End.X + 1 || rect.End.Y > screen.End.Y + 1)
                _overflows.Add($"{where}: {root.GetPathTo(control)} {Show(rect)} outside {Show(screen)}");
        }
    }

    private static Rect2 Rect(Control control) => control.GetGlobalTransformWithCanvas() * new Rect2(Vector2.Zero, control.Size);

    // True under a scroll area or a panel set to clip what is drawn under it.
    private static bool Clipped(Control control)
    {
        for (var n = control.GetParent(); n is not null and not Window; n = n.GetParent())
        {
            if (n is ScrollContainer or Control { ClipContents: true })
                return true;
        }
        return false;
    }

    // Controls inside a closed popup or another window are not on the screen.
    private static bool Hidden(Control control)
    {
        for (var n = control.GetParent(); n is not null; n = n.GetParent())
        {
            if (n is Window window && window != control.GetTree().Root)
                return true;
        }
        return false;
    }

    private static string Show(Rect2 rect) => $"({rect.Position.X:0}, {rect.Position.Y:0}) {rect.Size.X:0}×{rect.Size.Y:0}";

    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren())
        foreach (var n in Walk(child))
            yield return n;
    }
}
