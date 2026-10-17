namespace Mechanica.Core.World;

/// <summary>
/// Deterministic hashing and 2D gradient noise for world generation. It uses only operations IEEE 754
/// defines exactly (add, subtract, multiply, divide, floor, square root) and no transcendental functions,
/// so every peer regenerates the same world from the seed, bit for bit.
/// </summary>
public static class Noise
{
    /// <summary>Finalises a 32-bit hash (MurmurHash3's fmix32).</summary>
    public static uint Mix(uint h)
    {
        h ^= h >> 16;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        h *= 0xC2B2AE35u;
        h ^= h >> 16;
        return h;
    }

    /// <summary>Hash of a grid cell under a salt.</summary>
    public static uint Hash(uint salt, int x, int z) => Mix(Mix(salt ^ ((uint)x * 0x9E3779B1u)) ^ ((uint)z * 0x7FEB352Du));

    /// <summary>Hash of a grid cell and an extra index (for several values per cell).</summary>
    public static uint Hash(uint salt, int x, int z, int k) => Mix(Hash(salt, x, z) ^ ((uint)k * 0x68E31DA4u));

    /// <summary>A stream salt derived from a world seed.</summary>
    public static uint Salt(long seed, uint stream) => Mix((uint)seed ^ Mix((uint)(seed >> 32) ^ (stream * 0x27D4EB2Fu)));

    /// <summary>A stable hash of a string (FNV-1a over its UTF-16 code units).</summary>
    public static uint Hash(string text)
    {
        var h = 2166136261u;
        foreach (var c in text)
            h = (h ^ c) * 16777619u;
        return Mix(h);
    }

    /// <summary>A hash mapped to [0, 1).</summary>
    public static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

    /// <summary>2D gradient (Perlin) noise with unit cells, roughly in [-1, 1].</summary>
    public static float Gradient(float x, float z, uint salt)
    {
        var fx = MathF.Floor(x);
        var fz = MathF.Floor(z);
        var ix = (int)fx;
        var iz = (int)fz;
        var dx = x - fx;
        var dz = z - fz;
        var n00 = Dot(salt, ix, iz, dx, dz);
        var n10 = Dot(salt, ix + 1, iz, dx - 1f, dz);
        var n01 = Dot(salt, ix, iz + 1, dx, dz - 1f);
        var n11 = Dot(salt, ix + 1, iz + 1, dx - 1f, dz - 1f);
        var u = Fade(dx);
        var v = Fade(dz);
        var a = n00 + (n10 - n00) * u;
        var b = n01 + (n11 - n01) * u;
        return a + (b - a) * v;
    }

    /// <summary>
    /// Fractal sum of <paramref name="octaves"/> layers of <see cref="Gradient"/>, each <paramref name="lacunarity"/>
    /// times the frequency and <paramref name="gain"/> times the amplitude of the last, normalised to roughly [-1, 1].
    /// </summary>
    public static float Fractal(float x, float z, uint salt, int octaves, float lacunarity, float gain)
    {
        float sum = 0f, norm = 0f, amplitude = 1f, frequency = 1f;
        for (var i = 0; i < octaves; i++)
        {
            sum += amplitude * Gradient(x * frequency, z * frequency, Mix(salt + (uint)i));
            norm += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }
        return sum / norm;
    }

    /// <summary>0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>, a smooth S-curve between (a step when they meet).</summary>
    public static float SmoothStep(float edge0, float edge1, float x)
    {
        if (edge1 <= edge0)
            return x <= edge0 ? 0f : 1f;
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Dot(uint salt, int x, int z, float dx, float dz) => (Hash(salt, x, z) & 7) switch
    {
        0 => dx + dz,
        1 => -dx + dz,
        2 => dx - dz,
        3 => -dx - dz,
        4 => dx,
        5 => -dx,
        6 => dz,
        _ => -dz,
    };
}
