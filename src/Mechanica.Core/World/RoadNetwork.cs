using System.Numerics;

namespace Mechanica.Core.World;

/// <summary>What a <see cref="RoadChain"/> is.</summary>
public enum RoadKind
{
    /// <summary>Two-lane asphalt with curbs, junctions and bridges.</summary>
    Road,

    /// <summary>A narrow dirt track painted on the ground.</summary>
    Track,
}

/// <summary>What sits at the end of a <see cref="RoadChain"/>.</summary>
public enum RoadNodeKind
{
    /// <summary>Three or more roads meet on a junction plate.</summary>
    Junction,

    /// <summary>The road stops at the edge of a POI's pad.</summary>
    End,

    /// <summary>A dirt track meets a road or another track.</summary>
    Join,
}

/// <summary>A junction or road end.</summary>
/// <param name="Index">Place in <see cref="RoadNetwork.Nodes"/>.</param>
/// <param name="Kind">What it is.</param>
/// <param name="At">World x/z.</param>
/// <param name="Height">Height of the road surface there.</param>
/// <param name="Radius">
/// Metres from a junction's centre to the mouth of each arm, where its plate ends and the stop lines are (0 for an end or join).
/// </param>
/// <param name="Poi">Index of the POI (<see cref="WorldGenerator.Pois"/>) an end serves, or -1.</param>
/// <param name="Heading">Direction of a junction's first arm (radians from +x towards +z); the others are at right angles to it.</param>
/// <param name="Arms">
/// Which of a junction's four arms (bit k: <see cref="Heading"/> + k·90°) have a road: a T junction has three, a crossroads all
/// four. 0 for a junction of more than four roads, whose plate is round.
/// </param>
public sealed record RoadNode(int Index, RoadNodeKind Kind, Vector2 At, float Height, float Radius, int Poi, float Heading = 0f, int Arms = 0)
{
    /// <summary>Unit direction of arm <paramref name="k"/> (0..3) of a junction.</summary>
    public Vector2 Arm(int k)
    {
        var (s, c) = MathF.SinCos(Heading + k * MathF.PI / 2f);
        return new Vector2(c, s);
    }

    /// <summary>The arm (0..3) of a junction that points most nearly at a point.</summary>
    public int ArmToward(Vector2 p) => Enumerable.Range(0, 4).MaxBy(k => Vector2.Dot(Arm(k), p - At));

    /// <summary>
    /// The arm of a T junction (three arms) opposite the road that ends there, or -1: the two arms either side of it are
    /// one road running straight through, which keeps its markings across the plate and has no stop lines.
    /// </summary>
    public int Side => System.Numerics.BitOperations.PopCount((uint)Arms) == 3 ? Enumerable.Range(0, 4).Single(k => (Arms & 1 << ((k + 2) & 3)) == 0) : -1;
}

/// <summary>
/// One stretch of road between two <see cref="RoadNode"/>s: a centre line sampled every few metres with the height of the
/// road surface at each sample, and which samples are on a bridge.
/// </summary>
public sealed class RoadChain
{
    internal RoadChain(int index, RoadKind kind, int from, int to, Vector2[] points, float[] heights, bool[] bridge, float width)
    {
        Index = index;
        Kind = kind;
        From = from;
        To = to;
        Points = points;
        Heights = heights;
        Bridge = bridge;
        Width = width;
        Along = new float[points.Length];
        for (var i = 1; i < points.Length; i++)
            Along[i] = Along[i - 1] + Vector2.Distance(points[i - 1], points[i]);
    }

    /// <summary>Place in <see cref="RoadNetwork.Chains"/>.</summary>
    public int Index { get; }

    /// <summary>Road or track.</summary>
    public RoadKind Kind { get; }

    /// <summary>Node at the first point.</summary>
    public int From { get; }

    /// <summary>Node at the last point.</summary>
    public int To { get; }

    /// <summary>Centre line, world x/z.</summary>
    public Vector2[] Points { get; }

    /// <summary>Height of the road surface at each point.</summary>
    public float[] Heights { get; }

    /// <summary>True for the points on a bridge (the deck spans the ground there).</summary>
    public bool[] Bridge { get; }

    /// <summary>Metres along the centre line to each point.</summary>
    public float[] Along { get; }

    /// <summary>Surface width in metres.</summary>
    public float Width { get; }

