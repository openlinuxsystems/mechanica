using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Survival;

namespace Mechanica.Game.Tools;

// Given `beds`, the capture puts the Respawn Bed (a sleeping bag) and the Fancy Bed side by side on a wooden floor (the
// meadow's grass would hide the bag), sleeps in the bag, lies on it (the downed pose: the player is knocked out on it,
// its knockout bag hidden) and respawns standing on it (tools/capture-beds.sh).
public partial class CaptureScene
{
    private const int BedCreation = 9008;

    private void BedShots()
    {
        Engine.TimeScale = 1.0;
        int bag = 0;
        Transform3D Bag() => _main.Creations.NodeOf(BedCreation)?.PartNode(bag)?.GlobalTransform ?? Transform3D.Identity;
        void View(Vector3 offset, float height, float across = 0.9f)
        {
            _follow = null;
            _camera.MakeCurrent();
            var centre = Bag().Origin + new Vector3(across, 0f, 0f);
            _camera.GlobalPosition = centre + offset;
            _camera.LookAt(centre + Vector3.Up * height);
        }
        Shot("beds_00_wait", 60, () => { });
        Shot("beds_00_place", 60, () =>
        {
            var at = Ground(Player.GlobalPosition + new Vector3(4f, 0f, -3f));
            var creation = new Creation(BedCreation, _main.Mods!);
            int Put(string part, int x, int y, int z) =>
                creation.TryPlacePart(part, new GridCell(x, y, z), GridRotation.Identity, out var placed, out var error)
                    ? placed.Id : throw new InvalidOperationException(error);
            for (var x = -4; x <= 15; x++)
                for (var z = -3; z <= 10; z++)
                    Put("vanilla:wood_block", x, 0, z);
            bag = Put("vanilla:respawn_bed", 0, 1, 0);
            Put("vanilla:fancy_bed", 7, 1, 0);
            creation.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(at.X, at.Y + 0.2f, at.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(creation, _main.Assets);
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, at + new Vector3(0.5f, 0.6f, 6f), 0f);
        });
        Shot("beds_01_side_by_side", 30, () => View(new Vector3(-1.8f, 2.0f, -3.4f), 0.0f));
        Shot("beds_02_sleep", 10, () =>
        {
            _main.Actions.Request(SurvivalActions.SetSpawn, new PartUseArgs(BedCreation, bag), r => GD.Print($"[capture] sleep: {r.Ok} {r.Message}"));
            // At the bag's foot end, facing its foot, so the body that falls back lies along it with its head at the stuff sack.
            var t = Bag();
            var foot = t.Basis.Z with { Y = 0 };
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, t.Origin + t.Basis * new Vector3(-0.2f, 0.12f, 0.75f), Mathf.Atan2(foot.X, foot.Z));
        });
        Shot("beds_03_lying", 150, () =>
        {
            _main.Survival.Actions!.Damage(Player.PeerId, 1000, "capture");
            View(new Vector3(-2.4f, 1.6f, -1.2f), 0.1f);
        });
        Shot("beds_04_lying_close", 10, () =>
        {
            foreach (var drop in _main.Survival.DropNodes.Values)
                drop.Visible = false;
            View(new Vector3(-1.9f, 1.3f, 0.9f), 0.1f, 0f);
        });
        Shot("beds_05_lying_wide", 10, () => View(new Vector3(-2.4f, 1.6f, -1.2f), 0.1f));
        Shot("beds_06_wait", 150, () => { });
        Shot("beds_07_respawn", 60, () =>
            _main.Actions.Request(SurvivalActions.Respawn, new Core.Actions.NoArgs(), r => GD.Print($"[capture] respawn: {r.Ok} {r.Message}")));
        Shot("beds_08_respawned", 10, () =>
        {
            GD.Print($"[capture] respawned at {Player.GlobalPosition}, bag at {Bag().Origin}");
            View(new Vector3(-3.4f, 1.9f, -2.6f), 0.8f);
        });
    }
}
