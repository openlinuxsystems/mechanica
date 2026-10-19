using System.Numerics;

namespace Mechanica.Core.World;

/// <summary>What a <see cref="RoadMeshSurface"/> is drawn with.</summary>
public enum RoadMaterial
{
    /// <summary>
    /// Asphalt with its markings drawn from the UVs: <c>uv.x</c> runs 0 (left edge) to 1 (right edge, seen along the road),
    /// <c>uv.y</c> is metres along it; <c>uv2</c> is metres to the junction plate at the start and at the end (stop lines),
    /// large where the road does not end at a junction, and -1 on a junction plate (no markings); a T junction's plate carries
    /// the markings of the road running through it (see <c>RoadMesher.Plate</c>).
    /// </summary>
    Asphalt,

    /// <summary>Light concrete blocks (curbs, bridge decks, parapets, piers): <c>uv</c> in metres along and up.</summary>
    Concrete,

    /// <summary>Yellow and black safety stripes (the posts at each end of a bridge): <c>uv</c> in metres.</summary>
    Hazard,

    /// <summary>Yellow sign face.</summary>
    Sign,

    /// <summary>Grey metal (sign poles).</summary>
    Metal,
}

/// <summary>Triangles of one material, in chunk-local metres.</summary>
public sealed class RoadMeshSurface
{
    internal RoadMeshSurface(RoadMaterial material) => Material = material;

    /// <summary>The material.</summary>
    public RoadMaterial Material { get; }

    /// <summary>True when the surface collides (everything but sign faces and poles).</summary>
    public bool Solid => Material is not (RoadMaterial.Sign or RoadMaterial.Metal);

    /// <summary>Vertex positions, three per triangle.</summary>
    public List<Vector3> Vertices { get; } = [];

    /// <summary>Vertex normals.</summary>
    public List<Vector3> Normals { get; } = [];

    /// <summary>First UV channel.</summary>
    public List<Vector2> Uv { get; } = [];

    /// <summary>Second UV channel (asphalt only; zero elsewhere).</summary>
    public List<Vector2> Uv2 { get; } = [];

    // Front faces are clockwise seen from their front (Godot's rule): the triangle turns so its normal points along n.
    internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 wa, Vector2 wb, Vector2 wc)
    {
        if (Vector3.Dot(Vector3.Cross(c - a, b - a), n) < 0f)
            (b, c, ub, uc, wb, wc) = (c, b, uc, ub, wc, wb);
        Vertices.Add(a);
        Vertices.Add(b);
        Vertices.Add(c);
        for (var k = 0; k < 3; k++)
            Normals.Add(n);
        Uv.Add(ua);
        Uv.Add(ub);
        Uv.Add(uc);
        Uv2.Add(wa);
        Uv2.Add(wb);
        Uv2.Add(wc);
    }

    // A quad a-b-c-d (in order round its edge) facing n.
    internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud,
        Vector2 wa = default, Vector2 wb = default, Vector2 wc = default, Vector2 wd = default)
    {
        Triangle(a, b, c, n, ua, ub, uc, wa, wb, wc);
        Triangle(a, c, d, n, ua, uc, ud, wa, wc, wd);
    }

    // An axis-free box: centre, half extents along its own right/up/forward axes; the top and four sides (no bottom).
    internal void Box(Vector3 centre, Vector3 right, Vector3 forward, float halfWidth, float halfDepth, float bottom, float top)
    {
        var up = Vector3.UnitY;
        var corners = new[] { -right * halfWidth - forward * halfDepth, right * halfWidth - forward * halfDepth, right * halfWidth + forward * halfDepth, -right * halfWidth + forward * halfDepth };
        Vector3 At(int k, float y) => new(centre.X + corners[k].X, y, centre.Z + corners[k].Z);
        Quad(At(0, top), At(1, top), At(2, top), At(3, top), up, new(0, 0), new(2 * halfWidth, 0), new(2 * halfWidth, 2 * halfDepth), new(0, 2 * halfDepth));
        for (var k = 0; k < 4; k++)
        {
            var next = (k + 1) % 4;
            var mid = (corners[k] + corners[next]) / 2f;
            var side = Vector3.Normalize(new Vector3(mid.X, 0f, mid.Z));
            var width = Vector3.Distance(At(k, 0), At(next, 0));
            Quad(At(k, bottom), At(next, bottom), At(next, top), At(k, top), side, new(0, bottom), new(width, bottom), new(width, top), new(0, top));
        }
    }
}

