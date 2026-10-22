using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.World;
using Mechanica.Game.Mods;

namespace Mechanica.Game.Tools;

/// <summary>
/// Writes the vanilla derelict vehicles (<c>mods/vanilla/derelicts/*.json</c>, see <see cref="Derelicts"/>): each design is
/// built here part by part with the Creation API, the way a player builds one, so the files always fit the current parts.
/// Each file holds the Creation as a blueprint stores it and the parts a derelict is found without. Run
/// <c>tools/author-derelicts.sh</c> after changing a design or a part's shape; <c>DerelictTests</c> checks the files.
/// </summary>
public partial class DerelictAuthor : Node
{
    // A design: id, name, what it is usually found without, and how to build it.
    private sealed record Design(string Id, string Name, DerelictMissing[] Missing, System.Action<Kit> Build);

    private static DerelictMissing Gone(string part, float chance = 1f, int count = 1, int countMax = 1) =>
        new() { Part = part, Chance = chance < 1f ? chance : null, Count = count != 1 ? count : null, CountMax = countMax > count ? countMax : null };

    private static readonly Design[] Designs =
    [
        new("scrap_buggy", "Scrap Buggy",
            [Gone("scrap_gas_engine", 0.85f), Gone("scrap_wheel", 0.9f, 1, 2), Gone("gas_container", 0.5f), Gone("scrap_drivers_seat", 0.25f)],
            ScrapBuggy),
        new("go_kart", "Go-Kart", [Gone("scrap_gas_engine", 0.9f), Gone("scrap_wheel", 0.4f)], GoKart),
        new("farm_cart", "Farm Cart", [Gone("scrap_wheel", 0.7f), Gone("scrap_seat", 0.5f)], FarmCart),
        new("colony_runabout", "Colony Runabout",
            [Gone("gas_engine", 0.9f), Gone("gas_container", 0.6f), Gone("wheel", 0.6f), Gone("drivers_seat", 0.3f), Gone("headlight", 0.5f, 1, 2)],
            Runabout),
        new("flatbed_hauler", "Flatbed Hauler",
            [Gone("gas_engine", 0.9f), Gone("wheel", 0.85f, 1, 3), Gone("gas_container", 0.5f)], Flatbed),
        new("old_tractor", "Old Tractor",
            [Gone("scrap_gas_engine", 0.9f), Gone("big_wheel", 0.5f), Gone("scrap_wheel", 0.4f), Gone("controller", 0.5f)], Tractor),
        new("farm_trailer", "Farm Trailer", [Gone("wheel", 0.8f, 1, 2)], Trailer),
        new("rover_frame", "Rover Frame",
            [Gone("electric_engine", 0.9f), Gone("battery", 0.8f), Gone("wheel", 0.5f), Gone("headlight", 0.5f, 1, 2)], Rover),
    ];

