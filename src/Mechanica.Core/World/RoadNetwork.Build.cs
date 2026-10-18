using System.Numerics;

namespace Mechanica.Core.World;

// Laying the network out: routing on a grid, turning the routes into smooth graded chains, and the dirt tracks.
public sealed partial class RoadNetwork
{
    // Ground lower than this under the sea surface is too deep to bridge: lakes and the sea.
    private const float DeepWater = 6f;

    // Ground lower than this over the sea surface is water a road bridges.
    private const float Wet = 0.3f;

    // Metres a POI's footprint keeps the router out beyond its radius.
    private const float PoiMargin = 6f;

    // Longest straight a route is simplified to before it is curved, in metres.
    private const float MaxStraight = 280f;

    // How strongly the router prefers an existing road over new ground (cost factor).
    private const float Reuse = 0.3f;

    // A loop road may share an existing road, but only a little more cheaply than new ground, so it makes a new way.
    private const float LoopReuse = 0.85f;

    // The router's heuristic weight: above the reuse factor, so it trades a little optimality for speed.
    private const float Greed = 0.6f;

    private Grid? _grid;
    private bool[] _roadCells = [];
    private readonly HashSet<long> _edges = [];

    /// <summary>Lays out the roads between the generator's sites (its POIs placed so far).</summary>
    internal static RoadNetwork Build(WorldGenerator gen, RoadsDefinition def)
    {
        var network = new RoadNetwork(def);
        var grid = new Grid(gen, def.Cell);
        network._grid = grid;
        network._roadCells = new bool[grid.Count];
        network.Route(gen, def, grid);
        network.Index();
        return network;
    }

    private static string LinkOf(PoiInstance poi) => poi.Site?.Definition.Road ?? poi.Poi.Prefab.Road ?? RoadLinks.None;

    private void Route(WorldGenerator gen, RoadsDefinition def, Grid grid)
    {
        var sites = gen.Pois.Where(p => p.Site is not null && LinkOf(p) == RoadLinks.Road).ToList();
        if (sites.Count < 2)
            return;
        var byId = sites.ToDictionary(p => p.Site!.Id);
        var parent = sites.ToDictionary(p => p.Index, p => p.Index);
        int FindRoot(int i) => parent[i] == i ? i : parent[i] = FindRoot(parent[i]);
        var routes = new List<(PoiInstance A, PoiInstance B, List<int> Cells)>();
        void Connect(PoiInstance a, PoiInstance b, float reuse)
        {
            var cells = grid.Route(grid.CellOf(a.Centre), grid.CellOf(b.Centre), a.Index, b.Index, def.MaxGrade, def.BridgeCost, _roadCells, reuse);
            if (cells is null)
                return;
            routes.Add((a, b, cells));
            for (var k = 0; k < cells.Count; k++)
            {
                _roadCells[cells[k]] = true;
                if (k > 0)
                    _edges.Add(EdgeKey(cells[k - 1], cells[k]));
            }
            parent[FindRoot(a.Index)] = FindRoot(b.Index);
        }

        foreach (var link in def.Links ?? [])
        {
            if (byId.TryGetValue(link[0], out var a) && byId.TryGetValue(link[1], out var b))
                Connect(a, b, Reuse);
        }
        // A minimum spanning tree over the straight-line distances joins every site.
        var pairs = new List<(float Distance, PoiInstance A, PoiInstance B)>();
        for (var i = 0; i < sites.Count; i++)
            for (var j = i + 1; j < sites.Count; j++)
                pairs.Add((Vector2.Distance(sites[i].Centre, sites[j].Centre), sites[i], sites[j]));
        pairs.Sort((x, y) => x.Distance != y.Distance ? x.Distance.CompareTo(y.Distance) : (x.A.Index, x.B.Index).CompareTo((y.A.Index, y.B.Index)));
        foreach (var (_, a, b) in pairs)
        {
            if (FindRoot(a.Index) != FindRoot(b.Index))
                Connect(a, b, Reuse);
        }
        // Loops where the network makes nearby sites a long way round.
        var loops = 0;
        foreach (var (distance, a, b) in pairs)
        {
            if (loops >= def.MaxLoops || distance > def.LoopReach)
                break;
            var around = NetworkDistance(grid, grid.CellOf(a.Centre), grid.CellOf(b.Centre), distance * def.LoopFactor);
            if (around <= distance * def.LoopFactor)
                continue;
            Connect(a, b, LoopReuse);
            loops++;
        }
        BuildChains(gen, def, grid, sites);
    }

