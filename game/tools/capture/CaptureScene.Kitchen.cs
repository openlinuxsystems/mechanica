using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Items;

namespace Mechanica.Game.Tools;

// Consumables batch C4 in a running world (tools/capture-consumables.sh OUT_DIR kitchen): a Field Kitchen on a wooden
// floor, its station screen open with the burgers' recipes, the kitchen cooking a Garden Burger, and both burgers held and
// eaten, third and first person.
public partial class CaptureScene
{
    private const int KitchenCreation = 9010;

    private void KitchenShots()
    {
        Engine.TimeScale = 1.0;
        var kitchen = 0;
        Transform3D Kitchen() => _main.Creations.NodeOf(KitchenCreation)?.PartNode(kitchen)?.GlobalTransform ?? Transform3D.Identity;
        Items.InventoryScreen Screen() => (Items.InventoryScreen)_main.FindChild("InventoryScreen", true, false)!;
        void View(Vector3 offset, float height)
        {
            _follow = null;
            _camera.MakeCurrent();
            var centre = Kitchen().Origin;
            _camera.GlobalPosition = centre + offset;
            _camera.LookAt(centre + Vector3.Up * height);
        }
        void Eat(string item)
        {
            Hold(CharacterProp.Food);
            Player.HeldItem = _main.HeldItems.IndexOf(item);
        }
        Shot("kitchen_00_wait", 60, () => { });
        Shot("kitchen_00_place", 60, () =>
        {
            var at = Ground(Player.GlobalPosition + new Vector3(3f, 0f, -3f));
            var creation = new Creation(KitchenCreation, _main.Mods!);
            int Put(string part, int x, int y, int z) =>
                creation.TryPlacePart(part, new GridCell(x, y, z), GridRotation.Identity, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException(error);
            for (var x = -4; x <= 4; x++)
                for (var z = -3; z <= 5; z++)
                    Put("vanilla:wood_block", x, 0, z);
            kitchen = Put("vanilla:field_kitchen", 0, 1, 0);
            creation.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(at.X, at.Y + 0.2f, at.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, at + new Vector3(0.2f, 0.6f, 2.2f), 0f);
            var pockets = _main.Inventory.Items!.PlayerInventory(_main.Session.LocalPeer);
            pockets.Add("vanilla:carrot", 8);
            pockets.Add("vanilla:tomato", 10);
            pockets.Add("vanilla:potato", 30);
            pockets.Add("vanilla:grazer_steak", 2);
            pockets.Add("vanilla:garden_burger", 2);
            pockets.Add("vanilla:colony_burger", 2);
        });
        Shot("kitchen_01_idle", 30, () => View(new Vector3(1.6f, 1.3f, 2.2f), 0.2f));
        Shot("kitchen_02_screen", 60, () => Screen().Show(InventoryRef.OfPart(KitchenCreation, kitchen)));
        Shot("kitchen_03_cooking_screen", 60, () => _main.Actions.Request(InventoryActions.Craft,
            new CraftArgs(KitchenCreation, kitchen, "vanilla:garden_burger", 1), r => GD.Print($"[capture] cook: {r.Ok} {r.Message}")));
        Shot("kitchen_04_cooking", 40, () =>
        {
            Screen().Hide();
            View(new Vector3(1.4f, 1.1f, 1.9f), 0.2f);
        });
        Shot("kitchen_05_cooking_close", 30, () => View(new Vector3(0.5f, 0.45f, 1.1f), 0.1f));
        foreach (var (name, item) in new[] { ("garden_burger", "vanilla:garden_burger"), ("colony_burger", "vanilla:colony_burger") })
        {
            Shot($"kitchen_{name}_held", 30, () => { Eat(item); Look(new Vector3(-1.2f, 1.5f, -1.9f), 1.25f); });
            Shot($"kitchen_{name}_bite", 24, () => Player.Gesture(CharacterGesture.Eat));
            Shot($"kitchen_{name}_first_person", 40, () => { Input(); Player.MakeCameraCurrent(); });
            Shot($"kitchen_{name}_first_person_bite", 40, () => Player.Gesture(CharacterGesture.Eat));
        }
    }
}
