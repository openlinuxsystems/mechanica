using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Player;
using Mechanica.Game.Logic;
using Mechanica.Game.Player;

namespace Mechanica.Game.Tools;

// Given `connect_lines`, the capture puts a Gas Container, a Gas Engine, two bearings and a switch on a slab in
// Creative, links the switch and the bearings to the engine and takes the Connect Tool out in first person
// (tools/capture-connect.sh). It shows the board, a run of shots over the switch's line (its arrows running toward the
// engine) and over the engine's node (all its lines running), then drags from the engine to the container, which
// links container → engine, and hovers the new line.
public partial class CaptureScene
{
    private const int ConnectCreation = 9007;
    private Vector3 _connectAt;
    private readonly Dictionary<string, int> _connectParts = [];

    private void ConnectLineShots()
    {
        Engine.TimeScale = 1.0;
        Shot("connect_lines_00_wait", 60, () => { });
        Shot("connect_lines_00_board", 120, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 3, -3f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 12));
            _connectAt = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            var creation = new Creation(ConnectCreation, _main.Mods!);
            int Put(string part, int x, int y, int z) =>
                creation.TryPlacePart(part, new GridCell(x, y, z), GridRotation.Identity, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException(error);
            for (var x = -8; x <= 6; x++)
                for (var z = -3; z <= 3; z++)
                    Put("vanilla:metal_block", x, 0, z);
            var up = GridRotation.FromAxes(new GridCell(1, 0, 0), new GridCell(0, 1, 0));
            _connectParts["tank"] = Put("vanilla:gas_container", -8, 1, -2);
            _connectParts["engine"] = Put("vanilla:gas_engine", -1, 1, -1);
            _connectParts["switch"] = Put("vanilla:switch", -3, 1, 3);
            _connectParts["bearing"] = creation.TryPlacePart("vanilla:bearing", new GridCell(3, 1, 0), up, out var b1, out var e1) ? b1.Id : throw new InvalidOperationException(e1);
            _connectParts["bearing2"] = creation.TryPlacePart("vanilla:bearing", new GridCell(5, 1, 0), up, out var b2, out var e2) ? b2.Id : throw new InvalidOperationException(e2);
            creation.TryLink(_connectParts["switch"], _connectParts["engine"], out _);
            creation.TryLink(_connectParts["engine"], _connectParts["bearing"], out _);
            creation.TryLink(_connectParts["engine"], _connectParts["bearing2"], out _);
            creation.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(_connectAt.X, _connectAt.Y + 0.2f, _connectAt.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, _connectAt + new Vector3(-0.3f, 0.5f, 2.7f), 0f);
        });
        Shot("connect_lines_01_board", 90, () =>
        {
            foreach (var reader in _main.FindChildren("*", owned: false).OfType<LocalPlayerInput>())
                reader.SetPhysicsProcess(false);
            Player.MakeCameraCurrent();
            Tool<Building.ConnectTool>();
            AimAt(_connectAt + new Vector3(0, 0.3f, -0.2f));
        });
        for (var i = 0; i < 10; i++)
            Shot($"connect_lines_02_line_{i:00}", i == 0 ? 40 : 4, () => AimAt(Between("switch", "engine")));
        for (var i = 0; i < 6; i++)
            Shot($"connect_lines_03_engine_{i:00}", i == 0 ? 40 : 4, () => AimAt(PartCentre("engine")));
        Shot("connect_lines_04_drag", 30, () =>
        {
            // Under xvfb the mouse is never captured, and a drag stops when it is let go.
            Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Captured;
            Connector.PressDrag();
            AimAt(Between("engine", "tank"));
        });
        Shot("connect_lines_05_snap", 30, () => AimAt(PartCentre("tank")));
        Shot("connect_lines_06_linked", 60, () => Connector.ReleaseDrag(r => GD.Print($"[capture] link: {r} {string.Join(", ", _main.Creations.Find(ConnectCreation)!.Links)}")));
        for (var i = 0; i < 10; i++)
            Shot($"connect_lines_07_new_line_{i:00}", i == 0 ? 40 : 4, () => AimAt(Between("tank", "engine")));
    }

    private Vector3 PartCentre(string name) => ConnectionNodes.Centre(_main.Creations.NodeOf(ConnectCreation)!, _connectParts[name])!.Value;

    private Vector3 Between(string a, string b) => (PartCentre(a) + PartCentre(b)) / 2;

    private void AimAt(Vector3 target)
    {
        var d = target - Player.Camera.GlobalPosition;
        Player.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()) };
    }
}
