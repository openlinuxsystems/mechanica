using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.World;

/// <summary>
/// The three kinds of decorative flora. Each has its own Foliage density setting (Options, Graphics), so a player
/// can keep the bushes and drop the grass or the other way round.
/// </summary>
public enum FloraKind
{
    /// <summary>Tufts of grass blades.</summary>
    Grass,

    /// <summary>Low plants under the grass: clover and small flowers.</summary>
    GroundCover,

    /// <summary>Leafy bushes.</summary>
    Bush,
}

/// <summary>
/// Decorative flora a biome grows (<c>flora</c> in a biome of <c>world/worldgen.json</c>): grass, ground cover and
/// bushes drawn with GPU instancing. Flora is decor only: it has no collider, so players, bots and Creations pass
/// through it, and it is never saved or synced (every peer grows the same from the seed). Densities are for the
/// High foliage level; the player's Foliage density setting scales them.
/// </summary>
public sealed record FloraRule
{
    /// <summary>Which kind, and so which Foliage density setting controls it.</summary>
    public FloraKind? Kind { get; init; }

    /// <summary>Plants per square metre where the patch is full, at the High foliage level.</summary>
    public float Density { get; init; }

    /// <summary>Share (0..1) of the biome's ground the plants' patches cover; 1 grows them everywhere.</summary>
    public float Coverage { get; init; } = 1f;

    /// <summary>Typical width of a patch in metres: the wavelength of the noise that decides where patches are.</summary>
    public float PatchSize { get; init; } = 12f;

    /// <summary>Random uniform scale range <c>[min, max]</c>. Omitted means 1.</summary>
    public List<float>? Scale { get; init; }

    /// <summary>Models to pick from at random (see <see cref="FloraModels"/>). Omitted means every model of the kind.</summary>
    public List<string>? Models { get; init; }

    /// <summary>Tints to pick from at random, <c>#rrggbb</c>; the models' leaves take the tint, flowers and bolts keep their own colour.</summary>
    public List<string>? Colors { get; init; }

    /// <summary>Steepest ground (rise over run) the plants grow on.</summary>
    public float MaxSlope { get; init; } = 1f;

    /// <summary>Unknown keys, kept so a typo gets a warning.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