/// <summary>The roads in one chunk, ready to draw and collide with (empty when there are none).</summary>
public sealed class RoadMesh
{
    internal RoadMesh(IReadOnlyList<RoadMeshSurface> surfaces) => Surfaces = surfaces;

    /// <summary>No roads.</summary>
    public static RoadMesh Empty { get; } = new([]);

    /// <summary>Surfaces with at least one triangle.</summary>
    public IReadOnlyList<RoadMeshSurface> Surfaces { get; }

    /// <summary>True when there is nothing to draw.</summary>
    public bool IsEmpty => Surfaces.Count == 0;
}

/// <summary>
/// Builds the road geometry for one chunk from the <see cref="RoadNetwork"/> (World Map spec §3 and the road references):
/// asphalt ribbons with their markings, concrete curbs (where the roads have them), junction plates, and bridges with a deck, parapets, piers, hazard
/// posts and caution signs. Pure, so every peer and the far scenery build the same roads.
/// </summary>
public static class RoadMesher
{
    /// <summary>Height of a curb over the road surface, in metres.</summary>
    public const float CurbHeight = 0.15f;

    /// <summary>Height of a bridge parapet over the road surface, in metres.</summary>
    public const float ParapetHeight = 0.9f;

    /// <summary>Width of a bridge parapet in metres.</summary>
    public const float ParapetWidth = 0.5f;

    /// <summary>Depth of a bridge deck under the road surface, in metres.</summary>
    public const float DeckDepth = 1.2f;

    /// <summary>Metres the hazard posts on a bridge approach stand out from the asphalt's edge where there is no curb.</summary>
    public const float PostInset = 0.3f;

    /// <summary>Metres between bridge piers.</summary>
    public const float PierSpacing = 14f;

    // Value of uv2 where no stop line is anywhere near.
    private const float Far = 1000f;

    /// <summary>The road geometry whose pieces lie in the chunk [x0, x1) × [z0, z1), in metres relative to (x0, 0, z0).</summary>
    public static RoadMesh Build(WorldGenerator gen, float x0, float z0, float x1, float z1)
    {
        var roads = gen.Roads;
        if (roads.Chains.Count == 0 || roads.Definition is not { } def)
            return RoadMesh.Empty;
        var origin = new Vector3(x0, 0f, z0);
        var surfaces = Enum.GetValues<RoadMaterial>().ToDictionary(m => m, m => new RoadMeshSurface(m));
        bool Inside(Vector2 p) => p.X >= x0 && p.X < x1 && p.Y >= z0 && p.Y < z1;
        foreach (var chain in roads.ChainsNear(x0, z0, x1, z1, 2f))
        {
            if (chain.Kind != RoadKind.Road)
                continue;
            var from = roads.Nodes[chain.From];
            var to = roads.Nodes[chain.To];
            for (var i = 0; i + 1 < chain.Points.Length; i++)
            {
                if (!Inside((chain.Points[i] + chain.Points[i + 1]) / 2f))
                    continue;
                Segment(surfaces, chain, i, from, to, def, origin);
            }
            Bridges(surfaces, gen, chain, def, origin, Inside);
        }
        foreach (var node in roads.Nodes)
        {
            if (node.Kind == RoadNodeKind.Junction && Inside(node.At))
                Plate(surfaces[RoadMaterial.Asphalt], roads, node, def.Width, origin);
        }
        return new RoadMesh([.. surfaces.Values.Where(s => s.Vertices.Count > 0)]);
    }

    // Unit direction along the road at a point (the average of the segments either side).
    private static Vector2 Tangent(RoadChain chain, int i)
    {
        var a = chain.Points[Math.Max(0, i - 1)];
        var b = chain.Points[Math.Min(chain.Points.Length - 1, i + 1)];
        var d = b - a;
        return d.LengthSquared() > 1e-8f ? Vector2.Normalize(d) : Vector2.UnitX;
    }

