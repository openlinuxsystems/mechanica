using Godot;
using Mechanica.Core.Player;

namespace Mechanica.Game.Tools;

// Consumables batch C2a: the Fire Extinguisher in hand from the side, the front and first person, then spraying out a
// burning wooden block from the side, from behind and in first person (tools/capture-extinguisher.sh).
public partial class CaptureScene
{
    private const string Extinguisher = "vanilla:fire_extinguisher";

    private Vector3 _fireAt;

    // A camera off to the player's left, half way to the fire, taking in both.
    private void Side(Vector3 fire)
    {
        Player.ThirdPerson = true;
        _follow = null;
        _camera.MakeCurrent();
        var mid = (Player.GlobalPosition + fire) / 2;
        var left = (fire - Player.GlobalPosition).Cross(Vector3.Up).Normalized();
        _camera.GlobalPosition = mid - left * 3.6f + Vector3.Up * 1.5f;
        _camera.LookAt(mid + Vector3.Up * 0.7f);
    }

    private void ExtinguisherShots()
    {
        Engine.TimeScale = 1.0;
        var item = () => _main.Tools!.Item;
        Shot("extinguisher_00_wait", 60, () => { });
        Shot("extinguisher_00_take", 30, () =>
        {
            _main.Inventory.Items!.PlayerInventory(_main.Session.LocalPeer).Add(Extinguisher, 6);
            item().Choose(Extinguisher);
            _main.Tools!.Use(item());
        });
        Shot("extinguisher_01_side", 40, () => Look(new Vector3(-2.2f, 1.3f, 0.3f)));
        Shot("extinguisher_02_front", 10, () => Look(new Vector3(-1.2f, 1.4f, -2.2f)));
        Shot("extinguisher_03_first_person", 20, () => { Input(); Player.MakeCameraCurrent(); });
        Shot("extinguisher_04_fire", 120, () =>
        {
            // A burning stack of wooden blocks 3 m ahead.
            var ahead = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 0, -3f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(ahead + Vector3.Up * 4, ahead + Vector3.Down * 8));
            var at = hit.Count > 0 ? hit["position"].AsVector3() : ahead;
            var creation = new Core.Creations.Creation(SeatCreation, _main.Mods!);
            var ids = new List<int>();
            foreach (var cell in new[] { (0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0), (0, 0, 1), (1, 0, 1) })
            {
                if (!creation.TryPlacePart("vanilla:wood_block", new Core.Creations.GridCell(cell.Item1, cell.Item2, cell.Item3), Core.Creations.GridRotation.Identity, out var placed, out var error))
                    throw new InvalidOperationException(error);
                ids.Add(placed!.Id);
            }
            _fireAt = at;
            creation.SetAllBodyPoses(new Core.Creations.Pose(new System.Numerics.Vector3(at.X - 0.25f, at.Y + 0.02f, at.Z - 0.25f), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
            foreach (var id in ids)
                GD.Print($"[capture] ignite {id}: {_main.Fire.Actions!.Ignite(creation, id, 1f) ?? "ok"}");
            var d = (at + Vector3.Up * 0.25f - Player.Head.GlobalPosition).Normalized();
            Player.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()) };
            Side(at);
        });
        Shot("extinguisher_05_spray_side", 40, () => GD.Print($"[capture] hold: {item().Hold(r => GD.Print($"[capture] use: {r.Ok} {r.Message}"))}"));
        Shot("extinguisher_06_spray_behind", 20, () => Look(new Vector3(0.9f, 1.6f, 2.4f), 1.0f));
        Shot("extinguisher_06_spray_front", 15, () =>
        {
            _follow = null;
            _camera.GlobalPosition = _fireAt + (Player.GlobalPosition - _fireAt).Cross(Vector3.Up).Normalized() * 1.6f + Vector3.Up * 1.4f;
            _camera.LookAt(Player.GlobalPosition + Vector3.Up * 1.2f);
        });
        Shot("extinguisher_07_spray_first_person", 20, () => Player.MakeCameraCurrent());
        Shot("extinguisher_08_out", 120, () =>
        {
            Side(_fireAt);
            GD.Print($"[capture] fires left {_main.Fire.Actions!.Fires().Count(f => f.Strength > 0 && f.Part is not null)}, extinguishers {_main.Inventory.Own?.Count(Extinguisher)}");
        });
        Shot("extinguisher_09_let_go", 30, () =>
        {
            item().LetGo();
            GD.Print($"[capture] fires left {_main.Fire.Actions!.Fires().Count(f => f.Strength > 0 && f.Part is not null)}, extinguishers {_main.Inventory.Own?.Count(Extinguisher)}");
        });
    }
}
