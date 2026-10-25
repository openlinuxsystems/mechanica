using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Settings;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Tools;

// Given `vehicle_body`, the capture sets the debug scrap car down in a meadow with an Aluminum Block cab roof over its seat and the
// parts audit's vehicle body parts on it: the Front Bumper, the Rear Bumper on the tail with the License Plate above
// its hitch, an Exhaust Pipe stack at the back corner, Mud Flaps behind the rear wheels and the Roof Rack on the roof;
// then shoots it from round about and close up (tools/capture-vehicle-body.sh).
public partial class CaptureScene
{
    private const int VehicleBodyCar = 9200;

    private void VehicleBodyShots()
    {
        Engine.TimeScale = 1.0;
        Shot("vehicle_body_00_wait", 60, () => { });
        Shot("vehicle_body_00_go", 240, () =>
        {
            var g = _main.Streamer.Generator;
            Go(Find(p => new[] { (0f, 0f), (12f, 0f), (-12f, 0f), (0f, 12f), (0f, -12f) }.All(d => g.BiomeAt(p.X + d.Item1, p.Y + d.Item2).Id == "meadow")
                && g.SlopeAt(p.X, p.Y) < 0.06f && g.HeightAt(p.X, p.Y) > g.SeaLevel + 2f && !g.PoisAt(p.X, p.Y).Any()));
            GameSettings.Apply(GameSettings.Current with { Graphics = GameSettings.Current.Graphics with { Grass = FoliageLevel.Off, GroundCover = FoliageLevel.Off, Bushes = FoliageLevel.Off } });
            _main.Flora.BuildAll();
        });
        Shot("vehicle_body_00_step_aside", 30, () => _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, Ground(_meadow + new Vector3(-3.5f, 0, 3.5f)) + Vector3.Up * 0.3f, 0f));
        Shot("vehicle_body_00_build", 300, () => SpawnBodyCar(Ground(_meadow) + Vector3.Up * 0.05f));
        // Views in the car's frame: x across its 1.5 m deck (cells 0..5), y up from the deck, z from its front (0) to its
        // back (3 m); it faces -Z.
        Shot("vehicle_body_01_front_right", 40, () => CarFrameView(new Vector3(3.6f, 2.3f, -3.0f), new Vector3(0.7f, 0.6f, 1.4f)));
        Shot("vehicle_body_02_rear_left", 30, () => CarFrameView(new Vector3(-2.8f, 2.1f, 6.2f), new Vector3(0.7f, 0.5f, 1.8f)));
        Shot("vehicle_body_03_side", 30, () => CarFrameView(new Vector3(5.6f, 1.4f, 1.5f), new Vector3(0.7f, 0.6f, 1.5f)));
        Shot("vehicle_body_04_top", 30, () => CarFrameView(new Vector3(2.6f, 5.6f, 4.6f), new Vector3(0.7f, 1.2f, 1.6f)));
        Shot("vehicle_body_05_rear_close", 30, () => CarFrameView(new Vector3(1.6f, 0.9f, 5.0f), new Vector3(0.7f, 0.1f, 3.0f)));
        Shot("vehicle_body_06_flap_close", 30, () => CarFrameView(new Vector3(-1.7f, 0.5f, 4.4f), new Vector3(-0.3f, 0.0f, 2.8f)));
        Shot("vehicle_body_07_front_close", 30, () => CarFrameView(new Vector3(2.0f, 0.8f, -1.9f), new Vector3(0.7f, 0.1f, -0.1f)));
        Shot("vehicle_body_08_rack_close", 30, () => CarFrameView(new Vector3(2.5f, 2.6f, -0.6f), new Vector3(0.9f, 1.5f, 1.2f)));
    }

    // The debug car's layout (Core DebugCar) plus a cab roof on four pillars and the vehicle body parts, set down with its
    // wheels on the ground at `ground`.
    private void SpawnBodyCar(Vector3 ground)
    {
        var car = new Creation(VehicleBodyCar, _main.Build.Catalog);
        DebugCar.Build(car, new DebugCarSettings { EngineOn = false });
        // Local +Z (a part's front) turned to face -Z, the car's front.
        var forward = GridRotation.FromAxes(new GridCell(-1, 0, 0), new GridCell(0, 1, 0));
        void Put(string part, GridCell min, GridRotation rotation = default)
        {
            // `min` is the footprint's lowest cell whatever the rotation.
            var size = _main.Build.Catalog.FindDefinition(part)!.Size ?? Core.Data.GridSize.One;
            var lowest = new GridCell(int.MaxValue, int.MaxValue, int.MaxValue);
            foreach (var x in new[] { 0, size.X - 1 })
            {
                foreach (var y in new[] { 0, size.Y - 1 })
                {
                    foreach (var z in new[] { 0, size.Z - 1 })
                    {
                        var c = rotation.Apply(new GridCell(x, y, z));
                        lowest = new GridCell(Math.Min(lowest.X, c.X), Math.Min(lowest.Y, c.Y), Math.Min(lowest.Z, c.Z));
                    }
                }
            }
            if (!car.TryPlacePart(part, min - lowest, rotation, out _, out var error))
                GD.Print($"[capture] {part} at {min}: {error}");
        }
        // The cab, in Aluminum Blocks: a pillar at each corner of the seat and a 6 x 8 roof over it.
        foreach (var (x, z) in new[] { (0, 2), (5, 2), (0, 8), (5, 8) })
        {
            for (var y = 1; y <= 4; y++)
                Put("vanilla:aluminum_block", new GridCell(x, y, z));
        }
        for (var x = 0; x < DebugCar.Width; x++)
        {
            for (var z = 2; z <= 9; z++)
                Put("vanilla:aluminum_block", new GridCell(x, 5, z));
        }
        Put("vanilla:bumper_front", new GridCell(-1, 0, -1), forward);
        // The rear bumper on the deck's tail; the plate and the exhaust stack on the Gas Container's back above it.
        Put("vanilla:bumper_rear", new GridCell(-1, 0, DebugCar.Length));
        Put("vanilla:license_plate", new GridCell(2, 1, DebugCar.Length));
        Put("vanilla:exhaust_pipe", new GridCell(5, 1, DebugCar.Length));
        Put("vanilla:mud_flap", new GridCell(-2, -1, DebugCar.RearAxle + 2));
        Put("vanilla:mud_flap", new GridCell(DebugCar.Width, -1, DebugCar.RearAxle + 2));
        Put("vanilla:roof_rack", new GridCell(0, 6, 2));
        var rotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, Mathf.DegToRad(30f));
        var g = DebugCar.GroundPoint;
        var origin = ground.ToNumerics() - System.Numerics.Vector3.Transform(new System.Numerics.Vector3(g.X, g.Y, g.Z), rotation);
        car.SetAllBodyPoses(new Pose(origin, rotation));
        _main.Creations.Add(car, _main.Assets);
        GD.Print($"[capture] vehicle body car: {car.Parts.Count} parts");
    }

    private void CarFrameView(Vector3 from, Vector3 to)
    {
        if (_main.Creations.NodeOf(VehicleBodyCar) is not { } node)
        {
            GD.Print("[capture] the vehicle body car is missing");
            return;
        }
        var chassis = node.Bodies[0].GlobalTransform;
        _follow = null;
        _camera.MakeCurrent();
        _camera.GlobalPosition = chassis * from;
        _camera.LookAt(chassis * to);
    }
}