    // The right-hand side seen along the road (x east, z south: heading north, the right is east).
    private static Vector2 Right(Vector2 tangent) => new(-tangent.Y, tangent.X);

    private static Vector3 At(Vector2 p, float y, Vector3 origin) => new(p.X - origin.X, y, p.Y - origin.Z);

    // True when a road leaving a junction towards `toward` is one of the two arms of a T that run straight through.
    private static bool Through(RoadNode node, Vector2 toward) => node.Side is var side and >= 0 && node.ArmToward(toward) != side;

    private static void Segment(Dictionary<RoadMaterial, RoadMeshSurface> surfaces, RoadChain chain, int i, RoadNode from, RoadNode to,
        RoadsDefinition def, Vector3 origin)
    {
        var hw = chain.Width / 2f;
        var (t0, t1) = (Tangent(chain, i), Tangent(chain, i + 1));
        var (r0, r1) = (Right(t0), Right(t1));
        var (p0, p1) = (chain.Points[i], chain.Points[i + 1]);
        var (y0, y1) = (chain.Heights[i] + RoadNetwork.SurfaceLift, chain.Heights[i + 1] + RoadNetwork.SurfaceLift);
        var (s0, s1) = (chain.Along[i], chain.Along[i + 1]);
        // Metres to the edge of the junction plate at each end, for the stop lines (none for a road running through a T).
        var stopStart = from.Kind == RoadNodeKind.Junction && !Through(from, chain.Points[Math.Min(2, chain.Points.Length - 1)]);
        var stopEnd = to.Kind == RoadNodeKind.Junction && !Through(to, chain.Points[Math.Max(0, chain.Points.Length - 3)]);
        float Start(float s) => stopStart ? s - from.Radius : Far;
        float End(float s) => stopEnd ? chain.Length - s - to.Radius : Far;
        var up = Vector3.Normalize(Vector3.Cross(At(p1, y1, origin) - At(p0, y0, origin), At(p0 + r0, y0, origin) - At(p0, y0, origin)));
        if (up.Y < 0f)
            up = -up;
        surfaces[RoadMaterial.Asphalt].Quad(
            At(p0 - r0 * hw, y0, origin), At(p0 + r0 * hw, y0, origin), At(p1 + r1 * hw, y1, origin), At(p1 - r1 * hw, y1, origin), up,
            new(0, s0), new(1, s0), new(1, s1), new(0, s1),
            new(Start(s0), End(s0)), new(Start(s0), End(s0)), new(Start(s1), End(s1)), new(Start(s1), End(s1)));

        var bridge = chain.Bridge[i] && chain.Bridge[i + 1];
        // Curbs stop at a junction plate.
        bool UnderPlate(Vector2 p, RoadNode node) => node.Kind == RoadNodeKind.Junction && Vector2.Distance(p, node.At) < node.Radius + def.Curb;
        if (UnderPlate(p0, from) || UnderPlate(p1, from) || UnderPlate(p0, to) || UnderPlate(p1, to))
            return;
        var concrete = surfaces[RoadMaterial.Concrete];
        foreach (var side in new[] { -1f, 1f })
        {
            var (n0, n1) = (r0 * side, r1 * side);
            var outward = Vector3.Normalize(new Vector3((n0.X + n1.X) / 2f, 0f, (n0.Y + n1.Y) / 2f));
            if (!bridge)
            {
                // No curb at all where its width is 0: the asphalt meets the shoulder.
                if (def.Curb <= 0f)
                    continue;
                var (inner, outer, top) = (hw, hw + def.Curb, CurbHeight);
                Wall(concrete, p0, p1, n0, n1, y0, y1, inner, outer, top, -0.3f, s0, s1, outward, origin);
            }
            else
            {
                Wall(concrete, p0, p1, n0, n1, y0, y1, hw, hw + ParapetWidth, ParapetHeight, -DeckDepth, s0, s1, outward, origin);
            }
        }
        if (bridge)
        {
            var w = hw + ParapetWidth;
            concrete.Quad(At(p0 - r0 * w, y0 - DeckDepth, origin), At(p0 + r0 * w, y0 - DeckDepth, origin), At(p1 + r1 * w, y1 - DeckDepth, origin),
                At(p1 - r1 * w, y1 - DeckDepth, origin), -Vector3.UnitY, new(0, s0), new(2 * w, s0), new(2 * w, s1), new(0, s1));
        }
    }