    /// <summary>Length of the centre line in metres.</summary>
    public float Length => Along[^1];
}

/// <summary>The nearest point of a road to a query point.</summary>
/// <param name="Chain">The chain.</param>
/// <param name="Segment">Segment index (between points <c>Segment</c> and <c>Segment + 1</c>).</param>
/// <param name="T">0..1 along the segment.</param>
/// <param name="Distance">Horizontal metres from the query point to the centre line.</param>
/// <param name="Height">Road surface height there.</param>
/// <param name="Point">The nearest centre-line point.</param>
public readonly record struct RoadHit(RoadChain Chain, int Segment, float T, float Distance, float Height, Vector2 Point)
{
    /// <summary>True when the point is on a bridge.</summary>
    public bool OnBridge => Chain.Bridge[Segment] && Chain.Bridge[Segment + 1];
}

/// <summary>
/// The road network (World Map spec §3), laid out once when the world is generated, the same on every peer. Sites that ask
/// for a road are joined by the links the data requires, then a minimum spanning tree, then loops wherever the network
/// makes a trip much longer than the straight line. Roads are routed on a grid that prefers gentle grades and avoids
/// POIs and deep water, crossing rivers on bridges, and roads that meet share their cells so they join at junctions.
/// Each stretch is smoothed into gentle curves, given a graded profile (at most <see cref="RoadsDefinition.MaxGrade"/>),
/// and cuts and fills the ground under it. Dirt tracks then run from camps and small ruins to the nearest road.
/// </summary>
public sealed partial class RoadNetwork
{
    // Bucket side in metres for the segment index.
    private const float Bucket = 32f;

    // Longest a cut or fill may blend back into the natural ground, in metres.
    private const float MaxBlend = 48f;

    // Road surface over its levelled bed, so the ground never shows through the asphalt.
    internal const float SurfaceLift = 0.04f;

    // Metres between the samples of a road's centre line.
    private const float SampleStep = 4f;

    private readonly RoadsDefinition? _def;
    private readonly List<RoadNode> _nodes = [];
    private readonly List<RoadChain> _chains = [];
    private readonly Dictionary<int, RoadKind> _connected = [];
    private Dictionary<(int, int), List<(int Chain, int Segment)>> _segments = [];
    private Dictionary<(int, int), List<int>> _junctions = [];
    private Dictionary<int, IReadOnlyList<Vector2>> _plates = [];

    private RoadNetwork(RoadsDefinition? def) => _def = def;

    /// <summary>No roads at all.</summary>
    public static RoadNetwork Empty { get; } = new(null);

    /// <summary>The rules, or null when the world has no roads.</summary>
    public RoadsDefinition? Definition => _def;

    /// <summary>Junctions and road ends.</summary>
    public IReadOnlyList<RoadNode> Nodes => _nodes;

    /// <summary>Stretches of road and track.</summary>
    public IReadOnlyList<RoadChain> Chains => _chains;

    /// <summary>How each connected POI (by index in <see cref="WorldGenerator.Pois"/>) meets the network.</summary>
    public IReadOnlyDictionary<int, RoadKind> Connected => _connected;

    /// <summary>Half the width of the levelled bed: surface, curb and shoulder.</summary>
    private float Core(RoadChain chain) => chain.Kind == RoadKind.Road
        ? chain.Width / 2f + _def!.Curb + _def.Shoulder
        : chain.Width / 2f + 0.5f;

