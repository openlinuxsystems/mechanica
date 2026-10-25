using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Characters;
using Mechanica.Core.Survival;

namespace Mechanica.Game.Tools;

// Crops batch K2 in a running world (tools/capture-consumables.sh OUT_DIR crops): the nearest wild Pigment Flower to the
// spawn, close and among its neighbours; a Pigment Flower and its seed packet held, third and first person; and a row of
// soil mounds beside it with Pigment Flower crops at each grow stage (seen from in front of the row), dry and wilted at the end.
public partial class CaptureScene
{
    private void CropShots()
    {
        Engine.TimeScale = 1.0;
        const string soil = "vanilla:soil";
        const string seed = "vanilla:pigment_flower_seed";
        var wild = Vector3.Zero;
        var feet = Vector3.Zero;
        void View(Vector3 from, Vector3 to)
        {
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = from;
            _camera.LookAt(to);
        }
        void HoldItem(string item)
        {
            Hold(CharacterProp.Item);
            Player.HeldItem = _main.HeldItems.IndexOf(item);
        }
        Shot("crops_00_wait", 60, () => { });
        Shot("crops_00_go", 900, () =>
        {
            var gen = _main.Streamer.Generator;
            var home = Player.GlobalPosition.ToNumerics();
            var flower = gen.ObjectsIn(home.X - 1500, home.Z - 1500, home.X + 1500, home.Z + 1500)
                .Where(o => o.Definition.Id == "wild_pigment_flower" && gen.SlopeAt(o.Position.X, o.Position.Z) < 0.2f)
                .OrderBy(o => (o.Position - home).Length()).First();
            wild = flower.Position.ToGodot();
            GD.Print($"[capture] wild pigment flower {flower.Id} at {wild} in {gen.BiomeAt(wild.X, wild.Z).Id}");
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, Ground(wild + new Vector3(0f, 0f, 2.5f)) + Vector3.Up * 0.3f, 0f);
        });
        Shot("crops_wild_close", 120, () => View(wild + new Vector3(1.1f, 1.0f, 1.4f), wild + new Vector3(0f, 0.35f, 0f)));
        Shot("crops_wild_meadow", 30, () => View(wild + new Vector3(4f, 2.2f, 6f), wild + new Vector3(0f, 0.4f, 0f)));
        Shot("crops_flower_held", 30, () => { HoldItem("vanilla:pigment_flower"); Look(new Vector3(-0.8f, 1.45f, -1.2f), 1.2f); });
        Shot("crops_flower_first_person", 40, () => { Input(); Player.MakeCameraCurrent(); });
        Shot("crops_seed_held", 30, () => { HoldItem(seed); Look(new Vector3(-0.8f, 1.45f, -1.2f), 1.2f); });
        Shot("crops_seed_first_person", 40, () => { Input(); Player.MakeCameraCurrent(); });
        Shot("crops_00_place", 40, () =>
        {
            Hold(CharacterProp.None);
            feet = Player.GlobalPosition;
            for (var i = 0; i < 5; i++)
            {
                var at = Ground(feet + new Vector3(-3f + 1.5f * i, 0f, 2.5f)) + Vector3.Up * 0.125f;
                _main.Actions.Request(Core.Building.BuildActions.PlacePart,
                    new Core.Building.PlacePartArgs(soil, Core.Creations.GridCell.Zero, Position: Core.Creations.Vec3Numerics.ToVec3(at.ToNumerics())),
                    r => GD.Print($"[capture] mound: {r.Ok} {r.Message}"));
            }
        });
        Shot("crops_stages", 200, () =>
        {
            var host = _main.Survival.Actions!;
            var me = new ActionCaller(_main.Session.LocalPeer, ActionSource.Player);
            var mounds = _main.Creations.All.Where(c => c.Parts.Count == 1 && c.Parts.First().Part == soil)
                .Select(c => (Creation: c, Part: c.Parts.First().Id, At: c.BodyPose(0).Position)).OrderBy(m => m.At.X).ToList();
            GD.Print($"[capture] {mounds.Count} mounds");
            var definition = _main.Build.Catalog.FindDefinition(soil)!.Soil!;
            // Just planted, a third and two thirds grown, ripe; the last ripe and dry, so it wilts.
            var stages = new[] { (0f, 0), (400f, 1), (800f, 2), (1260f, 4), (1260f, 4) };
            for (var i = 0; i < mounds.Count && i < stages.Length; i++)
            {
                host.PlantSeed(me, mounds[i].Creation.Id, mounds[i].Part, seed);
                if (i < 4)
                    host.WaterSoil(me, mounds[i].Creation.Id, mounds[i].Part, 1260); // a bucket
                mounds[i].Creation.SoilOf(mounds[i].Part)!.Set(grown: stages[i].Item1, stage: stages[i].Item2,
                    updated: _main.Scripts.Runtime.Time, wetAt: SoilState.WetAt(definition));
            }
            // From beyond the row (behind the player, away from the wild flower), so the player stands behind it, not in the way.
            View(feet + new Vector3(0f, 3.4f, 7.5f), feet + new Vector3(0f, 0.2f, 2.5f));
        });
        Shot("crops_ripe_close", 30, () => View(feet + new Vector3(1.0f, 1.0f, 3.8f), feet + new Vector3(1.5f, 0.35f, 2.5f)));
        Shot("crops_wilted_close", 30, () => View(feet + new Vector3(2.5f, 1.0f, 3.8f), feet + new Vector3(3f, 0.3f, 2.5f)));
    }

    // Crops batch K3 (tools/capture-consumables.sh OUT_DIR crops_k3): the nearest wild Cotton to the spawn (one merged mesh
    // since K3, so a world prop draws all of it); Beetroot, Banana and Chili held and bitten, third and first person, and
    // their seed packets held; and three rows of soil mounds (Beetroot, Banana, Chili from the front) with each crop at
    // every grow stage, dry and wilted at the end of its row.
    private void CropShotsK3()
    {
        Engine.TimeScale = 1.0;
        const string soil = "vanilla:soil";
        string[] crops = ["beetroot", "banana", "chili"];
        var wild = Vector3.Zero;
        var feet = Vector3.Zero;
        void View(Vector3 from, Vector3 to)
        {
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = from;
            _camera.LookAt(to);
        }
        void HoldAs(CharacterProp prop, string item)
        {
            Hold(prop);
            Player.HeldItem = _main.HeldItems.IndexOf(item);
        }
        Shot("k3_00_wait", 60, () => { });
        Shot("k3_00_go", 900, () =>
        {
            var gen = _main.Streamer.Generator;
            var home = Player.GlobalPosition.ToNumerics();
            var cotton = gen.ObjectsIn(home.X - 2000, home.Z - 2000, home.X + 2000, home.Z + 2000)
                .Where(o => o.Definition.Id == "wild_cotton" && gen.SlopeAt(o.Position.X, o.Position.Z) < 0.2f)
                .OrderBy(o => (o.Position - home).Length()).First();
            wild = cotton.Position.ToGodot();
            GD.Print($"[capture] wild cotton {cotton.Id} at {wild} in {gen.BiomeAt(wild.X, wild.Z).Id}");
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, Ground(wild + new Vector3(0f, 0f, 3f)) + Vector3.Up * 0.3f, 0f);
        });
        Shot("k3_wild_cotton_close", 120, () => View(wild + new Vector3(1.4f, 1.1f, 1.8f), wild + new Vector3(0f, 0.4f, 0f)));
        Shot("k3_wild_cotton_wide", 30, () => View(wild + new Vector3(3.4f, 1.8f, 1.6f), wild + new Vector3(0f, 0.4f, 0f)));
        foreach (var crop in crops)
        {
            var food = "vanilla:" + crop;
            Shot($"k3_{crop}_held", 30, () => { HoldAs(CharacterProp.Food, food); Look(new Vector3(-0.8f, 1.45f, -1.2f), 1.2f); });
            Shot($"k3_{crop}_bite", 24, () => Player.Gesture(CharacterGesture.Eat));
            Shot($"k3_{crop}_first_person", 40, () => { Input(); Player.MakeCameraCurrent(); });
            Shot($"k3_{crop}_first_person_bite", 40, () => Player.Gesture(CharacterGesture.Eat));
            Shot($"k3_{crop}_seed_held", 30, () => { HoldAs(CharacterProp.Item, food + "_seed"); Look(new Vector3(-0.8f, 1.45f, -1.2f), 1.2f); });
        }
        Shot("k3_00_place", 40, () =>
        {
            Hold(CharacterProp.None);
            feet = Player.GlobalPosition;
            for (var row = 0; row < crops.Length; row++)
                for (var i = 0; i < 5; i++)
                {
                    var at = Ground(feet + new Vector3(-3f + 1.5f * i, 0f, 2.5f + 1.6f * row)) + Vector3.Up * 0.125f;
                    _main.Actions.Request(Core.Building.BuildActions.PlacePart,
                        new Core.Building.PlacePartArgs(soil, Core.Creations.GridCell.Zero, Position: Core.Creations.Vec3Numerics.ToVec3(at.ToNumerics())),
                        r => GD.Print($"[capture] mound: {r.Ok} {r.Message}"));
                }
        });
        Shot("k3_stages", 240, () =>
        {
            var host = _main.Survival.Actions!;
            var me = new ActionCaller(_main.Session.LocalPeer, ActionSource.Player);
            var mounds = _main.Creations.All.Where(c => c.Parts.Count == 1 && c.Parts.First().Part == soil)
                .Select(c => (Creation: c, Part: c.Parts.First().Id, At: c.BodyPose(0).Position))
                .OrderBy(m => MathF.Round((m.At.Z - feet.Z - 2.5f) / 1.6f)).ThenBy(m => m.At.X).ToList();
            GD.Print($"[capture] {mounds.Count} mounds");
            var definition = _main.Build.Catalog.FindDefinition(soil)!.Soil!;
            // Just planted, a third and two thirds grown, ripe; the last ripe and dry, so it wilts.
            var stages = new[] { (0f, 0), (420f, 1), (840f, 2), (1260f, 4), (1260f, 4) };
            for (var i = 0; i < mounds.Count && i < 5 * crops.Length; i++)
            {
                var (grown, stage) = stages[i % 5];
                host.PlantSeed(me, mounds[i].Creation.Id, mounds[i].Part, $"vanilla:{crops[i / 5]}_seed");
                if (i % 5 < 4)
                    host.WaterSoil(me, mounds[i].Creation.Id, mounds[i].Part, 1260); // a bucket
                mounds[i].Creation.SoilOf(mounds[i].Part)!.Set(grown: grown, stage: stage,
                    updated: _main.Scripts.Runtime.Time, wetAt: SoilState.WetAt(definition));
            }
            View(feet + new Vector3(0f, 4.2f, 10.5f), feet + new Vector3(0f, 0.2f, 4.1f));
        });
        for (var row = 0; row < crops.Length; row++)
        {
            var z = 2.5f + 1.6f * row;
            // From above, between this row and the next, so the next row (behind the camera) is not in the way.
            Shot($"k3_{crops[row]}_row", 30, () => View(feet + new Vector3(0f, 3.4f, z + 1.1f), feet + new Vector3(0f, 0.2f, z - 0.2f)));
            Shot($"k3_{crops[row]}_ripe_close", 30, () => View(feet + new Vector3(1.2f, 1.3f, z + 0.8f), feet + new Vector3(1.5f, 0.35f, z)));
            Shot($"k3_{crops[row]}_wilted_close", 30, () => View(feet + new Vector3(2.7f, 1.3f, z + 0.8f), feet + new Vector3(3f, 0.3f, z)));
        }
    }

    // Crops batch K4 (tools/capture-consumables.sh OUT_DIR crops_k4): Blueberry, Orange, Broccoli and Pineapple held and
    // bitten, third and first person, and their seed packets held; then four rows of soil mounds (one crop a row, seen from
    // in front) with each crop at every grow stage, dry and wilted at the end of its row, and the four rows together.
    private void CropShotsK4()
    {
        Engine.TimeScale = 1.0;
        const string soil = "vanilla:soil";
        string[] crops = ["blueberry", "orange", "broccoli", "pineapple"];
        var feet = Vector3.Zero;
        void View(Vector3 from, Vector3 to)
        {
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = from;
            _camera.LookAt(to);
        }
        void HoldAs(CharacterProp prop, string item)
        {
            Hold(prop);
            Player.HeldItem = _main.HeldItems.IndexOf(item);
        }
        Shot("k4_00_wait", 60, () => { });
        foreach (var crop in crops)
        {
            var food = "vanilla:" + crop;
            Shot($"k4_{crop}_held", 30, () => { HoldAs(CharacterProp.Food, food); Look(new Vector3(-0.8f, 1.45f, -1.2f), 1.2f); });
            Shot($"k4_{crop}_bite", 24, () => Player.Gesture(CharacterGesture.Eat));
            Shot($"k4_{crop}_first_person", 40, () => { Input(); Player.MakeCameraCurrent(); });
            Shot($"k4_{crop}_first_person_bite", 40, () => Player.Gesture(CharacterGesture.Eat));
            Shot($"k4_{crop}_seed_held", 30, () => { HoldAs(CharacterProp.Item, food + "_seed"); Look(new Vector3(-0.8f, 1.45f, -1.2f), 1.2f); });
        }
        Shot("k4_00_place", 40, () =>
        {
            Hold(CharacterProp.None);
            feet = Player.GlobalPosition;
            for (var row = 0; row < crops.Length; row++)
                for (var i = 0; i < 5; i++)
                {
                    // Packed so the far row is still within build reach (7.5 m).
                    var at = Ground(feet + new Vector3(-2.4f + 1.2f * i, 0f, 1.6f + 1.4f * row)) + Vector3.Up * 0.125f;
                    _main.Actions.Request(Core.Building.BuildActions.PlacePart,
                        new Core.Building.PlacePartArgs(soil, Core.Creations.GridCell.Zero, Position: Core.Creations.Vec3Numerics.ToVec3(at.ToNumerics())),
                        r => GD.Print($"[capture] mound: {r.Ok} {r.Message}"));
                }
        });
        Shot("k4_stages", 240, () =>
        {
            var host = _main.Survival.Actions!;
            var me = new ActionCaller(_main.Session.LocalPeer, ActionSource.Player);
            var mounds = _main.Creations.All.Where(c => c.Parts.Count == 1 && c.Parts.First().Part == soil)
                .Select(c => (Creation: c, Part: c.Parts.First().Id, At: c.BodyPose(0).Position))
                .OrderBy(m => MathF.Round((m.At.Z - feet.Z - 1.6f) / 1.4f)).ThenBy(m => m.At.X).ToList();
            GD.Print($"[capture] {mounds.Count} mounds");
            var definition = _main.Build.Catalog.FindDefinition(soil)!.Soil!;
            // Just planted, a third and two thirds grown, ripe; the last ripe and dry, so it wilts.
            var stages = new[] { (0f, 0), (420f, 1), (840f, 2), (1260f, 4), (1260f, 4) };
            for (var i = 0; i < mounds.Count && i < 5 * crops.Length; i++)
            {
                var (grown, stage) = stages[i % 5];
                host.PlantSeed(me, mounds[i].Creation.Id, mounds[i].Part, $"vanilla:{crops[i / 5]}_seed");
                if (i % 5 < 4)
                    host.WaterSoil(me, mounds[i].Creation.Id, mounds[i].Part, 1260); // a bucket
                mounds[i].Creation.SoilOf(mounds[i].Part)!.Set(grown: grown, stage: stage,
                    updated: _main.Scripts.Runtime.Time, wetAt: SoilState.WetAt(definition));
            }
            View(feet + new Vector3(0f, 4.6f, 11f), feet + new Vector3(0f, 0.2f, 3.7f));
        });
        for (var row = 0; row < crops.Length; row++)
        {
            var z = 1.6f + 1.4f * row;
            // From above, between this row and the next, so the next row (behind the camera) is not in the way.
            Shot($"k4_{crops[row]}_row", 30, () => View(feet + new Vector3(0f, 4.2f, z + 1.0f), feet + new Vector3(0f, 0.2f, z - 0.3f)));
            Shot($"k4_{crops[row]}_ripe_close", 30, () => View(feet + new Vector3(1.0f, 1.3f, z + 0.8f), feet + new Vector3(1.2f, 0.35f, z)));
            Shot($"k4_{crops[row]}_wilted_close", 30, () => View(feet + new Vector3(2.2f, 1.3f, z + 0.8f), feet + new Vector3(2.4f, 0.3f, z)));
        }
    }
}