    // A strip along one side of the road from `inner` to `outer` metres out, `top` over the surface, its outer face going
    // down to `bottom` (under the surface): a curb, or a bridge parapet with the deck's edge below it.
    private static void Wall(RoadMeshSurface surface, Vector2 p0, Vector2 p1, Vector2 n0, Vector2 n1, float y0, float y1, float inner, float outer,
        float top, float bottom, float s0, float s1, Vector3 outward, Vector3 origin)
    {
        var (a0, a1) = (p0 + n0 * inner, p1 + n1 * inner);
        var (b0, b1) = (p0 + n0 * outer, p1 + n1 * outer);
        surface.Quad(At(a0, y0 + top, origin), At(b0, y0 + top, origin), At(b1, y1 + top, origin), At(a1, y1 + top, origin), Vector3.UnitY,
            new(s0, 0), new(s0, outer - inner), new(s1, outer - inner), new(s1, 0));
        surface.Quad(At(a0, y0 - 0.02f, origin), At(a1, y1 - 0.02f, origin), At(a1, y1 + top, origin), At(a0, y0 + top, origin), -outward,
            new(s0, 0), new(s1, 0), new(s1, top), new(s0, top));
        surface.Quad(At(b0, y0 + bottom, origin), At(b1, y1 + bottom, origin), At(b1, y1 + top, origin), At(b0, y0 + top, origin), outward,
            new(s0, bottom), new(s1, bottom), new(s1, top), new(s0, top));
    }

    // Piers under each bridge, hazard posts and a caution sign at each end.
    private static void Bridges(Dictionary<RoadMaterial, RoadMeshSurface> surfaces, WorldGenerator gen, RoadChain chain, RoadsDefinition def, Vector3 origin,
        Func<Vector2, bool> inside)
    {
        var hw = chain.Width / 2f;
        var n = chain.Points.Length;
        for (var k = 1; k < n; k++)
        {
            // A pier wherever the deck crosses a multiple of the spacing, in pairs under the two lanes.
            if (chain.Bridge[k] && chain.Bridge[k - 1] && k + 1 < n && chain.Bridge[k + 1]
                && MathF.Floor(chain.Along[k] / PierSpacing) != MathF.Floor(chain.Along[k - 1] / PierSpacing))
            {
                var t = Tangent(chain, k);
                var r = Right(t);
                var deck = chain.Heights[k] + RoadNetwork.SurfaceLift - DeckDepth;
                foreach (var side in new[] { -1f, 1f })
                {
                    var p = chain.Points[k] + r * side * (hw - 1.8f);
                    if (!inside(p))
                        continue;
                    var ground = gen.HeightAt(p.X, p.Y);
                    if (ground > deck - 0.5f)
                        continue;
                    surfaces[RoadMaterial.Concrete].Box(At(p, 0f, origin), new Vector3(r.X, 0, r.Y), new Vector3(t.X, 0, t.Y), 0.65f, 0.65f, ground - 0.5f, deck);
                }
            }
            if (chain.Bridge[k] == chain.Bridge[k - 1])
                continue;
            // A bridge starts (k is its first sample) or ends (k - 1 its last): posts and a sign on the approach.
            var starts = chain.Bridge[k];
            var edge = starts ? k : k - 1;
            var away = starts ? -1f : 1f;
            Approach(surfaces, chain, chain.Along[edge], away, hw, def, origin, inside);
        }
    }