    /// <summary>
    /// The ground height at a point once the roads have cut and filled it: level with the road surface across the road and
    /// its shoulders, blending back into the natural ground beyond. Bridges leave the ground under them alone.
    /// </summary>
    public float Shape(float x, float z, float h)
    {
        if (_chains.Count == 0)
            return h;
        var key = Key(x, z);
        var p = new Vector2(x, z);
        // Where segments and junctions overlap, each pulls towards its own height by its strength and nearness, so the
        // ground never steps where one takes over from another. Roads and their junctions shape the ground first; tracks
        // then shape what the roads leave, and never the roads' own beds: a track that runs in beside a road, lower than
        // it, would otherwise pull the ground out from under the asphalt's edge.
        Pull road = default, track = default;
        if (_segments.TryGetValue(key, out var near))
        {
            foreach (var (c, i) in near)
            {
                var chain = _chains[c];
                if (chain.Bridge[i] && chain.Bridge[i + 1])
                    continue;
                var (d, t) = Project(chain, i, p);
                var core = Core(chain);
                if (d >= core + MaxBlend)
                    continue;
                var e = chain.Heights[i] + (chain.Heights[i + 1] - chain.Heights[i]) * t;
                var k = 1f - Noise.SmoothStep(core, core + Blend(e, h), d);
                if (k <= 0f)
                    continue;
                var w = MathF.Pow(k, 8f) / MathF.Pow(1f + d, 4f);
                if (chain.Kind == RoadKind.Road)
                    road.Add(w, e - 0.03f, k);
                else
                    track.Add(w, e, k);
            }
        }
        if (_junctions.TryGetValue(key, out var nodes))
        {
            foreach (var n in nodes)
            {
                var node = _nodes[n];
                var core = node.Radius + _def!.Curb + _def.Shoulder;
                var d = Vector2.Distance(p, node.At);
                var k = 1f - Noise.SmoothStep(core, core + Blend(node.Height, h), d);
                if (k <= 0f)
                    continue;
                // Measured from the plate's rim, so the plate outweighs the roads that end on it.
                road.Add(MathF.Pow(k, 8f) / MathF.Pow(1f + MathF.Max(0f, d - node.Radius), 4f), node.Height - 0.03f, k);
            }
        }
        var shaped = road.Apply(h, 1f);
        return track.Apply(shaped, 1f - road.Best);
    }

    // The heights that shape the ground at a point, weighted, and the strongest pull among them (0..1).
    private struct Pull
    {
        private float _weights, _sum;

        public float Best { get; private set; }

        public void Add(float weight, float height, float strength)
        {
            _weights += weight;
            _sum += weight * height;
            Best = MathF.Max(Best, strength);
        }

        // The ground h pulled towards the weighted height by the strongest pull, scaled by `share`.
        public readonly float Apply(float h, float share) => _weights > 0f ? h + (_sum / _weights - h) * Best * share : h;
    }

    // A taller cut or fill blends out further, so its banks stay walkable.
    private static float Blend(float road, float ground) => Math.Clamp(6f + 1.6f * MathF.Abs(road - ground), 6f, MaxBlend);

    /// <summary>True where a road's bed (surface, curbs, shoulders, junction plates) or a track covers the ground: nothing grows there.</summary>
    public bool Covers(float x, float z) => Within(x, z, 0f, bridges: false);

    /// <summary>True when a point is within <paramref name="margin"/> metres of a road's bed or a track, bridges included.</summary>
    public bool Near(float x, float z, float margin)
    {
        if (_chains.Count == 0)
            return false;
        // Margins wider than a bucket's reach look in the neighbouring buckets too.
        var reach = (int)MathF.Ceiling(MathF.Max(0f, margin - MaxBlend) / Bucket);
        if (reach == 0)
            return Within(x, z, margin, bridges: true);
        var (bx, bz) = Key(x, z);
        var p = new Vector2(x, z);
        for (var j = bz - reach; j <= bz + reach; j++)
        {
            for (var i = bx - reach; i <= bx + reach; i++)
            {
                if (_segments.TryGetValue((i, j), out var near) && near.Any(s => Project(_chains[s.Chain], s.Segment, p).Distance < Core(_chains[s.Chain]) + margin))
                    return true;
                if (_junctions.TryGetValue((i, j), out var nodes) && nodes.Any(n => OffPlate(_nodes[n], p) < margin))
                    return true;
            }
        }
        return false;
    }

    private bool Within(float x, float z, float margin, bool bridges)
    {
        if (_chains.Count == 0)
            return false;
        var key = Key(x, z);
        var p = new Vector2(x, z);
        if (_segments.TryGetValue(key, out var near))
        {
            foreach (var (c, i) in near)
            {
                var chain = _chains[c];
                if (!bridges && chain.Bridge[i] && chain.Bridge[i + 1])
                    continue;
                if (Project(chain, i, p).Distance < Core(chain) + margin)
                    return true;
            }
        }
        return _junctions.TryGetValue(key, out var nodes) && nodes.Any(n => OffPlate(_nodes[n], p) < _def!.Curb + _def.Shoulder + margin);
    }

