using Godot;
using Mechanica.Core.Settings;
using Mechanica.Core.World;
using Mechanica.Game.Settings;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.World;

/// <summary>
/// Draws decorative flora (grass, ground cover, bushes) on the loaded chunks and scenery round the camera, GPU-instanced:
/// per chunk, kind and model one <see cref="MultiMesh"/> with the full mesh for near chunks, one with the far mesh beyond
/// <see cref="FloraSettings.LodShare"/> of the draw distance and, for grass and ground cover past
/// <see cref="FloraSettings.ThinShare"/>, one with a larger few of the plants. Plants are grown off the main thread
/// (<see cref="FloraScatter"/>, at the densest level) and drawn at the player's Foliage density level for each kind
/// (Options, Graphics), which sets how many of them are drawn, how far, and how much they sway; changing a level rebuilds
/// the chunks from the plants already grown. Flora has no colliders: everything walks and drives through it, and it bends
/// out of the way of the players, bots and vehicles nearest the camera.
/// <para>Nothing pops: plants sink away towards the end of each kind's distance (never past the view distance, where
/// the ground ends), the meshes of a chunk cross-fade where they hand over, a chunk's plants rise out of the ground when first drawn,
/// and when a chunk's ground changes (scenery turning into a loaded chunk) its old plants stay until the new ones are
/// built.</para>
/// </summary>
public partial class FloraField : Node3D
{
    /// <summary>Where the tuning is read from.</summary>
    public const string DefaultSettingsPath = "res://data/flora.json";

    private static readonly FloraKind[] Kinds = [FloraKind.Grass, FloraKind.GroundCover, FloraKind.Bush];

    private readonly Dictionary<ChunkCoord, Patch> _patches = [];
    private readonly Dictionary<FloraKind, FoliageLevel> _levels = [];
    private readonly List<(Node3D Node, float Depth, double Age)> _appearing = [];
    private float _view = -1f;

    /// <summary>The world whose loaded chunks grow flora. Set before adding to the tree.</summary>
    public WorldStreamer World { get; init; } = null!;

    /// <summary>Where the players, bots and Creation bodies are, for bending plants out of their way.</summary>
    public Func<IEnumerable<Vector3>> Pushers { get; init; } = () => [];

    /// <summary>Tuning; null reads <see cref="DefaultSettingsPath"/> when the node starts.</summary>
    public FloraSettings? Settings { get; set; }

    /// <summary>
    /// Draws flora. Off without a display (test scenes and dedicated hosts draw nothing anyone sees); a test that checks
    /// flora turns it on.
    /// </summary>
    public bool Draw { get; set; } = DisplayServer.GetName() != "headless";

    /// <summary>Where flora is drawn round; null uses the current camera, or the spawn before there is one.</summary>
    public Func<Vector3>? Viewer { get; set; }

    /// <summary>The chunks with flora built, each a node holding one child per kind drawn.</summary>
    public IEnumerable<(ChunkCoord Chunk, Node3D Node)> Patches => _patches.Select(p => (p.Key, p.Value.Node));

    /// <summary>How many plants of a kind are drawn in total (counting each once, not per mesh).</summary>
    public int Drawn(FloraKind kind) => _patches.Values.Sum(p => p.Drawn.GetValueOrDefault(kind));

