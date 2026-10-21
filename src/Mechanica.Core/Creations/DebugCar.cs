using System.Text.Json;
using Mechanica.Core.Data;

namespace Mechanica.Core.Creations;

/// <summary>
/// The debug scrap car's settings (<c>game/data/debug_car.json</c>): whether a new world in a debug build gets the car,
/// where it stands from the player spawn, which way it faces, how much gasoline its Gas Container holds and the
/// engine's power. Release exports never spawn it.
/// </summary>
public sealed record DebugCarSettings
{
    /// <summary>Whether a new world in a debug build gets the car.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Where the car's middle stands from the player spawn, in metres (x east, z south); its wheels go on the ground.</summary>
    public float[] Offset { get; init; } = [4f, 0f, -8f];

    /// <summary>Degrees the car turns from facing north (-Z), counter-clockwise seen from above.</summary>
    public float Yaw { get; init; }

    /// <summary>Gasoline the car's Gas Container starts with.</summary>
    public int Gasoline { get; init; } = 40;

    /// <summary>The engine's power setting (the Scrap Gas Engine has 3; 3 is its full torque, every step turns its top speed).</summary>
    public int Gear { get; init; } = 3;

    /// <summary>Whether the engine starts switched on.</summary>
    public bool EngineOn { get; init; } = true;

    /// <summary>Reads <paramref name="json"/>; throws <see cref="FormatException"/> on bad data.</summary>
    public static DebugCarSettings Parse(string json)
    {
        DebugCarSettings? settings;
        try
        {
            settings = DataJson.Read<DebugCarSettings>(json);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid debug car JSON: {e.Message}", e);
        }
        settings ??= new DebugCarSettings();
        if (settings.Offset is not { Length: 3 } || !settings.Offset.All(float.IsFinite) || !float.IsFinite(settings.Yaw))
            throw new FormatException("offset must be [x, y, z] and yaw a finite number.");
        if (settings.Gasoline < 0 || settings.Gear < 1)
            throw new FormatException("gasoline must not be negative and gear must be at least 1.");
        return settings;
    }
}

/// <summary>The parts of a built debug car that get wired up or filled.</summary>
/// <param name="Seat">The Scrap Driver's Seat.</param>
/// <param name="Engine">The Scrap Gas Engine.</param>
/// <param name="Container">The Gas Container.</param>
/// <param name="Steering">The upright bearing the front axle turns on.</param>
/// <param name="Driven">The rear wheel bearings the engine turns.</param>
/// <param name="Free">The front wheel bearings, which roll free.</param>
public sealed record DebugCarParts(int Seat, int Engine, int Container, int Steering, IReadOnlyList<int> Driven, IReadOnlyList<int> Free);

/// <summary>
/// A working car of scrap parts for driving round while the game is in development (debug builds only): a 6 by 12
/// block Scrap Wood deck with a Scrap Metal bumper, a Scrap Driver's Seat, a Scrap Gas Engine behind it driving the rear
/// wheels, a Gas Container beside the engine, and four Scrap Wheels on bearings. The front axle turns on an upright
/// bearing the seat steers, and the Gas Container is linked to the engine, which burns its gasoline. The deck runs along Z with the front at z = 0, facing -Z the way the seat and engine face.
/// </summary>
public static class DebugCar
{
    /// <summary>Deck width in cells (x 0..5).</summary>
    public const int Width = 6;

    /// <summary>Deck length in cells (z 0..11).</summary>
    public const int Length = 12;

    /// <summary>The cell row of the front and rear axles.</summary>
    public const int FrontAxle = 1, RearAxle = 10;

    /// <summary>
    /// The cell under the middle of the car at wheel-bottom height: the deck's centre, two cells down for the axles and
    /// one and a half for the wheel radius.
    /// </summary>
    public static Vec3 GroundPoint => new(MechanicaInfo.FromGridCell(Width - 1) / 2f, MechanicaInfo.FromGridCell(-2) - 1.5f * MechanicaInfo.GridSize,
        MechanicaInfo.FromGridCell(Length - 1) / 2f);

