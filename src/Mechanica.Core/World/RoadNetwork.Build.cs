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

    // Radius of the rounded kerb line where two arms of a junction meet, in metres. A junction's plate reaches this far
    // past the road's edge down each arm, which is where its stop lines are.
    private const float Fillet = 6f;

    // Metres a road runs straight on past the mouth of its junction's arm before it bends into its route.
    private const float ArmStraight = 12f;

    // Metres beyond that straight that the route is cleared of points, so the bend into it is gentle.
    private const float ArmBend = 50f;

    // Radius in metres a road's centre line bends round its corners at least, where its straights are long enough.
    private const float BendRadius = 15f;

    // Corners of a straightened route that turn less than this (radians) are left to the curve to round.
    private const float MinRound = 0.35f;

    // Pieces the bend from an arm into its route is laid out in.
    private const int BendSteps = 5;

    // Metres past a junction's plate over which a road eases from level into its full grade.
    private const float Ramp = 20f;

    // Metres out from a junction that a road's heading is read from.
    private const float ArmSight = 80f;

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
        var mouth = def.Width / 2f + Fillet;
        for (var c = 0; c < clusters.Count; c++)
        {
            foreach (var cell in clusters[c])
                cluster[cell] = c;
            var at = clusters[c].Aggregate(Vector2.Zero, (sum, cell) => sum + grid.Centre(cell)) / clusters[c].Count;
            var height = MathF.Max(gen.GroundBeforeRoads(at.X, at.Y), gen.SeaLevel + def.BridgeClearance);
            junctionNode[c] = AddNode(RoadNodeKind.Junction, at, height, mouth, -1);
        }

        // The ways between nodes: their cells, and the junction (or site) at each end.
        var ways = new List<(List<int> Cells, int From, int To, PoiInstance? FromPoi, PoiInstance? ToPoi)>();
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
                ways.Add((cells, fromCluster >= 0 ? junctionNode[fromCluster] : -1, toCluster >= 0 ? junctionNode[toCluster] : -1,
                    ends.GetValueOrDefault(start), ends.GetValueOrDefault(cur)));
            }
        }

        // Each junction turns to fit the ways that meet there and gives each its own arm at right angles to the others.
        var arms = new Dictionary<(int Way, bool AtStart), Vector2>();
        var dropped = new HashSet<int>();
        foreach (var j in junctionNode)
        {
            var meeting = new List<(int Way, bool AtStart)>();
            for (var w = 0; w < ways.Count; w++)
            {
                if (dropped.Contains(w))
                    continue;
                if (ways[w].From == j)
                    meeting.Add((w, true));
                if (ways[w].To == j)
                    meeting.Add((w, false));
            }
            if (meeting.Count == 0)
                continue;
            if (meeting.Count > 4)
            {
                // A crossroads takes four roads at most: keep those that lead to a POI, then the shortest, and drop the
                // rest (links between junctions the network can do without; the plate closes their arms).
                foreach (var extra in meeting.OrderByDescending(m => (m.AtStart ? ways[m.Way].ToPoi : ways[m.Way].FromPoi) is not null)
                    .ThenBy(m => ways[m.Way].Cells.Count).ThenBy(m => m.Way).Skip(4).ToList())
                    dropped.Add(extra.Way);
                meeting.RemoveAll(m => dropped.Contains(m.Way));
            }
            var node = _nodes[j];
            var away = meeting.Select(m => Away(grid, ways[m.Way].Cells, m.AtStart, node.At, cluster)).ToList();
            var (heading, slots) = Square(away);
            _nodes[j] = node = node with { Heading = heading, Arms = slots.Aggregate(0, (mask, k) => mask | 1 << k) };
            for (var m = 0; m < meeting.Count; m++)
                arms[meeting[m]] = node.Arm(slots[m]);
        }
        var roads = new List<(List<Vector2> Points, int From, int To, PoiInstance? FromPoi, PoiInstance? ToPoi)>();
        for (var w = 0; w < ways.Count; w++)
        {
            if (dropped.Contains(w))
                continue;
            var (cells, from, to, fromPoi, toPoi) = ways[w];
            var points = Lay(def, grid, cells, from, to, fromPoi, toPoi, cluster,
                arms.TryGetValue((w, true), out var fromArm) ? fromArm : null, arms.TryGetValue((w, false), out var toArm) ? toArm : null);
            if (points is null)
                continue;
            from = from >= 0 ? from : AddNode(RoadNodeKind.End, points[0], fromPoi?.PadHeight ?? gen.GroundBeforeRoads(points[0].X, points[0].Y), 0f, fromPoi?.Index ?? -1);
            to = to >= 0 ? to : AddNode(RoadNodeKind.End, points[^1], toPoi?.PadHeight ?? gen.GroundBeforeRoads(points[^1].X, points[^1].Y), 0f, toPoi?.Index ?? -1);
            roads.Add((points, from, to, fromPoi, toPoi));
        }
        var flat = def.Width / 2f + Fillet + 1f;
        foreach (var j in junctionNode)
        {
            var node = _nodes[j];
            // Arms whose road came to nothing are closed off, and the plate sits at a height every road out of it can climb
            // from within its grade, level across the plate, as near the ground as that allows.
            var open = 0;
            float low = float.NegativeInfinity, high = float.PositiveInfinity;
            foreach (var (points, from, to, _, _) in roads)
            {
                foreach (var atStart in new[] { true, false }.Where(s => (s ? from : to) == j))
                {
                    // A little way out down the road (a road trimmed short at a POI may have only two points).
                    var near = atStart ? points[Math.Min(2, points.Count - 1)] : points[Math.Max(0, points.Count - 3)];
                    open |= 1 << Enumerable.Range(0, 4).MaxBy(k => Vector2.Dot(node.Arm(k), near - node.At));
                    var other = _nodes[atStart ? to : from];
                    if (other.Kind == RoadNodeKind.Junction)
                        continue;
                    var length = 0f;
                    for (var k = 1; k < points.Count; k++)
                        length += Vector2.Distance(points[k - 1], points[k]);
                    var reach = def.MaxGrade * MathF.Max(0f, length - flat - Ramp / 2f);
                    low = MathF.Max(low, other.Height - reach);
                    high = MathF.Min(high, other.Height + reach);
                }
            }
            var height = low <= high ? Math.Clamp(node.Height, low, high) : (low + high) / 2f;
            _nodes[j] = node with { Height = MathF.Max(height, gen.SeaLevel + def.BridgeClearance), Arms = node.Arms == 0 ? 0 : open };
        }
        foreach (var (points, from, to, fromPoi, toPoi) in roads)
        {
            // Level across each junction's plate.
            var (heights, bridge) = Profile(gen, points, _nodes[from].Height, _nodes[to].Height, def.MaxGrade, def.BridgeClearance, bridges: true,
                _nodes[from].Kind == RoadNodeKind.Junction ? flat : 0f, _nodes[to].Kind == RoadNodeKind.Junction ? flat : 0f);
            _chains.Add(new RoadChain(_chains.Count, RoadKind.Road, from, to, [.. points], heights, bridge, def.Width));
            if (fromPoi is not null)
                _connected[fromPoi.Index] = RoadKind.Road;
            if (toPoi is not null)
                _connected[toPoi.Index] = RoadKind.Road;
        }
    }

    // Which way a road heads out of a junction: towards its route a little way off, past the bends of the cells nearest it.
    private static Vector2 Away(Grid grid, List<int> cells, bool atStart, Vector2 centre, Dictionary<int, int> cluster)
    {
        var order = atStart ? cells : Enumerable.Reverse(cells);
        var far = centre;
        foreach (var cell in order.Where(c => !cluster.ContainsKey(c)))
        {
            far = grid.Centre(cell);
            if (Vector2.Distance(far, centre) >= ArmSight)
                break;
        }
        var d = far - centre;
        return d.LengthSquared() > 1e-6f ? Vector2.Normalize(d) : Vector2.UnitX;
    }

    // The heading of a junction's first arm, and the arm (0..3, each at a right angle to the next) each way gets, that bend
    // the ways the least: every way its own arm, the sum of the squared turns smallest.
    internal static (float Heading, int[] Slots) Square(IReadOnlyList<Vector2> away)
    {
        var angles = away.Select(d => MathF.Atan2(d.Y, d.X)).ToArray();
        var best = (Cost: float.PositiveInfinity, Heading: 0f, Slots: Array.Empty<int>());
        var slots = new int[angles.Length];
        for (var step = 0; step < 180; step++)
        {
            var heading = step * MathF.PI / 360f;
            Assign(0, 0, 0f);

            void Assign(int k, int used, float cost)
            {
                if (cost >= best.Cost)
                    return;
                if (k == angles.Length)
                {
                    best = (cost, heading, (int[])slots.Clone());
                    return;
                }
                for (var slot = 0; slot < 4; slot++)
                {
                    if ((used & 1 << slot) != 0)
                        continue;
                    slots[k] = slot;
                    var turn = Turn(angles[k] - heading - slot * MathF.PI / 2f);
                    Assign(k + 1, used | 1 << slot, cost + turn * turn);
                }
            }
        }
        return (best.Heading, best.Slots);

        static float Turn(float a) => MathF.IEEERemainder(a, 2f * MathF.PI);
    }

    private int AddNode(RoadNodeKind kind, Vector2 at, float height, float radius, int poi)
    {
        _nodes.Add(new RoadNode(_nodes.Count, kind, at, height, radius, poi));
        return _nodes.Count - 1;
    }

    // The centre line of one chain of cells between two nodes: a smooth road that leaves each junction down its arm and
    // stops at the edge of a site's pad. Null when nothing is left of it.
    private List<Vector2>? Lay(RoadsDefinition def, Grid grid, List<int> cells, int fromJunction, int toJunction,
        PoiInstance? fromPoi, PoiInstance? toPoi, Dictionary<int, int> cluster, Vector2? fromArm, Vector2? toArm)
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
        var points = Curve(Squared(Round(Simplify(grid, line, owners, def.MaxGrade, allowWater: true), BendRadius), fromArm, toArm, def.Width / 2f + Fillet));
        if (fromPoi is not null && Trim(points, fromPoi, atStart: true) is false)
            return null;
        if (toPoi is not null && Trim(points, toPoi, atStart: false) is false)
            return null;
        return points.Count < 2 ? null : points;
    }

    // A route whose ends at junctions run straight down their arms: through the plate's mouth and on a little, then in an
    // even bend (a quadratic Bézier leaving along the arm) into the first route point clear of the junction.
    private static List<Vector2> Squared(List<Vector2> line, Vector2? fromArm, Vector2? toArm, float mouth)
    {
        if (fromArm is null && toArm is null)
            return line;
        var (a, b) = (line[0], line[^1]);
        var straight = mouth + ArmStraight;
        var clear = straight + ArmBend;
        var middle = line.Skip(1).SkipLast(1)
            .Where(p => (fromArm is null || Vector2.Distance(p, a) >= clear) && (toArm is null || Vector2.Distance(p, b) >= clear)).ToList();
        var result = new List<Vector2> { a };
        if (fromArm is { } f)
            result.AddRange(Bend(a, f, middle.Count > 0 ? middle[0] : b));
        result.AddRange(middle);
        if (toArm is { } t)
            result.AddRange(Enumerable.Reverse(Bend(b, t, middle.Count > 0 ? middle[^1] : a)));
        result.Add(b);
        return result;

        // The arm's mouth and the end of its straight, then the bend from there towards the route (not including it): an
        // arc of BendRadius turning towards it until it heads straight for it, so a route that leaves the junction the
        // other way swings round wide instead of doubling back on itself. A quadratic Bézier where the route's next point
        // is too close for that.
        List<Vector2> Bend(Vector2 centre, Vector2 arm, Vector2 to)
        {
            var from = centre + arm * straight;
            var points = new List<Vector2> { centre + arm * mouth, from };
            var reach = Vector2.Distance(from, to);
            if (reach < 1f)
                return points;
            var away = to - from;
            var side = arm.X * away.Y - arm.Y * away.X >= 0f ? 1f : -1f;
            var left = new Vector2(-arm.Y, arm.X) * side;
            var pivot = from + left * BendRadius;
            var off = to - pivot;
            if (off.Length() > BendRadius * 1.05f)
            {
                // The point on the circle where the road leaves it heading straight for `to`.
                var phi = MathF.Atan2(off.Y, off.X);
                var beta = MathF.Acos(BendRadius / off.Length());
                var a0 = MathF.Atan2(from.Y - pivot.Y, from.X - pivot.X);
                var a1 = phi - side * beta;
                var sweep = a1 - a0;
                while (side * sweep < 0f)
                    sweep += side * MathF.Tau;
                while (side * sweep >= MathF.Tau)
                    sweep -= side * MathF.Tau;
                // Heading straight for it already (rounding can put it a hair the other way round the circle).
                if (side * sweep > MathF.Tau - 0.02f)
                    sweep = 0f;
                var steps = (int)MathF.Ceiling(MathF.Abs(sweep) * BendRadius / SampleStep);
                for (var k = 1; k <= steps; k++)
                {
                    var angle = a0 + sweep * k / steps;
                    points.Add(pivot + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * BendRadius);
                }
                return points;
            }
            var control = from + arm * reach * 0.5f;
            for (var k = 1; k < BendSteps; k++)
            {
                var u = (float)k / BendSteps;
                points.Add((1 - u) * (1 - u) * from + 2 * (1 - u) * u * control + u * u * to);
            }
            return points;
        }
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

    // Radians a route turns from one direction to the next (0 straight on, π straight back).
    private static float Turn(Vector2 a, Vector2 b) =>
        a.LengthSquared() < 1e-8f || b.LengthSquared() < 1e-8f ? 0f : MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b)), -1f, 1f));

    // Rounds each corner of a straightened route into an arc of the given radius, sampled along it, so the curve through
    // the points never bends tighter than that where the straights either side leave room (and as wide as they allow
    // where not). Without it the curve passes through every corner, and a sharp one folds the road over itself.
    private static List<Vector2> Round(List<Vector2> points, float radius)
    {
        var n = points.Count;
        if (n < 3)
            return points;
        // How far back along each straight each corner's arc starts: as much as the radius needs, shared out where two
        // arcs want more of one straight than it has (a tighter arc, but still a round one).
        var reach = new float[n];
        var turn = new float[n];
        for (var v = 1; v + 1 < n; v++)
        {
            turn[v] = Turn(points[v] - points[v - 1], points[v + 1] - points[v]);
            reach[v] = turn[v] < MinRound ? 0f : radius * MathF.Tan(MathF.Min(turn[v], MathF.PI - 0.05f) / 2f);
        }
        for (var s = 0; s + 1 < n; s++)
        {
            var length = Vector2.Distance(points[s], points[s + 1]);
            var want = reach[s] + reach[s + 1];
            if (want > length * 0.98f)
            {
                var scale = length * 0.98f / want;
                reach[s] *= scale;
                reach[s + 1] *= scale;
            }
        }
        var result = new List<Vector2> { points[0] };
        for (var v = 1; v + 1 < n; v++)
        {
            if (reach[v] <= 0.01f)
            {
                result.Add(points[v]);
                continue;
            }
            var (inward, outward) = (Vector2.Normalize(points[v] - points[v - 1]), Vector2.Normalize(points[v + 1] - points[v]));
            var (start, end) = (points[v] - inward * reach[v], points[v] + outward * reach[v]);
            // The arc's centre is a radius in from the start, on the inside of the turn.
            var r = reach[v] / MathF.Tan(MathF.Min(turn[v], MathF.PI - 0.05f) / 2f);
            var side = MathF.Sign(inward.X * outward.Y - inward.Y * outward.X);
            var centre = start + new Vector2(-inward.Y, inward.X) * side * r;
            var (a0, a1) = (MathF.Atan2(start.Y - centre.Y, start.X - centre.X), MathF.Atan2(end.Y - centre.Y, end.X - centre.X));
            var sweep = a1 - a0;
            if (side > 0f && sweep < 0f)
                sweep += MathF.Tau;
            if (side < 0f && sweep > 0f)
                sweep -= MathF.Tau;
            var steps = Math.Max(2, (int)MathF.Ceiling(MathF.Abs(sweep) * r / SampleStep));
            for (var k = 0; k <= steps; k++)
            {
                var angle = a0 + sweep * k / steps;
                result.Add(centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * r);
            }
        }
        result.Add(points[^1]);
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
        float clearance, bool bridges, float flatStart = 0f, float flatEnd = 0f)
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
        // The first and last metres given as flat run level: they count as no distance to climb over. Past them the grade
        // allowed grows evenly over Ramp metres, so the road rounds into its climb (the ground's triangles never poke through
        // a sharp crest or dip).
        var climb = new float[n];
        for (var k = 1; k < n; k++)
        {
            var mid = (along[k - 1] + along[k]) / 2f;
            climb[k] = climb[k - 1] + (along[k] - along[k - 1]) * MathF.Min(Rate(mid, flatStart), Rate(along[^1] - mid, flatEnd));
        }
        var heights = Grade(smooth, climb, start, end, maxGrade);
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

    // The share of the full grade a road may climb at `s` metres from an end that runs level for `flat` metres.
    private static float Rate(float s, float flat) => flat <= 0f ? 1f : Math.Clamp((s - flat) / Ramp, 0f, 1f);

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
    // The first POI but its own whose ground a track comes onto, with its half width to spare, looked at every 2 m.
    private static PoiInstance? OverPoi(WorldGenerator gen, List<Vector2> points, PoiInstance own, float halfWidth)
    {
        for (var k = 1; k < points.Count; k++)
        {
            var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(points[k - 1], points[k]) / 2f));
            for (var s = 0; s <= steps; s++)
            {
                var p = Vector2.Lerp(points[k - 1], points[k], (float)s / steps);
                if (gen.PoisNear(p.X, p.Y).FirstOrDefault(o => o != own && Vector2.Distance(p, o.Centre) < o.Radius + halfWidth) is { } hit)
                    return hit;
            }
        }
        return null;
    }

    // A track from a POI to the nearest road or track the grid can route to, as its points and where it joins; null when none.
    private (List<Vector2> Points, RoadHit Join)? PlanTrack(WorldGenerator gen, Grid grid, RoadsDefinition def, PoiInstance poi, bool[] goal, float limit)
    {
        var cells = grid.Route(grid.CellOf(poi.Centre), -1, poi.Index, -1, def.TrackMaxGrade, 0f, goal, 1f, limit);
        if (cells is null || cells.Count < 2)
            return null;
        // The track meets the road (or track) at the nearest point of its centre line to the last cell.
        var last = grid.Centre(cells[^1]);
        if (Nearest(last.X, last.Y, MaxBlend) is not { } join)
            return null;
        var line = cells.Take(cells.Count - 1).Select(grid.Centre).Append(join.Point).ToList();
        // It meets at a right angle, from the side it comes from.
        var along = join.Chain.Points[join.Segment + 1] - join.Chain.Points[join.Segment];
        var side = along.LengthSquared() > 1e-6f ? Vector2.Normalize(new Vector2(-along.Y, along.X)) : Vector2.UnitX;
        if (Vector2.Dot(side, (line.Count > 1 ? line[^2] : grid.Centre(cells[0])) - join.Point) < 0f)
            side = -side;
        var simple = Simplify(grid, line, [poi.Index], def.TrackMaxGrade, allowWater: false);
        var squared = Squared(simple, null, side, join.Chain.Width / 2f + 1f);
        // Unless that would take it over another POI's ground: then it joins as it comes.
        var clear = Enumerable.Range(1, squared.Count - 1).All(k => grid.Clear(squared[k - 1], squared[k], [poi.Index], def.TrackMaxGrade * 1.5f, false));
        var points = Curve(clear ? squared : simple);
        // A rounded corner that clips another POI's ground joins as it comes instead.
        if (clear && OverPoi(gen, points, poi, def.TrackWidth / 2f) is not null)
            points = Curve(simple);
        return (points, join);
    }

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
            // The grid only knows each POI by its cells, so a track can still clip the edge of another POI's ground:
            // then that POI's whole disc is closed to tracks and the route is planned again.
            (List<Vector2> Points, RoadHit Join)? planned = null;
            for (var attempt = 0; attempt < 4 && planned is null; attempt++)
            {
                if (PlanTrack(gen, grid, def, poi, goal, limit) is not { } plan)
                    break;
                if (OverPoi(gen, plan.Points, poi, def.TrackWidth / 2f) is { } clipped)
                    grid.Block(clipped, def.TrackWidth / 2f + 1f);
                else
                    planned = plan;
            }
            if (planned is not { } found)
                continue;
            var (points, join) = found;
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

        // Closes every cell a POI's disc (widened by a margin) touches, not only those whose centre it covers.
        public void Block(PoiInstance poi, float margin)
        {
            var reach = poi.Radius + margin;
            var (i0, j0) = Coords(CellOf(poi.Centre - new Vector2(reach)));
            var (i1, j1) = Coords(CellOf(poi.Centre + new Vector2(reach)));
            for (var j = j0; j <= j1; j++)
            {
                for (var i = i0; i <= i1; i++)
                {
                    var cell = j * _nx + i;
                    var nearest = Vector2.Clamp(poi.Centre, Centre(cell) - new Vector2(_cell / 2f), Centre(cell) + new Vector2(_cell / 2f));
                    if (_owner[cell] < 0 && Vector2.Distance(nearest, poi.Centre) < reach)
                        _owner[cell] = poi.Index;
                }
            }
        }

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
