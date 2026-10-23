using Godot;

namespace Mechanica.Game.Tools;

// Given `daynight`, the capture walks the sky through a day (tools/capture-daynight.sh): the crash site from a little
// way up, then the view from the highest ground near it looking back across the land, at each of a set of hours.
public partial class CaptureScene
{
    private static readonly double[] DayNightHours = [5.7, 7, 10.5, 14, 17.2, 18.3, 19.3, 23];

    private void DayNightShots()
    {
        Engine.TimeScale = 1.0;
        var gen = _main.Streamer.Generator;
        Shot("daynight_00_wait", 60, () => { });
        var crash = gen.Pois.First(p => p.Poi.Prefab.Id == "crash_site");
        var centre = new Vector3(crash.Centre.X, crash.PadHeight, crash.Centre.Y);
        // The highest ground in a ring 250 to 600 m out: a lookout over the land round the crash.
        var high = Vector3.Zero;
        for (var r = 250f; r <= 600f; r += 50f)
        for (var a = 0f; a < Mathf.Tau; a += Mathf.Tau / 48f)
        {
            var p = centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
            p.Y = gen.HeightAt(p.X, p.Z);
            if (p.Y > gen.SeaLevel + 1f && p.Y > high.Y)
                high = p;
        }
        var views = new (string Name, Vector3 Stand, Func<(Vector3 From, Vector3 To)> View)[]
        {
            ("crash", centre + new Vector3(0, 0, 60f), () => (Ground(centre + new Vector3(35f, 0, 55f)) + Vector3.Up * 9f, centre + Vector3.Up * 3f)),
            ("lookout", high, () => (high + Vector3.Up * 2.5f, centre + (high - centre).Normalized() * 120f + Vector3.Up * 2f)),
        };
        foreach (var (name, stand, view) in views)
        {
            Shot($"daynight_{name}_go", 300, () =>
            {
                GD.Print($"[capture] {name} at {stand}");
                _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, Ground(stand) + Vector3.Up * 2f, 0f);
            });
            foreach (var hour in DayNightHours)
            {
                Shot($"daynight_{name}_{hour:00.0}", 30, () =>
                {
                    _main.Flora.BuildAll();
                    _main.DayNight.Hour = hour;
                    var (from, to) = view();
                    _follow = null;
                    _camera.MakeCurrent();
                    _camera.GlobalPosition = from;
                    _camera.LookAt(to);
                });
            }
        }
    }
}