    private static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    // Metres along existing roads between two cells, or +∞ when that is over the limit or they do not connect.
    private float NetworkDistance(Grid grid, int from, int to, float limit)
    {
        if (!_roadCells[from] || !_roadCells[to])
            return float.PositiveInfinity;
        var best = new Dictionary<int, float> { [from] = 0f };
        var queue = new PriorityQueue<int, float>();
        queue.Enqueue(from, 0f);
        while (queue.TryDequeue(out var cell, out var cost))
        {
            if (cell == to)
                return cost;
            if (cost > best[cell] || cost > limit)
                continue;
            foreach (var (next, length) in grid.Neighbours(cell))
            {
                if (!_edges.Contains(EdgeKey(cell, next)))
                    continue;
                var c = cost + length;
                if (c < best.GetValueOrDefault(next, float.PositiveInfinity))
                {
                    best[next] = c;
                    queue.Enqueue(next, c);
                }
            }
        }
        return float.PositiveInfinity;
    }

    // The routed cells as a graph: junctions where three or more ways meet (close ones merged), ends at the sites, and the
    // chains of cells between them, each smoothed and graded into a road.
    private void BuildChains(WorldGenerator gen, RoadsDefinition def, Grid grid, List<PoiInstance> sites)
    {
        var degree = new Dictionary<int, int>();
        foreach (var edge in _edges)
        {
            var (a, b) = ((int)(edge >> 32), (int)(uint)edge);
            degree[a] = degree.GetValueOrDefault(a) + 1;
            degree[b] = degree.GetValueOrDefault(b) + 1;
        }
        // Every routed cell in a site's footprint is that site's end, so ways that branch inside it all stop at its pad.
        var ends = new Dictionary<int, PoiInstance>();
        var byIndex = sites.ToDictionary(s => s.Index);
        foreach (var cell in degree.Keys)
        {
            if (byIndex.TryGetValue(grid.Owner(cell), out var site))
                ends[cell] = site;
        }
        foreach (var site in sites)
            ends.TryAdd(grid.CellOf(site.Centre), site);
        bool IsNode(int cell) => degree[cell] != 2 || ends.ContainsKey(cell);

        // Junction cells within two cells of each other are one junction.
        var cluster = new Dictionary<int, int>();
        var junctionCells = degree.Where(d => d.Value >= 3 && !ends.ContainsKey(d.Key)).Select(d => d.Key).Order().ToList();
        var clusters = new List<List<int>>();
        foreach (var cell in junctionCells)
        {
            var (ci, cj) = grid.Coords(cell);
            var joined = clusters.FindIndex(c => c.Any(o => grid.Coords(o) is var (oi, oj) && Math.Abs(oi - ci) <= 2 && Math.Abs(oj - cj) <= 2));
            if (joined < 0)
            {
                joined = clusters.Count;
                clusters.Add([]);
            }
            clusters[joined].Add(cell);
        }
        // Clusters that grew into each other merge.
        for (var merged = true; merged;)
        {
            merged = false;
            for (var a = 0; a < clusters.Count && !merged; a++)
            {
                for (var b = a + 1; b < clusters.Count && !merged; b++)
                {
                    if (clusters[a].Any(x => clusters[b].Any(y => grid.Coords(x) is var (xi, xj) && grid.Coords(y) is var (yi, yj) && Math.Abs(xi - yi) <= 2 && Math.Abs(xj - yj) <= 2)))
                    {
                        clusters[a].AddRange(clusters[b]);
                        clusters.RemoveAt(b);
                        merged = true;
                    }
                }
            }
        }
        var junctionNode = new int[clusters.Count];
        var radius = def.Width * 0.8f;
        for (var c = 0; c < clusters.Count; c++)
        {
            foreach (var cell in clusters[c])
                cluster[cell] = c;
            var at = clusters[c].Aggregate(Vector2.Zero, (sum, cell) => sum + grid.Centre(cell)) / clusters[c].Count;
            var height = MathF.Max(gen.GroundBeforeRoads(at.X, at.Y), gen.SeaLevel + def.BridgeClearance);
            junctionNode[c] = AddNode(RoadNodeKind.Junction, at, height, radius, -1);
        }

        var visited = new HashSet<long>();
        foreach (var start in degree.Keys.Where(IsNode).Order())
        {
            foreach (var (first, _) in grid.Neighbours(start))
            {
                if (!_edges.Contains(EdgeKey(start, first)) || !visited.Add(EdgeKey(start, first)))
                    continue;
                var cells = new List<int> { start, first };
                var (prev, cur) = (start, first);
                while (!IsNode(cur))
                {
                    var next = grid.Neighbours(cur).Select(n => n.Cell).First(n => n != prev && _edges.Contains(EdgeKey(cur, n)));
                    visited.Add(EdgeKey(cur, next));
                    cells.Add(next);
                    (prev, cur) = (cur, next);
                }
                if (ends.TryGetValue(start, out var startSite) && ends.TryGetValue(cur, out var endSite) && startSite == endSite)
                    continue;
                var fromCluster = cluster.GetValueOrDefault(start, -1);
                var toCluster = cluster.GetValueOrDefault(cur, -1);
                if (fromCluster >= 0 && fromCluster == toCluster && cells.Count <= 6)
                    continue;
                AddRoad(gen, def, grid, cells, fromCluster >= 0 ? junctionNode[fromCluster] : -1, toCluster >= 0 ? junctionNode[toCluster] : -1,
                    ends.GetValueOrDefault(start), ends.GetValueOrDefault(cur), cluster);
            }
        }
    }

