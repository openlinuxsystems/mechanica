using Godot;
using Mechanica.Core.Net;
using Mechanica.Game.Menus;
using Mechanica.Game.Quests;
using Mechanica.Game.Settings;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Tests;

/// <summary>
/// The maps: a new world shows the minimap in the top right with the story's goal on it and the quest log under it.
/// A labelled pin dropped nearby sits by its offset on the minimap, with its label, and one far away waits on the rim.
/// M opens the full map with the goal and the pins (an empty label numbers the pin), and frees the mouse; asking to
/// remove a pin asks first; M closes it again. Show minimap off hides the minimap and gives its corner back to the quest log.
/// </summary>
public partial class MapsTest : StagedTest
{
    private Main _main = null!;
    private MapHud _maps = null!;

    protected override string Tag => "maps";

    private MapMarkerSync Markers => _maps.Markers!;

    private Vector3 Feet => _main.LocalPlayer!.GlobalPosition;

    public override void _Ready()
    {
        Add("a world starts with the minimap up", frame =>
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
            if (_main.Characters.Window is { } choice)
            {
                choice.Confirm();
                return false;
            }
            _maps = _main.GetNode<MapHud>("MapHud");
            if ((!_maps.Minimap.IsVisibleInTree() || !_maps.Minimap.HasPicture) && frame < 600)
                return false;
            var screen = GetViewport().GetVisibleRect().Size;
            var margin = HudRoot.Margin(GetViewport());
            var rect = _maps.Minimap.GetGlobalRect();
            Check(_maps.Minimap.IsVisibleInTree() && _maps.Minimap.HasPicture, "the minimap shows the map round the player");
            Check(Mathf.IsEqualApprox(rect.End.X, screen.X - margin.X) && Mathf.IsEqualApprox(rect.Position.Y, margin.Y), $"it sits in the top right corner ({rect})");
            Check(_maps.Minimap.Shown.Any(p => p.Kind == LogbookMarkKind.Quest), $"the story's goal is on it ({string.Join(", ", _maps.Minimap.Shown.Select(p => p.Waypoint.Id))})");
            var log = _main.GetNode<QuestHud>("QuestHud").GetNode<Control>("QuestLog");
            Check(!log.Visible || log.GetGlobalRect().Position.Y >= rect.End.Y, $"the quest log sits under it ({log.GetGlobalRect()})");
            return true;
        });
        Add("a labelled pin nearby is on the minimap with its label", frame =>
        {
            if (frame == 0)
            {
                _maps.Window.AskLabel(Feet + new Vector3(40, 0, -60));
                Check(_maps.Window.Naming.Visible, "a right click on the map asks for a label");
                _maps.Window.LabelEdit.Text = "Copper ridge";
                _maps.Window.PlacePin();
                Check(!_maps.Window.Naming.Visible, "placing the pin puts the label row away");
                return false;
            }
            if (!Markers.Own.Any(m => m.Name == "Copper ridge") || !_maps.Minimap.Shown.Any(p => p.Waypoint.Name == "Copper ridge"))
                return frame < 60 ? false : Check(false, $"the pin is placed ({_maps.Window.Status})");
            var pin = _maps.Minimap.Shown.First(p => p.Waypoint.Name == "Copper ridge");
            var size = _maps.Minimap.Size.X;
            var mpp = _maps.Settings.MinimapMetresPerPixel;
            var expected = new Vector2(size / 2 + 40 / mpp, size / 2 - 60 / mpp);
            Check(!pin.AtEdge && pin.At.DistanceTo(expected) < 1.5f, $"it sits 40 m east and 60 m north of the player ({pin.At}, expected {expected})");
            return true;
        });
        Add("a pin far away waits on the rim", frame =>
        {
            if (frame == 0)
            {
                _maps.Window.AskLabel(Feet + new Vector3(-1500, 0, 0));
                _maps.Window.LabelEdit.Text = "";
                _maps.Window.PlacePin();
                return false;
            }
            var far = Markers.Own.FirstOrDefault(m => m.Name != "Copper ridge");
            if (far is null || !_maps.Minimap.Shown.Any(p => p.Waypoint.Id == far.Id))
                return frame < 60 ? false : Check(false, $"the second pin is placed ({_maps.Window.Status})");
            Check(far.Name == "Marker 2", $"an empty label numbers the pin ({far.Name})");
            var pin = _maps.Minimap.Shown.First(p => p.Waypoint.Id == far.Id);
            Check(pin.AtEdge && Mathf.IsEqualApprox(pin.At.X, MinimapView.RimInset) && Mathf.Abs(pin.At.Y - _maps.Minimap.Size.Y / 2) < 1f,
                $"it waits on the west rim, level with the player ({pin.At})");
            return true;
        });
        Add("M opens the full map", frame =>
        {
            if (frame == 0)
            {
                // M opens the map only while playing (the mouse captured), which a headless run never is.
                Check(!_maps.CanOpen(), "M does nothing while the mouse is free");
                _maps.CanOpen = () => true;
                PressMap();
                return false;
            }
            if (!_maps.Window.Map.HasMap && frame < 600)
                return false;
            Check(_maps.IsOpen && _maps.Window.Visible && _maps.Window.Map.HasMap, "the full map is open and drawn");
            Check(!_maps.Minimap.Visible, "the minimap steps aside");
            var marks = _maps.Window.Map.Marks();
            Check(marks.Any(m => m.Kind == LogbookMarkKind.Quest) && marks.Count(m => m.Kind == LogbookMarkKind.Own) == 2,
                $"it shows the mission goal and both pins ({string.Join(", ", marks.Select(m => m.Waypoint.Name))})");
            Check(_maps.Window.Map.LabelQuests, "mission goals are named on it");
            return true;
        });
        Add("removing a pin asks first", frame =>
        {
            if (frame == 0)
            {
                var pin = Markers.Own.First(m => m.Name == "Copper ridge");
                _maps.Window.AskRemove(pin.Id);
                Check(ConfirmDialog.Current is { } dialog && dialog.Message.Contains("Copper ridge"), "a confirm dialog names the pin");
                Check(Markers.Own.Any(m => m.Id == pin.Id), "the pin stays until the answer");
                ConfirmDialog.Current?.Confirm();
                return false;
            }
            return Markers.Own.All(m => m.Name != "Copper ridge") || (frame < 60 ? false : Check(false, "the pin is removed"));
        });
        Add("M closes it again", frame =>
        {
            if (frame == 0)
            {
                PressMap();
                return false;
            }
            Check(!_maps.IsOpen && !_maps.Window.Visible, "the full map is closed");
            return true;
        });
        Add("Show minimap off hides it", frame =>
        {
            if (frame == 0)
            {
                GameSettings.Apply(GameSettings.Current with { Interface = GameSettings.Current.Interface with { ShowMinimap = false } });
                return false;
            }
            if (frame < 3)
                return false;
            Check(!_maps.Minimap.Visible && MapHud.MinimapBottom == 0, "the minimap is hidden");
            var log = _main.GetNode<QuestHud>("QuestHud").GetNode<Control>("QuestLog");
            Check(!log.Visible || log.GetGlobalRect().Position.Y < 100, $"the quest log takes the corner back ({log.GetGlobalRect()})");
            GameSettings.Apply(new());
            return true;
        });
    }

    private void PressMap()
    {
        GetViewport().PushInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.M, Keycode = Key.M });
        GetViewport().PushInput(new InputEventKey { Pressed = false, PhysicalKeycode = Key.M, Keycode = Key.M });
    }
}
