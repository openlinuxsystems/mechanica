using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Quests;
using Mechanica.Core.Saves;
using Mechanica.Core.Ui;
using Mechanica.Game.Quests;
using Mechanica.Game.Ui;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Logbook (UI spec §5, §10, §13), host plus a client. The client's Logbook opens with its quests on the left
/// (active ones with their step) and the island's map on the right, drawn in blues. Dragging the map pans it, the
/// wheel zooms about the cursor, and the view is kept when the Logbook closes and opens. A right click marks a place
/// through the host; the marker shows in the list, on the compass and on the map, a click on it makes it the
/// compass's primary waypoint, and its ✕ removes it. A pin dropped with a label on the client's full map reaches the host
/// with its label and shows on the client's minimap. Markers are saved with the player and come back when they rejoin.
/// </summary>
public partial class LogbookTest : StagedTest
{
    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private QuestHud _hud = null!;
    private CompassHud _compass = null!;
    private MapHud _maps = null!;
    private System.Numerics.Vector2 _centre;
    private float _zoom;

    protected override string Tag => "logbook";

    // Drawing the whole island takes a while on a slow machine.
    protected override int StageTimeoutFrames => 3000;

    private LogbookWindow Logbook => _hud.Logbook;

    private LogbookMap Map => Logbook.Map;

    private MapMarkerSync Markers => _client.Main.MapMarkers;

