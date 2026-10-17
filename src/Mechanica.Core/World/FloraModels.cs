using System.Numerics;

namespace Mechanica.Core.World;

/// <summary>
/// A flora mesh as flat-shaded triangles (three vertices each, no index), so the low-poly facets read like the
/// concept art. Vertex data the flora shader reads:
/// <list type="bullet">
/// <item><see cref="Colors"/>: R is the shade (dark at the root, light at the tip), G how far the colour turns to
/// the yellow accent of a blade tip, A is 1 for leaves (they take the plant's tint) and 0 for flowers and bolts,
/// whose RGB is their own colour.</item>
/// <item><see cref="Uvs"/>: X is the blade's or lump's rank (0..1): far away the shader drops the higher ranks first,
/// so a tuft thins out instead of popping. Y is the height share (0 root .. 1 top), which scales the wind sway.</item>
/// </list>
/// </summary>
public sealed class FloraMesh
{
    internal FloraMesh(Vector3[] positions, Vector3[] normals, Vector4[] colors, Vector2[] uvs)
    {
        Positions = positions;
        Normals = normals;
        Colors = colors;
        Uvs = uvs;
        Min = positions.Length == 0 ? Vector3.Zero : positions.Aggregate(Vector3.Min);
        Max = positions.Length == 0 ? Vector3.Zero : positions.Aggregate(Vector3.Max);
    }

    /// <summary>Vertex positions in metres, the plant's ground point at the origin, +Y up.</summary>
    public Vector3[] Positions { get; }

    /// <summary>Unit face normals, one per vertex.</summary>
    public Vector3[] Normals { get; }

    /// <summary>Shade, accent and tint mask (see the class summary).</summary>
    public Vector4[] Colors { get; }

    /// <summary>Rank and height share (see the class summary).</summary>
    public Vector2[] Uvs { get; }

    /// <summary>Number of triangles.</summary>
    public int Triangles => Positions.Length / 3;

    /// <summary>Lowest corner of the bounds.</summary>
    public Vector3 Min { get; }

    /// <summary>Highest corner of the bounds.</summary>
    public Vector3 Max { get; }
}

/// <summary>A built-in flora model: its full mesh and a lighter one for the middle distance.</summary>
/// <param name="Id">Id that <see cref="FloraRule.Models"/> and <see cref="WorldObjectDefinition.Model"/> use.</param>
/// <param name="Kind">The kind of flora it is.</param>
/// <param name="Mesh">The full mesh.</param>
/// <param name="Lod">The far mesh: about half the blades, or coarser lumps without small details.</param>
public sealed record FloraModel(string Id, FloraKind Kind, FloraMesh Mesh, FloraMesh Lod);

/// <summary>
/// The built-in flora models, generated in code so every peer and the tests get identical meshes: chunky,
/// faceted grass tufts, clover and flowers, and leafy bushes, after the Scrap Mechanic-style concept sheet
/// (bright greens, yellow tips, the odd bolt). Each model's ground point is at the origin.
/// </summary>
public static class FloraModels
{
    private static readonly Lazy<IReadOnlyList<FloraModel>> Models = new(Build);
    private static readonly Lazy<(Vector3[] Vertices, int[] Faces)[]> Spheres = new(() => [Icosphere(0), Icosphere(1)]);

    /// <summary>Every model, grass first, then ground cover, then bushes.</summary>
    public static IReadOnlyList<FloraModel> All => Models.Value;

    /// <summary>The model with this id, or null.</summary>
    public static FloraModel? Find(string? id) => All.FirstOrDefault(m => m.Id == id);

    /// <summary>The models of one kind, in <see cref="All"/> order.</summary>
    public static IReadOnlyList<FloraModel> OfKind(FloraKind kind) => [.. All.Where(m => m.Kind == kind)];