    private int AddNode(RoadNodeKind kind, Vector2 at, float height, float radius, int poi)
    {
        _nodes.Add(new RoadNode(_nodes.Count, kind, at, height, radius, poi));
        return _nodes.Count - 1;
    }

    // One chain of cells between two nodes, made into a smooth, graded road.
    private void AddRoad(WorldGenerator gen, RoadsDefinition def, Grid grid, List<int> cells, int fromJunction, int toJunction,
        PoiInstance? fromPoi, PoiInstance? toPoi, Dictionary<int, int> cluster)
    {
        // Cells inside a junction's cluster are replaced by the junction itself.
        var inner = cells.Where((c, k) => k > 0 && k < cells.Count - 1 && !cluster.ContainsKey(c)).ToList();
        var line = new List<Vector2> { fromJunction >= 0 ? _nodes[fromJunction].At : fromPoi?.Centre ?? grid.Centre(cells[0]) };
        line.AddRange(inner.Select(grid.Centre));
        line.Add(toJunction >= 0 ? _nodes[toJunction].At : toPoi?.Centre ?? grid.Centre(cells[^1]));
        var owners = new HashSet<int>();
        if (fromPoi is not null)
            owners.Add(fromPoi.Index);
        if (toPoi is not null)
            owners.Add(toPoi.Index);
        var points = Curve(Simplify(grid, line, owners, def.MaxGrade, allowWater: true));
        if (fromPoi is not null && Trim(points, fromPoi, atStart: true) is false)
            return;
        if (toPoi is not null && Trim(points, toPoi, atStart: false) is false)
            return;
        if (points.Count < 2)
            return;
        var from = fromJunction >= 0 ? fromJunction : AddNode(RoadNodeKind.End, points[0], fromPoi?.PadHeight ?? gen.GroundBeforeRoads(points[0].X, points[0].Y), 0f, fromPoi?.Index ?? -1);
        var to = toJunction >= 0 ? toJunction : AddNode(RoadNodeKind.End, points[^1], toPoi?.PadHeight ?? gen.GroundBeforeRoads(points[^1].X, points[^1].Y), 0f, toPoi?.Index ?? -1);
        var (heights, bridge) = Profile(gen, points, _nodes[from].Height, _nodes[to].Height, def.MaxGrade, def.BridgeClearance, bridges: true);
        _chains.Add(new RoadChain(_chains.Count, RoadKind.Road, from, to, [.. points], heights, bridge, def.Width));
        if (fromPoi is not null)
            _connected[fromPoi.Index] = RoadKind.Road;
        if (toPoi is not null)
            _connected[toPoi.Index] = RoadKind.Road;
    }