    /// <summary>
    /// The outline of a junction's plate, anticlockwise seen from above (+x towards +z), starting down its first arm: a square
    /// the road's width across, each arm's mouth reaching out to <see cref="RoadNode.Radius"/>, and a rounded corner where two
    /// arms meet. A round plate for a junction of more than four roads.
    /// </summary>
    public IReadOnlyList<Vector2> PlateOutline(RoadNode node) => _plates.TryGetValue(node.Index, out var outline) ? outline : Outline(node);

    private List<Vector2> Outline(RoadNode node)
    {
        var outline = new List<Vector2>();
        if (node.Arms == 0)
        {
            for (var k = 0; k < 28; k++)
            {
                var (sin, cos) = MathF.SinCos(2f * MathF.PI * k / 28);
                outline.Add(node.At + new Vector2(cos, sin) * node.Radius);
            }
            return outline;
        }
        var hw = _def!.Width / 2f;
        var e = node.Radius;
        var f = e - hw;
        bool Has(int k) => (node.Arms & 1 << (k & 3)) != 0;
        for (var q = 0; q < 4; q++)
        {
            // In this quarter's frame, arm q runs along +u and arm q + 1 along +v.
            var (u, v) = (node.Arm(q), node.Arm(q + 1));
            void Add(float a, float b) => outline.Add(node.At + u * a + v * b);
            var (here, next) = (Has(q), Has(q + 1));
            if (here)
            {
                Add(e, 0f);
                Add(e, hw);
            }
            else
            {
                Add(hw, 0f);
            }
            if (here && next)
            {
                // The kerb line rounds the corner about (hw + f, hw + f), from the side of arm q to the side of arm q + 1.
                const int steps = 8;
                for (var k = 1; k < steps; k++)
                {
                    var (sin, cos) = MathF.SinCos(-MathF.PI / 2f - MathF.PI / 2f * k / steps);
                    Add(hw + f + f * cos, hw + f + f * sin);
                }
                Add(hw, e);
            }
            else if (next)
            {
                Add(hw, hw);
                Add(hw, e);
            }
            else if (!here)
            {
                Add(hw, hw);
            }
        }
        return outline;
    }

