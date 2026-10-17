using System.Numerics;
using Mechanica.Core.Creations;

namespace Mechanica.Core.World;

/// <summary>
/// Footprint boxes of a Creation's Bodies, used to find the chunks it touches. Each Body's box is taken
/// in the Creation's grid frame from its parts' cells, so it moves with the Body's pose. Collision shapes
/// that reach past their part's cells are covered by <see cref="StreamingSettings.CreationBuffer"/>.
/// </summary>
public static class CreationBounds
{
    /// <summary>Grid-frame box of each Body, in <see cref="Creation.Bodies"/> order.</summary>
    public static IReadOnlyList<WorldBounds> LocalBodyBounds(Creation creation)
    {
        var half = new Vector3(MechanicaInfo.GridSize / 2f);
        var result = new List<WorldBounds>(creation.Bodies.Count);
        foreach (var body in creation.Bodies)
        {
            var box = WorldBounds.Empty;
            foreach (var id in body.PartIds)
            {
                foreach (var cell in creation.CellsOf(id))
                {
                    var centre = cell.Metres.ToNumerics();
                    box = box.Union(new WorldBounds(centre - half, centre + half));
                }
            }
            result.Add(box);
        }
        return result;
    }

    /// <summary>World box of a Creation whose Bodies sit at <paramref name="poses"/> (same order as <paramref name="localBounds"/>).</summary>
    public static WorldBounds World(IReadOnlyList<WorldBounds> localBounds, IReadOnlyList<Pose> poses)
    {
        if (localBounds.Count != poses.Count)
            throw new ArgumentException($"{localBounds.Count} body boxes but {poses.Count} poses.", nameof(poses));
        var box = WorldBounds.Empty;
        for (var i = 0; i < poses.Count; i++)
            box = box.Union(localBounds[i].Transform(poses[i]));
        return box;
    }

    /// <summary>World box of a Creation at its stored Body poses.</summary>
    public static WorldBounds World(Creation creation) =>
        World(LocalBodyBounds(creation), [.. Enumerable.Range(0, creation.Bodies.Count).Select(creation.BodyPose)]);
}