    // Cuts the road where it enters a POI's footprint, so it stops at the edge of the pad. False when nothing is left.
    private static bool Trim(List<Vector2> points, PoiInstance poi, bool atStart)
    {
        var r = MathF.Max(8f, poi.Radius * 0.75f);
        if (atStart)
            points.Reverse();
        // From the far end, find the first point inside the circle; cut the line where it crosses.
        var k = points.FindIndex(p => Vector2.Distance(p, poi.Centre) < r);
        var ok = k != 0;
        if (k > 0)
        {
            var (a, b) = (points[k - 1], points[k]);
            var (da, db) = (Vector2.Distance(a, poi.Centre), Vector2.Distance(b, poi.Centre));
            var cut = Vector2.Lerp(a, b, (da - r) / MathF.Max(1e-3f, da - db));
            points.RemoveRange(k, points.Count - k);
            if (Vector2.Distance(points[^1], cut) > 0.5f)
                points.Add(cut);
            else
                points[^1] = cut;
        }
        if (atStart)
            points.Reverse();
        return ok && points.Count >= 2;
    }

    // Straightens a route of cell centres: from each point, the furthest later point a straight line reaches over clear
    // ground (no deep water, other POIs or steeper climb than the route had; water only where the route crossed it too).
    private static List<Vector2> Simplify(Grid grid, List<Vector2> line, HashSet<int> owners, float maxGrade, bool allowWater)
    {
        if (line.Count <= 2)
            return line;
        var wet = new int[line.Count + 1];
        for (var k = 0; k < line.Count; k++)
            wet[k + 1] = wet[k] + (grid.KindAt(line[k]) == Grid.Water ? 1 : 0);
        var result = new List<Vector2> { line[0] };
        var i = 0;
        while (i < line.Count - 1)
        {
            var best = i + 1;
            for (var j = i + 2; j < line.Count; j++)
            {
                if (Vector2.Distance(line[i], line[j]) > MaxStraight)
                    break;
                var water = allowWater && wet[j + 1] - wet[i] > 0;
                if (grid.Clear(line[i], line[j], owners, maxGrade, water))
                    best = j;
            }
            result.Add(line[best]);
            i = best;
        }
        return result;
    }

