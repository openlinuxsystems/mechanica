using Godot;
using Mechanica.Core.Player;
using Mechanica.Game.Logic;

namespace Mechanica.Game.Tools;

// Given `driving`, the driver's seats on a slab, each sat in and driven with no Creation to move (tools/capture-driving.sh):
// a run of frames per seat steering right, back, left and back, then pressing the throttle and the brake, seen from in
// front of the hands, from the side at the feet, and from the driver's own eyes. A third argument keeps the seats it
// names (comma-separated part ids).
public partial class CaptureScene
{
    private static readonly string[] DrivingSeats = ["drivers_seat", "scrap_drivers_seat", "drivers_saddle"];

    private void DrivingShots(string? only)
    {
        Engine.TimeScale = 1.0;
        var seats = new Dictionary<string, int>();
        var camera = _main.SeatCamera;
        if (camera is null)
            AddChild(camera = new SeatCamera { Logic = _main.Logic, Players = _main.PlayerSync, ReadDeviceInput = false });
        var parts = DrivingSeats.Where(p => only is not { Length: > 0 } || only.Split(',').Contains(p)).ToList();
        Shot("driving_00_wait", 60, () => { });
        Shot("driving_00_placed", 90, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 3, -3.5f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 12));
            var at = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            var creation = new Core.Creations.Creation(SeatCreation, _main.Mods!);
            int Put(string part, int x, int y, int z) =>
                creation.TryPlacePart(part, new Core.Creations.GridCell(x, y, z), Core.Creations.GridRotation.Identity, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException(error);
            for (var x = -8; x <= 16; x++)
                for (var z = -2; z <= 4; z++)
                    Put("vanilla:metal_block", x, 0, z);
            var cell = -8;
            foreach (var part in parts)
            {
                var size = _main.Mods!.FindDefinition("vanilla:" + part)!.Size!.Value;
                seats[part] = Put("vanilla:" + part, cell, 1, size.Z > 4 ? -2 : 0);
                cell += size.X + 2;
            }
            creation.SetAllBodyPoses(new Core.Creations.Pose(new System.Numerics.Vector3(at.X, at.Y + 0.2f, at.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
        });
        foreach (var part in parts)
        {
            Shot($"driving_{part}_0_sit", 120, () =>
            {
                _seat = seats[part];
                Player.ThirdPerson = true;
                SetControls(0, 0);
                _main.Actions.Request(Core.Logic.LogicActions.EnterSeat, new Core.Logic.LogicPartArgs(SeatCreation, _seat), r => GD.Print($"[capture] sit {part}: {r}"));
            });
            DrivingRun($"driving_{part}_1_hands", () => LookAtSeat(new Vector3(-0.75f, 1.35f, -1.25f), 0.85f), Steering);
            DrivingRun($"driving_{part}_2_feet", () => LookAtSeat(new Vector3(-1.5f, 0.45f, -0.55f), 0.35f), Pedals);
            DrivingRun($"driving_{part}_3_eyes", () =>
            {
                Player.ThirdPerson = false;
                camera.Camera.MakeCurrent();
            }, Steering);
            Shot($"driving_{part}_4_leave", 40, () =>
            {
                Player.ThirdPerson = true;
                _main.Actions.Request(Core.Logic.LogicActions.LeaveSeat, new Core.Actions.NoArgs(), r => GD.Print($"[capture] stand: {r}"));
            });
        }
    }

    // A recorded run (30 fps) with the camera set up once and the controls set each frame from what `controls` gives it.
    private void DrivingRun(string name, Action camera, Func<int, (float X, float Z)> controls)
    {
        Shot(name, DrivingFrames, () =>
        {
            _frame = 0;
            _time = 0;
            _track = null;
            _stillCamera = true;
            SetControls(0, 0);
            camera();
            DirAccess.MakeDirRecursiveAbsolute($"{_out}/{name}");
        });
        _recording[name] = f =>
        {
            var (x, z) = controls(f);
            SetControls(x, z);
        };
    }

    private const int DrivingFrames = 165;

    // Right (D) for a second, straight, left (A), straight; tapping D twice at the end.
    private static (float X, float Z) Steering(int f) => f switch
    {
        < 10 => (0, 0),
        < 50 => (1, 0),
        < 75 => (0, 0),
        < 115 => (-1, 0),
        < 135 => (0, 0),
        < 145 => (1, 0),
        < 152 => (0, 0),
        < 160 => (1, 0),
        _ => (0, 0),
    };

    // The throttle (W), let go, the brake (S), let go, while steering a little right.
    private static (float X, float Z) Pedals(int f) => f switch
    {
        < 10 => (0, 0),
        < 60 => (0, 1),
        < 80 => (0, 0),
        < 125 => (0, -1),
        < 140 => (0, 0),
        _ => (1, 1),
    };

    private void SetControls(float x, float z) =>
        Player.CurrentInput = new PlayerInput(x, z, Player.CurrentInput.Yaw, Player.CurrentInput.Pitch, false, false, false);
}