    private static IReadOnlyList<FloraModel> Build()
    {
        var list = new List<FloraModel>();
        void Add(string id, FloraKind kind, Action<Builder> build)
        {
            var seed = Noise.Hash(id);
            var full = new Builder(seed, lod: false);
            build(full);
            var lod = new Builder(seed, lod: true);
            build(lod);
            list.Add(new FloraModel(id, kind, full.ToMesh(), lod.ToMesh()));
        }

        // Grass tufts (concept sheet: short & dense, tall & narrow, wide & sweeping, mixed height, yellow-green tint).
        Add("grass_short", FloraKind.Grass, b => b.Tuft(blades: 16, radius: 0.12f, height: (0.16f, 0.28f), width: 0.055f, lean: 0.35f, tips: 0.1f));
        Add("grass_tall", FloraKind.Grass, b => b.Tuft(blades: 11, radius: 0.08f, height: (0.45f, 0.75f), width: 0.05f, lean: 0.2f, tips: 0.15f));
        Add("grass_wide", FloraKind.Grass, b => b.Tuft(blades: 13, radius: 0.14f, height: (0.3f, 0.5f), width: 0.06f, lean: 0.8f, tips: 0.1f));
        Add("grass_mixed", FloraKind.Grass, b => b.Tuft(blades: 14, radius: 0.13f, height: (0.18f, 0.62f), width: 0.05f, lean: 0.45f, tips: 0.2f));
        Add("grass_tipped", FloraKind.Grass, b => b.Tuft(blades: 12, radius: 0.11f, height: (0.3f, 0.55f), width: 0.055f, lean: 0.4f, tips: 0.7f));
        Add("grass_patch", FloraKind.Grass, b =>
        {
            // A dense clump of several small tufts, like the grass mats of the in-game reference.
            for (var k = 0; k < 4; k++)
            {
                var at = new Vector3(b.Range(-0.25f, 0.25f), 0f, b.Range(-0.25f, 0.25f));
                b.Tuft(blades: 9, radius: 0.08f, height: (0.2f, 0.42f), width: 0.05f, lean: 0.45f, tips: 0.2f, at: at);
            }
        });

        // Ground cover: clover and little flowers.
        Add("clover", FloraKind.GroundCover, b => b.Clover(leaves: 9, radius: 0.16f));
        Add("sprouts", FloraKind.GroundCover, b => b.Tuft(blades: 10, radius: 0.1f, height: (0.07f, 0.14f), width: 0.04f, lean: 0.6f, tips: 0.3f));
        Add("flowers_yellow", FloraKind.GroundCover, b => b.Flowers(stems: 4, new Vector3(1f, 0.84f, 0.16f), new Vector3(0.95f, 0.55f, 0.1f)));
        Add("flowers_white", FloraKind.GroundCover, b => b.Flowers(stems: 5, new Vector3(0.96f, 0.96f, 0.92f), new Vector3(1f, 0.8f, 0.2f)));
        Add("flowers_purple", FloraKind.GroundCover, b => b.Flowers(stems: 4, new Vector3(0.62f, 0.42f, 0.86f), new Vector3(1f, 0.85f, 0.35f)));

        // Bushes (concept sheet: round, spiky, low & spreading, flowering, cool green; and the red berry bush).
        Add("bush_round", FloraKind.Bush, b => b.Bush(lumps: 16, radius: 0.6f, height: 0.85f, bolts: 1));
        Add("bush_spiky", FloraKind.Bush, b =>
        {
            b.Lump(new Vector3(0f, 0.12f, 0f), new Vector3(0.28f, 0.18f, 0.28f), 0.55f, 0f, b.Rank());
            b.Tuft(blades: 26, radius: 0.22f, height: (0.6f, 1.05f), width: 0.11f, lean: 0.55f, tips: 0.25f);
        });
        Add("bush_low", FloraKind.Bush, b => b.Bush(lumps: 14, radius: 0.75f, height: 0.5f, bolts: 1));
        Add("bush_flowering", FloraKind.Bush, b =>
        {
            b.Bush(lumps: 15, radius: 0.6f, height: 0.8f, bolts: 0);
            b.FlowersOnBush(9, 0.6f, 0.8f, new Vector3(1f, 0.85f, 0.15f), new Vector3(0.9f, 0.55f, 0.1f));
        });
        Add("bush_lumpy", FloraKind.Bush, b => b.Bush(lumps: 22, radius: 0.62f, height: 0.85f, bolts: 0, tips: 0.9f));
        Add("bush_berry", FloraKind.Bush, b =>
        {
            b.Bush(lumps: 15, radius: 0.62f, height: 0.85f, bolts: 4);
            b.FlowersOnBush(12, 0.62f, 0.85f, new Vector3(0.92f, 0.2f, 0.12f), new Vector3(1f, 0.45f, 0.12f));
        });
        return list;
    }