    /// <summary>Reads the tuning from <see cref="DefaultSettingsPath"/>.</summary>
    public static FloraSettings LoadDefaultSettings()
    {
        using var file = FileAccess.Open(DefaultSettingsPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {DefaultSettingsPath}: {FileAccess.GetOpenError()}");
        return FloraSettings.FromJson(file.GetAsText());
    }

    public override void _Ready()
    {
        Settings ??= LoadDefaultSettings();
        FloraMaterials.EnsureGlobals();
    }

    public override void _ExitTree()
    {
        foreach (var name in FloraMaterials.Pushers)
            RenderingServer.GlobalShaderParameterSet(name, Vector4.Zero);
    }

    public override void _Process(double delta)
    {
        Step(Settings!.BuildBudget);
        Appear(delta);
    }

    /// <summary>Builds every patch in reach now, waiting for plants still growing. For tests and captures.</summary>
    public void BuildAll()
    {
        for (var i = 0; i < 100 && Step(double.PositiveInfinity, wait: true) > 0; i++)
        {
        }
    }

    // Updates the wind and pushers, drops patches out of reach and builds the nearest missing or stale ones within the
    // budget. Returns how many patches still need building.
    private int Step(double budget, bool wait = false)
    {
        var settings = Settings!;
        if (!Draw || World.Generator is null)
        {
            Clear();
            return 0;
        }
        var graphics = GameSettings.Current.Graphics;
        var changed = false;
        var view = World.ViewDistance ?? graphics.Effective().ViewDistance;
        if (view != _view)
        {
            // Every kind's distance may change: rebuild every patch, drawing the old plants until the new ones are in.
            _view = view;
            _levels.Clear();
            foreach (var patch in _patches.Values)
                patch.Built.Clear();
        }
        foreach (var kind in Kinds)
        {
            var level = graphics.Foliage(kind);
            if (_levels.TryGetValue(kind, out var was) && was == level)
                continue;
            _levels[kind] = level;
            ConfigureMaterial(kind);
            changed = true;
        }
        var viewer = Viewer?.Invoke() ?? GetViewport()?.GetCamera3D()?.GlobalPosition ?? World.SpawnPoint();
        UpdateGlobals(viewer);
        // Once every patch in reach is built, nothing changes until the chunks, the levels or the settings do, or the
        // viewer moves (well inside the margins patches are kept and built with).
        if (!wait && !changed && _settled is var (revision, at, built) && revision == World.Revision && built == settings
            && at.DistanceTo(viewer) < SettleDistance)
            return 0;
        _settled = null;

        var reach = Kinds.Max(Reach);
        foreach (var (coord, patch) in _patches.ToList())
        {
            var inReach = RectDistance(patch.Chunk, viewer) <= reach + 16f;
            if (inReach && Ground(coord, out var chunk))
            {
                // Scenery turned into a loaded chunk (or back): grow its plants again, drawing the old ones meanwhile.
                if (chunk != patch.Chunk)
                    patch.Regrow(chunk);
                continue;
            }
            // The scenery made way for a chunk still being built: keep its plants until the chunk is in.
            if (inReach && World.IsPending(coord))
                continue;
            patch.Node.QueueFree();
            _patches.Remove(coord);
        }
        if (reach <= 0f)
            return 0;
        foreach (var (coord, chunk) in World.Chunks.Concat(World.Scenery))
        {
            if (!_patches.ContainsKey(coord) && RectDistance(chunk, viewer) <= reach)
            {
                var node = new Node3D { Name = $"Flora_{coord.X}_{coord.Z}", Position = Centre(chunk) };
                AddChild(node, forceReadableName: true);
                _patches[coord] = new Patch(chunk, node);
            }
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var waiting = 0;
        foreach (var patch in _patches.Values.OrderBy(p => RectDistance(p.Chunk, viewer)))
        {
            var distance = RectDistance(patch.Chunk, viewer);
            foreach (var kind in Kinds)
            {
                var level = _levels[kind];
                var wanted = distance <= Reach(kind) + 8f ? level : FoliageLevel.Off;
                if (patch.Built.GetValueOrDefault(kind, FoliageLevel.Off) == wanted && (wanted != FoliageLevel.Off || !patch.Kinds.ContainsKey(kind)))
                    continue;
                if (wanted == FoliageLevel.Off)
                {
                    patch.Remove(kind);
                    continue;
                }
                if (!patch.Grown.TryGetValue(kind, out var growing))
                {
                    var (generator, data, density) = (World.Generator, patch.Chunk.Data, settings.MaxDensity);
                    patch.Grown[kind] = growing = Task.Run(() => FloraScatter.Grow(generator, data, kind, density));
                }
                if (wait)
                    growing.Wait();
                if (!growing.IsCompleted)
                {
                    waiting++;
                    continue;
                }
                if (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds >= budget)
                {
                    waiting++;
                    continue;
                }
                // Plants replacing ones already drawn take over at once; plants new to the patch grow in.
                var fresh = !patch.Kinds.ContainsKey(kind);
                patch.Remove(kind);
                var (node, count) = Build(patch, kind, level, growing.Result);
                if (node is not null)
                {
                    patch.Kinds[kind] = node;
                    if (fresh && !wait && settings.AppearTime > 0f)
                        StartAppearing(node, kind);
                }
                patch.Drawn[kind] = count;
                patch.Built[kind] = level;
            }
        }
        if (waiting == 0)
            _settled = (World.Revision, viewer, settings);
        return waiting;
    }

    // How far the viewer moves before a settled field looks again, in m.
    private const float SettleDistance = 2f;

    // Where the field last had nothing left to build: the chunks' revision, the viewer and the settings then.
    private (int Revision, Vector3 Viewer, FloraSettings Settings)? _settled;

    // Metres a kind is drawn at its level: no farther than the view distance, so plants have sunk away where the ground ends.
    private float Reach(FloraKind kind) => Mathf.Min(Settings!.Distance(kind, _levels[kind]), _view);

    // Raises a newly built kind's plants out of the ground over the appear time. Moving the kind's node costs nothing
    // per plant (a per-mesh shader parameter would use up the renderer's instance-parameter buffer).
    private void StartAppearing(Node3D node, FloraKind kind)
    {
        var depth = kind == FloraKind.Bush ? BushSink : PlantSink;
        node.Position = new Vector3(0f, -depth, 0f);
        _appearing.Add((node, depth, 0.0));
    }

    // Metres plants start below the ground when they appear: deeper than the tallest bush or tuft.
    private const float BushSink = 2.5f, PlantSink = 1f;

    private void Appear(double delta)
    {
        if (_appearing.Count == 0)
            return;
        var time = Settings!.AppearTime;
        for (var i = _appearing.Count - 1; i >= 0; i--)
        {
            var (node, depth, age) = _appearing[i];
            age += delta;
            var done = !IsInstanceValid(node) || node.IsQueuedForDeletion() || age >= time;
            if (IsInstanceValid(node))
                node.Position = new Vector3(0f, done ? 0f : -depth * (1f - Mathf.SmoothStep(0f, 1f, (float)(age / time))), 0f);
            if (done)
                _appearing.RemoveAt(i);
            else
                _appearing[i] = (node, depth, age);
        }
    }

    // A loaded chunk, or failing that scenery, at a coordinate.
    private bool Ground(ChunkCoord coord, out TerrainChunk chunk) =>
        World.Chunks.TryGetValue(coord, out chunk!) || World.Scenery.TryGetValue(coord, out chunk!);

    // A node holding the kind's meshes, or null when none of its plants are drawn at this level.
    private (Node3D? Node, int Count) Build(Patch patch, FloraKind kind, FoliageLevel level, List<FloraInstance> plants)
    {
        var settings = Settings!;
        var keep = settings.Keep(level);
        var distance = Reach(kind);
        // Where one mesh hands over to the next they cross-fade (dithered) over this many metres either side.
        var blend = distance * settings.BlendShare;
        var half = patch.Chunk.Data.Size / 2f;
        // Visibility ranges are measured to the chunk centre: near chunks (the one the viewer stands in and its
        // neighbours) get the full meshes, the rest the far meshes, and the shader thins plants out per plant.
        var lodAt = distance * settings.LodShare + half;
        var end = distance + half * Mathf.Sqrt2;
        var centre = patch.Node.Position;
        var root = new Node3D { Name = kind.ToString() };
        var tilt = kind == FloraKind.Bush ? 0.3f : 0.6f;
        var count = 0;
        // Past the thin distance, grass and ground cover keep a share of their plants, drawn larger to cover the gaps.
        var thin = kind != FloraKind.Bush;
        var thinAt = thin ? distance * settings.ThinShare + half : end;
        foreach (var group in plants.Where(p => p.Rank < keep).GroupBy(p => p.Model))
        {
            var list = group.ToList();
            var buffer = Buffer(list, centre, tilt, 1f);
            var (full, lod) = FloraMaterials.Meshes(group.Key);
            root.AddChild(Instance($"{group.Key.Id}_near", full, buffer, list.Count, kind, 0f, Mathf.Min(lodAt, thinAt), blend));
            if (thinAt > lodAt)
                root.AddChild(Instance($"{group.Key.Id}_far", lod, buffer, list.Count, kind, lodAt, thinAt, blend));
            if (thin)
            {
                var sparse = list.Where(p => p.Rank < keep * settings.ThinKeep).ToList();
                if (sparse.Count > 0)
                    root.AddChild(Instance($"{group.Key.Id}_thin", lod, Buffer(sparse, centre, tilt, settings.ThinScale), sparse.Count, kind, thinAt, end, blend));
            }
            count += list.Count;
        }
        if (count == 0)
        {
            root.Free();
            return (null, 0);
        }
        patch.Node.AddChild(root);
        return (root, count);
    }

    // MultiMesh data for plants, relative to the patch centre and scaled by grow.
    private static float[] Buffer(List<FloraInstance> list, Vector3 centre, float tilt, float grow)
    {
        var buffer = new float[list.Count * Stride];
        for (var i = 0; i < list.Count; i++)
        {
            var plant = list[i];
            var up = Vector3.Up.Lerp(plant.Normal.ToGodot(), tilt).Normalized();
            var basis = new Basis(new Quaternion(Vector3.Up, up)) * new Basis(Vector3.Up, plant.Yaw).Scaled(Vector3.One * plant.Scale * grow);
            var origin = plant.Position.ToGodot() - centre;
            var tint = new Color(plant.Tint.X, plant.Tint.Y, plant.Tint.Z).SrgbToLinear();
            var o = i * Stride;
            // MultiMesh buffer layout: the 3×4 transform row by row, the colour (white), then the custom data.
            (buffer[o], buffer[o + 1], buffer[o + 2], buffer[o + 3]) = (basis.Column0.X, basis.Column1.X, basis.Column2.X, origin.X);
            (buffer[o + 4], buffer[o + 5], buffer[o + 6], buffer[o + 7]) = (basis.Column0.Y, basis.Column1.Y, basis.Column2.Y, origin.Y);
            (buffer[o + 8], buffer[o + 9], buffer[o + 10], buffer[o + 11]) = (basis.Column0.Z, basis.Column1.Z, basis.Column2.Z, origin.Z);
            (buffer[o + 12], buffer[o + 13], buffer[o + 14], buffer[o + 15]) = (1f, 1f, 1f, 1f);
            (buffer[o + 16], buffer[o + 17], buffer[o + 18], buffer[o + 19]) = (tint.R, tint.G, tint.B, plant.Yaw / Mathf.Tau);
        }
        return buffer;
    }

    // The last mesh ends past the fade end, where its plants have sunk away, so only hand-overs between meshes blend.
    private static MultiMeshInstance3D Instance(string name, Mesh mesh, float[] buffer, int count, FloraKind kind, float begin, float end, float blend)
    {
        var multi = New(mesh, count);
        multi.Buffer = buffer;
        return new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = multi,
            MaterialOverride = FloraMaterials.For(kind),
            CastShadow = kind == FloraKind.Bush ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeBegin = begin,
            VisibilityRangeEnd = end,
            VisibilityRangeBeginMargin = begin > 0f ? blend : 0f,
            VisibilityRangeEndMargin = blend,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        };
    }

    // Floats per plant in a MultiMesh buffer: transform, colour and custom data.
    private const int Stride = 20;

    /// <summary>
    /// A MultiMesh for flora: transforms, the tint and wind phase as custom data, and instance colours, which stay white.
    /// The colours are there because the Compatibility renderer multiplies vertex colours by the instance colour slot,
    /// which without colours holds the custom data and turns the leaves black.
    /// </summary>
    public static MultiMesh New(Mesh mesh, int count) => new()
    {
        TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
        UseColors = true,
        UseCustomData = true,
        Mesh = mesh,
        InstanceCount = count,
    };

    private void ConfigureMaterial(FloraKind kind)
    {
        var settings = Settings!;
        var distance = Reach(kind);
        var wind = settings.Level(_levels[kind])?.Wind ?? 0f;
        var bush = kind == FloraKind.Bush;
        FloraMaterials.Configure(FloraMaterials.For(kind), wind * (bush ? settings.BushSway : 1f), bush ? settings.BushSpeed : 1f,
            distance * (1f - settings.FadeShare), distance);
    }

    private void UpdateGlobals(Vector3 viewer)
    {
        var settings = Settings!;
        var direction = new Vector2(settings.WindDirection[0], settings.WindDirection[1]).Normalized();
        RenderingServer.GlobalShaderParameterSet(FloraMaterials.Wind, new Vector4(direction.X, direction.Y, settings.WindStrength, settings.WindSpeed));
        RenderingServer.GlobalShaderParameterSet(FloraMaterials.PushStrength, settings.PushStrength);
        var nearest = Pushers().Where(p => p.DistanceSquaredTo(viewer) < 60f * 60f).OrderBy(p => p.DistanceSquaredTo(viewer)).Take(FloraMaterials.Pushers.Length).ToList();
        for (var i = 0; i < FloraMaterials.Pushers.Length; i++)
        {
            var value = i < nearest.Count ? new Vector4(nearest[i].X, nearest[i].Y, nearest[i].Z, settings.PushRadius) : Vector4.Zero;
            RenderingServer.GlobalShaderParameterSet(FloraMaterials.Pushers[i], value);
        }
    }

    private void Clear()
    {
        foreach (var patch in _patches.Values)
            patch.Node.QueueFree();
        _patches.Clear();
        _settled = null;
    }

    private static Vector3 Centre(TerrainChunk chunk)
    {
        var size = chunk.Data.Size;
        var (x, z) = chunk.Coord.Origin(size);
        return new Vector3(x + size / 2f, 0f, z + size / 2f);
    }

    // Distance (x/z) from the viewer to the nearest point of the chunk's square.
    private static float RectDistance(TerrainChunk chunk, Vector3 viewer)
    {
        var size = chunk.Data.Size;
        var (x, z) = chunk.Coord.Origin(size);
        var dx = Mathf.Max(0f, Mathf.Max(x - viewer.X, viewer.X - (x + size)));
        var dz = Mathf.Max(0f, Mathf.Max(z - viewer.Z, viewer.Z - (z + size)));
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private sealed class Patch(TerrainChunk chunk, Node3D node)
    {
        public TerrainChunk Chunk { get; private set; } = chunk;

        public Node3D Node { get; } = node;

        public Dictionary<FloraKind, Task<List<FloraInstance>>> Grown { get; } = [];

        public Dictionary<FloraKind, Node3D> Kinds { get; } = [];

        public Dictionary<FloraKind, FoliageLevel> Built { get; } = [];

        public Dictionary<FloraKind, int> Drawn { get; } = [];

        // Takes new ground for the same chunk: its plants grow again and replace the drawn ones as each kind is built.
        public void Regrow(TerrainChunk ground)
        {
            Chunk = ground;
            Grown.Clear();
            Built.Clear();
        }

        public void Remove(FloraKind kind)
        {
            if (Kinds.Remove(kind, out var node))
                node.QueueFree();
            Built.Remove(kind);
            Drawn.Remove(kind);
        }
    }
}
