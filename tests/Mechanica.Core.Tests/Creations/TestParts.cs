using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Creations;

/// <summary>Part definitions for graph tests, built in code.</summary>
internal static class TestParts
{
    public const string Block = "test:block";
    public const string Beam = "test:beam";
    public const string Bearing = "test:bearing";
    public const string Capped = "test:capped";
    public const string Piston = "test:piston";
    public const string Ore = "test:ore";
    public const string SocketedBlock = "test:socketed_block";
    public const string Soil = "test:soil";

    public static PartCatalog Catalog() => new PartCatalog()
        // No sockets: attaches on every face.
        .Add(Block, Part(PartCategory.Block, mass: 4, size: GridSize.One))
        .Add(Beam, Part(PartCategory.Block, mass: 8, size: new GridSize(2, 1, 1)))
        // Base below (structural), rotor above (rotational), like the sample bearing.
        .Add(Bearing, Part(PartCategory.Interactive, mass: 3, size: GridSize.One,
            Socket("base", SocketType.Structural, new Vec3(0, -0.125f, 0), new Vec3(0, -1, 0)),
            Socket("rotor", SocketType.Rotational, new Vec3(0, 0.125f, 0), new Vec3(0, 1, 0))))
        // Not a block, so only top and bottom attach.
        .Add(Capped, Part(PartCategory.Interactive, mass: 4, size: GridSize.One,
            Socket("top", SocketType.Structural, new Vec3(0, 0.125f, 0), new Vec3(0, 1, 0)),
            Socket("bottom", SocketType.Structural, new Vec3(0, -0.125f, 0), new Vec3(0, -1, 0))))
        .Add(Piston, Part(PartCategory.Interactive, mass: 5, size: GridSize.One,
            Socket("base", SocketType.Structural, new Vec3(0, -0.125f, 0), new Vec3(0, -1, 0)),
            Socket("head", SocketType.Linear, new Vec3(0, 0.125f, 0), new Vec3(0, 1, 0))))
        // A block attaches on every face even when it lists sockets.
        .Add(SocketedBlock, Part(PartCategory.Block, mass: 4, size: GridSize.One,
            Socket("top", SocketType.Structural, new Vec3(0, 0.125f, 0), new Vec3(0, 1, 0))))
        // Ground-only, like farm soil: upright on the ground, never on a Creation.
        .Add(Soil, Part(PartCategory.Interactive, mass: 20, size: GridSize.One,
            Socket("base", SocketType.Structural, new Vec3(0, -0.125f, 0), new Vec3(0, -1, 0))) with { Name = "Soil", GroundOnly = true })
        .Add(Ore, new PartDefinition { Id = "ore", Name = "Ore", Category = PartCategory.Resource });

    /// <summary>Places a part or fails the test.</summary>
    public static PartInstance Place(this Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        Assert.True(creation.TryPlacePart(part, cell, rotation, out var placed, out var error), error);
        return placed;
    }

    public static Func<int> Ids(int first = 100)
    {
        var next = first;
        return () => next++;
    }

    private static PartDefinition Part(PartCategory category, float mass, GridSize size, params SocketDefinition[] sockets) => new()
    {
        Id = "x",
        Name = "X",
        Category = category,
        Mass = mass,
        Size = size,
        Sockets = sockets.Length == 0 ? null : [.. sockets],
    };

    private static SocketDefinition Socket(string id, SocketType type, Vec3 position, Vec3 normal) =>
        new() { Id = id, Type = type, Position = position, Normal = normal };
}
