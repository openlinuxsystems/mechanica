using Godot;
using Mechanica.Core.Net;
using Mechanica.Core.Saves;
using Mechanica.Core.Survival;
using Mechanica.Core.Ui;
using Mechanica.Game.Menus;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// The world loading screen and boot splash (UI spec §14–15). An offline world shows the screen over everything:
/// the real stages in order with a bar that never goes back, the world's name and mode, a tip, clicks kept from
/// the game, at least the minimum time, then a fade into the world. An unreadable save fails with the bar red and
/// Return to Menu. A client joining waits on the host's own load (the host reports its stage), then loads. The
/// menu's transitions and the splash cover the screen and go.
/// </summary>
public partial class LoadingTest : StagedTest
{
    private readonly List<LoadStage> _seen = [];
    private readonly List<int> _percents = [];
    private readonly List<InputEvent> _leaked = [];
    private Main _main = null!;
    private NetTestHarness _net = null!;
    private NetTestHarness.Instance _host = null!;
    private NetTestHarness.Instance _client = null!;
    private string _badSave = "";
    private ulong _started;
    private bool _returned;
    private bool _looked;
    private (string, string) _lastText;

    protected override string Tag => "loading";

    protected override int StageTimeoutFrames => 1200;

    public override void _Ready()
    {
        var recorder = new Recorder { Name = "Recorder", Leaked = _leaked };
        AddChild(recorder);
        Add("an offline world opens behind the loading screen", frame =>
        {
            if (frame == 0)
            {
                _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
                _main.Options = LaunchOptions.Offline with { Seed = 7, GameMode = GameMode.Survival };
                _main.ShowLoading = true;
                AddChild(_main);
                MoveChild(recorder, -1);
                _started = Time.GetTicksMsec();
                return false;
            }
            var screen = _main.LoadingScreen;
            Record();
            if (frame == 1)
            {
                Check(screen is not null && screen.Layer > 4, "the screen is up, over the HUD");
                Check(Input.MouseMode == Input.MouseModeEnum.Visible, "the mouse is free while loading");
                return false;
            }
            // Once the screen has had a few frames of its own.
            if (!_looked && _main.Loading.Stage >= LoadStage.Chunks && screen!.WorldText.Length > 0)
            {
                _looked = true;
                Check(screen!.TipText.Length > 0, $"a tip shows ({screen.TipText})");
                Check(screen.WorldText.Contains("Survival"), $"the world's name and mode show ({screen.WorldText})");
                foreach (var at in new[] { new Vector2(200, 200), new Vector2(640, 500), new Vector2(1000, 650) })
                    Click(at);
                Check(_leaked.Count == 0, $"clicks on the loading screen don't reach the game ({_leaked.Count} did)");
            }
            if (_main.Loading.Entered)
                Check(_looked, "the screen showed the world while loading");
            return _main.Loading.Entered;
        });
        Add("it stays up the minimum time, then fades into the world", frame =>
        {
            Record();
            if (_main.LoadingScreen is { } screen)
            {
                if (_main.Loading.Entered && !screen.Leaving && !Done)
                    _lastText = (screen.PercentText, screen.StageText);
                return false;
            }
            Check(_lastText == ("100%", "Entering world…"), $"it reached 100% and said it was entering ({_lastText})");
            var shown = (Time.GetTicksMsec() - _started) / 1000.0;
            Check(shown >= _main.Loading.Settings.MinSeconds, $"it showed for at least {_main.Loading.Settings.MinSeconds} s ({shown:0.00} s)");
            var reached = _main.Loading.Reached;
            Check(reached.SequenceEqual(reached.Order()), $"stages came in order ({string.Join(", ", reached)})");
            Check(reached.Contains(LoadStage.Chunks) && reached.Contains(LoadStage.Physics) && reached.Contains(LoadStage.Finalizing) && reached[^1] == LoadStage.Entering
                && !reached.Contains(LoadStage.WaitingForHost), $"it went through the real stages ({string.Join(", ", reached)})");
            Check(_percents.SequenceEqual(_percents.Order()) && _percents.Last() == 100, $"the bar only went up, to 100% ({string.Join(" ", _percents.Distinct())})");
            Check(_main.LocalPlayer is not null, "the player is in the world");
            RemoveChild(_main);
            _main.QueueFree();
            return true;
        });
        Add("an unreadable save fails with Return to Menu", frame =>
        {
            if (frame == 0)
            {
                _badSave = ProjectSettings.GlobalizePath("user://loading-test-broken.json");
                System.IO.File.WriteAllText(_badSave, "{ not a save");
                _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
                _main.Options = LaunchOptions.Offline with { Seed = 7, LoadPath = _badSave };
                _main.ShowLoading = true;
                AddChild(_main);
                _main.LoadingScreen!.ReturnRequested += () => _returned = true;
                return false;
            }
            if (frame < 3)
                return false;
            var screen = _main.LoadingScreen!;
            Check(screen.Failed, "the failure shows");
            Check(screen.ErrorText.Contains("loading-test-broken.json"), $"the error names the save ({screen.ErrorText})");
            Check(screen.BarColour.R > screen.BarColour.B && screen.BarColour.R > screen.BarColour.G, $"the bar is red ({screen.BarColour})");
            Check(!_main.Loading.Entered && !screen.Leaving, "it does not go into the world");
            screen.ReturnButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(_returned, "Return to Menu goes back");
            RemoveChild(_main);
            _main.QueueFree();
            System.IO.File.Delete(_badSave);
            return true;
        });
        Add("a host reports its own load to a joining client", frame =>
        {
            if (frame == 0)
            {
                _net = new NetTestHarness { Name = "Net" };
                AddChild(_net);
                _host = _net.StartHost();
                return false;
            }
            if (!_host.Main.Loading.Entered)
                return false;
            Check(_host.Main.Session.HostStage == LoadStage.Entering, "the host reports itself loaded");
            // Hold the host mid-load to see the client wait for it.
            _host.Main.Session.ReportStage(LoadStage.Chunks);
            _seen.Clear();
            _client = _net.StartClient("Client");
            return true;
        });
        Add("the client waits for the host", frame =>
        {
            var loading = _client.Main.Loading;
            if (_seen.LastOrDefault() != loading.Stage || _seen.Count == 0)
                _seen.Add(loading.Stage);
            if (_client.Main.Session.HostStage != LoadStage.Chunks)
                return false;
            if (frame < 30)
                return false;
            Check(loading.Stage == LoadStage.WaitingForHost, $"the client waits ({loading.Stage})");
            Check(_client.Main.Session.HostName == "Host", $"it knows the host ({_client.Main.Session.HostName})");
            Check(loading.Percent == 0, "the bar stays empty while waiting");
            _host.Main.Session.ReportStage(LoadStage.Entering);
            return true;
        });
        Add("then loads the world once the host is ready", _ =>
        {
            var loading = _client.Main.Loading;
            if (_seen.LastOrDefault() != loading.Stage)
                _seen.Add(loading.Stage);
            if (!loading.Entered)
                return false;
            var reached = loading.Reached;
            Check(reached[0] == LoadStage.Connecting && reached.Contains(LoadStage.WaitingForHost) && reached.Contains(LoadStage.Physics) && reached[^1] == LoadStage.Entering,
                $"the client connected, waited, then loaded ({string.Join(", ", reached)})");
            Check(_client.Main.LocalPlayer is not null, "the client's player is in the world");
            _net.Stop(_client);
            _net.Stop(_host);
            return true;
        });
        Add("a join that fails says why", frame =>
        {
            if (frame == 0)
            {
                _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
                // An address that can't be connected to fails at once.
                _main.Options = LaunchOptions.ForJoin(new NetEndpoint("", 1));
                _main.ShowLoading = true;
                AddChild(_main);
                return false;
            }
            // It fails at once, but how many physics frames "at once" is depends on the machine's load.
            if (frame < 3 || (_main.LoadingScreen?.Failed != true && frame < 300))
                return false;
            Check(_main.LoadingScreen?.Failed == true, $"the failure shows ({_main.Loading.Stage}, {_main.Session.State})");
            Check(_main.LoadingScreen?.ErrorText.Length > 0, $"with the reason ({_main.LoadingScreen?.ErrorText})");
            RemoveChild(_main);
            _main.QueueFree();
            return true;
        });
        Add("a transition covers the screen until the next scene", frame =>
        {
            if (frame == 0)
            {
                _returned = false;
                var screen = LoadingScreen.Transition(this, "Returning to menu…", () => _returned = true);
                Check(screen.GetParent() == GetTree().Root, "it sits over the whole game");
                return false;
            }
            var transition = GetTree().Root.GetNodeOrNull<LoadingScreen>("Transition");
            if (!_returned)
                return false;
            Check(transition is not null && transition.StageText == "Returning to menu…", $"it says what is happening ({transition?.StageText})");
            transition?.QueueFree();
            return true;
        });
        Add("the boot splash shows the logo, then goes", frame =>
        {
            if (frame == 0)
            {
                AddChild(new BootSplash { Name = "Splash", Seconds = 0.2 });
                return false;
            }
            if (frame == 2)
                Check(GetNodeOrNull<BootSplash>("Splash")?.FindChild("Logo", true, false) is Label, "the logo shows");
            return GetNodeOrNull("Splash") is null && frame > 2;
        });
    }

    private void Record()
    {
        if (_seen.LastOrDefault() != _main.Loading.Stage || _seen.Count == 0)
            _seen.Add(_main.Loading.Stage);
        _percents.Add(_main.Loading.Percent);
    }

    private void Click(Vector2 at)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
    }

    // Stands in for the game behind the screen: every press that reaches unhandled input.
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
