namespace Mechanica.Core.World;

/// <summary>What kind of water a <see cref="WaterBody"/> is.</summary>
public enum WaterBodyKind
{
    /// <summary>A pond or marsh above sea level: a POI pool of plain water.</summary>
    Pond,

    /// <summary>A river or creek channel.</summary>
    River,

    /// <summary>Open water at sea level: a lake or the sea (the body's id is the biome it lies in).</summary>
    Open,
}

/// <summary>The water over a world position (<see cref="WorldGenerator.WaterBodyAt"/>).</summary>
/// <param name="Kind">Pond, river or open water.</param>
/// <param name="Id">The pond's POI (<c>mod_id:poi_id</c>), <c>river</c>, or the biome id of open water (<c>lake</c>, <c>coastal</c>, ...).</param>
/// <param name="Surface">Height of the water surface.</param>
/// <param name="Depth">Metres from the surface down to the ground there.</param>
public readonly record struct WaterBody(WaterBodyKind Kind, string Id, float Surface, float Depth);
