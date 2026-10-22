using Godot;
using Mechanica.Core.Player;

namespace Mechanica.Game.Tools;

// Parts batch 17a: the player climbing a Ladder that stands on a slab against a deck as tall as it, shot from the side and
// from behind on the way up, and stepping off at the top (tools/capture-ladder.sh).
public partial class CaptureScene
{
    private void LadderShots()
    {
        Engine.TimeScale = 1.0;
        Shot("ladder_00_wait", 60, () => { });
        Shot("ladder_01_placed", 60, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 3, -3.5f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 12));
            var at = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            var creation = new Core.Creations.Creation(SeatCreation, _main.Mods!);
            void Put(string part, int x, int y, int z)
            {
                if (!creation.TryPlacePart(part, new Core.Creations.GridCell(x, y, z), Core.Creations.GridRotation.Identity, out _, out var error))
                    throw new InvalidOperationException(error);
            }
            for (var x = -6; x < 6; x++)
                for (var z = -9; z < 8; z++)
                    Put("vanilla:metal_block", x, 0, z);
            for (var y = 1; y < 8; y++)
                Put("vanilla:metal_block", -6, y, -9);
            for (var x = -6; x < 6; x++)
                for (var z = -9; z < -1; z++)
                    Put("vanilla:metal_block", x, 8, z);
            Put("vanilla:ladder", -1, 1, -1);
            creation.SetAllBodyPoses(new Core.Creations.Pose(new System.Numerics.Vector3(at.X, at.Y + 0.1f, at.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
            // In front of the ladder (it faces +z), looking at it.
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, new Vector3(at.X, at.Y + 0.5f, at.Z + 0.9f), 0f);
            Player.CurrentInput = PlayerInput.None;
        });
        Shot("ladder_02_grab", 40, () =>
        {
            Player.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            Look(new Vector3(2.2f, 1.2f, 1.6f), 1.0f);
        });
        Shot("ladder_03_climb_side", 30, () => Look(new Vector3(2.6f, 0.6f, 0.2f), 1.0f));
        Shot("ladder_04_climb_behind", 20, () => Look(new Vector3(1.2f, 0.8f, 2.6f), 1.0f));
        Shot("ladder_05_hang", 30, () =>
        {
            Player.CurrentInput = PlayerInput.None;
            Look(new Vector3(-2.0f, 0.6f, 1.8f), 1.0f);
        });
        Shot("ladder_06_top", 90, () =>
        {
            Player.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            Look(new Vector3(2.4f, 1.4f, 1.4f), 0.8f);
        });
    }
}