    /// <summary>Metres from a point to a junction's plate (0 inside it).</summary>
    public float OffPlate(RoadNode node, Vector2 p)
    {
        // Points well inside the mouths' circle are on it or close; only nearby points need the outline.
        if (Vector2.Distance(p, node.At) > node.Radius + MaxBlend + 1f)
            return Vector2.Distance(p, node.At) - node.Radius;
        var outline = PlateOutline(node);
        var inside = false;
        var best = float.PositiveInfinity;
        for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
        {
            var (a, b) = (outline[j], outline[i]);
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y))
                inside = !inside;
            var ab = b - a;
            var t = ab.LengthSquared() > 0f ? Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            best = MathF.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return inside ? 0f : best;
    }

    /// <summary>
    /// How much of a road's gravel shoulder and of a dirt track covers a point (0..1 each); false when neither does.
    /// </summary>
    public bool Paint(float x, float z, out float shoulder, out float track)
    {
        shoulder = track = 0f;
        if (_chains.Count == 0)
            return false;
        var key = Key(x, z);
        var p = new Vector2(x, z);
        if (_junctions.TryGetValue(key, out var nodes))
        {
            // Gravel round a junction's plate too, so its rounded corners have their shoulder.
            var edge = _def!.Curb + _def.Shoulder;
            foreach (var n in nodes)
                shoulder = MathF.Max(shoulder, 1f - Noise.SmoothStep(edge - 0.2f, edge + 0.9f, OffPlate(_nodes[n], p)));
        }
        if (!_segments.TryGetValue(key, out var near))
            return shoulder > 0f;
        foreach (var (c, i) in near)
        {
            var chain = _chains[c];
            if (chain.Bridge[i] && chain.Bridge[i + 1])
                continue;
            var d = Project(chain, i, p).Distance;
            if (chain.Kind == RoadKind.Road)
            {
                var edge = chain.Width / 2f + _def!.Curb + _def.Shoulder;
                shoulder = MathF.Max(shoulder, 1f - Noise.SmoothStep(edge - 0.2f, edge + 0.9f, d));
            }
            else
            {
                // A ragged edge, worn wider in places.
                var edge = chain.Width / 2f + 0.6f * Noise.Fractal(x / 3f, z / 3f, 0x7124Cu, 2, 2f, 0.5f);
                track = MathF.Max(track, 1f - Noise.SmoothStep(edge - 0.7f, edge + 0.9f, d));
            }
        }
        return shoulder > 0f || track > 0f;
    }

    /// <summary>The nearest road or track point within <paramref name="reach"/> metres (at most 48), or null.</summary>
    public RoadHit? Nearest(float x, float z, float reach, RoadKind? kind = null)
    {
        if (_chains.Count == 0 || !_segments.TryGetValue(Key(x, z), out var near))
            return null;
        var p = new Vector2(x, z);
        RoadHit? best = null;
        foreach (var (c, i) in near)
        {
            var chain = _chains[c];
            if (kind is { } k && chain.Kind != k)
                continue;
            var (d, t) = Project(chain, i, p);
            if (d > reach || (best is { } b && b.Distance <= d))
                continue;
            best = new RoadHit(chain, i, t, d, chain.Heights[i] + (chain.Heights[i + 1] - chain.Heights[i]) * t,
                Vector2.Lerp(chain.Points[i], chain.Points[i + 1], t));
        }
        return best;
    }

    /// <summary>Total length in metres of the chains of a kind.</summary>
    public float LengthOf(RoadKind kind) => _chains.Where(c => c.Kind == kind).Sum(c => c.Length);

    /// <summary>The chains whose centre line comes within <paramref name="margin"/> of a box.</summary>
    internal IEnumerable<RoadChain> ChainsNear(float x0, float z0, float x1, float z1, float margin)
    {
        foreach (var chain in _chains)
        {
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);
            foreach (var p in chain.Points)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            if (max.X + margin >= x0 && min.X - margin < x1 && max.Y + margin >= z0 && min.Y - margin < z1)
                yield return chain;
        }
    }

    private static (int, int) Key(float x, float z) => ((int)MathF.Floor(x / Bucket), (int)MathF.Floor(z / Bucket));

    private static (float Distance, float T) Project(RoadChain chain, int i, Vector2 p)
    {
        var a = chain.Points[i];
        var ab = chain.Points[i + 1] - a;
        var len = ab.LengthSquared();
        var t = len > 0f ? Math.Clamp(Vector2.Dot(p - a, ab) / len, 0f, 1f) : 0f;
        return (Vector2.Distance(p, a + ab * t), t);
    }

    // Every segment and junction goes in each bucket its influence reaches.
    private void Index()
    {
        var segments = new Dictionary<(int, int), List<(int, int)>>();
        foreach (var chain in _chains)
            IndexChain(chain, segments);
        var junctions = new Dictionary<(int, int), List<int>>();
        foreach (var node in _nodes.Where(n => n.Kind == RoadNodeKind.Junction))
        {
            var reach = node.Radius + _def!.Curb + _def.Shoulder + MaxBlend;
            foreach (var key in Keys(node.At - new Vector2(reach), node.At + new Vector2(reach)))
            {
                if (!junctions.TryGetValue(key, out var list))
                    junctions[key] = list = [];
                list.Add(node.Index);
            }
        }
        _segments = segments;
        _junctions = junctions;
        _plates = _nodes.Where(n => n.Kind == RoadNodeKind.Junction).ToDictionary(n => n.Index, n => (IReadOnlyList<Vector2>)Outline(n));
    }

    private void IndexChain(RoadChain chain, Dictionary<(int, int), List<(int, int)>> segments)
    {
        var reach = Core(chain) + MaxBlend;
        for (var i = 0; i + 1 < chain.Points.Length; i++)
        {
            var min = Vector2.Min(chain.Points[i], chain.Points[i + 1]) - new Vector2(reach);
            var max = Vector2.Max(chain.Points[i], chain.Points[i + 1]) + new Vector2(reach);
            foreach (var key in Keys(min, max))
            {
                if (!segments.TryGetValue(key, out var list))
                    segments[key] = list = [];
                list.Add((chain.Index, i));
            }
        }
    }

    private static IEnumerable<(int, int)> Keys(Vector2 min, Vector2 max)
    {
        for (var bz = (int)MathF.Floor(min.Y / Bucket); bz <= (int)MathF.Floor(max.Y / Bucket); bz++)
            for (var bx = (int)MathF.Floor(min.X / Bucket); bx <= (int)MathF.Floor(max.X / Bucket); bx++)
                yield return (bx, bz);
    }
}
