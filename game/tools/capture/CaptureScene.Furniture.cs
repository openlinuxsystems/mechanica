using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Player;

namespace Mechanica.Game.Tools;

// Parts audit D4: a small room on a wooden floor, its front wall concrete with the Door Frame (1.25 x 2.5 m) and a
// Window Frame in it, a trestle Wooden Table with two Chairs, a Stool, a Pallet and a Stairs Block inside, and the player
// walking in through the Door Frame from outside (tools/capture-furniture.sh).
public partial class CaptureScene
{
    private const int FurnitureCreation = 9017;

    private void FurnitureShots()
    {
        Engine.TimeScale = 1.0;
        var door = -1;
        // The doorstep: the Door Frame's foot at the middle of its opening, from the frame as the physics has it now.
        Vector3 Origin() => (_main.Creations.NodeOf(FurnitureCreation)?.PartNode(door)?.GlobalTransform ?? Transform3D.Identity) * new Vector3(0f, -1.25f, 0f);
        void View(Vector3 from, Vector3 to)
        {
            _follow = null;
            Player.ThirdPerson = true;
            _camera.MakeCurrent();
            _camera.GlobalPosition = Origin() + from;
            _camera.LookAt(Origin() + to);
            GD.Print($"[capture] door step at {Origin()}, player {Player.GlobalPosition - Origin()} from it");
        }
        Shot("furniture_00_wait", 60, () => { });
        Shot("furniture_00_place", 60, () =>
        {
            var at = Ground(Player.GlobalPosition + new Vector3(4f, 0f, -6f));
            var creation = new Creation(FurnitureCreation, _main.Mods!);
            void Put(string part, int x, int y, int z)
            {
                if (!creation.TryPlacePart($"vanilla:{part}", new GridCell(x, y, z), GridRotation.Identity, out _, out var error))
                    throw new InvalidOperationException($"{part} at {x},{y},{z}: {error}");
            }
            // The floor, a little wider than the room so the doorstep is on it too.
            for (var x = -9; x <= 8; x++)
                for (var z = -11; z <= 6; z++)
                    Put("wood_block", x, 0, z);
            // The front wall (z = -1), three metres tall, with the door (x -2..2, y 1..10) and the window (x 3..6, y 4..7).
            door = creation.TryPlacePart("vanilla:door_frame", new GridCell(-2, 1, -1), GridRotation.Identity, out var placedDoor, out var doorError)
                ? placedDoor.Id : throw new InvalidOperationException(doorError);
            var wall = new List<GridCell>();
            for (var x = -8; x <= 7; x++)
            {
                for (var y = 1; y <= 12; y++)
                {
                    var inDoor = x is >= -2 and <= 2 && y <= 10;
                    var inWindow = x is >= 3 and <= 6 && y is >= 4 and <= 7;
                    if (!inDoor && !inWindow)
                        wall.Add(new GridCell(x, y, -1));
                }
            }
            // A part goes on only next to one already there: go round until every wall block (and the window, once the
            // blocks under it are in) has found a neighbour.
            var window = false;
            for (var placedAny = true; (wall.Count > 0 || !window) && placedAny;)
            {
                placedAny = false;
                foreach (var cell in wall.ToList())
                {
                    if (creation.TryPlacePart("vanilla:concrete_block", cell, GridRotation.Identity, out _, out _))
                    {
                        wall.Remove(cell);
                        placedAny = true;
                    }
                }
                if (!window && creation.TryPlacePart("vanilla:window_frame", new GridCell(3, 4, -1), GridRotation.Identity, out _, out _))
                    placedAny = window = true;
            }
            if (wall.Count > 0 || !window)
                throw new InvalidOperationException($"{wall.Count} wall blocks (window placed: {window}) attach to nothing");
            // Low side and back walls, so the camera sees in over them.
            for (var z = -11; z <= -1; z++)
            {
                for (var y = 1; y <= 4; y++)
                {
                    Put("concrete_block", -9, y, z);
                    Put("concrete_block", 8, y, z);
                }
            }
            for (var x = -8; x <= 7; x++)
                for (var y = 1; y <= 4; y++)
                    Put("concrete_block", x, y, -11);
            // The furniture: the table with two chairs facing it, a stool, a pallet and the stairs.
            Put("wooden_table", -7, 1, -8);
            Put("chair", -6, 1, -10);
            Put("chair", -3, 1, -10);
            Put("stool", -8, 1, -4);
            Put("pallet", 2, 1, -10);
            Put("stairs_block", 3, 1, -5);
            // Built on the ground: the floor is anchored, so the room stays put while the player walks into it.
            foreach (var id in creation.Parts.Where(p => p.Part == "vanilla:wood_block").Select(p => p.Id).ToList())
                creation.SetAnchored(id, true);
            creation.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(at.X, at.Y + 0.2f, at.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
        });
        Shot("furniture_01_outside", 60, () =>
        {
            // Outside, in front of the door, facing it (yaw 0 faces -z).
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, Origin() + new Vector3(0f, 0.3f, 3.6f), 0f);
            Player.CurrentInput = PlayerInput.None;
            View(new Vector3(3.2f, 2.6f, 6.5f), new Vector3(0f, 1.2f, -1f));
        });
        Shot("furniture_02_room", 10, () => View(new Vector3(-0.3f, 4.2f, -6.2f), new Vector3(-0.3f, 0.4f, -1.4f)));
        Shot("furniture_03_approach", 55, () =>
        {
            Player.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            Look(new Vector3(1.4f, 1.6f, 3.0f), 1.2f);
        });
        Shot("furniture_04_doorway", 12, () => View(new Vector3(2.6f, 1.9f, 2.4f), new Vector3(0f, 1.1f, -0.2f)));
        Shot("furniture_05_through", 20, () => View(new Vector3(-1.6f, 3.6f, -3.2f), new Vector3(0f, 1.0f, -0.6f)));
        Shot("furniture_06_inside", 40, () =>
        {
            Player.CurrentInput = PlayerInput.None;
            View(new Vector3(-1.8f, 3.6f, -3.6f), new Vector3(0f, 0.9f, -0.4f));
        });
        Shot("furniture_07_inside_wide", 10, () => View(new Vector3(1.6f, 2.2f, -2.4f), new Vector3(-1.2f, 0.5f, -1.9f)));
    }
}
