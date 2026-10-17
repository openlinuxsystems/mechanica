using Godot;
using Mechanica.Core.Input;
using Mechanica.Core.Net;
using Mechanica.Game.Menus;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// Windows keep their own mouse input in a running world (UI spec §13). Wheel turns and clicks anywhere over
/// the in-game menu, Options (every tab, scrolled past both ends), the save screen, AI Reports, the inventory, the Fabricator screen, the upgrade dialog, the Logbook, the Lift's
/// blueprint menu, the Paint Tool's colour picker, a part's menu and a confirm dialog never reach the game behind them (nor over the hotbar while the inventory is open), nor do clicks on the screen around a window opened from
/// the menu, where a wheel changes the hotbar and a click takes the
/// mouse back and closes the menu. The windows are still open afterwards.
/// </summary>
public partial class WindowsTest : StagedTest
{
    private readonly List<InputEvent> _leaked = [];
    private Main _main = null!;
    private PauseMenu _menu = null!;
    private (int Creation, int Part) _station;
    private ulong _chatFrame;

    protected override string Tag => "windows";

    public override void _Ready()
    {
        var recorder = new Recorder { Name = "Recorder", Leaked = _leaked };
        AddChild(recorder);
        Add("a world starts with the player's own input", frame =>
        {
            if (frame == 0)
            {
                _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
                _main.Options = LaunchOptions.Offline with { Seed = 7 };
                AddChild(_main);
                return false;
            }
            if (_main.LocalPlayer is null || _main.GetNodeOrNull("LocalPlayerInput") is null)
                return false;
            _menu = _main.GetNode<PauseMenu>("PauseMenu");
            // Last in the tree, so it hears unhandled input before the game's own handlers can use it up.
            MoveChild(recorder, -1);
            return true;
        });
        Add("the in-game menu keeps the wheel and clicks", frame =>
        {
            if (frame == 0)
            {
                _menu.Open();
                return false;
            }
            Scrub(_menu.GetNode<Control>("Menu").GetChild<Control>(0));
            Check(_menu.IsOpen, "the menu is still open");
            return Clean("the menu");
        });
        Add("Options keeps the wheel on every tab, past both ends", frame =>
        {
            if (frame == 0)
            {
                _menu.OpenOptions();
                return false;
            }
            var options = Find<OptionsScreen>(_menu);
            options.ShowTab(Array.IndexOf(OptionsScreen.Tabs, "Gameplay"));
            var difficulty = Walk(options).OfType<Label>().FirstOrDefault(l => l.Name == "WorldDifficulty");
            Check(difficulty?.Text == "Normal", $"Gameplay shows the world's difficulty ({difficulty?.Text})");
            for (var tab = 0; tab < OptionsScreen.Tabs.Length; tab++)
            {
                options.ShowTab(tab);
                Scrub(options, wheels: 80);
            }
            Scrub(_menu.GetNode<Control>("Menu"), wheels: 4);
            Check(_menu.IsOpen && IsInstanceValid(options) && !options.IsQueuedForDeletion(), "Options is still open");
            return Clean("Options");
        });
        Add("a confirm dialog keeps them too", frame =>
        {
            var options = Find<OptionsScreen>(_menu);
            if (frame == 0)
            {
                ConfirmDialog.Ask(options, "Reset", "Reset?", "Reset", () => { });
                return false;
            }
            Scrub(ConfirmDialog.Current!.Window);
            Scrub(_menu.GetNode<Control>("Menu"), wheels: 4);
            ConfirmDialog.Current!.Cancel();
            options.RequestClose();
            return Clean("the confirm dialog and the dimmed screen behind it");
        });
        Add("the save screen keeps them", frame =>
        {
            if (frame == 0)
            {
                _menu.OpenSaves();
                return false;
            }
            Scrub(Find<Saves.SaveScreen>(_menu));
            Check(_menu.IsOpen, "the menu is still open");
            _menu.Close();
            return Clean("the save screen");
        });
        Add("AI Reports keeps them", frame =>
        {
            if (frame == 0)
            {
                _menu.Open();
                _main.Reports.Reporter.Note("Test agent", new Core.Reports.NoteParams("Something to read"));
                _menu.OpenReports();
                return false;
            }
            var reports = Find<Remote.AiReportsWindow>(_menu);
            Check(reports.Listed.Count >= 1, "the window lists the note");
            Scrub(reports);
            Check(_menu.IsOpen && IsInstanceValid(reports), "the menu and AI Reports are still open");
            _menu.Close();
            return Clean("AI Reports");
        });
        Add("chat sends a line and keeps the mouse", frame =>
        {
            var chat = _main.GetNode<Chat.ChatPanel>("ChatPanel");
            // Headless, the mouse can't be captured, so the Chat key's own path (only while playing) is not
            // reachable here; Open is what it calls.
            if (frame == 0)
            {
                chat.Open();
                return false;
            }
            if (frame == 1)
            {
                Check(chat.IsOpen && chat.Input.HasFocus() && GameInput.Label(InputActions.Chat) == "Enter", "the box opens with the focus; Chat is on Enter");
                chat.Send("hello [world]");
                return false;
            }
            if (!chat.Shown.Any() && frame < 120)
                return false;
            if (!Check(chat.Shown.Any(), "the line shows"))
                return true;
            Check(chat.Shown.Last() == $"{_main.Session.Profile.Name}: hello [world]", $"the line shows with the speaker's name ({chat.Shown.Last()})");
            Check(!chat.IsOpen, "sending closes the box");
            chat.Open();
            Scrub(chat.GetNode<Control>("Chat"));
            Check(chat.IsOpen, "chat is still open");
            chat.Close();
            return Clean("the chat panel");
        });
        Add("the chat background follows Chat opacity", frame =>
        {
            var chat = _main.GetNode<Chat.ChatPanel>("ChatPanel");
            if (frame == 0)
            {
                Settings.GameSettings.Apply(Settings.GameSettings.Current with { Interface = Settings.GameSettings.Current.Interface with { ChatOpacity = 0.25f } });
                chat.Open();
                chat.Send("again");
                _chatFrame = Engine.GetProcessFrames();
                return false;
            }
            // The panel draws its background in _Process, which can lag several physics ticks behind on a busy machine.
            if ((Engine.GetProcessFrames() < _chatFrame + 2 || chat.Shown.LastOrDefault()?.EndsWith("again") != true) && frame < 120)
                return false;
            Check(Mathf.IsEqualApprox(chat.BackgroundAlpha, 0.25f), $"a recent line shows on a 25% background ({chat.BackgroundAlpha})");
            Settings.GameSettings.Apply(new());
            return true;
        });
        Add("the inventory keeps them", frame =>
        {
            var inventory = _main.GetNode<Items.InventoryScreen>("InventoryScreen");
            if (frame == 0)
            {
                inventory.Show(null);
                return false;
            }
            Scrub(inventory.GetNode<Control>("Panel"));
            // The hotbar takes drops while the inventory is open, so it keeps the mouse too.
            Scrub(_main.Tools!.Bar!, wheels: 4);
            Check(inventory.IsOpen, "the inventory is still open");
            inventory.Hide();
            return Clean("the inventory");
        });
        Add("the Fabricator screen keeps them", frame =>
        {
            var inventory = _main.GetNode<Items.InventoryScreen>("InventoryScreen");
            if (frame == 0)
            {
                _station = BuildFabricator();
                inventory.Show(Core.Items.InventoryRef.OfPart(_station.Creation, _station.Part));
                return false;
            }
            if (!inventory.StationMode)
                return false;
            Scrub(inventory.GetNode<Control>("Panel"));
            Check(inventory.IsOpen && inventory.StationMode, "the Fabricator screen is still open");
            inventory.Hide();
            return Clean("the Fabricator screen");
        });
        Add("the upgrade dialog keeps them", frame =>
        {
            var dialog = _main.Upgrades!;
            if (frame == 0)
            {
                dialog.Open(_station);
                return false;
            }
            Scrub(dialog.Window);
            Check(dialog.IsOpen, "the upgrade dialog is still open");
            dialog.Close();
            return Clean("the upgrade dialog");
        });
        Add("the Logbook keeps them", frame =>
        {
            var log = _main.GetNode<Quests.QuestHud>("QuestHud");
            if (frame == 0)
            {
                log.Open();
                return false;
            }
            // The map takes the wheel (zoom) and right clicks (markers) itself; none reach the game.
            Scrub(log.Logbook);
            Check(log.IsOpen, "the Logbook is still open");
            log.Close();
            return Clean("the Logbook");
        });
        Add("the Lift's blueprint menu keeps them", frame =>
        {
            var menu = _main.Blueprints!;
            if (frame == 0)
            {
                menu.Open();
                return false;
            }
            Scrub(menu.Window);
            Check(menu.IsOpen, "the blueprint menu is still open");
            menu.Close();
            return Clean("the blueprint menu");
        });
        Add("the Paint Tool's colour picker keeps them", frame =>
        {
            var picker = _main.Tools!.Paint.Picker;
            if (frame == 0)
            {
                picker.Open(0);
                return false;
            }
            Scrub(picker.Ring, wheels: 4);
            Check(picker.IsOpen, "the picker is still open");
            picker.Close();
            return Clean("the colour picker");
        });
        Add("a part's menu keeps them", frame =>
        {
            var panel = _main.PartSettings!;
            if (frame == 0)
            {
                panel.Open(_station.Creation, BuildEngine(_station.Creation));
                return false;
            }
            Scrub(panel.Window);
            Check(panel.IsOpen, "the engine's menu is still open");
            panel.Close();
            return Clean("a part's menu");
        });
        Add("the character choice keeps them", _ =>
        {
            var window = _main.Characters.Window;
            if (!Check(window is not null, "a new world asks the player to choose a character"))
                return true;
            var panel = Walk(window!).OfType<PanelContainer>().First();
            Check(panel.GetGlobalRect().Size.X > 300 && window!.GetGlobalRect().Size == GetViewport().GetVisibleRect().Size, "the choice covers the screen");
            Scrub(panel);
            Scrub(window!.GetChild<CanvasLayer>(0).GetChild<Control>(0), wheels: 4);
            Check(_main.Characters.Window is not null, "the choice is still open");
            return Clean("the character choice");
        });
    }

