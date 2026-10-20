using Godot;
using Mechanica.Core.Creations;

namespace Mechanica.Game.Tools;

// Given `engine_menu`, the capture puts a row of engines down on a slab in Survival (a Gas Engine at levels 1, 4 and 5, a
// Scrap Gas Engine and an Electric Engine 3), fuels the first from a linked Gas Container, gives the player a few
// Component Kits and opens each engine's menu (tools/capture-engine-menu.sh). Then it pulls the first engine's lever to
// Off and back up to 4, and upgrades it from the menu.
public partial class CaptureScene
{
    private const int EngineCreation = 9006;
    private Vector3 _enginesAt;
    private readonly Dictionary<string, int> _engines = [];

    private void EngineMenuShots()
    {
        Engine.TimeScale = 1.0;
        Shot("engine_menu_00_wait", 60, () => { });
        Shot("engine_menu_00_board", 120, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 3, -4f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 12));
            _enginesAt = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            var creation = new Creation(EngineCreation, _main.Mods!);
            int Put(string part, int x, int y, int z) =>
                creation.TryPlacePart(part, new GridCell(x, y, z), GridRotation.Identity, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException(error);
            for (var x = -6; x <= 12; x++)
                for (var z = -2; z <= 3; z++)
                    Put("vanilla:metal_block", x, 0, z);
            _engines["gas_1"] = Put("vanilla:gas_engine", -5, 1, 0);
            _engines["gas_4"] = Put("vanilla:gas_engine", -1, 1, 0);
            _engines["gas_5"] = Put("vanilla:gas_engine", 2, 1, 0);
            _engines["scrap"] = Put("vanilla:scrap_gas_engine", 5, 1, 0);
            _engines["electric_3"] = Put("vanilla:electric_engine", 9, 1, 0);
            var tank = Put("vanilla:gas_container", -5, 1, 2);
            creation.SetLevel(_engines["gas_4"], 3);
            creation.SetLevel(_engines["gas_5"], 4);
            creation.SetLevel(_engines["electric_3"], 2);
            creation.TryLink(tank, _engines["gas_1"], out _);
            // An engine runs only from a control: a NOR gate with no inputs is always on, so Gas Engine 1 runs.
            var gate = Put("vanilla:logic_gate", -3, 1, 2);
            creation.SetPartSettings(gate, new Dictionary<string, System.Text.Json.JsonElement> { ["mode"] = System.Text.Json.JsonSerializer.SerializeToElement("nor") });
            creation.TryLink(gate, _engines["gas_1"], out _);
            creation.InventoryOf(tank)!.Add("vanilla:gasoline", 7);
            // Off, so the shots show the lever at rest rather than mid-start.
            foreach (var id in _engines.Values)
                creation.SetPartSettings(id, new Dictionary<string, System.Text.Json.JsonElement> { ["on"] = System.Text.Json.JsonSerializer.SerializeToElement(id == _engines["gas_1"]) });
            creation.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(_enginesAt.X, _enginesAt.Y + 0.2f, _enginesAt.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
            _main.Inventory.Items!.PlayerInventory(_main.Session.LocalPeer).Add("vanilla:component_kit", 4);
            Player.ThirdPerson = true;
            _camera.MakeCurrent();
            _follow = null;
            _camera.GlobalPosition = _enginesAt + new Vector3(0.6f, 2.6f, 4.8f);
            _camera.LookAt(_enginesAt + new Vector3(0.6f, 0.3f, 0));
        });
        foreach (var name in new[] { "gas_1", "gas_4", "gas_5", "scrap", "electric_3" })
            Shot($"engine_menu_{name}", 90, () => OpenEngineMenu(name));
        Shot("engine_menu_gas_1_off", 60, () =>
        {
            OpenEngineMenu("gas_1");
            _main.PartSettings!.Lever.Lever.Pick(0);
        });
        Shot("engine_menu_gas_1_power_4", 90, () => _main.PartSettings!.Lever.Lever.Pick(4));
        Shot("engine_menu_gas_1_upgraded", 120, () => _main.PartSettings!.Lever.Upgrade(r => GD.Print($"[capture] upgrade: {r}")));
        Shot("engine_menu_99_close", 10, () => _main.PartSettings!.Close());
    }

    private void OpenEngineMenu(string name)
    {
        var panel = _main.PartSettings!;
        panel.Close();
        panel.Open(EngineCreation, _engines[name]);
        var at = _main.Creations.Find(EngineCreation)!.FindPart(_engines[name])!.Cell;
        _camera.GlobalPosition = _enginesAt + new Vector3(at.X * 0.25f + 1.4f, 1.6f, 2.6f);
        _camera.LookAt(_enginesAt + new Vector3(at.X * 0.25f, 0.4f, 0));
    }
}