    // An icosahedron, subdivided `level` times and pushed onto the unit sphere.
    private static (Vector3[] Vertices, int[] Faces) Icosphere(int level)
    {
        var t = (1f + MathF.Sqrt(5f)) / 2f;
        var vertices = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (var i = 0; i < vertices.Count; i++)
            vertices[i] = Vector3.Normalize(vertices[i]);
        var faces = new List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        for (var l = 0; l < level; l++)
        {
            var mid = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (!mid.TryGetValue(key, out var m))
                {
                    mid[key] = m = vertices.Count;
                    vertices.Add(Vector3.Normalize(vertices[a] + vertices[b]));
                }
                return m;
            }
            var next = new List<int>();
            for (var f = 0; f < faces.Count; f += 3)
            {
                int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange([a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca]);
            }
            faces = next;
        }
        return ([.. vertices], [.. faces]);
    }

    private sealed class Builder(uint seed, bool lod)
    {
        private readonly List<Vector3> _positions = [];
        private readonly List<Vector3> _normals = [];
        private readonly List<Vector4> _colors = [];
        private readonly List<Vector2> _uvs = [];
        private int _draw;
        private int _lumps;

        public float Next() => Noise.Unit(Noise.Hash(seed, _draw++, 0));

        public float Range(float min, float max) => min + (max - min) * Next();

        public float Rank() => Next();

        // Grass blades fanning out from a root circle: each blade a folded strip (two faces meeting at a midrib)
        // that tapers to a point and curves outwards. The far mesh keeps half the blades, a little wider.
        public void Tuft(int blades, float radius, (float Min, float Max) height, float width, float lean, float tips, Vector3 at = default)
        {
            for (var k = 0; k < blades; k++)
            {
                var angle = (k + Range(-0.3f, 0.3f)) * (2f * MathF.PI / blades);
                var distance = radius * MathF.Sqrt(Range(0.05f, 1f));
                var outward = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
                var root = at + outward * distance;
                var h = Range(height.Min, height.Max);
                var bend = lean * Range(0.5f, 1.2f) * (0.4f + distance / radius);
                var w = width * Range(0.8f, 1.2f);
                var rank = Next();
                var accent = Next() < tips ? 1f : 0f;
                var twist = Range(-0.5f, 0.5f);
                if (lod && rank >= 0.5f)
                    continue;
                Blade(root, outward, h, lod ? w * 1.4f : w, bend, twist, accent, rank);
            }
        }

        private void Blade(Vector3 root, Vector3 outward, float height, float width, float bend, float twist, float accent, float rank)
        {
            // Turn the facing a little so blades don't all face the centre.
            var facing = Vector3.Normalize(outward + new Vector3(-outward.Z, 0f, outward.X) * twist);
            var side = new Vector3(-facing.Z, 0f, facing.X);
            ReadOnlySpan<float> levels = [0f, 0.55f];
            Span<Vector3> left = stackalloc Vector3[2], rib = stackalloc Vector3[2], right = stackalloc Vector3[2];
            Span<float> shade = stackalloc float[2];
            for (var i = 0; i < levels.Length; i++)
            {
                var t = levels[i];
                var centre = root + Vector3.UnitY * (height * t) + facing * (bend * height * t * t);
                var half = width * 0.5f * (1f - t * t * 0.7f);
                left[i] = centre - side * half;
                right[i] = centre + side * half;
                rib[i] = centre + facing * (half * 0.45f);
                shade[i] = 0.5f + 0.4f * t;
            }
            var tip = root + Vector3.UnitY * height + facing * (bend * height);
            for (var i = 0; i < levels.Length - 1; i++)
            {
                Quad(left[i], rib[i], rib[i + 1], left[i + 1], facing, shade[i], shade[i + 1], accent * levels[i], accent * levels[i + 1], rank);
                Quad(rib[i], right[i], right[i + 1], rib[i + 1], facing, shade[i], shade[i + 1], accent * levels[i], accent * levels[i + 1], rank);
            }
            const int last = 1;
            Leaf(left[last], rib[last], tip, facing, shade[last], 1f, accent * 0.75f, accent, rank);
            Leaf(rib[last], right[last], tip, facing, shade[last], 1f, accent * 0.75f, accent, rank);
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, float shadeLow, float shadeHigh, float accentLow, float accentHigh, float rank)
        {
            Tri(a, b, c, outward, new(shadeLow, accentLow, 0, 1), new(shadeLow, accentLow, 0, 1), new(shadeHigh, accentHigh, 0, 1), rank);
            Tri(a, c, d, outward, new(shadeLow, accentLow, 0, 1), new(shadeHigh, accentHigh, 0, 1), new(shadeHigh, accentHigh, 0, 1), rank);
        }

        private void Leaf(Vector3 a, Vector3 b, Vector3 tip, Vector3 outward, float shadeLow, float shadeHigh, float accentLow, float accentHigh, float rank) =>
            Tri(a, b, tip, outward, new(shadeLow, accentLow, 0, 1), new(shadeLow, accentLow, 0, 1), new(shadeHigh, accentHigh, 0, 1), rank);

        // A jittered icosphere lump, shaded darker underneath. `tips` turns its upper outward face to the accent.
        public void Lump(Vector3 centre, Vector3 size, float shade, float tips, float rank, Vector4? color = null, float jitter = 0.22f)
        {
            var (vertices, faces) = Spheres.Value[lod ? 0 : 1];
            var moved = new Vector3[vertices.Length];
            var salt = (uint)_lumps++ * 0x9E3779B1u;
            for (var i = 0; i < vertices.Length; i++)
                moved[i] = centre + vertices[i] * size * (1f + jitter * (Noise.Unit(Noise.Hash(seed ^ salt, i, 7)) - 0.5f));
            for (var f = 0; f < faces.Length; f += 3)
            {
                Vector3 a = moved[faces[f]], b = moved[faces[f + 1]], c = moved[faces[f + 2]];
                var mid = (a + b + c) / 3f;
                var outward = mid - centre;
                if (color is { } own)
                {
                    Tri(a, b, c, outward, own, own, own, rank);
                    continue;
                }
                var up = Vector3.Normalize(outward).Y;
                var s = Math.Clamp(shade + 0.25f * up, 0.3f, 1f);
                var accent = tips * Noise.SmoothStep(0.35f, 0.85f, up);
                var v = new Vector4(s, accent, 0, 1);
                Tri(a, b, c, outward, v, v, v, rank);
            }
        }

        // A dome of leafy lumps round a core, lighter towards the top, with a few grey bolts for the mechanical touch.
        public void Bush(int lumps, float radius, float height, int bolts, float tips = 0.15f)
        {
            Lump(new Vector3(0f, height * 0.45f, 0f), new Vector3(radius * 0.75f, height * 0.5f, radius * 0.75f), 0.45f, 0f, 0f);
            var surface = new List<(Vector3 At, Vector3 Normal)>();
            for (var k = 0; k < lumps; k++)
            {
                // Spread evenly over the dome (golden angle), then jitter.
                var y = 0.1f + 0.9f * (k + 0.5f) / lumps;
                var ring = MathF.Sqrt(1f - y * y);
                var angle = k * 2.39996323f + Range(-0.3f, 0.3f);
                var dir = new Vector3(ring * MathF.Cos(angle), y, ring * MathF.Sin(angle));
                var centre = new Vector3(dir.X * radius * 0.78f, height * (0.2f + 0.62f * y), dir.Z * radius * 0.78f);
                var r = radius * Range(0.28f, 0.42f);
                var size = new Vector3(r, r * Range(0.75f, 1f), r);
                var rank = Next();
                Lump(centre, size, 0.5f + 0.35f * y + Range(-0.06f, 0.06f), tips, rank);
                surface.Add((centre + Vector3.Normalize(dir) * r * 0.85f, Vector3.Normalize(dir)));
            }
            for (var k = 0; k < bolts; k++)
            {
                var (at, normal) = surface[(int)(Next() * surface.Count) % surface.Count];
                if (!lod)
                    Bolt(at, normal);
            }
        }

        // A hex nut with a bolt end poking out of it, raw grey.
        private void Bolt(Vector3 at, Vector3 normal)
        {
            var steel = new Vector4(0.56f, 0.58f, 0.62f, 0);
            var dark = new Vector4(0.36f, 0.37f, 0.4f, 0);
            Prism(at, normal, 6, 0.05f, 0.035f, steel);
            Prism(at + normal * 0.035f, normal, 6, 0.022f, 0.025f, dark);
        }

        private void Prism(Vector3 at, Vector3 axis, int sides, float radius, float length, Vector4 color)
        {
            var u = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
            var v = Vector3.Cross(axis, u);
            var top = at + axis * length;
            for (var i = 0; i < sides; i++)
            {
                float a0 = i * 2f * MathF.PI / sides, a1 = (i + 1) * 2f * MathF.PI / sides;
                var p0 = (u * MathF.Cos(a0) + v * MathF.Sin(a0)) * radius;
                var p1 = (u * MathF.Cos(a1) + v * MathF.Sin(a1)) * radius;
                var outward = p0 + p1;
                Tri(at + p0, at + p1, top + p1, outward, color, color, color, 0f);
                Tri(at + p0, top + p1, top + p0, outward, color, color, color, 0f);
                Tri(top, top + p0, top + p1, axis, color, color, color, 0f);
            }
        }

        // Round leaves on short stalks, lying low and overlapping.
        public void Clover(int leaves, float radius)
        {
            for (var k = 0; k < leaves; k++)
            {
                var angle = k * 2.39996323f;
                var d = radius * MathF.Sqrt((k + 0.5f) / leaves);
                var at = new Vector3(d * MathF.Cos(angle), Range(0.03f, 0.08f), d * MathF.Sin(angle));
                var rank = Next();
                var size = Range(0.045f, 0.065f) * (lod ? 1.3f : 1f);
                if (lod && rank >= 0.5f)
                    continue;
                Lump(at, new Vector3(size, size * 0.35f, size), 0.65f, 0.1f, rank, jitter: 0.3f);
            }
        }

        // Thin stems with five-petal heads, and a few leaves at the base.
        public void Flowers(int stems, Vector3 petal, Vector3 middle)
        {
            Tuft(blades: 6, radius: 0.06f, height: (0.08f, 0.14f), width: 0.045f, lean: 0.7f, tips: 0f);
            for (var k = 0; k < stems; k++)
            {
                var angle = k * 2.39996323f + Range(-0.4f, 0.4f);
                var d = Range(0.02f, 0.1f);
                var root = new Vector3(d * MathF.Cos(angle), 0f, d * MathF.Sin(angle));
                var h = Range(0.16f, 0.3f);
                var outward = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
                var rank = Next();
                var size = Range(0.035f, 0.05f);
                if (lod && rank >= 0.6f)
                    continue;
                Blade(root, outward, h, 0.018f, 0.15f, 0f, 0f, rank);
                Blossom(root + Vector3.UnitY * h + outward * (0.15f * h), size, petal, middle, rank);
            }
        }

        // Flowers scattered over the top half of a bush.
        public void FlowersOnBush(int count, float radius, float height, Vector3 petal, Vector3 middle)
        {
            for (var k = 0; k < count; k++)
            {
                var y = 0.35f + 0.6f * (k + 0.5f) / count;
                var ring = MathF.Sqrt(1f - y * y);
                var angle = k * 2.39996323f + 1.1f;
                var at = new Vector3(ring * MathF.Cos(angle) * radius * 1.02f, height * (0.25f + 0.75f * y), ring * MathF.Sin(angle) * radius * 1.02f);
                Blossom(at, Range(0.06f, 0.085f), petal, middle, Next());
            }
        }

        private void Blossom(Vector3 at, float size, Vector3 petal, Vector3 middle, float rank)
        {
            var spin = Range(0f, 2f * MathF.PI);
            var petals = lod ? 0 : 5;
            for (var p = 0; p < petals; p++)
            {
                var a = spin + p * (2f * MathF.PI / 5f);
                var offset = new Vector3(MathF.Cos(a), 0.1f, MathF.Sin(a)) * size * 0.75f;
                var tone = p % 2 == 0 ? petal : Vector3.Lerp(petal, middle, 0.35f);
                Lump(at + offset, new Vector3(size * 0.55f, size * 0.28f, size * 0.55f), 1f, 0f, rank, new Vector4(tone, 0f), 0.15f);
            }
            var heart = lod ? size * 0.9f : size * 0.4f;
            Lump(at + Vector3.UnitY * (size * 0.12f), new Vector3(heart, heart * 0.7f, heart), 1f, 0f, rank, new Vector4(lod ? petal : middle, 0f), 0.1f);
        }

        // Adds a triangle facing `outward` (Godot's front faces wind clockwise seen from the front).
        private void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Vector4 ca, Vector4 cb, Vector4 cc, float rank)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() < 1e-14f)
                return;
            if (Vector3.Dot(normal, outward) > 0f)
            {
                (b, c) = (c, b);
                (cb, cc) = (cc, cb);
                normal = -normal;
            }
            normal = -Vector3.Normalize(normal);
            foreach (var (p, color) in new[] { (a, ca), (b, cb), (c, cc) })
            {
                _positions.Add(p);
                _normals.Add(normal);
                _colors.Add(color);
                _uvs.Add(new Vector2(rank, 0f));
            }
        }

        public FloraMesh ToMesh()
        {
            var top = _positions.Count == 0 ? 1f : MathF.Max(_positions.Max(p => p.Y), 0.01f);
            var uvs = _uvs.Select((uv, i) => new Vector2(uv.X, Math.Clamp(_positions[i].Y / top, 0f, 1f))).ToArray();
            return new FloraMesh([.. _positions], [.. _normals], [.. _colors], uvs);
        }
    }
}