    // A Fabricator on a small wooden floor in front of the player.
    private (int Creation, int Part) BuildFabricator()
    {
        var build = _main.Build;
        var creation = new Core.Creations.Creation(build.NextCreationId(), build.Catalog);
        for (var x = 0; x < 8; x++)
        for (var z = 0; z < 8; z++)
            creation.TryPlacePart("vanilla:wood_block", new Core.Creations.GridCell(x, 0, z), default, out _, out _);
        Core.Creations.PartInstance? fabricator = null;
        for (var z = 0; z < 4 && fabricator is null; z++)
        {
            if (creation.TryPlacePart("vanilla:fabricator", new Core.Creations.GridCell(2, 1, z), default, out var placed, out _))
                fabricator = placed;
        }
        Check(fabricator is not null, "the Fabricator fits on the floor");
        var feet = _main.LocalPlayer!.GlobalPosition;
        creation.SetAllBodyPoses(new Core.Creations.Pose((feet + new Vector3(-1, 0, -3)).ToNumerics(), System.Numerics.Quaternion.Identity));
        build.Added(creation);
        return (creation.Id, fabricator?.Id ?? 0);
    }

    // A gas engine on the Fabricator's floor, for its menu.
    private int BuildEngine(int creationId)
    {
        var creation = _main.Creations.Find(creationId)!;
        Core.Creations.PartInstance? engine = null;
        for (var z = 0; z < 8 && engine is null; z++)
        {
            if (creation.TryPlacePart("vanilla:gas_engine", new Core.Creations.GridCell(6, 1, z), default, out var placed, out _))
                engine = placed;
        }
        Check(engine is not null, "the engine fits on the floor");
        _main.Build.Changed(creation);
        return engine?.Id ?? 0;
    }

