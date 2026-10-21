using Godot;
using Mechanica.Core.World;

namespace Mechanica.Game.Tools;

// Given `fields`, the capture shows the farmland and roadside scenes nearest the middle of the world: a wheat, vegetable and
// corn field, an orchard and a ditch from wide and close, then the roads, a stretch of cable poles, a guard rail on a bend
// and a junction sign seen from the road (tools/capture-fields.sh).
public partial class CaptureScene
{
    private void FieldShots()
    {
        Engine.TimeScale = 1.0;
        var gen = _main.Streamer.Generator;
        Shot("fields_00_wait", 60, () => { });
        foreach (var id in new[] { "scene_wheat_field", "scene_vegetable_field", "scene_corn_field", "scene_ploughed_field", "scene_orchard", "scene_field_ditch" })
        {
            Shot($"fields_00_{id}_go", 150, () =>
            {
                var (at, yaw) = NearestScattered(gen, id);
                _meadow = at;
                _away = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                GD.Print($"[capture] {id} at {_meadow}");
                _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + _away.Rotated(Vector3.Up, 0.6f) * 30f + Vector3.Up * 2f, 0f);
            });
            Shot($"fields_{id}_wide", 30, () => { _main.Flora.BuildAll(); ViewAt(_meadow, _away.Rotated(Vector3.Up, 0.6f) * 48f + Vector3.Up * 22f, 0f); });
            Shot($"fields_{id}_close", 30, () => ViewAt(_meadow, _away.Rotated(Vector3.Up, -0.3f) * 24f + Vector3.Up * 3.5f, 0.5f));
        }
        // Roadside: stand on the road beside the thing and look along the road at it.
        foreach (var id in new[] { "colony_cable_pole", "scene_guard_rail", "road_sign_junction", "road_sign_warning_curve" })
        {
            Shot($"fields_00_{id}_go", 150, () =>
            {
                var (at, yaw) = NearestScattered(gen, id);
                _meadow = at;
                _away = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                GD.Print($"[capture] {id} at {_meadow}");
                _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _meadow + _away * 9f + Vector3.Up * 2f, 0f);
            });
            // Along the road (the object's x axis) from 30 m off, a little up; and from across the road.
            Shot($"fields_{id}_road", 30, () => { _main.Flora.BuildAll(); ViewAt(_meadow, _away * 8f + _away.Rotated(Vector3.Up, Mathf.Pi / 2f) * 30f + Vector3.Up * 4f, 1.5f); });
            Shot($"fields_{id}_across", 30, () => ViewAt(_meadow, _away * 26f + _away.Rotated(Vector3.Up, Mathf.Pi / 2f) * 6f + Vector3.Up * 5f, 2f));
        }
    }

    // The middle and turn of the scattered object or scene of this id nearest the world's middle (a scene: its pieces'
    // middle), looking out ring by ring of cells.
    private static (Vector3 At, float Yaw) NearestScattered(WorldGenerator gen, string id)
    {
        var rules = gen.Data.ScatterRules.Where(r => r.Object.Id == id).ToList();
        var spacing = rules.Min(r => r.Spacing);
        for (var ring = 0f; ring < 7000f; ring += spacing)
        {
            (Vector3 At, float Yaw)? best = null;
            foreach (var rule in rules)
            {
                var s = rule.Spacing;
                int inner = (int)MathF.Floor(ring / s), outer = (int)MathF.Ceiling((ring + spacing) / s);
                for (var iz = -outer; iz <= outer; iz++)
                {
                    for (var ix = -outer; ix <= outer; ix++)
                    {
                        if (Math.Max(Math.Abs(ix), Math.Abs(iz)) < inner)
                            continue;
                        var placed = gen.InCell(rule, ix, iz).ToList();
                        if (placed.Count == 0)
                            continue;
                        var sum = System.Numerics.Vector3.Zero;
                        foreach (var p in placed)
                            sum += p.Position;
                        var at = (sum / placed.Count).ToGodot();
                        if (best is null || new Vector2(at.X, at.Z).Length() < new Vector2(best.Value.At.X, best.Value.At.Z).Length())
                            best = (at, placed[0].Yaw);
                    }
                }
            }
            if (best is not null)
                return best.Value;
        }
        throw new InvalidOperationException($"no {id} in the world");
    }
}
