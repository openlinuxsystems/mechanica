using System.Numerics;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Actions;

/// <summary>Arguments of <c>SpawnPart</c>: a part to drop into the world as a new one-part Creation.</summary>
/// <param name="Part">Qualified part id (<c>mod_id:part_id</c>; a bare id means vanilla).</param>
/// <param name="Position">World position of the part's footprint centre, <c>[x, y, z]</c> metres.</param>
/// <param name="Yaw">Turn about the vertical axis in degrees.</param>
public sealed record SpawnPartArgs(string Part, float[] Position, float Yaw = 0);

/// <summary>
/// Host-side rules for the Creation actions the Test Sandbox and debug tools use (<c>SpawnPart</c>,
/// <c>ClearCreations</c>). The game registers them on its <see cref="ActionDispatcher"/>; validation
/// and Creation building live here so they are testable without Godot.
/// </summary>
public static class CreationActions
{
    /// <summary>Action name: spawn a part as a new Creation.</summary>
    public const string SpawnPart = "SpawnPart";

    /// <summary>Action name: remove every Creation.</summary>
    public const string ClearCreations = "ClearCreations";

    /// <summary>Most Creations the world may hold before spawning is refused.</summary>
    public const int MaxCreations = 256;

    /// <summary>How far from the world origin, horizontally, a part may be spawned, in metres.</summary>
    public const float MaxSpawnDistance = 2048;

    /// <summary>Lowest and highest spawn height, in metres.</summary>
    public const float MinSpawnHeight = -64, MaxSpawnHeight = 1024;

    /// <summary>Checks a spawn request. Returns null to accept, or the reason to refuse.</summary>
    public static string? ValidateSpawn(SpawnPartArgs args, IPartCatalog catalog, int creationCount)
    {
        if (args.Position is not { Length: 3 } p)
            return "position must be [x, y, z].";
        if (!p.All(float.IsFinite) || !float.IsFinite(args.Yaw))
            return "position and yaw must be finite numbers.";
        if (MathF.Sqrt(p[0] * p[0] + p[2] * p[2]) > MaxSpawnDistance)
            return $"position is more than {MaxSpawnDistance} m from the world centre.";
        if (p[1] < MinSpawnHeight || p[1] > MaxSpawnHeight)
            return $"height must be between {MinSpawnHeight} and {MaxSpawnHeight} m.";
        if (catalog.FindDefinition(args.Part) is not { } definition)
            return $"no part '{args.Part}' is loaded.";
        if (!definition.IsPlaceable)
            return $"'{args.Part}' is an inventory item, not a placeable part.";
        if (creationCount >= MaxCreations)
            return $"the world already holds {MaxCreations} Creations; clear some first.";
        return null;
    }

    /// <summary>
    /// A new one-part Creation for an accepted request, owned by the host, with its Body posed so the
    /// part's footprint centre sits at the requested position.
    /// </summary>
    public static Creation BuildSpawn(SpawnPartArgs args, int creationId, IPartCatalog catalog)
    {
        var creation = new Creation(creationId, catalog);
        if (!creation.TryPlacePart(args.Part, GridCell.Zero, GridRotation.Identity, out var placed, out var error))
            throw new InvalidOperationException(error);
        var centre = PartGeometry.CentreMetres(creation.DefinitionOf(placed.Id), GridCell.Zero, GridRotation.Identity).ToNumerics();
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, args.Yaw * MathF.PI / 180f);
        var position = new Vector3(args.Position[0], args.Position[1], args.Position[2]) - Vector3.Transform(centre, rotation);
        creation.SetAllBodyPoses(new Pose(position, rotation));
        return creation;
    }
}