    public override void _Ready()
    {
        var content = ModPaths.LoadContent();
        var folder = System.IO.Path.Combine(ModPaths.ModsRoot, "vanilla", Derelicts.Folder);
        System.IO.Directory.CreateDirectory(folder);
        var failed = false;
        foreach (var design in Designs)
        {
            var creation = new Creation(0, content);
            try
            {
                design.Build(new Kit(creation));
            }
            catch (System.InvalidOperationException e)
            {
                GD.PushError($"[derelicts] {design.Id}: {e.Message}");
                failed = true;
                continue;
            }
            var shape = JsonNode.Parse(Blueprint.Write(creation, design.Name, System.DateTimeOffset.UnixEpoch))!["creation"]!.DeepClone();
            var file = new JsonObject
            {
                ["id"] = design.Id,
                ["name"] = design.Name,
                ["missing"] = JsonSerializer.SerializeToNode(design.Missing, DataJson.WriteOptions),
                ["creation"] = shape,
            };
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, $"{design.Id}.json"), file.ToJsonString(DataJson.WriteOptions) + "\n");
            GD.Print($"[derelicts] {design.Id}: {creation.Parts.Count} parts");
        }
        GetTree().Quit(failed ? 1 : 0);
    }

    // ---- designs: x across (0 is the left), y up (0 is the deck), z front (0) to back; seats and engines face -Z ----

    // The debug car's layout, left to rot: a scrap wood deck with a scrap metal bumper, seat, engine and Gas Container, and a
    // steered front axle.
    private static void ScrapBuggy(Kit k)
    {
        k.Box("scrap_wood_block", (0, 0, 1), (5, 0, 11));
        k.Box("scrap_metal_block", (0, 0, 0), (5, 0, 0));
        var seat = k.Place("scrap_drivers_seat", (1, 1, 3));
        var engine = k.Place("scrap_gas_engine", (0, 1, 9));
        var tank = k.Place("gas_container", (2, 1, 9));
        var steering = k.SteeredAxle(1, 6, "scrap_wheel");
        var driven = k.Axle(10, 6, "scrap_wheel");
        k.Drive(seat, engine, tank, steering, driven);
    }

    // A low scrap metal frame, a seat over the back axle and the engine behind it: an early win near camps.
    private static void GoKart(Kit k)
    {
        k.Box("scrap_metal_block", (0, 0, 0), (5, 0, 0));
        k.Box("scrap_metal_block", (0, 0, 1), (0, 0, 11));
        k.Box("scrap_metal_block", (5, 0, 1), (5, 0, 11));
        k.Box("scrap_metal_block", (1, 0, 1), (4, 0, 11));
        var seat = k.Place("scrap_drivers_seat", (1, 1, 2));
        var engine = k.Place("scrap_gas_engine", (0, 1, 8));
        var tank = k.Place("gas_container", (2, 1, 9));
        var steering = k.SteeredAxle(1, 6, "scrap_wheel");
        var driven = k.Axle(8, 6, "scrap_wheel");
        k.Drive(seat, engine, tank, steering, driven);
    }

    // A wooden cart with side boards on one axle, a seat at the front and a tongue to pull it by.
    private static void FarmCart(Kit k)
    {
        k.Box("wood_block", (0, 0, 2), (5, 0, 11));
        k.Box("wood_block", (0, 1, 5), (0, 1, 11));
        k.Box("wood_block", (5, 1, 5), (5, 1, 11));
        k.Box("wood_block", (1, 1, 11), (4, 1, 11));
        k.Box("scrap_wood_block", (2, 0, 0), (3, 0, 1));
        k.Place("scrap_seat", (1, 1, 2));
        k.Axle(8, 6, "scrap_wheel");
        // A prop under the front so it stands level on its one axle.
        k.Box("scrap_wood_block", (2, -1, 2), (3, -3, 2));
    }

    // A Colony Works car: metal body, glass windscreen, a real Gas Engine and wheels, headlights.
    private static void Runabout(Kit k)
    {
        k.Box("metal_block", (0, 0, 0), (5, 0, 13));
        k.Box("metal_block", (0, 1, 0), (5, 1, 2));
        k.Box("metal_block", (0, 1, 3), (0, 2, 12));
        k.Box("metal_block", (5, 1, 3), (5, 2, 12));
        k.Box("metal_block", (1, 1, 13), (4, 2, 13));
        k.Box("glass_block", (1, 2, 3), (4, 3, 3));
        k.Place("headlight", (0, 1, -1));
        k.Place("headlight", (5, 1, -1));
        var seat = k.Place("drivers_seat", (1, 1, 5));
        var engine = k.Place("gas_engine", (1, 1, 11));
        var tank = k.Place("gas_container", (1, 2, 0));
        var steering = k.SteeredAxle(2, 6, "wheel");
        var driven = k.Axle(11, 6, "wheel");
        k.Drive(seat, engine, tank, steering, driven);
    }

    // A long flatbed with a cab at the front and two rear axles.
    private static void Flatbed(Kit k)
    {
        k.Box("metal_block", (0, 0, 0), (5, 0, 21));
        k.Box("metal_block", (0, 1, 0), (5, 1, 0));
        k.Box("metal_block", (0, 1, 1), (0, 4, 6));
        k.Box("metal_block", (5, 1, 1), (5, 4, 6));
        k.Box("metal_block", (1, 1, 7), (4, 4, 7));
        k.Box("metal_block", (0, 5, 0), (5, 5, 7));
        k.Box("glass_block", (1, 2, 0), (4, 4, 0));
        var seat = k.Place("drivers_seat", (1, 1, 2));
        var engine = k.Place("gas_engine", (1, 1, 9));
        var tank = k.Place("gas_container", (3, 1, 9));
        var steering = k.SteeredAxle(2, 6, "wheel");
        var driven = k.Axle(15, 6, "wheel");
        k.Axle(19, 6, "wheel");
        k.Drive(seat, engine, tank, steering, driven);
    }

    // Big wheels at the back, small steered wheels at the front, an engine under a hood and a saddle.
    private static void Tractor(Kit k)
    {
        k.Box("metal_block", (1, 0, 0), (4, 0, 5));
        k.Box("metal_block", (0, 0, 6), (5, 0, 13));
        k.Box("metal_block", (1, 1, 0), (4, 2, 5));
        k.Place("exhaust_pipe", (4, 3, 2));
        var seat = k.Place("drivers_saddle", (1, 1, 7));
        k.Place("controller", (4, 1, 7));
        var engine = k.Place("scrap_gas_engine", (2, 3, 3));
        var tank = k.Place("gas_container", (1, 1, 11));
        var steering = k.SteeredAxle(1, 6, "scrap_wheel", depth: 3);
        var driven = k.Axle(9, 6, "big_wheel");
        k.Drive(seat, engine, tank, steering, driven);
    }

    // A wooden trailer with low sides, one axle and a hitch bar.
    private static void Trailer(Kit k)
    {
        k.Box("metal_block", (0, 0, 2), (5, 0, 11));
        k.Box("wood_block", (0, 1, 2), (0, 2, 11));
        k.Box("wood_block", (5, 1, 2), (5, 2, 11));
        k.Box("wood_block", (1, 1, 2), (4, 2, 2));
        k.Box("wood_block", (1, 1, 11), (4, 2, 11));
        k.Box("metal_block", (2, 0, 0), (3, 0, 1));
        k.Axle(7, 6, "wheel");
        k.Box("metal_block", (2, -1, 2), (3, -3, 2));
    }

    // A Salvager Tech rover chassis: hull plate frame, electric drive on a Battery, a roll bar and lamps.
    private static void Rover(Kit k)
    {
        k.Box("hull_plate_block", (0, 0, 0), (5, 0, 11));
        k.Box("hull_plate_block", (0, 1, 4), (0, 3, 4));
        k.Box("hull_plate_block", (5, 1, 4), (5, 3, 4));
        k.Box("hull_plate_block", (0, 4, 4), (5, 4, 4));
        k.Place("headlight", (1, 0, -1));
        k.Place("headlight", (4, 0, -1));
        var seat = k.Place("drivers_seat", (1, 1, 5));
        var engine = k.Place("electric_engine", (0, 1, 10));
        var battery = k.Place("battery", (2, 1, 9));
        var steering = k.SteeredAxle(1, 6, "wheel");
        var driven = k.Axle(10, 6, "wheel");
        k.Drive(seat, engine, battery, steering, driven);
    }

    /// <summary>Places parts on one Creation by cell, and builds axles the way the debug car does.</summary>
    private sealed class Kit(Creation creation)
    {
        public int Place(string part, (int X, int Y, int Z) cell, GridRotation rotation = default) =>
            creation.TryPlacePart($"vanilla:{part}", new GridCell(cell.X, cell.Y, cell.Z), rotation, out var placed, out var error)
                ? placed.Id
                : throw new System.InvalidOperationException($"{part} at {cell} does not fit: {error}");

        /// <summary>Fills the box of cells from <paramref name="from"/> to <paramref name="to"/>, each block once it has a neighbour to attach to.</summary>
        public void Box(string part, (int X, int Y, int Z) from, (int X, int Y, int Z) to)
        {
            var pending = new List<(int, int, int)>();
            for (var x = System.Math.Min(from.X, to.X); x <= System.Math.Max(from.X, to.X); x++)
            {
                for (var y = System.Math.Min(from.Y, to.Y); y <= System.Math.Max(from.Y, to.Y); y++)
                {
                    for (var z = System.Math.Min(from.Z, to.Z); z <= System.Math.Max(from.Z, to.Z); z++)
                        pending.Add((x, y, z));
                }
            }
            // The first part of a Creation attaches to nothing; every other one needs a neighbour, so fill outwards.
            while (pending.Count > 0)
            {
                var placed = pending.Where(cell => creation.TryPlacePart($"vanilla:{part}", new GridCell(cell.Item1, cell.Item2, cell.Item3),
                    GridRotation.Identity, out _, out _)).ToList();
                if (placed.Count == 0)
                    throw new System.InvalidOperationException($"{part} from {from} to {to} does not attach to the rest at {pending[0]}.");
                pending.RemoveAll(placed.Contains);
            }
        }

        /// <summary>
        /// A rigid axle across the full width (posts down from the deck at both sides, a beam <paramref name="depth"/> cells
        /// under it) with a bearing and a wheel at each end. Returns the two bearings.
        /// </summary>
        public List<int> Axle(int z, int width, string wheel, int depth = 2)
        {
            for (var y = -1; y > -depth; y--)
            {
                Place("scrap_metal_block", (0, y, z));
                Place("scrap_metal_block", (width - 1, y, z));
            }
            Box("scrap_metal_block", (0, -depth, z), (width - 1, -depth, z));
            return Wheels(z, width, wheel, -depth);
        }

        /// <summary>A front axle that turns on an upright bearing under the middle of the deck. Returns that bearing.</summary>
        public int SteeredAxle(int z, int width, string wheel, int depth = 2)
        {
            var middle = width / 2 - 1;
            var steering = Place("bearing", (middle, -1, z), Facing(new GridCell(0, -1, 0)));
            for (var y = -2; y > -depth; y--)
                Place("scrap_metal_block", (middle, y, z));
            Place("scrap_metal_block", (middle, -depth, z));
            for (var x = middle - 1; x >= 0; x--)
                Place("scrap_metal_block", (x, -depth, z));
            for (var x = middle + 1; x < width; x++)
                Place("scrap_metal_block", (x, -depth, z));
            Wheels(z, width, wheel, -depth);
            return steering;
        }

        private List<int> Wheels(int z, int width, string wheel, int y)
        {
            var bearings = new List<int>();
            var half = wheel == "big_wheel" ? 2 : 1;
            foreach (var side in new[] { -1, 1 })
            {
                var outward = new GridCell(side, 0, 0);
                var cell = new GridCell(side < 0 ? -1 : width, y, z);
                var rotation = Facing(outward);
                bearings.Add(Place("bearing", (cell.X, cell.Y, cell.Z), rotation));
                var at = cell + outward - rotation.Apply(new GridCell(half, 0, half));
                Place(wheel, (at.X, at.Y, at.Z), rotation);
            }
            return bearings;
        }

        /// <summary>Wires it to drive: the seat to the engine and the steering, the tank to the engine, the engine to the driven bearings.</summary>
        public void Drive(int seat, int engine, int tank, int steering, IEnumerable<int> driven)
        {
            Link(seat, engine);
            Link(seat, steering);
            Link(tank, engine);
            foreach (var bearing in driven)
                Link(engine, bearing);
        }

        private void Link(int from, int to)
        {
            if (!creation.TryLink(from, to, out var error))
                throw new System.InvalidOperationException($"cannot link part {from} to {to}: {error}");
        }

        // Local +Y turned to point along outward.
        private static GridRotation Facing(GridCell outward) =>
            outward.Y != 0 ? GridRotation.FromAxes(new GridCell(1, 0, 0), outward) : GridRotation.FromAxes(new GridCell(0, -outward.X - outward.Z, 0), outward);
    }
}