    private bool Clean(string what)
    {
        Check(_leaked.Count == 0, $"no click or wheel over {what} reaches the game ({_leaked.Count} did{(_leaked.Count > 0 ? $", first {_leaked[0].AsText()} at {((InputEventMouse)_leaked[0]).Position}" : "")})");
        _leaked.Clear();
        return true;
    }

    // Wheel both ways, well past either end, then a click on every point of a grid over the control's
    // padding and labels (buttons are left alone: pressing them is not what this checks).
    private void Scrub(Control control, int wheels = 30)
    {
        foreach (var at in Points(control.GetGlobalRect()))
        {
            for (var i = 0; i < wheels; i++)
                Press(at, i < wheels / 2 ? MouseButton.WheelDown : MouseButton.WheelUp);
            Move(at);
            if (GetViewport().GuiGetHoveredControl() is not BaseButton and not Godot.Range and not LineEdit)
                Press(at, MouseButton.Right);
        }
    }

    private static IEnumerable<Vector2> Points(Rect2 rect)
    {
        for (var x = 0.04f; x < 1; x += 0.12f)
        for (var y = 0.04f; y < 1; y += 0.12f)
            yield return rect.Position + rect.Size * new Vector2(x, y);
    }

    private void Move(Vector2 at) => GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });

    private void Press(Vector2 at, MouseButton button)
    {
        Move(at);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at, Factor = 1 });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at, Factor = 1 });
    }

    private static T Find<T>(Node root) where T : Node => Walk(root).OfType<T>().First();

    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren())
        foreach (var n in Walk(child))
            yield return n;
    }

    // Stands in for the game behind the windows: every press that reaches unhandled input.
    private sealed partial class Recorder : Node
    {
        public List<InputEvent> Leaked { get; init; } = null!;

        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton { Pressed: true })
                Leaked.Add(@event);
        }
    }
}
