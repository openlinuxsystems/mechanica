using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Characters;
using Mechanica.Core.Survival;

namespace Mechanica.Game.Tools;

// Consumables batch C2b in a running world (tools/capture-consumables.sh): Bloom Tea and a Grazer Shake held and drunk,
// third and first person, Fertilizer in hand, and two tomato crops side by side, the right one fertilized.
public partial class CaptureScene
{
    private void ConsumableShots()
    {
        const string soil = "vanilla:soil";
        const string seed = "vanilla:tomato_seed";
        var feet = Vector3.Zero;
        void View(Vector3 from, Vector3 to)
        {
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = from;
            _camera.LookAt(to);
        }
        void PlaceMound(float x, float z) => _main.Actions.Request(Core.Building.BuildActions.PlacePart,
            new Core.Building.PlacePartArgs(soil, Core.Creations.GridCell.Zero, Position: Core.Creations.Vec3Numerics.ToVec3((feet + new Vector3(x, 0.125f, z)).ToNumerics())),
            r => GD.Print($"[capture] mound: {r.Ok} {r.Message}"));
        void Drink(string item)
        {
            Hold(CharacterProp.Drink);
            Player.HeldItem = _main.HeldItems.IndexOf(item);
        }
        // The game runs at a tenth of its speed here and a frame takes about 0.013 s of game time: the drink clip tips the
        // bottle up at 0.2 s and gulps at 0.25 s and 0.4 s, some 16, 20 and 30 frames in.
        Shot("consumables_00_wait", 60, () => { });
        foreach (var (name, item) in new[] { ("bloom_tea", "vanilla:bloom_tea"), ("grazer_shake", "vanilla:grazer_shake") })
        {
            Shot($"consumables_{name}_held", 30, () => { Drink(item); Look(new Vector3(-1.2f, 1.5f, -1.9f), 1.25f); });
            Shot($"consumables_{name}_drink_tip", 16, () => Player.Gesture(CharacterGesture.Drink));
            Shot($"consumables_{name}_drink_gulp", 12, () => Look(new Vector3(-1.9f, 1.5f, -0.2f), 1.4f));
            Shot($"consumables_{name}_first_person", 40, () => { Input(); Player.MakeCameraCurrent(); });
            Shot($"consumables_{name}_first_person_gulp", 20, () => Player.Gesture(CharacterGesture.Drink));
        }
        Shot("consumables_fertilizer_held", 30, () =>
        {
            Hold(CharacterProp.Item);
            Player.HeldItem = _main.HeldItems.IndexOf("vanilla:fertilizer");
            Look(new Vector3(-1.2f, 1.4f, -1.9f), 1.1f);
        });
        Shot("consumables_fertilizer_first_person", 20, () => { Input(); Player.MakeCameraCurrent(); });
        Shot("consumables_crops_place", 40, () =>
        {
            Hold(CharacterProp.None);
            feet = Player.GlobalPosition;
            PlaceMound(-0.6f, -2f);
            PlaceMound(0.6f, -2f);
        });
        Shot("consumables_crops", 200, () =>
        {
            Engine.TimeScale = 1.0;
            var host = _main.Survival.Actions!;
            var me = new ActionCaller(_main.Session.LocalPeer, ActionSource.Player);
            var mounds = _main.Creations.All.Where(c => c.Parts.Count == 1 && c.Parts.First().Part == soil)
                .Select(c => (Creation: c, Part: c.Parts.First().Id, At: c.BodyPose(0).Position)).OrderBy(m => m.At.X).ToList();
            GD.Print($"[capture] {mounds.Count} mounds");
            var definition = _main.Build.Catalog.FindDefinition(soil)!.Soil!;
            for (var i = 0; i < mounds.Count; i++)
            {
                host.PlantSeed(me, mounds[i].Creation.Id, mounds[i].Part, seed);
                host.WaterSoil(me, mounds[i].Creation.Id, mounds[i].Part, 300);
                var state = mounds[i].Creation.SoilOf(mounds[i].Part)!;
                // The same time in the ground; the fertilized one has grown 15 times as far.
                state.Set(grown: i == 0 ? 60 : 300, stage: i == 0 ? 0 : 3, updated: _main.Scripts.Runtime.Time,
                    wetAt: SoilState.WetAt(definition));
                if (i == 1)
                    host.BoostSoil(me, mounds[i].Creation.Id, mounds[i].Part, 15);
            }
            View(feet + new Vector3(0f, 1.3f, -0.6f), feet + new Vector3(0f, 0.2f, -2f));
        });
        Shot("consumables_crops_close", 30, () => View(feet + new Vector3(0.9f, 0.8f, -1.2f), feet + new Vector3(0.6f, 0.15f, -2f)));
    }
}
