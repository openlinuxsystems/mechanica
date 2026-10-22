using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.World;

namespace Mechanica.Game.Tools;

// Given `derelicts`, the capture lays a concrete yard by the spawn and shows each derelict vehicle (derelicts/*.json) twice
// on it, whole on the left and as a world leaves it (missing what one roll takes) on the right, from the front and the
// back; then the nearest Roadside Breakdown and Abandoned Tractor where the world put them (tools/capture-derelicts.sh).
public partial class CaptureScene
{
    private const int DerelictYard = 9100;

    private void DerelictShots()
    {
        Engine.TimeScale = 1.0;
        var derelicts = _main.Streamer.Generator.Data.Derelicts.Values.OrderBy(d => d.QualifiedId, StringComparer.Ordinal).ToList();
        var yard = Vector3.Zero;
        var shown = new List<int>();
        void View(Vector3 centre, Vector3 offset)
        {
            _follow = null;
            _camera.MakeCurrent();
            _camera.GlobalPosition = centre + offset;
            _camera.LookAt(centre + Vector3.Up * 0.6f);
        }
        Shot("derelicts_00_wait", 60, () => { });
        Shot("derelicts_00_yard", 30, () =>
        {
            yard = Ground(Player.GlobalPosition + new Vector3(10f, 0f, -12f));
            var floor = new Creation(DerelictYard, _main.Mods!);
            for (var x = -28; x <= 28; x++)
                for (var z = -16; z <= 16; z++)
                    floor.TryPlacePart("vanilla:concrete_block", new GridCell(x, 0, z), GridRotation.Identity, out _, out _);
            foreach (var id in floor.Parts.Select(p => p.Id).ToList())
                floor.SetAnchored(id, true);
            floor.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(yard.X, yard.Y + 0.4f, yard.Z), System.Numerics.Quaternion.Identity));
            _main.Creations.Add(floor, _main.Assets);
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, yard + new Vector3(0f, 1.2f, 9f), 0f);
        });
        var top = new Vector3(0f, 0.68f, 0f);
        for (var i = 0; i < derelicts.Count; i++)
        {
            var derelict = derelicts[i];
            var name = derelict.Definition.Id!;
            Shot($"derelicts_{i + 1:00}_{name}_place", 120, () =>
            {
                foreach (var id in shown)
                    _main.Creations.Remove(id);
                shown.Clear();
                var whole = Derelicts.Read(derelict, _main.Mods!, 9101, name, [])!;
                whole.SetAllBodyPoses(Derelicts.PoseFor(whole, (yard + top + new Vector3(-3.2f, 0f, 0f)).ToNumerics(), 0f));
                // A roll that takes something visible: the first seed that takes two or more parts.
                var seed = Enumerable.Range(1, 200).First(s => Derelicts.Missing(derelict, whole, (uint)s).Count >= 2);
                var found = Derelicts.Read(derelict, _main.Mods!, 9102, name, [])!;
                foreach (var part in Derelicts.Missing(derelict, whole, (uint)seed))
                    found.RemovePart(part, () => -1);
                found.SetAllBodyPoses(Derelicts.PoseFor(found, (yard + top + new Vector3(3.2f, 0f, 0f)).ToNumerics(), 0f));
                _main.Creations.Add(found, _main.Assets);
                shown.Add(found.Id);
                _main.Creations.Add(whole, _main.Assets);
                shown.Add(whole.Id);
                GD.Print($"[capture] {name}: {whole.Parts.Count} parts whole, {found.Parts.Count} as found");
            });
            Shot($"derelicts_{i + 1:00}_{name}_front", 10, () => View(yard + top, new Vector3(-4.5f, 3.6f, -8.5f)));
            Shot($"derelicts_{i + 1:00}_{name}_back", 10, () => View(yard + top, new Vector3(4.5f, 3.2f, 8.5f)));
        }
        foreach (var poi in new[] { "vanilla:roadside_breakdown", "vanilla:abandoned_tractor", "vanilla:abandoned_runabout" })
        {
            var name = poi[(poi.IndexOf(':') + 1)..];
            WorldPartSpot? site = null;
            Shot($"derelicts_site_{name}_go", 300, () =>
            {
                var from = Player.GlobalPosition.ToNumerics();
                site = Derelicts.SpotsOf(_main.Streamer.Generator).Where(s => s.Poi == poi)
                    .MinBy(s => System.Numerics.Vector3.Distance(s.Floor, from));
                if (site is not null)
                    _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, site.Floor.ToGodot() + new Vector3(0f, 1.5f, 12f), 0f);
            });
            Shot($"derelicts_site_{name}", 10, () =>
            {
                if (site is not null)
                    View(site.Floor.ToGodot(), new Vector3(-7f, 4.5f, 9f));
            });
        }
    }
}