    // A centripetal Catmull-Rom curve through the points, sampled about every SampleStep metres.
    private static List<Vector2> Curve(List<Vector2> points)
    {
        if (points.Count == 2)
        {
            var straight = new List<Vector2>();
            var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(points[0], points[1]) / SampleStep));
            for (var s = 0; s <= steps; s++)
                straight.Add(Vector2.Lerp(points[0], points[1], (float)s / steps));
            return straight;
        }
        var result = new List<Vector2> { points[0] };
        for (var k = 0; k + 1 < points.Count; k++)
        {
            var p1 = points[k];
            var p2 = points[k + 1];
            var p0 = k > 0 ? points[k - 1] : 2f * p1 - p2;
            var p3 = k + 2 < points.Count ? points[k + 2] : 2f * p2 - p1;
            float t0 = 0f, t1 = t0 + Knot(p0, p1), t2 = t1 + Knot(p1, p2), t3 = t2 + Knot(p2, p3);
            var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(p1, p2) / SampleStep));
            for (var s = 1; s <= steps; s++)
            {
                var t = t1 + (t2 - t1) * s / steps;
                var a1 = Lerp(p0, p1, t0, t1, t);
                var a2 = Lerp(p1, p2, t1, t2, t);
                var a3 = Lerp(p2, p3, t2, t3, t);
                var b1 = Lerp(a1, a2, t0, t2, t);
                var b2 = Lerp(a2, a3, t1, t3, t);
                result.Add(Lerp(b1, b2, t1, t2, t));
            }
        }
        return result;

        static float Knot(Vector2 a, Vector2 b) => MathF.Max(1e-3f, MathF.Sqrt(Vector2.Distance(a, b)));

        static Vector2 Lerp(Vector2 a, Vector2 b, float ta, float tb, float t) => tb - ta < 1e-6f ? a : a + (b - a) * ((t - ta) / (tb - ta));
    }

    // The road surface's heights along a centre line: the ground smoothed, raised clear of the water over bridges, held to
    // the maximum grade, and meeting the given heights at both ends. Bridge samples are those over water, widened a little
    // so each bridge rests on its banks.
    private static (float[] Heights, bool[] Bridge) Profile(WorldGenerator gen, List<Vector2> points, float start, float end, float maxGrade,
        float clearance, bool bridges)
    {
        var n = points.Count;
        var ground = new float[n];
        var water = new bool[n];
        var sea = gen.SeaLevel;
        for (var k = 0; k < n; k++)
        {
            ground[k] = gen.GroundBeforeRoads(points[k].X, points[k].Y);
            water[k] = bridges && ground[k] < sea + Wet;
        }
        var deck = sea + clearance;
        var smooth = new float[n];
        const int window = 6;
        for (var k = 0; k < n; k++)
        {
            float sum = 0f, count = 0f;
            for (var w = Math.Max(0, k - window); w <= Math.Min(n - 1, k + window); w++)
            {
                sum += water[w] ? deck : ground[w];
                count++;
            }
            smooth[k] = sum / count;
            if (water[k])
                smooth[k] = MathF.Max(smooth[k], deck);
        }
        var along = new float[n];
        for (var k = 1; k < n; k++)
            along[k] = along[k - 1] + Vector2.Distance(points[k - 1], points[k]);
        var heights = Grade(smooth, along, start, end, maxGrade);
        for (var k = 0; k < n; k++)
        {
            if (water[k] && heights[k] < sea + 0.8f)
                heights[k] = sea + 0.8f;
        }
        var bridge = new bool[n];
        for (var k = 0; k < n; k++)
        {
            if (!water[k])
                continue;
            for (var w = Math.Max(0, k - 2); w <= Math.Min(n - 1, k + 2); w++)
                bridge[w] = true;
        }
        // Close short gaps between bridge spans, and never start a bridge on the end point.
        for (var k = 1; k < n; k++)
        {
            if (bridge[k] || !bridge[k - 1])
                continue;
            var next = Array.IndexOf(bridge, true, k);
            if (next > 0 && next - k <= 4)
                for (var w = k; w < next; w++)
                    bridge[w] = true;
        }
        bridge[0] = bridge[^1] = false;
        return (heights, bridge);
    }

    // Holds a wanted profile to a maximum grade with fixed ends: a pass forward from the start, then back from the end.
    private static float[] Grade(float[] wanted, float[] along, float start, float end, float maxGrade)
    {
        var n = wanted.Length;
        var h = (float[])wanted.Clone();
        h[0] = start;
        for (var k = 1; k < n; k++)
        {
            var step = maxGrade * (along[k] - along[k - 1]);
            h[k] = Math.Clamp(h[k], h[k - 1] - step, h[k - 1] + step);
        }
        h[n - 1] = end;
        for (var k = n - 2; k >= 0; k--)
        {
            var step = maxGrade * (along[k + 1] - along[k]);
            h[k] = Math.Clamp(h[k], h[k + 1] - step, h[k + 1] + step);
        }
        return h;
    }

    /// <summary>
    /// Runs dirt tracks (World Map spec §3, secondary roads) from every POI that asks for one to the nearest road or track
    /// within <see cref="RoadsDefinition.TrackReach"/>, once the spaced POIs are placed.
    /// </summary>
    internal void AddTracks(WorldGenerator gen)
    {
        if (_def is not { } def || _grid is not { } grid)
            return;
        grid.Refresh(gen);
        // Tracks head for the cells the built roads run through (a routed cell can end up off the smoothed road).
        var goal = new bool[grid.Count];
        foreach (var chain in _chains)
        {
            foreach (var point in chain.Points)
                goal[grid.CellOf(point)] = true;
        }
        var near = grid.Proximity(goal, def.TrackReach);
        foreach (var poi in gen.Pois)
        {
            if (LinkOf(poi) != RoadLinks.Track || _connected.ContainsKey(poi.Index))
                continue;
            // A site's track is part of the map, so it runs however far the road is; scattered POIs only take a near one.
            if (poi.Site is null && !near[grid.CellOf(poi.Centre)])
                continue;
            var limit = def.TrackReach * (poi.Site is null ? 3f : 12f);
            var cells = grid.Route(grid.CellOf(poi.Centre), -1, poi.Index, -1, def.TrackMaxGrade, 0f, goal, 1f, limit);
            if (cells is null || cells.Count < 2)
                continue;
            // The track meets the road (or track) at the nearest point of its centre line to the last cell.
            var last = grid.Centre(cells[^1]);
            if (Nearest(last.X, last.Y, MaxBlend) is not { } join)
                continue;
            var line = cells.Take(cells.Count - 1).Select(grid.Centre).Append(join.Point).ToList();
            var points = Curve(Simplify(grid, line, [poi.Index], def.TrackMaxGrade, allowWater: false));
            if (!Trim(points, poi, atStart: true))
                continue;
            var (heights, bridge) = Profile(gen, points, poi.PadHeight, join.Height, def.TrackMaxGrade, 1f, bridges: true);
            var from = AddNode(RoadNodeKind.End, points[0], poi.PadHeight, 0f, poi.Index);
            var to = AddNode(RoadNodeKind.Join, points[^1], join.Height, 0f, -1);
            _chains.Add(new RoadChain(_chains.Count, RoadKind.Track, from, to, [.. points], heights, bridge, def.TrackWidth));
            _connected[poi.Index] = RoadKind.Track;
            foreach (var point in points)
                goal[grid.CellOf(point)] = true;
            // Later tracks may join this one.
            IndexChain(_chains[^1], _segments);
        }
        _grid = null;
    }

    /// <summary>
    /// The routing grid: one cell per <see cref="RoadsDefinition.Cell"/> metres over the world, with the ground height at
    /// each cell's centre, whether it is dry, water to bridge or too deep, and which POI's footprint (if any) covers it.
    /// </summary>
    private sealed class Grid
    {
        public const byte Land = 0;
        public const byte Water = 1;
        public const byte Deep = 2;

        private static readonly (int Di, int Dj)[] Directions = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

        private readonly float _cell;
        private readonly float _x0, _z0;
        private readonly int _nx, _nz;
        private readonly float[] _height;
        private readonly byte[] _kind;
        private readonly int[] _owner;
        private readonly float _sea;
        private int _poisSeen;

        public Grid(WorldGenerator gen, float cell)
        {
            _cell = cell;
            _nx = (int)MathF.Ceiling(gen.WorldSize / cell);
            _nz = (int)MathF.Ceiling(gen.WorldDepth / cell);
            _x0 = -_nx * cell / 2f;
            _z0 = -_nz * cell / 2f;
            _sea = gen.SeaLevel;
            _height = new float[_nx * _nz];
            _kind = new byte[_nx * _nz];
            _owner = new int[_nx * _nz];
            Array.Fill(_owner, -1);
            Parallel.For(0, _nz, j =>
            {
                for (var i = 0; i < _nx; i++)
                    Sample(gen, j * _nx + i);
            });
            Refresh(gen);
        }

        public int Count => _nx * _nz;

        private void Sample(WorldGenerator gen, int cell)
        {
            var c = Centre(cell);
            var h = gen.InWorld(c.X, c.Y) ? gen.GroundBeforeRoads(c.X, c.Y) : _sea - 100f;
            _height[cell] = h;
            _kind[cell] = h < _sea - DeepWater ? Deep : h < _sea + Wet ? Water : Land;
        }

        // Marks the footprints of POIs placed since the last look, and re-samples the ground they flattened.
        public void Refresh(WorldGenerator gen)
        {
            for (; _poisSeen < gen.Pois.Count; _poisSeen++)
            {
                var poi = gen.Pois[_poisSeen];
                var reach = poi.Radius + PoiMargin;
                var (i0, j0) = Coords(CellOf(poi.Centre - new Vector2(reach)));
                var (i1, j1) = Coords(CellOf(poi.Centre + new Vector2(reach)));
                for (var j = j0; j <= j1; j++)
                {
                    for (var i = i0; i <= i1; i++)
                    {
                        var cell = j * _nx + i;
                        if (Vector2.Distance(Centre(cell), poi.Centre) >= reach)
                            continue;
                        if (_owner[cell] < 0)
                            _owner[cell] = poi.Index;
                        Sample(gen, cell);
                    }
                }
            }
        }

        public Vector2 Centre(int cell) => new(_x0 + (cell % _nx + 0.5f) * _cell, _z0 + (cell / _nx + 0.5f) * _cell);

        public (int I, int J) Coords(int cell) => (cell % _nx, cell / _nx);

        public int CellOf(Vector2 p)
        {
            var i = Math.Clamp((int)MathF.Floor((p.X - _x0) / _cell), 0, _nx - 1);
            var j = Math.Clamp((int)MathF.Floor((p.Y - _z0) / _cell), 0, _nz - 1);
            return j * _nx + i;
        }

        public byte KindAt(Vector2 p) => _kind[CellOf(p)];

        // Index of the POI whose footprint covers a cell, or -1.
        public int Owner(int cell) => _owner[cell];

        // Which cells lie within reach (metres, along the grid) of a marked cell.
        public bool[] Proximity(bool[] marked, float reach)
        {
            var distance = new float[Count];
            Array.Fill(distance, float.PositiveInfinity);
            var queue = new PriorityQueue<int, float>();
            for (var cell = 0; cell < Count; cell++)
            {
                if (!marked[cell])
                    continue;
                distance[cell] = 0f;
                queue.Enqueue(cell, 0f);
            }
            while (queue.TryDequeue(out var cell, out var d))
            {
                if (d > distance[cell])
                    continue;
                foreach (var (next, length) in Neighbours(cell))
                {
                    var c = d + length;
                    if (c <= reach && c < distance[next])
                    {
                        distance[next] = c;
                        queue.Enqueue(next, c);
                    }
                }
            }
            return [.. distance.Select(x => x <= reach)];
        }

        public IEnumerable<(int Cell, float Length)> Neighbours(int cell)
        {
            var (i, j) = Coords(cell);
            foreach (var (di, dj) in Directions)
            {
                int ni = i + di, nj = j + dj;
                if (ni < 0 || nj < 0 || ni >= _nx || nj >= _nz)
                    continue;
                yield return (nj * _nx + ni, di != 0 && dj != 0 ? _cell * 1.41421356f : _cell);
            }
        }

        // Whether a straight line stays on ground a route may use: not deep, not another POI's, water only when allowed,
        // and no steeper between cells than the grade allows (with a little slack: the profile smooths it).
        public bool Clear(Vector2 a, Vector2 b, HashSet<int> owners, float maxGrade, bool water)
        {
            var length = Vector2.Distance(a, b);
            var steps = Math.Max(1, (int)MathF.Ceiling(length / (_cell / 4f)));
            var last = -1;
            for (var s = 0; s <= steps; s++)
            {
                var cell = CellOf(Vector2.Lerp(a, b, (float)s / steps));
                if (cell == last)
                    continue;
                if (_kind[cell] == Deep || (_kind[cell] == Water && !water) || (_owner[cell] >= 0 && !owners.Contains(_owner[cell])))
                    return false;
                if (last >= 0 && MathF.Abs(_height[cell] - _height[last]) > 1.4f * maxGrade * Vector2.Distance(Centre(cell), Centre(last)))
                    return false;
                last = cell;
            }
            return true;
        }

        /// <summary>
        /// The cheapest route from <paramref name="start"/> to <paramref name="goal"/> (or, with goal -1, to any cell marked in
        /// <paramref name="goals"/>), as cells; null when there is none within <paramref name="limit"/>.
        /// </summary>
        public List<int>? Route(int start, int goal, int startOwner, int goalOwner, float maxGrade, float bridgeCost, bool[] goals, float reuse,
            float limit = float.PositiveInfinity)
        {
            var cost = new Dictionary<int, float> { [start] = 0f };
            var from = new Dictionary<int, int>();
            var queue = new PriorityQueue<int, float>();
            var target = goal >= 0 ? Centre(goal) : Vector2.Zero;
            queue.Enqueue(start, 0f);
            while (queue.TryDequeue(out var cell, out var priority))
            {
                var g = cost[cell];
                if (goal >= 0 ? cell == goal : goals[cell] && cell != start)
                {
                    var path = new List<int> { cell };
                    while (from.TryGetValue(path[^1], out var back))
                        path.Add(back);
                    path.Reverse();
                    return path;
                }
                var h = goal >= 0 ? Greed * Vector2.Distance(Centre(cell), target) : 0f;
                if (priority > g + h + 1e-3f || g > limit)
                    continue;
                foreach (var (next, length) in Neighbours(cell))
                {
                    var kind = _kind[next];
                    if (kind == Deep || (kind == Water && bridgeCost <= 0f))
                        continue;
                    var owner = _owner[next];
                    if (owner >= 0 && owner != startOwner && owner != goalOwner)
                        continue;
                    var grade = MathF.Abs(_height[next] - _height[cell]) / length;
                    var m = 1f + 1.5f * (grade / maxGrade) * (grade / maxGrade);
                    if (grade > maxGrade)
                        m += 25f * (grade - maxGrade) / maxGrade;
                    if (kind == Water || _kind[cell] == Water)
                        m *= bridgeCost;
                    if (goals[next] && goals[cell] && goal >= 0)
                        m *= reuse;
                    var c = g + length * m;
                    if (c < cost.GetValueOrDefault(next, float.PositiveInfinity))
                    {
                        cost[next] = c;
                        from[next] = cell;
                        queue.Enqueue(next, c + (goal >= 0 ? Greed * Vector2.Distance(Centre(next), target) : 0f));
                    }
                }
            }
            return null;
        }
    }
}
