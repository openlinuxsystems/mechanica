using Godot;
using Mechanica.Core.Net;
using Mechanica.Core.Survival;
using Mechanica.Game.Menus;
using Mechanica.Game.Net;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Menus, LAN discovery and help with a host plus a client (chunk 24, D10). The host's world is listed on
/// the LAN; the Join game screen finds it, and joining the listed game connects a client. The in-game menu
/// says the host is hosting and lets it save, but not the client. The main menu's New game screen turns its
/// boxes into launch options, Settings renames the player's profile, and help opens on the page for what
/// the player is doing and searches every page.
/// </summary>
public partial class MenuTest : StagedTest
{
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance? _client;
    private JoinScreen _join = null!;
    private NetEndpoint? _picked;
    private PauseMenu? _hostMenu;
    private PauseMenu? _clientMenu;

    protected override string Tag => "menus";

    public override void _Ready()
    {
        var lanPort = (int)GD.RandRange(40000, 60000);
        _harness = new NetTestHarness { Name = "Harness", LanPort = lanPort };
        AddChild(_harness);
        _host = _harness.StartHost();
        _host.Main.Saves.WorldName = "Menu World";

        Add("the host is listed on the LAN", _ => _host.Main.Lan.Listening);
        Add("the Join game screen finds the host's world", frame =>
        {
            if (frame == 0)
            {
                _join = new JoinScreen { Browser = new LanBrowser { Port = lanPort } };
                _join.JoinRequested += endpoint => _picked = endpoint;
                AddChild(_join);
            }
            if (_join.Listed.FirstOrDefault(e => e.Session.World == "Menu World") is not { } entry)
                return false;
            Check(entry.Session is { Host: "Host", Players: 1, MaxPlayers: NetProtocol.DefaultMaxPlayers, Mode: "creative", Compatible: true },
                $"it shows the host, players and mode ({entry.Session})");
            Check(entry.Session.Port == _harness.Port && entry.Session.Mods.Any(m => m.StartsWith("vanilla ", StringComparison.Ordinal)),
                $"and the port and mods to join with ({entry.Session.Port}, {string.Join(", ", entry.Session.Mods)})");
            return true;
        });
        Add("joining the listed game connects a client", frame =>
        {
            if (frame == 0)
            {
                _join.JoinListed(_join.Listed.ToList().FindIndex(e => e.Session.World == "Menu World"));
                Check(_picked is { } p && p.Port == _harness.Port, $"the listed game's endpoint is picked ({_picked})");
                _client = _harness.StartClient("Client");
            }
            return _host.Main.PlayerSync.Players.Count == 2 && _client!.Main.Session.State == NetSession.SessionState.Connected;
        });
        Add("the listing counts the client", _ => _join.Listed.Any(e => e.Session is { World: "Menu World", Players: 2 }));
        Add("an address with no port joins the default port", _ =>
        {
            _join.Address.Text = "192.168.1.20";
            _picked = null;
            _join.JoinAddress();
            Check(_picked == new NetEndpoint("192.168.1.20", NetProtocol.DefaultPort), $"picked {_picked}");
            _join.Address.Text = "host:99999";
            _picked = null;
            _join.JoinAddress();
            Check(_picked is null && _join.Status.Length > 0, $"a bad port is refused ({_join.Status})");
            _join.QueueFree();
            return true;
        });
        Add("the in-game menu shows the session and who can save", _ =>
        {
            _hostMenu = new PauseMenu { Session = _host.Main.Session, Saves = _host.Main.Saves, Lan = _host.Main.Lan };
            _clientMenu = new PauseMenu { Session = _client!.Main.Session, Saves = _client.Main.Saves };
            AddChild(_hostMenu);
            AddChild(_clientMenu);
            _hostMenu.Open();
            _clientMenu.Open();
            Check(_hostMenu.SessionText.Contains($"Hosting on port {_harness.Port}, 2 of", StringComparison.Ordinal) && _hostMenu.SessionText.Contains("Join game", StringComparison.Ordinal),
                $"the host's menu says it is hosting and listed ({_hostMenu.SessionText})");
            Check(_hostMenu.CanSave && !_clientMenu.CanSave, "only the host can save");
            _hostMenu.Close();
            _clientMenu.Close();
            Check(!_hostMenu.IsOpen, "Resume closes the menu");
            return true;
        });
        Add("help opens on the page for what the player is doing and searches", _ =>
        {
            var topic = "playing.md";
            var menu = new PauseMenu { Session = _host.Main.Session, HelpTopic = () => topic };
            AddChild(menu);
            menu.OpenHelp();
            Check(menu.Help.Current == "playing.md" && menu.Help.Text.Contains("Controls", StringComparison.Ordinal), $"F1 shows How to Play ({menu.Help.Current})");
            topic = "quests.md";
            menu.OpenHelp();
            Check(menu.Help.Current == "quests.md", $"with the quest log open it shows the quests page ({menu.Help.Current})");
            menu.Help.Search("choose dialogue");
            Check(menu.Help.Listed.Contains("quests.md") && !menu.Help.Listed.Contains("packaging-guide.md"),
                $"searching finds the pages with every word ({string.Join(", ", menu.Help.Listed)})");
            menu.Help.Search("");
            Check(menu.Help.Listed.Count > 10, $"an empty search lists every page ({menu.Help.Listed.Count})");
            menu.QueueFree();
            return true;
        });
        Add("New game turns its boxes into launch options", _ =>
        {
            var menu = new MainMenu { StartWorld = () => { } };
            AddChild(menu);
            var screen = menu.OpenNewGame();
            screen.WorldName.Text = "  Island  ";
            screen.Seed.Text = "island";
            screen.SetMode(GameMode.Creative);
            screen.Start();
            var options = Main.MenuOptions;
            Check(options is { Mode: SessionMode.Offline, GameMode: GameMode.Creative, Seed: not null } && Main.MenuWorldName == "Island",
                $"an offline Creative world named Island ({options}, {Main.MenuWorldName})");
            var seed = options!.Seed;
            screen.Start();
            Check(Main.MenuOptions!.Seed == seed, "a text seed is always the same world");
            screen.Lan.ButtonPressed = true;
            screen.Seed.Text = "42";
            screen.Start();
            Check(Main.MenuOptions is { Mode: SessionMode.Host, Port: NetProtocol.DefaultPort, Seed: 42 }, $"Open to LAN hosts ({Main.MenuOptions})");
            screen.SetMode(GameMode.Survival);
            screen.SetDifficulty(Difficulty.Easy);
            screen.Start();
            Check(Main.MenuOptions is { GameMode: GameMode.Survival, Difficulty: Difficulty.Easy }, $"Survival can be Easy ({Main.MenuOptions})");
            screen.WorldName.Text = "";
            Main.MenuOptions = null;
            screen.Start();
            Check(Main.MenuOptions is null && screen.Status.Length > 0, "a world needs a name");
            Main.MenuWorldName = null;
            menu.QueueFree();
            return true;
        });
        Add("Options renames the player's profile", _ =>
        {
            var file = ProjectSettings.GlobalizePath("user://test_profile.json");
            if (File.Exists(file))
                File.Delete(file);
            var screen = new OptionsScreen { ProfileFile = file };
            AddChild(screen);
            var id = Main.UserProfile(null, file).Id;
            screen.PlayerName.Text = "  Zed ";
            screen.Commit();
            var saved = Main.UserProfile(null, file);
            Check(saved.Name == "Zed" && saved.Id == id, $"the profile keeps its id and has the new name ({saved})");
            screen.QueueFree();
            File.Delete(file);
            return true;
        });
    }
}