    private IReadOnlyList<MapMarker> HostMarkers => _host.Main.MapMarkers.Markers.MarkersOf(_client.Peer);

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins", _ =>
        {
            if (_host.Main.PlayerSync.Players.Count != 2 || _client.Main.LocalPlayer is null || _client.Main.Quests.Tracker is null)
                return false;
            Join();
            return true;
        });
        Add("L opens the Logbook with the quests and the map", frame =>
        {
            if (frame == 0)
            {
                _hud.Open();
                return false;
            }
            if (!Map.HasMap)
                return false;
            Check(Logbook.Visible && _hud.IsOpen, "the Logbook is open");
            Check(Logbook.List.FindChild(LogbookWindow.NodeName("Quest", "vanilla:getting_started"), owned: false) is Button,
                "Getting Started is listed");
            Check(Map.Size.X > 200 && Map.Size.Y > 100, $"the map has room ({Map.Size})");
            Check(Map.View is { Placed: true } view && Mathf.IsEqualApprox(view.MetresPerPixel, view.MaxMetresPerPixel(Map.Size.ToNumerics())),
                "the map starts with the whole island");
            return true;
        });
        Add("dragging pans and the wheel zooms about the cursor", frame =>
        {
            var view = Map.View!;
            var middle = Map.GetGlobalRect().GetCenter();
            if (frame == 0)
            {
                _centre = view.Centre;
                Press(middle, MouseButton.Left, true);
                Drag(middle + new Vector2(30, 0), new Vector2(30, 0));
                Drag(middle + new Vector2(60, 0), new Vector2(30, 0));
                Press(middle + new Vector2(60, 0), MouseButton.Left, false);
                return false;
            }
            if (frame == 1)
            {
                Check(view.Centre.X < _centre.X - 1, $"dragging right moves the map west ({_centre} → {view.Centre})");
                _zoom = view.MetresPerPixel;
                var at = middle + new Vector2(40, 20);
                _centre = view.ToWorld((at - Map.GetGlobalRect().Position).ToNumerics(), Map.Size.ToNumerics());
                for (var i = 0; i < 4; i++)
                    Press(at, MouseButton.WheelUp, true);
                return false;
            }
            var cursor = middle + new Vector2(40, 20) - Map.GetGlobalRect().Position;
            Check(view.MetresPerPixel < _zoom * 0.5f, $"four wheel steps zoom in ({_zoom} → {view.MetresPerPixel} m per pixel)");
            Check(System.Numerics.Vector2.Distance(view.ToWorld(cursor.ToNumerics(), Map.Size.ToNumerics()), _centre) < view.MetresPerPixel * 2,
                "the ground under the cursor stays put");
            _zoom = view.MetresPerPixel;
            _centre = view.Centre;
            return true;
        });
        Add("the area in view is drawn again more sharply", _ => Map.HasDetail);
        Add("the view is kept when the Logbook closes and opens", frame =>
        {
            if (frame == 0)
            {
                _hud.Close();
                Check(!Logbook.Visible, "L closes it");
                _hud.Open();
                return false;
            }
            Check(Map.View!.Centre == _centre && Mathf.IsEqualApprox(Map.View.MetresPerPixel, _zoom), "same pan and zoom");
            return true;
        });
        Add("a right click marks a place through the host", frame =>
        {
            if (frame == 0)
            {
                Press(Map.GetGlobalRect().GetCenter() + new Vector2(-50, 30), MouseButton.Right, true);
                Press(Map.GetGlobalRect().GetCenter() + new Vector2(-50, 30), MouseButton.Right, false);
                return false;
            }
            if (Markers.Own.Count == 0)
                return false;
            var marker = Markers.Own[0];
            Check(HostMarkers.SequenceEqual(Markers.Own), "the host has the same marker");
            Check(marker is { Id: "marker:1", Name: "Marker 1" }, $"it is numbered ({marker})");
            var expected = Map.View!.ToWorld((Map.Size / 2 + new Vector2(-50, 30)).ToNumerics(), Map.Size.ToNumerics());
            Check(new Vector2(marker.X - expected.X, marker.Z - expected.Y).Length() < Map.View.MetresPerPixel * 2, "where the click was");
            Check(Mathf.IsEqualApprox(marker.Y, _client.Main.Streamer.Generator.HeightAt(marker.X, marker.Z)), "on the ground");
            Check(_compass.Marks.Any(m => m.Waypoint.Id == marker.Id), "the compass shows it");
            Check(Logbook.Marks().Any(m => m.Waypoint.Id == marker.Id && m.Kind == LogbookMarkKind.Own), "the map shows it");
            Logbook.Refresh();
            Check(Logbook.List.FindChild(LogbookWindow.NodeName("Marker", marker.Id), owned: false) is Button, "the list shows it");
            return true;
        });
        Add("clicking a marker makes it the primary waypoint", frame =>
        {
            var marker = Markers.Own[0];
            var at = Map.GetGlobalRect().Position + Map.ScreenOf(marker.ToWaypoint().Position.ToGodot());
            if (frame == 0)
            {
                Press(at, MouseButton.Left, true);
                Press(at, MouseButton.Left, false);
                return false;
            }
            Check(_compass.Primary == marker.Id, $"the compass follows it ({_compass.Primary})");
            Check(Map.ScreenOf(marker.ToWaypoint().Position.ToGodot()).DistanceTo(Map.Size / 2) < 2, "the map centres on it");
            Logbook.Refresh();
            Check(Logbook.List.FindChild(LogbookWindow.NodeName("Marker", marker.Id), owned: false) is Button { Text: "◆ Marker 1" }, "the list marks it primary");
            // A quest in the list does the same.
            var quest = Logbook.Marks().FirstOrDefault(m => m.Kind == LogbookMarkKind.Quest).Waypoint;
            if (quest is not null && Logbook.List.FindChild(LogbookWindow.NodeName("Quest", quest.Id["quest:".Length..]), owned: false) is Button button)
            {
                button.EmitSignal(BaseButton.SignalName.Pressed);
                Check(_compass.Primary == quest.Id, $"clicking a quest follows it ({_compass.Primary})");
            }
            return true;
        });
        Add("the host refuses a marker outside the world", frame =>
        {
            if (frame == 0)
                Markers.Add(new Vector3(1e6f, 0, 0), done: _results.Add);
            if (_results.Count == 0)
                return false;
            Check(!_results[0].Ok && HostMarkers.Count == 1, $"refused ({_results[0].Message})");
            _results.Clear();
            return true;
        });
        Add("its ✕ removes it", frame =>
        {
            if (frame == 0)
            {
                var row = Logbook.List.FindChild(LogbookWindow.NodeName("Marker", "marker:1"), owned: false)!.GetParent();
                row.GetNode<Button>("Remove").EmitSignal(BaseButton.SignalName.Pressed);
                return false;
            }
            if (Markers.Own.Count != 0)
                return false;
            Check(HostMarkers.Count == 0, "the host forgets it");
            Check(Logbook.Marks().All(m => m.Waypoint.Id != "marker:1"), "the map drops it");
            return true;
        });
        Add("the client's full map drops a labelled pin through the host, on its minimap too", frame =>
        {
            if (frame == 0)
            {
                _maps = new MapHud
                {
                    Quests = _client.Main.Quests, Players = _client.Main.PlayerSync, Markers = Markers, Settings = Main.LogbookSettings,
                    World = () => _client.Main.Streamer.Generator, Compass = _compass,
                };
                AddChild(_maps);
                _results.Clear();
                _maps.Window.AskLabel(_client.Main.LocalPlayer!.GlobalPosition + new Vector3(30, 0, 30));
                _maps.Window.LabelEdit.Text = "Camp ridge";
                _maps.Window.PlacePin();
                return false;
            }
            if (_results.Count > 0)
                return Markers.Own.Count == 0 && HostMarkers.Count == 0;
            if (Markers.Own.Count == 0 || !_maps.Minimap.Shown.Any(p => p.Waypoint.Name == "Camp ridge"))
                return false;
            Check(HostMarkers is [{ Name: "Camp ridge" }], $"the host has it with its label ({string.Join(", ", HostMarkers)})");
            Check(_maps.Pins().Any(p => p.Kind == LogbookMarkKind.Own && p.Waypoint.Name == "Camp ridge"), "the full map shows it");
            Check(_maps.Pins().Any(p => p.Kind == LogbookMarkKind.Quest), "and the client's current mission");
            // Taken away again, so the save below holds only its own marker.
            Markers.Remove(Markers.Own[0].Id, _results.Add);
            return false;
        });
        Add("a marker is saved with the player", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Markers.Add(new Vector3(40, 3, -60), "Camp", _results.Add);
                return false;
            }
            if (_results.Count == 0)
                return false;
            _results.Clear();
            var save = _host.Main.Saves.Capture("Logbook");
            var saved = save.Player(PlayerProfile.Fixed("Client").Id);
            Check(saved?.Markers is [{ Name: "Camp", X: 40, Z: -60 }], $"the save holds it ({saved?.Markers?.Count})");
            _hud.Close();
            _hud.QueueFree();
            _compass.QueueFree();
            _maps.QueueFree();
            _harness.Stop(_client);
            return true;
        });
        Add("the client comes back to its marker", frame =>
        {
            if (frame == 0)
            {
                _client = _harness.StartClient("Client");
                return false;
            }
            if (_client.Main.LocalPlayer is null || Markers.Own.Count == 0)
                return false;
            Check(Markers.Own[0] is { Name: "Camp", X: 40, Z: -60 }, $"the marker is back ({Markers.Own[0]})");
            return true;
        });
    }

    // A Logbook and compass for the client, as the game makes them.
    private void Join()
    {
        var main = _client.Main;
        _hud = new QuestHud
        {
            Quests = main.Quests, Actions = main.Actions, Players = main.PlayerSync,
            Markers = main.MapMarkers, MapSettings = Main.LogbookSettings, World = () => main.Streamer.Generator,
        };
        AddChild(_hud);
        _compass = new CompassHud
        {
            Players = main.PlayerSync,
            Waypoints = () => main.LocalPlayer is { } me ? [.. main.Quests.Waypoints(me.GlobalPosition.ToNumerics()), .. main.MapMarkers.Own.Select(m => m.ToWaypoint())] : [],
        };
        AddChild(_compass);
        _hud.Compass = _compass;
    }

    private void Press(Vector2 at, MouseButton button, bool pressed)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = button, Pressed = pressed, Position = at, GlobalPosition = at, Factor = 1,
            ButtonMask = pressed && button == MouseButton.Left ? MouseButtonMask.Left : 0,
        });
    }

    private void Drag(Vector2 at, Vector2 by) =>
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = by, ButtonMask = MouseButtonMask.Left });
}
