using System.Runtime.CompilerServices;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Items;

/// <summary>
/// One part an IO port of another reaches (Parts map E1): through the start part's <paramref name="Port"/>, along the
/// conduits <paramref name="Pipes"/> (in order from the start; empty when the two ports touch), to
/// <paramref name="Part"/>'s port <paramref name="RemotePort"/>.
/// </summary>
/// <param name="Port">The start part's port the link leaves through.</param>
/// <param name="Part">The part reached (never a conduit).</param>
/// <param name="RemotePort">The reached part's port.</param>
/// <param name="TakesIn">True when items may go into the reached part through its port.</param>
/// <param name="GivesOut">True when items may come out of the reached part through its port.</param>
/// <param name="Pipes">The conduit parts between, from the start part's side.</param>
public sealed record PipeLink(string Port, int Part, string RemotePort, bool TakesIn, bool GivesOut, IReadOnlyList<int> Pipes);

/// <summary>
/// The item transport network of a Creation (Parts map E1, docs/io-ports.md): which parts' IO ports reach which, through
/// conduits (Vacuum Pipes). Two ports join when they sit on the same block face from either side, facing each other; a
/// conduit passes items from each of its ports to the others. Nothing here is stored: the network is worked out from the
/// Creation's parts and their port cells (and kept until the Creation changes), so a save holds only what the containers
/// hold.
/// </summary>
public static class TransportNetwork
{
    private static readonly ConditionalWeakTable<Creation, Graph> Graphs = [];

    /// <summary>Every part <paramref name="part"/>'s IO ports reach, in port order then part and port id; empty when it has none.</summary>
    public static IReadOnlyList<PipeLink> Reach(Creation creation, int part) =>
        creation.FindPart(part) is null ? [] : GraphOf(creation).Reach(part);

    /// <summary>
    /// The containers (parts with an inventory that are not stations) items can go to from <paramref name="part"/>: through a
    /// port of its that gives out, to a port of theirs that takes in. By part id.
    /// </summary>
    public static IReadOnlyList<int> Into(Creation creation, int part) =>
        [.. Reach(creation, part).Where(l => l.TakesIn && Gives(creation, part, l.Port) && StorageLinks.IsContainer(creation, l.Part))
            .Select(l => l.Part).Distinct().Order()];

    /// <summary>
    /// The containers items can come from into <paramref name="part"/>: through a port of theirs that gives out, to a port of
    /// its that takes in. By part id.
    /// </summary>
    public static IReadOnlyList<int> From(Creation creation, int part) =>
        [.. Reach(creation, part).Where(l => l.GivesOut && Takes(creation, part, l.Port) && StorageLinks.IsContainer(creation, l.Part))
            .Select(l => l.Part).Distinct().Order()];

    /// <summary>
    /// Where a port sits, in the Creation's grid: the cell inside the part it is on and the direction it faces out of
    /// that cell. Another part's port joins it when it sits at <c>(cell + direction, -direction)</c>.
    /// </summary>
    public static (GridCell Cell, GridCell Direction) FaceOf(PartDefinition definition, ItemPortDefinition port, GridCell origin, GridRotation rotation)
    {
        var normal = rotation.Apply(port.Normal ?? Vec3.Zero);
        var direction = new GridCell((int)MathF.Round(normal.X), (int)MathF.Round(normal.Y), (int)MathF.Round(normal.Z));
        var point = PartGeometry.Centre(definition, origin, rotation) + rotation.Apply((port.Position ?? Vec3.Zero) * (1f / MechanicaInfo.GridSize));
        var inside = point - direction.AsVector * 0.5f;
        return (new GridCell((int)MathF.Round(inside.X), (int)MathF.Round(inside.Y), (int)MathF.Round(inside.Z)), direction);
    }

    private static bool Gives(Creation creation, int part, string port) =>
        creation.DefinitionOf(part).IoPorts?.FirstOrDefault(p => p.Id == port)?.GivesOut == true;

    private static bool Takes(Creation creation, int part, string port) =>
        creation.DefinitionOf(part).IoPorts?.FirstOrDefault(p => p.Id == port)?.TakesIn == true;

    private static Graph GraphOf(Creation creation)
    {
        if (Graphs.TryGetValue(creation, out var graph) && graph.Revision == creation.Revision)
            return graph;
        graph = new Graph(creation);
        Graphs.AddOrUpdate(creation, graph);
        return graph;
    }

    // The ports of one Creation by the face they sit on, and the links worked out from each part so far.
    private sealed class Graph
    {
        private readonly Creation _creation;
        private readonly Dictionary<(GridCell Cell, GridCell Direction), (int Part, ItemPortDefinition Port)> _faces = [];
        private readonly Dictionary<int, IReadOnlyList<PipeLink>> _reach = [];

        public Graph(Creation creation)
        {
            _creation = creation;
            Revision = creation.Revision;
            foreach (var part in creation.Parts)
            {
                var definition = creation.DefinitionOf(part.Id);
                foreach (var port in definition.IoPorts ?? [])
                {
                    if (port.Id is not null && port.Normal is not null)
                        _faces.TryAdd(FaceOf(definition, port, part.Cell, part.Rotation), (part.Id, port));
                }
            }
        }

        public int Revision { get; }

        public IReadOnlyList<PipeLink> Reach(int start)
        {
            if (_reach.TryGetValue(start, out var known))
                return known;
            var links = new List<PipeLink>();
            var instance = _creation.FindPart(start)!;
            var definition = _creation.DefinitionOf(start);
            foreach (var port in definition.IoPorts ?? [])
            {
                if (port.Id is null || port.Normal is null)
                    continue;
                var found = new List<PipeLink>();
                Walk(start, port.Id, FaceOf(definition, port, instance.Cell, instance.Rotation), found);
                links.AddRange(found.DistinctBy(l => (l.Part, l.RemotePort)).OrderBy(l => l.Part).ThenBy(l => l.RemotePort, StringComparer.Ordinal));
            }
            return _reach[start] = links;
        }

        // Breadth first from one port of the start part, through conduits, to every other part's port it reaches.
        private void Walk(int start, string startPort, (GridCell Cell, GridCell Direction) face, List<PipeLink> found)
        {
            var seen = new HashSet<int> { start };
            var queue = new Queue<((GridCell, GridCell) Face, IReadOnlyList<int> Pipes)>();
            queue.Enqueue((face, []));
            while (queue.Count > 0)
            {
                var ((cell, direction), pipes) = queue.Dequeue();
                if (!_faces.TryGetValue((cell + direction, -direction), out var other) || other.Part == start)
                    continue;
                var definition = _creation.DefinitionOf(other.Part);
                if (definition.Conduit != true)
                {
                    found.Add(new PipeLink(startPort, other.Part, other.Port.Id!, other.Port.TakesIn, other.Port.GivesOut, pipes));
                    continue;
                }
                if (!seen.Add(other.Part))
                    continue;
                var instance = _creation.FindPart(other.Part)!;
                IReadOnlyList<int> path = [.. pipes, other.Part];
                foreach (var port in definition.IoPorts!)
                {
                    if (port != other.Port && port.Normal is not null)
                        queue.Enqueue((FaceOf(definition, port, instance.Cell, instance.Rotation), path));
                }
            }
        }
    }
}