    /// <summary>
    /// Builds the car into <paramref name="car"/> (an empty Creation), links the seat to the engine and the steering
    /// bearing, the Gas Container to the engine and the engine to the rear wheel bearings, sets the engine's power and switch, and fills the Gas Container.
    /// Throws when a part does not fit or link, which means the vanilla parts changed shape.
    /// </summary>
    public static DebugCarParts Build(Creation car, DebugCarSettings settings)
    {
        for (var x = 0; x < Width; x++)
        {
            for (var z = 0; z < Length; z++)
                Place(car, z == 0 ? "vanilla:scrap_metal_block" : "vanilla:scrap_wood_block", new GridCell(x, 0, z));
        }
        var seat = Place(car, "vanilla:scrap_drivers_seat", new GridCell(1, 1, 3));
        var engine = Place(car, "vanilla:scrap_gas_engine", new GridCell(0, 1, 9));
        var container = Place(car, "vanilla:gas_container", new GridCell(2, 1, 9));

        // The front axle: a Scrap Metal beam hanging off an upright bearing under the deck.
        var steering = Place(car, "vanilla:bearing", new GridCell(2, -1, FrontAxle), Facing(new GridCell(0, -1, 0)));
        Place(car, "vanilla:scrap_metal_block", new GridCell(2, -2, FrontAxle));
        foreach (var x in new[] { 1, 0, 3, 4, 5 })
            Place(car, "vanilla:scrap_metal_block", new GridCell(x, -2, FrontAxle));
        // The rear axle: a beam on two posts down from the deck.
        foreach (var x in new[] { 0, Width - 1 })
            Place(car, "vanilla:scrap_metal_block", new GridCell(x, -1, RearAxle));
        for (var x = 0; x < Width; x++)
            Place(car, "vanilla:scrap_metal_block", new GridCell(x, -2, RearAxle));

        var free = new List<int>();
        var driven = new List<int>();
        foreach (var z in new[] { FrontAxle, RearAxle })
        {
            foreach (var side in new[] { -1, 1 })
            {
                var outward = new GridCell(side, 0, 0);
                var bearing = new GridCell(side < 0 ? -1 : Width, -2, z);
                var rotation = Facing(outward);
                (z == FrontAxle ? free : driven).Add(Place(car, "vanilla:bearing", bearing, rotation));
                Place(car, "vanilla:scrap_wheel", bearing + outward - rotation.Apply(new GridCell(1, 0, 1)), rotation);
            }
        }

        Link(car, seat, engine);
        Link(car, seat, steering);
        Link(car, container, engine);
        foreach (var bearing in driven)
            Link(car, engine, bearing);
        car.SetPartSettings(engine, new Dictionary<string, JsonElement>
        {
            ["gear"] = JsonSerializer.SerializeToElement(settings.Gear),
            ["on"] = JsonSerializer.SerializeToElement(settings.EngineOn),
        });
        if (settings.Gasoline > 0)
            car.InventoryOf(container)!.Add("vanilla:gasoline", settings.Gasoline);
        return new DebugCarParts(seat, engine, container, steering, driven, free);
    }

    // Local +Y turned to point along outward.
    private static GridRotation Facing(GridCell outward) =>
        outward.Y != 0 ? GridRotation.FromAxes(new GridCell(1, 0, 0), outward) : GridRotation.FromAxes(new GridCell(0, -outward.X - outward.Z, 0), outward);

    private static int Place(Creation car, string part, GridCell cell, GridRotation rotation = default) =>
        car.TryPlacePart(part, cell, rotation, out var placed, out var error)
            ? placed.Id
            : throw new InvalidOperationException($"The debug car's {part} at {cell} does not fit: {error}");

    private static void Link(Creation car, int from, int to)
    {
        if (!car.TryLink(from, to, out var error))
            throw new InvalidOperationException($"The debug car cannot link part {from} to {to}: {error}");
    }
}
