using Godot;
using Mechanica.Core.Settings;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Tools;

// Given `site_signs`, the capture builds the parts audit's batch D5 parts (the Workshop Sign, the Warehouse Key Sign, the
// Speed Limit Sign and the Barrier Tape) in a row in a meadow, in Creative through the Action API, and shoots the row
// from the front, the front right and behind, and each part close up (tools/capture-site-signs.sh).
public partial class CaptureScene
{
    private static readonly string[] SiteSignParts = ["outpost_sign", "warehouse_key_sign", "speed_limit_sign", "barrier_tape"];

    private void SiteSignShots()
    {
        Engine.TimeScale = 1.0;
        Shot("site_signs_00_wait", 60, () => { });
        Shot("site_signs_00_go", 240, () =>
        {
            var g = _main.Streamer.Generator;
            Go(Find(p => new[] { (0f, 0f), (12f, 0f), (-12f, 0f), (0f, 12f), (0f, -12f) }.All(d => g.BiomeAt(p.X + d.Item1, p.Y + d.Item2).Id == "meadow")
                && g.SlopeAt(p.X, p.Y) < 0.06f && g.HeightAt(p.X, p.Y) > g.SeaLevel + 2f && !g.PoisAt(p.X, p.Y).Any()));
            GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Grass = FoliageLevel.Off, GroundCover = FoliageLevel.Off, Bushes = FoliageLevel.Off } });
            _main.Flora.BuildAll();
        });
        // A part grows from its placed cell toward +x and +z, and faces +z (its builder). The placed position is that cell's
        // centre, so 0.16 m up sets the part's foot 3.5 cm above the grass: inside building.json's anchor margin, so it is
        // anchored there as a player's build is (dropped from higher up, a tall thin sign is a loose body and tips over).
        float[] xs = [-5f, -3f, -1f, 1f];
        for (var i = 0; i < SiteSignParts.Length; i++)
        {
            var (part, x) = (SiteSignParts[i], xs[i]);
            Shot($"site_signs_01_place_{part}", 20, () =>
                Place($"vanilla:{part}", [new(0, 0, 0)], Ground(_meadow + new Vector3(x, 0, 0)) + Vector3.Up * 0.16f));
        }
        Shot("site_signs_02_row_front", 40, () => View(new Vector3(-1.2f, 1.9f, 6.5f), new Vector3(-1.2f, 0.7f, 0f)));
        Shot("site_signs_03_row_front_right", 30, () => View(new Vector3(-7.5f, 2.0f, 5f), new Vector3(-1.5f, 0.7f, 0f)));
        Shot("site_signs_04_row_back", 30, () => View(new Vector3(2.5f, 2.0f, -5.5f), new Vector3(-1.5f, 0.7f, 0f)));
        for (var i = 0; i < SiteSignParts.Length; i++)
        {
            var (part, x) = (SiteSignParts[i], xs[i]);
            var w = part == "barrier_tape" ? 1f : 0.4f;
            Shot($"site_signs_05_close_{part}", 30, () => View(new Vector3(x + w - 0.9f, 1.4f, 2.6f + w), new Vector3(x + w, 0.8f, 0.2f)));
        }
    }
}