    private static void Approach(Dictionary<RoadMaterial, RoadMeshSurface> surfaces, RoadChain chain, float along, float away, float hw,
        RoadsDefinition def, Vector3 origin, Func<Vector2, bool> inside)
    {
        for (var post = 0; post < 5; post++)
        {
            var (p, t, y) = PointAt(chain, along + away * (0.6f + 1.2f * post));
            var r = Right(t);
            foreach (var side in new[] { -1f, 1f })
            {
                var at = p + r * side * (hw + MathF.Max(def.Curb, PostInset * 2f) / 2f);
                if (inside(at))
                    surfaces[RoadMaterial.Hazard].Box(At(at, 0f, origin), new Vector3(r.X, 0, r.Y), new Vector3(t.X, 0, t.Y), 0.16f, 0.16f, y, y + 0.75f);
            }
        }
        // A yellow caution sign on the right of the approaching traffic, facing it.
        var (sp, st, sy) = PointAt(chain, along + away * 10f);
        var facing = st * away;
        var right = Right(-facing);
        var foot = sp + right * (hw + def.Curb + 1.2f);
        if (!inside(foot))
            return;
        var forward3 = new Vector3(facing.X, 0, facing.Y);
        var right3 = new Vector3(right.X, 0, right.Y);
        surfaces[RoadMaterial.Metal].Box(At(foot, 0f, origin), right3, forward3, 0.05f, 0.05f, sy - 0.3f, sy + 2.2f);
        var centre = At(foot, sy + 2.4f, origin) + forward3 * 0.06f;
        const float half = 0.45f;
        var corners = new[] { centre + Vector3.UnitY * half, centre + right3 * half, centre - Vector3.UnitY * half, centre - right3 * half };
        surfaces[RoadMaterial.Sign].Quad(corners[0], corners[1], corners[2], corners[3], forward3, new(0.5f, 1), new(1, 0.5f), new(0.5f, 0), new(0, 0.5f));
        surfaces[RoadMaterial.Metal].Quad(corners[0], corners[1], corners[2], corners[3], -forward3, new(0.5f, 1), new(1, 0.5f), new(0.5f, 0), new(0, 0.5f));
    }

    // Position, direction and road surface height at a distance along a chain (clamped to its ends).
    private static (Vector2 Point, Vector2 Tangent, float Height) PointAt(RoadChain chain, float along)
    {
        along = Math.Clamp(along, 0f, chain.Length);
        var k = Array.BinarySearch(chain.Along, along);
        if (k < 0)
            k = ~k - 1;
        k = Math.Clamp(k, 0, chain.Points.Length - 2);
        var span = chain.Along[k + 1] - chain.Along[k];
        var t = span > 0f ? (along - chain.Along[k]) / span : 0f;
        var dir = chain.Points[k + 1] - chain.Points[k];
        return (Vector2.Lerp(chain.Points[k], chain.Points[k + 1], t), dir.LengthSquared() > 0f ? Vector2.Normalize(dir) : Vector2.UnitX,
            chain.Heights[k] + (chain.Heights[k + 1] - chain.Heights[k]) * t + RoadNetwork.SurfaceLift);
    }

    // A junction's plate: flat asphalt over the ends of the roads that meet there, squared to their arms with rounded corners
    // between them (RoadNetwork.PlateOutline). Plain, except across a T, where the road running through keeps its centre
    // line and the edge line on the far side from the road that ends there: the plate's uv is then that road's (uv.x across
    // it, 0 to 1 edge to edge; uv.y metres along) and uv2 is (-2, the side of the ending road: +1 where uv.x > 0.5).
    private static void Plate(RoadMeshSurface asphalt, RoadNetwork roads, RoadNode node, float width, Vector3 origin)
    {
        var y = node.Height + RoadNetwork.SurfaceLift + 0.012f;
        var centre = At(node.At, y, origin);
        var plain = new Vector2(-1f, -1f);
        Func<Vector2, Vector2> uv = p => p;
        var marks = plain;
        if (node.Side is var side and >= 0)
        {
            var along = node.Arm(side + 1);
            var right = Right(along);
            uv = p => new Vector2(0.5f + Vector2.Dot(p - node.At, right) / width, Vector2.Dot(p - node.At, along));
            marks = new Vector2(-2f, MathF.Sign(Vector2.Dot(node.Arm(side), right)));
        }
        var outline = roads.PlateOutline(node);
        for (var k = 0; k < outline.Count; k++)
        {
            var (p, q) = (outline[k], outline[(k + 1) % outline.Count]);
            // Corners along a straight edge make no triangle.
            if (MathF.Abs((p.X - node.At.X) * (q.Y - node.At.Y) - (p.Y - node.At.Y) * (q.X - node.At.X)) < 1e-4f)
                continue;
            asphalt.Triangle(centre, At(p, y, origin), At(q, y, origin), Vector3.UnitY, uv(node.At), uv(p), uv(q), marks, marks, marks);
        }
    }
}
