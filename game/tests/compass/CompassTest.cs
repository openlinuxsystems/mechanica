using Godot;
using Mechanica.Core.Net;
using Mechanica.Core.Ui;
using Mechanica.Game.Menus;
using Mechanica.Game.Settings;
using Mechanica.Game.Ui;
using Vec3 = System.Numerics.Vector3;

namespace Mechanica.Game.Tests;

/// <summary>
/// The compass (UI spec §13): the story's goal is on it in a new world; waypoints sit where their bearing
/// puts them, behind ones at an edge, distance shown within 500 m and a world diamond within 100 m. Clicking
/// a diamond makes it primary; other players, AI agents included, are on it at any distance; the bar hides under the inventory and with Show compass off.
/// </summary>
public partial class CompassTest : StagedTest
{
    private Main _main = null!;
    private CompassHud _compass = null!;
    private int _closedAt;
    private int _agentPeer;

    protected override string Tag => "compass";

    public override void _Ready()
    {
        Add("a world starts with the compass up", frame =>
        {
            if (frame == 0)
            {
                GameSettings.Apply(new());
                _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
                _main.Options = LaunchOptions.Offline with { Seed = 7 };
                AddChild(_main);
                return false;
            }
            if (_main.LocalPlayer is null || frame < 30)
                return false;
            // A new world asks which character to play first, and that window covers the game until answered.
            if (_main.Characters.Window is { } choice)
            {
                choice.Confirm();
                return false;
            }
            // The HUD stays out of the way of the window until it has gone.
            if (_main.Characters.LocalNeedsChoice || !_main.GetNode<CompassHud>("Compass").Bar.Visible && frame < 300)
                return false;
            _compass = _main.GetNode<CompassHud>("Compass");
            Check(_compass.Bar.Visible, "the bar shows");
            var size = GetViewport().GetVisibleRect().Size;
            var rect = _compass.Bar.GetGlobalRect();
            Check(Mathf.IsEqualApprox(rect.Size.X, size.X * 0.4f) && Mathf.IsEqualApprox(rect.Position.Y, size.Y * 0.1f) && Mathf.IsEqualApprox(rect.GetCenter().X, size.X / 2),
                $"top centre, 10% down and 40% wide ({rect})");
            Check(_compass.Marks.Any(m => m.Waypoint.Id.StartsWith("quest:", StringComparison.Ordinal)), $"the story's first goal is on it ({string.Join(", ", _compass.Marks.Select(m => m.Waypoint.Id))})");
            return true;
        });
        Add("waypoints sit by their bearing", frame =>
        {
            if (frame == 0)
            {
                var me = _main.LocalPlayer!.GlobalPosition.ToNumerics();
                var heading = _compass.Heading * MathF.PI / 180f;
                Vec3 Toward(float turn, float metres) => me + new Vec3(MathF.Sin(heading + turn), 0, -MathF.Cos(heading + turn)) * metres;
                _compass.Waypoints = () =>
                [
                    new Waypoint("ahead", "Ahead", Toward(0, 50)),
                    new Waypoint("right", "Right", Toward(MathF.PI / 4, 300)),
                    new Waypoint("behind", "Behind", Toward(MathF.PI * 0.9f, 800)),
                ];
                return false;
            }
            // The test steps on physics frames; wait for the bar's next idle frame to pick the waypoints up.
            if (!_compass.Marks.Any(m => m.Waypoint.Id == "ahead") && frame < 60)
                return false;
            var marks = _compass.Marks.ToDictionary(m => m.Waypoint.Id, m => m.Mark);
            Check(Mathf.Abs(marks["ahead"].X) < 0.02f && !marks["ahead"].Behind, $"ahead is in the middle ({marks["ahead"]})");
            Check(Mathf.Abs(marks["right"].X - 0.5f) < 0.02f, $"45° right is halfway right ({marks["right"]})");
            Check(marks["behind"] is { Behind: true, X: 1f }, $"behind on the right is pinned to the right edge ({marks["behind"]})");
            Check(Compass.DistanceLabel(marks["right"].Distance) == "300 m" && Compass.DistanceLabel(marks["behind"].Distance) is null, "distance shows within 500 m");
            Check(_compass.Primary == "ahead", "the first waypoint is primary");
            Check(_compass.WorldMarkerOf("ahead") is not null && _compass.WorldMarkerOf("right") is null, "only the near one has a diamond in the world");
            return true;
        });
        Add("clicking a diamond makes it primary", frame =>
        {
            if (frame == 0)
            {
                var bar = _compass.Bar;
                var x = bar.Size.X / 2 * (1 + 0.5f * 0.94f);
                var at = bar.GetGlobalRect().Position + new Vector2(x, 12);
                GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
                return false;
            }
            Check(_compass.Primary == "right", $"the clicked one is primary ({_compass.Primary})");
            return true;
        });
        Add("other players are on it at any distance", frame =>
        {
            if (frame == 0)
            {
                var joined = _main.AgentHost.Join("compass-test", "Scout", out var error);
                if (!Check(joined is not null, $"an AI agent joins ({error})"))
                    return true;
                _agentPeer = joined!.Peer;
                // Far behind the player: well past the 500 m waypoints show their distance within.
                var me = _main.LocalPlayer!.GlobalPosition;
                var heading = _compass.Heading * MathF.PI / 180f;
                var behind = me - new Vector3(MathF.Sin(heading), 0, -MathF.Cos(heading)) * 2000f;
                Check(_main.PlayerSync.HostTeleport(_agentPeer, behind), "the agent is moved 2 km behind");
                return false;
            }
            if (!_compass.PlayerMarks.Any(m => m.Peer == _agentPeer && m.Mark.Distance > 1500) && frame < 60)
                return false;
            var mark = _compass.PlayerMarks.FirstOrDefault(m => m.Peer == _agentPeer);
            Check(mark.Name == "Scout (AI)", $"the agent is on the bar by name ({string.Join(", ", _compass.PlayerMarks.Select(m => m.Name))})");
            Check(mark.Mark.Behind && mark.Mark.Distance > 1500, $"behind and 2 km away ({mark.Mark})");
            Check(_compass.PlayerMarks.All(m => m.Peer != _main.Session.LocalPeer), "the local player is not marked");
            Check(_compass.Primary == "right", "a player never becomes the primary waypoint");
            _main.AgentHost.Leave("compass-test");
            return true;
        });
        Add("a player who leaves comes off it", frame =>
        {
            if (_compass.PlayerMarks.Any(m => m.Peer == _agentPeer) && frame < 60)
                return false;
            Check(!_compass.PlayerMarks.Any(m => m.Peer == _agentPeer), "the agent's mark is gone");
            return true;
        });
        Add("the bar hides under the inventory", frame =>
        {
            var inventory = _main.GetNode<Items.InventoryScreen>("InventoryScreen");
            // Stages tick on physics frames and the HUD on idle frames, which a slow frame can run several of at
            // once, so each step waits (a bounded number of ticks) for the bar to catch up.
            if (frame == 0)
            {
                inventory.Show(null);
                _closedAt = -1;
                return false;
            }
            if (_closedAt < 0)
            {
                if (_compass.Bar.Visible && frame < 60)
                    return false;
                Check(!_compass.Bar.Visible, "hidden while the inventory is open");
                inventory.Hide();
                _closedAt = frame;
                return false;
            }
            if (!_compass.Bar.Visible && frame < _closedAt + 60)
                return false;
            Check(_compass.Bar.Visible, "back when it closes");
            return true;
        });
        Add("Show compass off hides it", frame =>
        {
            if (frame == 0)
            {
                GameSettings.Apply(GameSettings.Current with { Interface = GameSettings.Current.Interface with { ShowCompass = false } });
                return false;
            }
            if ((_compass.Bar.Visible || _compass.WorldMarkerOf("ahead") is not null) && frame < 60)
                return false;
            Check(!_compass.Bar.Visible && _compass.WorldMarkerOf("ahead") is null, "the bar and its world diamonds are hidden");
            GameSettings.Apply(new());
            return true;
        });
    }
}
