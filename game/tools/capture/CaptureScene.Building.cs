using Godot;
using Mechanica.Core.Settings;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Tools;

// Given `parts_building`, the capture builds the parts map's batch 11 and 12 parts (stairs, railings, windows, windshields
// and signs) in rows in a meadow, in Creative through the Action API, and shoots each row from the front, from the front
// right and close up (tools/capture-parts-building.sh). A third argument picks the parts (comma separated).
public partial class CaptureScene
{
    private static readonly string[] BuildingParts =
    [
        "ramp_block", "stair_step", "stair_landing", "metal_railing", "metal_railing_long", "railing_join", "stair_banister",
        "stair_baluster", "net_fence", "concave_wedge",
        "window_square", "window_large", "window_metal", "side_window", "windshield", "windshield_large", "antenna",
        "stop_sign", "caution_sign", "danger_sign", "no_entry_sign", "direction_arrow_left", "direction_arrow_straight",
        "direction_arrow_right", "construction_sign", "falling_objects_sign", "skull_sign", "beware_bots_sign",
        "exit_sign", "traffic_cone", "banner_holder", "billboard", "hard_work_sign", "sale_sign", "welcome_sign",
    ];

    private void BuildingPartShots(string? only)
    {
        Engine.TimeScale = 1.0;
        var parts = only is null ? BuildingParts : BuildingParts.Where(only.Split(',').Contains).ToArray();
        const int perRow = 6;
        var rows = (parts.Length + perRow - 1) / perRow;
        Shot("building_00_wait", 60, () => { });
        Shot("building_00_go", 240, () =>
        {
            var g = _main.Streamer.Generator;
            Go(Find(p => new[] { (0f, 0f), (12f, 0f), (-12f, 0f), (0f, 12f), (0f, -12f) }.All(d => g.BiomeAt(p.X + d.Item1, p.Y + d.Item2).Id == "meadow")
                && g.SlopeAt(p.X, p.Y) < 0.06f && g.HeightAt(p.X, p.Y) > g.SeaLevel + 2f && !g.PoisAt(p.X, p.Y).Any()));
            GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Grass = FoliageLevel.Off, GroundCover = FoliageLevel.Off, Bushes = FoliageLevel.Off } });
            _main.Flora.BuildAll();
        });
        for (var i = 0; i < parts.Length; i++)
        {
            var (part, row, col) = (parts[i], i / perRow, i % perRow);
            Shot($"building_01_place_{part}", 20, () =>
                Place($"vanilla:{part}", [new(0, 0, 0)], Ground(_meadow + new Vector3(-7.5f + col * 3f, 0, -row * 4f)) + Vector3.Up * 0.3f));
        }
        for (var row = 0; row < rows; row++)
        {
            var z = -row * 4f;
            Shot($"building_{row + 2:00}_row_front", 40, () => View(new Vector3(0.5f, 2.6f, z + 8f), new Vector3(0.5f, 0.6f, z)));
            Shot($"building_{row + 2:00}_row_front_right", 30, () => View(new Vector3(-10f, 2.6f, z + 6f), new Vector3(-1f, 0.6f, z)));
            for (var col = 0; col < perRow && row * perRow + col < parts.Length; col++)
            {
                var x = -7.5f + col * 3f;
                var part = parts[row * perRow + col];
                // A part grows from its placed cell toward +x and +z, and faces +z (its builder).
                Shot($"building_{row + 2:00}_close_{part}", 30, () => View(new Vector3(x - 0.8f, 1.6f, z + 3.4f), new Vector3(x + 0.6f, 0.6f, z + 0.3f)));
            }
        }
    }
}
