using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Items;

namespace Mechanica.Game.Tools;

// Parts map batch 16 in a running world (tools/capture-garden.sh OUT_DIR): a wooden porch with the eight pots in two rows
// and a Beehive whose two-way port is piped to a Small Chest of Pigment Flowers; its station screen, and the hive working
// on its own with its bees out.
public partial class CaptureScene
{
    private const int GardenCreation = 9016;

    private static readonly string[] GardenPots =
    [
        "vanilla:big_pot", "vanilla:potted_plant", "vanilla:potted_plant_small", "vanilla:potted_seedling", "vanilla:potted_vine",
        "vanilla:potted_cactus_bloom", "vanilla:potted_blue_flower", "vanilla:potted_cactus",
    ];

    private void GardenShots()
    {
        Engine.TimeScale = 1.0;
        var hive = 0;
        var chest = 0;
        var origin = Vector3.Zero;
        Items.InventoryScreen Screen() => (Items.InventoryScreen)_main.FindChild("InventoryScreen", true, false)!;
        Vector3 At(float x, float y, float z) => origin + new Vector3(x, y + Porch, z) * GardenGrid;
        void View(Vector3 eye, Vector3 target)
        {
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = eye;
            _camera.LookAt(target);
        }
        Shot("garden_00_wait", 60, () => { });
        Shot("garden_00_place", 60, () =>
        {
            var at = Ground(Player.GlobalPosition + new Vector3(3f, 0f, -4f));
            var creation = new Creation(GardenCreation, _main.Mods!);
            int Put(string part, int x, int y, int z, GridRotation? rotation = null) =>
                creation.TryPlacePart(part, new GridCell(x, y + Porch, z), rotation ?? GridRotation.Identity, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException($"{part}: {error}");
            for (var x = -2; x <= 13; x++)
                for (var z = -2; z <= 14; z++)
                    Put("vanilla:wood_block", x, 0, z);
            // A porch on posts, so the meadow grass stays under the deck instead of poking through it.
            foreach (var x in new[] { -2, 2, 6, 10, 13 })
                foreach (var z in new[] { -2, 2, 6, 10, 14 })
                    for (var y = -1; y >= -Porch; y--)
                        Put("vanilla:wood_block", x, y, z);
            chest = Put("vanilla:small_chest", 0, 1, 0);
            var alongX = GridRotation.FromAxes(new GridCell(0, 0, -1), new GridCell(0, 1, 0));
            Put("vanilla:wood_block", 3, 1, 1);
            Put("vanilla:wood_block", 4, 1, 1);
            Put("vanilla:vacuum_pipe", 3, 2, 1, alongX);
            Put("vanilla:vacuum_pipe", 4, 2, 1, alongX);
            hive = Put("vanilla:beehive", 7, 1, 2, GridRotation.FromAxes(new GridCell(-1, 0, 0), new GridCell(0, 1, 0)));
            for (var i = 0; i < GardenPots.Length; i++)
            {
                int[] columns = [-1, 3, 6, 8, -1, 2, 4, 6];
                Put(GardenPots[i], columns[i], 1, i < 4 ? 6 : 10);
            }
            origin = new Vector3(at.X, at.Y + 0.2f, at.Z);
            creation.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(origin.X, origin.Y, origin.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, at + new Vector3(1.0f, 1.4f, -1.2f), 0f);
        });
        Shot("garden_01_yard", 40, () => View(At(5, 14, -7), At(5, 1, 6)));
        Shot("garden_02_pots", 30, () => View(At(5, 5, 4), At(5, 1.5f, 9)));
        Shot("garden_03_pots_low", 30, () => View(At(0, 4, 2), At(4, 1.5f, 9)));
        Shot("garden_04_hive_port", 30, () => View(At(3.5f, 4.5f, -3.5f), At(5.5f, 2, 1.5f)));
        Shot("garden_05_hive_idle", 30, () => View(At(6, 4, -5), At(6, 3, 1)));
        Shot("garden_06_menu", 60, () =>
        {
            _main.Creations.Find(GardenCreation)!.InventoryOf(hive)!.Add("vanilla:pigment_flower", 3);
            Screen().Show(InventoryRef.OfPart(GardenCreation, hive));
        });
        Shot("garden_07_working", 90, () =>
        {
            Screen().Hide();
            _main.Creations.Find(GardenCreation)!.InventoryOf(chest)!.Add("vanilla:pigment_flower", 5);
            View(At(6, 4, -5), At(6, 3, 1));
        });
        Shot("garden_08_working_close", 30, () => View(At(6.5f, 3, -3.5f), At(6, 2.5f, 0.5f)));
    }

    private const float GardenGrid = 0.25f;

    // The porch deck's height in blocks.
    private const int Porch = 3;
}
