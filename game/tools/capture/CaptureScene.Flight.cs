using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Logic;
using Mechanica.Core.Settings;
using Mechanica.Game.Settings;

namespace Mechanica.Game.Tools;

// Given `flight`, the capture builds two small battery-powered craft on the ground (tools/capture-flight.sh): a hover craft
// (a Hover Jet under a wooden deck with a Battery Container on it) and a rotor craft (four Rotors along a beam with a
// Battery Container in the middle), each with a switch and its container linked to what flies it, and the containers
// holding a few batteries. It shoots them on the ground, then switches the hover craft on and follows it up from below
// (the fan glowing), then the rotor craft.
public partial class CaptureScene
{
    private const int HoverCraft = 9011;
    private const int RotorCraft = 9012;
    private readonly Dictionary<int, int> _flightSwitch = [];

    private void FlightShots()
    {
        Engine.TimeScale = 1.0;
        var at = Vector3.Zero;
        Shot("flight_00_wait", 90, () => GameSettings.Apply(GameSettings.Current with
        {
            Graphics = GameSettings.Current.Graphics with { Grass = FoliageLevel.Off, GroundCover = FoliageLevel.Off },
        }));
        Shot("flight_00_build", 150, () =>
        {
            var from = Player.GlobalPosition + Player.GlobalTransform.Basis * new Vector3(0, 3, -4f);
            var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 12));
            at = hit.Count > 0 ? hit["position"].AsVector3() : Player.GlobalPosition;
            // Its jet hangs a cell below the deck: clear of the ground.
            Craft(HoverCraft, at + new Vector3(4.5f, 0.6f, 0f), creation =>
            {
                // Wood, so it lifts gently (26.5 kg against the jet's 300 N).
                foreach (var (x, z) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
                    Put(creation, "vanilla:wood_block", x, 0, z);
                var battery = Put(creation, "vanilla:battery", 0, 1, 0);
                var jet = Put(creation, "vanilla:hover_jet", 1, -1, 2, Upward(new GridCell(0, -1, 0)));
                var toggle = Put(creation, "vanilla:switch", 1, 0, 3, Upward(new GridCell(0, 0, 1)));
                Wire(creation, toggle, battery, jet);
                creation.InventoryOf(battery)!.Add("vanilla:battery_pack", 3);
            });
            Craft(RotorCraft, at + new Vector3(-1.5f, 0.25f, 0.5f), creation =>
            {
                // Cardboard: 54.5 kg against the rotors' 600 N.
                for (var x = -5; x <= 8; x++)
                    Put(creation, "vanilla:cardboard_block", x, 0, 1);
                var battery = Put(creation, "vanilla:battery", 0, 1, 0);
                var toggle = Put(creation, "vanilla:switch", 9, 0, 1, Upward(new GridCell(1, 0, 0)));
                Put(creation, "vanilla:switch", -6, 0, 1, Upward(new GridCell(-1, 0, 0)));
                foreach (var hub in new[] { -5, -2, 5, 8 })
                {
                    var rotor = Put(creation, "vanilla:rotor", hub - 1, 1, 0);
                    if (hub is -2 or 5)
                        creation.SetPartSettings(rotor, new Dictionary<string, System.Text.Json.JsonElement>
                        {
                            ["spin"] = System.Text.Json.JsonSerializer.SerializeToElement("counterclockwise"),
                        });
                    Wire(creation, toggle, battery, rotor);
                }
                creation.InventoryOf(battery)!.Add("vanilla:battery_pack", 8);
            });
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, at + new Vector3(-6f, 0.3f, 6f), 0f);
        });
        Shot("flight_01_ground", 60, () => FlightCamera(() => at + new Vector3(1.5f, 2.6f, 7.5f), () => at + new Vector3(1.8f, 0.4f, 0f)));
        Shot("flight_02_ground_hover_jet", 40, () => FlightCamera(() => Craft(HoverCraft) + new Vector3(-1.6f, 0.9f, 2.2f), () => Craft(HoverCraft) + new Vector3(0.4f, 0.1f, 0.4f)));
        Shot("flight_03_ground_rotors", 40, () => FlightCamera(() => Craft(RotorCraft) + new Vector3(0.4f, 2.1f, 3.4f), () => Craft(RotorCraft) + new Vector3(0.4f, 0.3f, 0.2f)));
        Shot("flight_04_hover_lifting", 60, () =>
        {
            Activate(HoverCraft);
            FlightCamera(() => at + new Vector3(3.2f, 0.9f, 5.2f), () => Craft(HoverCraft) + new Vector3(0.4f, 0f, 0.4f));
        });
        Shot("flight_05_hover_from_below", 30, () =>
            FlightCamera(() => new Vector3(Craft(HoverCraft).X + 1.8f, at.Y + 0.4f, Craft(HoverCraft).Z + 2.6f), () => Craft(HoverCraft) + new Vector3(0.4f, -0.2f, 0.4f)));
        Shot("flight_06_rotors_lifting", 200, () =>
        {
            Activate(RotorCraft);
            FlightCamera(() => Craft(RotorCraft) + new Vector3(-1.5f, 1.4f, 5.5f), () => Craft(RotorCraft) + new Vector3(0.4f, 0.2f, 0.2f));
        });
        Shot("flight_07_rotors_flying", 40, () =>
            FlightCamera(() => Craft(RotorCraft) + new Vector3(2.2f, 0.6f, 2.6f), () => Craft(RotorCraft) + new Vector3(0.2f, 0.3f, 0.2f)));
    }

    private void Craft(int id, Vector3 position, Action<Creation> build)
    {
        var creation = new Creation(id, _main.Mods!);
        build(creation);
        creation.SetAllBodyPoses(new Pose(position.ToNumerics(), System.Numerics.Quaternion.Identity));
        _main.Creations.Add(creation, _main.Assets);
    }

    // Where a craft's Body is now.
    private Vector3 Craft(int id) => _main.Creations.NodeOf(id)?.Bodies[0].GlobalPosition ?? Vector3.Zero;

    private static int Put(Creation creation, string part, int x, int y, int z, GridRotation rotation = default) =>
        creation.TryPlacePart(part, new GridCell(x, y, z), rotation, out var placed, out var error)
            ? placed.Id : throw new InvalidOperationException($"{part}: {error}");

    // Local +Y turned to point along outward.
    private static GridRotation Upward(GridCell outward) =>
        outward.Y != 0 ? GridRotation.FromAxes(new GridCell(1, 0, 0), outward) : GridRotation.FromAxes(new GridCell(0, -outward.X - outward.Z, 0), outward);

    private void Wire(Creation creation, int toggle, int battery, int target)
    {
        foreach (var from in new[] { toggle, battery })
        {
            if (!creation.TryLink(from, target, out var error))
                throw new InvalidOperationException(error);
        }
        _flightSwitch[creation.Id] = toggle;
    }

    // The player steps up to the craft, flips its switch and steps back out of the shot.
    private void Activate(int craft)
    {
        var away = Player.GlobalPosition;
        _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, Craft(craft) + new Vector3(0f, 0.1f, 2.5f), 0f);
        _main.Actions.Request(LogicActions.ActivatePart, new LogicPartArgs(craft, _flightSwitch[craft]), r =>
        {
            GD.Print($"[capture] switch {craft}: {r}");
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, away, 0f);
        });
    }

    // The capture camera, placed and aimed every frame (the craft move).
    private void FlightCamera(Func<Vector3> from, Func<Vector3> to)
    {
        Player.ThirdPerson = true;
        _camera.MakeCurrent();
        _follow = null;
        _iconTick = () =>
        {
            _camera.GlobalPosition = from();
            _camera.LookAt(to());
        };
        _iconTick();
    }
}
