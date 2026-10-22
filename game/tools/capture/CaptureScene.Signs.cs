using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Logic;
using Mechanica.Core.Settings;
using Mechanica.Core.World;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Tools;

// Given `signs`, the capture puts a Signboard, a Colony Sign and a Paper Note (on a scrap wood wall) in a meadow in
// Creative, writes on each through the Action API and shoots them, then visits the world's nearest sign of each POI kind
// that has one and shoots it from in front of its words (tools/capture-signs.sh); a third argument `meadow` stops before the
// world's signs.
public partial class CaptureScene
{
    private static readonly (string Part, string Words)[] SignWords =
    [
        ("signboard", "GONE FISHING\nback at dusk"),
        ("colony_sign", "COLONY STORES\nStaff only\nKeys kept upstairs"),
        ("paper_note", "Took the engine\nto the Workshop.\nWheels are in\nthe shed. - M"),
    ];

    private void SignShots(bool meadowOnly)
    {
        Engine.TimeScale = 1.0;
        var made = new Dictionary<string, int>();
        void Made(string part, ActionResult r)
        {
            GD.Print($"[capture] place {part}: {r}");
            if (r.Ok)
                made[part] = r.Value!.Value.Deserialize<BatchResult>(ActionJson.Options)!.Creation;
        }
        Shot("signs_00_wait", 60, () => { });
        Shot("signs_00_go", 240, () =>
        {
            var g = _main.Streamer.Generator;
            Go(Find(p => new[] { (0f, 0f), (12f, 0f), (-12f, 0f), (0f, 12f), (0f, -12f) }.All(d => g.BiomeAt(p.X + d.Item1, p.Y + d.Item2).Id == "meadow")
                && g.SlopeAt(p.X, p.Y) < 0.06f && g.HeightAt(p.X, p.Y) > g.SeaLevel + 2f && !g.PoisAt(p.X, p.Y).Any()));
            GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Grass = FoliageLevel.Off, GroundCover = FoliageLevel.Off, Bushes = FoliageLevel.Off } });
            _main.Flora.BuildAll();
        });
        Shot("signs_01_place", 30, () => Place("vanilla:signboard", [GridCell.Zero], Ground(_meadow + new Vector3(-3f, 0, 0)) + Vector3.Up * 0.3f));
        Shot("signs_01_place_colony", 30, () => Place("vanilla:colony_sign", [GridCell.Zero], Ground(_meadow + new Vector3(0f, 0, 0)) + Vector3.Up * 0.3f));
        Shot("signs_01_wall", 30, () =>
        {
            var at = Ground(_meadow + new Vector3(2.5f, 0, 0)) + Vector3.Up * 0.3f;
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, at + new Vector3(0.4f, 0.3f, 2f), 0f);
            var cells = Enumerable.Range(0, 4).SelectMany(x => Enumerable.Range(0, 5).Select(y => new GridCell(x, y, 0))).ToList();
            _main.Actions.Request(BuildActions.PlaceParts, new PlacePartsArgs("vanilla:scrap_wood_block", cells, default, null, new Core.Data.Vec3(at.X, at.Y, at.Z)),
                r => Made("wall", r));
        });
        Shot("signs_01_note", 30, () =>
            _main.Actions.Request(BuildActions.PlacePart, new PlacePartArgs("vanilla:paper_note", new GridCell(1, 3, 1), default, made["wall"]), r => GD.Print($"[capture] note: {r}")));
        Shot("signs_02_write", 60, () =>
        {
            // Writing on a part takes build reach.
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, Ground(_meadow + new Vector3(0f, 0, 3f)) + Vector3.Up * 0.3f, 0f);
            foreach (var (part, words) in SignWords)
            {
                var creation = _main.Creations.All.Where(c => c.Parts.Any(p => p.Part == $"vanilla:{part}")).MaxBy(c => c.Id)!;
                var id = creation.Parts.First(p => p.Part == $"vanilla:{part}").Id;
                _main.Actions.Request(LogicActions.ConfigurePart, new ConfigurePartArgs(creation.Id, id,
                    new Dictionary<string, JsonElement?> { ["text"] = JsonSerializer.SerializeToElement(words) }), r => GD.Print($"[capture] write {part}: {r}"));
            }
        });
        Shot("signs_02_step_back", 30, () => _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, Ground(_meadow + new Vector3(8f, 0, 8f)) + Vector3.Up * 0.3f, 0f));
        Shot("signs_03_row", 40, () => View(new Vector3(0.2f, 2.0f, 6.5f), new Vector3(0.2f, 0.7f, 0f)));
        Shot("signs_03_row_right", 30, () => View(new Vector3(-6f, 1.8f, 5f), new Vector3(0f, 0.6f, 0f)));
        foreach (var (part, _) in SignWords)
            Shot($"signs_04_close_{part}", 30, () => WordsView(_main.Creations.All.Where(c => c.Parts.Any(p => p.Part == $"vanilla:{part}")).MaxBy(c => c.Id)?.Id, 1.3f, 0f));
        Shot("signs_04_back_colony_sign", 30, () => View(new Vector3(1.2f, 1.6f, -3f), new Vector3(0.4f, 0.9f, 0f)));

        if (meadowOnly)
            return;
        // The nearest sign with words of each POI kind that has one, from in front of its words and from further back.
        var spots = WorldParts.SpotsOf(_main.Streamer.Generator).Where(s => s.Settings?.ContainsKey("text") == true)
            .GroupBy(s => s.Poi).Select(g => g.MinBy(s => s.Floor.Length())!).OrderBy(s => s.Poi, StringComparer.Ordinal).ToList();
        foreach (var spot in spots)
        {
            var name = spot.Poi.Split(':')[^1];
            Shot($"signs_10_{name}_go", 240, () =>
            {
                GD.Print($"[capture] {spot.Poi} sign at {spot.Floor}: {spot.Settings!["text"].GetString()!.Replace('\n', '|')}");
                _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, spot.Floor.ToGodot() + new Vector3(0, 1f, 0) + new Vector3(Mathf.Sin(spot.Yaw), 0, Mathf.Cos(spot.Yaw)) * 8f, 0f);
            });
            int? Sign() => _main.Creations.All.FirstOrDefault(c => c.WorldPart == spot.Key)?.Id;
            Shot($"signs_11_{name}_close", 30, () =>
            {
                _main.Flora.BuildAll();
                WordsView(Sign(), 2.2f, 0f);
            });
            Shot($"signs_12_{name}_wide", 30, () => WordsView(Sign(), 9f, 0.5f));
        }
    }

    // Looks at a Creation's words from in front of them, `distance` m out and turned `turn` radians about them.
    private void WordsView(int? creation, float distance, float turn)
    {
        if (creation is not { } id || _main.Creations.NodeOf(id)?.Texts.Select(t => t.Label).FirstOrDefault() is not { } label)
        {
            GD.Print($"[capture] creation {creation}: no words drawn");
            return;
        }
        var face = label.GlobalBasis.Z.Normalized().Rotated(Vector3.Up, turn);
        _follow = null;
        _camera.MakeCurrent();
        _camera.GlobalPosition = label.GlobalPosition + face * distance + Vector3.Up * (0.1f + distance * 0.2f);
        _camera.LookAt(label.GlobalPosition);
    }
}
