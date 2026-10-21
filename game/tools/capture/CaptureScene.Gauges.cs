using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Items;
using Mechanica.Core.Settings;
using Mechanica.Core.Ui;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Tools;

// Given `gauges`, the capture builds a cart in Survival: a slab with a Scrap Driver's Seat, two Gas Containers, two Battery
// Containers and a Chemical Container, partly filled, and sits the player in the seat (tools/capture-gauges.sh). It shoots the
// vehicle gauges one row per container, renames the second Gas Container "Main tank" through its window, then shows one row
// per type, then none.
public partial class CaptureScene
{
    private const int GaugeCreation = 9007;
    private readonly Dictionary<string, int> _gaugeParts = [];

    private void GaugeShots()
    {
        Engine.TimeScale = 1.0;
        Shot("gauges_00_wait", 90, () => GameSettings.Apply(GameSettings.Current with
        {
            Graphics = GameSettings.Current.Graphics with { Grass = FoliageLevel.Off, GroundCover = FoliageLevel.Off },
            Interface = GameSettings.Current.Interface with { VehicleGauges = VehicleGaugeMode.PerContainer },
        }));
        Shot("gauges_00_cart", 120, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 3, -3f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 12));
            var at = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            var creation = new Creation(GaugeCreation, _main.Mods!);
            int Put(string part, int x, int y, int z) =>
                creation.TryPlacePart(part, new GridCell(x, y, z), GridRotation.Identity, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException(error);
            for (var x = -8; x <= 11; x++)
                for (var z = -8; z <= 6; z++)
                    Put("vanilla:metal_block", x, 0, z);
            _gaugeParts["seat"] = Put("vanilla:scrap_drivers_seat", 0, 1, -6);
            _gaugeParts["gas_1"] = Put("vanilla:gas_container", -7, 1, 2);
            _gaugeParts["gas_2"] = Put("vanilla:gas_container", -2, 1, 2);
            _gaugeParts["battery_1"] = Put("vanilla:battery", 3, 1, 2);
            _gaugeParts["battery_2"] = Put("vanilla:battery", 8, 1, 2);
            _gaugeParts["chemical_1"] = Put("vanilla:chemical_container", 8, 1, -3);
            creation.InventoryOf(_gaugeParts["gas_1"])!.Add("vanilla:gasoline", 62);
            creation.InventoryOf(_gaugeParts["gas_2"])!.Add("vanilla:gasoline", 8);
            creation.InventoryOf(_gaugeParts["battery_1"])!.Add("vanilla:battery_pack", 50);
            creation.InventoryOf(_gaugeParts["battery_2"])!.Add("vanilla:battery_pack", 31);
            creation.InventoryOf(_gaugeParts["chemical_1"])!.Add("vanilla:chemical", 74);
            creation.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(at.X, at.Y + 0.2f, at.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, at + new Vector3(-1.2f, 0.6f, -1.4f), 0f);
        });
        Shot("gauges_01_per_container", 150, () =>
        {
            _main.Actions.Request(Core.Logic.LogicActions.EnterSeat, new Core.Logic.LogicPartArgs(GaugeCreation, _gaugeParts["seat"]), r => GD.Print($"[capture] sit: {r}"));
            Player.ThirdPerson = true;
        });
        Shot("gauges_02_rename_window", 60, () =>
        {
            var screen = _main.FindChild("InventoryScreen", true, false) as Items.InventoryScreen;
            screen!.Show(InventoryRef.OfPart(GaugeCreation, _gaugeParts["gas_2"]));
        });
        Shot("gauges_03_renaming", 30, () =>
        {
            var screen = (Items.InventoryScreen)_main.FindChild("InventoryScreen", true, false)!;
            screen.StartRename();
            screen.ContainerNameField.Text = "Main tank";
        });
        Shot("gauges_04_renamed_window", 40, () =>
        {
            var screen = (Items.InventoryScreen)_main.FindChild("InventoryScreen", true, false)!;
            screen.FinishRename(save: true);
        });
        Shot("gauges_05_renamed", 60, () =>
        {
            ((Items.InventoryScreen)_main.FindChild("InventoryScreen", true, false)!).Hide();
            Input();
        });
        Shot("gauges_06_per_type", 40, () => GameSettings.Apply(GameSettings.Current with
        {
            Interface = GameSettings.Current.Interface with { VehicleGauges = VehicleGaugeMode.PerType },
        }));
        Shot("gauges_07_off", 40, () => GameSettings.Apply(GameSettings.Current with
        {
            Interface = GameSettings.Current.Interface with { VehicleGauges = VehicleGaugeMode.Off },
        }));
        Shot("gauges_08_per_container_chat", 60, () =>
        {
            GameSettings.Apply(GameSettings.Current with { Interface = GameSettings.Current.Interface with { VehicleGauges = VehicleGaugeMode.PerContainer } });
            ((Mechanica.Game.Chat.ChatPanel)_main.FindChild("ChatPanel", true, false)!).Send("heading to the Workshop");
        });
    }
}
