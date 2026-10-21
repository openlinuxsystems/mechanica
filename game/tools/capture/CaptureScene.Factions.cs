using Godot;

namespace Mechanica.Game.Tools;

// Given `factions`, the capture shows the bot factions' sites nearest the middle of the world, each from wide and close
// (the Scuttler nest, Rakehand shrine, Spooler reel tower and Thresher den), then one of each faction's loose marks
// (tools/capture-factions.sh).
public partial class CaptureScene
{
    private void FactionShots()
    {
        Engine.TimeScale = 1.0;
        var gen = _main.Streamer.Generator;
        Shot("factions_00_wait", 60, () => { });
        foreach (var id in new[] { "scene_scuttler_nest", "scene_rakehand_shrine", "scene_spooler_tower", "scene_thresher_den",
                     "scene_scuttler_marks", "scene_rakehand_marks", "scene_spooler_marks", "scene_thresher_marks" })
        {
            var marks = id.EndsWith("_marks");
            Shot($"factions_00_{id}_go", 150, () =>
            {
                var (at, yaw) = NearestScattered(gen, id);
                _meadow = at;
                _away = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                GD.Print($"[capture] {id} at {_meadow}");
                _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + _away.Rotated(Vector3.Up, 0.6f) * 30f + Vector3.Up * 2f, 0f);
            });
            var wide = marks ? 16f : 52f;
            Shot($"factions_{id}_wide", 30, () => { _main.Flora.BuildAll(); ViewAt(_meadow, _away.Rotated(Vector3.Up, 0.6f) * wide + Vector3.Up * wide * 0.45f, marks ? 0.5f : 2f); });
            Shot($"factions_{id}_close", 30, () => ViewAt(_meadow, _away.Rotated(Vector3.Up, -0.4f) * (marks ? 7f : 22f) + Vector3.Up * (marks ? 2.2f : 4f), marks ? 0.4f : 2.5f));
        }
    }
}
