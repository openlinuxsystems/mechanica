using System.Numerics;

namespace Mechanica.Core.World;

/// <summary>What the game reports about one Creation each update.</summary>
/// <param name="Id">Creation id.</param>
/// <param name="Bounds">World box of every Body (<see cref="CreationBounds"/>). Ignored while the Creation is parked.</param>
/// <param name="LinearSpeed">Fastest Body speed in m/s.</param>
/// <param name="AngularSpeed">Fastest Body turn rate in rad/s.</param>
/// <param name="HoldAwake">True while something keeps the Creation running whatever its motion (its logic is doing something: <see cref="Logic.SignalBus.IsActive"/>).</param>
public readonly record struct CreationSample(int Id, WorldBounds Bounds, float LinearSpeed, float AngularSpeed, bool HoldAwake = false);

/// <summary>
/// What the game must do after an <see cref="ChunkStreamer.Update"/>, applied in this order: park, unload,
/// load, resume. Parking comes first so a Creation is out of the physics world before the ground under
/// it goes, and resuming comes last so the ground is back first. Lists are sorted.
/// </summary>
/// <param name="Park">Creations to take out of the simulation where they are, keeping their state.</param>
/// <param name="Unload">Chunks to drop.</param>
/// <param name="Load">Chunks to build.</param>
/// <param name="Resume">Parked Creations to put back into the simulation as they were.</param>
/// <param name="FellAsleep">Live Creations that just went to sleep; the game puts their Bodies to sleep.</param>
public sealed record StreamingUpdate(
    IReadOnlyList<int> Park,
    IReadOnlyList<ChunkCoord> Unload,
    IReadOnlyList<ChunkCoord> Load,
    IReadOnlyList<int> Resume,
    IReadOnlyList<int> FellAsleep)
{
    /// <summary>True when there is nothing to do.</summary>
    public bool IsEmpty => Park.Count + Unload.Count + Load.Count + Resume.Count + FellAsleep.Count == 0;
}

/// <summary>
/// Decides which chunks are loaded and which Creations are simulated (chunk 12). The host runs one for
/// the simulation, fed every player and every Creation it is the authority for; each client runs one for
/// its own player only, for the ground it shows and predicts on.
/// <list type="bullet">
/// <item>Chunks near any player stay loaded.</item>
/// <item>A Creation is a unit: it force-loads every chunk its Bodies' box touches, plus a buffer, while it is
/// attended (a player is near) or awake. Its joints therefore never straddle an unloaded chunk.</item>
/// <item>A Creation falls asleep after idling for <see cref="StreamingSettings.SleepDelay"/>, and an asleep,
/// unattended Creation stops force-loading. It parks (leaves the physics world, keeping its state) just
/// before a chunk it touches unloads, and resumes when a player comes near.</item>
/// <item>Sleep budget: at most <see cref="StreamingSettings.MaxUnattendedCreations"/> awake unattended
/// Creations, force-loading at most <see cref="StreamingSettings.MaxUnattendedChunks"/> extra chunks, keep
/// running, moving ones first; the rest, farthest from any player first, park until the budget frees up or a
/// player comes near.</item>
/// <item>A chunk nothing needs unloads after <see cref="StreamingSettings.UnloadDelay"/>, so crossing back
/// and forth over a border does not churn.</item>
/// </list>
/// Pure logic, no Godot.
/// </summary>
public sealed class ChunkStreamer
{
    private readonly HashSet<ChunkCoord> _loaded = [];
    private readonly Dictionary<ChunkCoord, float> _unneededFor = [];
    private readonly SortedDictionary<int, Tracked> _creations = [];
    private readonly int _minChunkX, _maxChunkX, _minChunkZ, _maxChunkZ;

    private sealed class Tracked
    {
        public WorldBounds Bounds = WorldBounds.Empty;
        public float Idle;
        public bool Asleep;
        public bool Parked;
        public bool HoldAwake;
        public bool Moving;
    }

    /// <summary>Starts with nothing loaded.</summary>
    public ChunkStreamer(StreamingSettings settings)
    {
        settings.Validate();
        Settings = settings;
        var halfX = settings.WorldSize / 2f;
        var halfZ = settings.Depth / 2f;
        _minChunkX = (int)MathF.Floor(-halfX / settings.ChunkSize);
        _maxChunkX = (int)MathF.Ceiling(halfX / settings.ChunkSize) - 1;
        _minChunkZ = (int)MathF.Floor(-halfZ / settings.ChunkSize);
        _maxChunkZ = (int)MathF.Ceiling(halfZ / settings.ChunkSize) - 1;
    }

    /// <summary>The tuning in use.</summary>
    public StreamingSettings Settings { get; }

    /// <summary>Chunks loaded after the last update.</summary>
    public IReadOnlyCollection<ChunkCoord> Loaded => _loaded;

    /// <summary>True when the chunk is loaded.</summary>
    public bool IsLoaded(ChunkCoord chunk) => _loaded.Contains(chunk);

    /// <summary>True when the chunk lies inside the world.</summary>
    public bool InWorld(ChunkCoord chunk) =>
        chunk.X >= _minChunkX && chunk.X <= _maxChunkX && chunk.Z >= _minChunkZ && chunk.Z <= _maxChunkZ;

    /// <summary>True when the Creation is tracked and parked.</summary>
    public bool IsParked(int creationId) => _creations.TryGetValue(creationId, out var t) && t.Parked;

    /// <summary>True when the Creation is tracked.</summary>
    public bool IsTracked(int creationId) => _creations.ContainsKey(creationId);

    /// <summary>A tracked Creation's last box (where it was parked, for a parked one), or null.</summary>
    public WorldBounds? BoundsOf(int creationId) => _creations.TryGetValue(creationId, out var t) ? t.Bounds : null;

    /// <summary>True when the Creation is tracked and asleep.</summary>
    public bool IsAsleep(int creationId) => _creations.TryGetValue(creationId, out var t) && t.Asleep;

    /// <summary>The in-world chunks a box touches, with no buffer.</summary>
    public IEnumerable<ChunkCoord> ChunksOf(WorldBounds bounds) => bounds.Chunks(Settings.ChunkSize).Where(InWorld);

    /// <summary>
    /// Tracks a Creation that starts parked and asleep, as one loaded from a save into a place nobody is
    /// near. It resumes like any parked Creation.
    /// </summary>
    public void RegisterParked(int creationId, WorldBounds bounds) =>
        _creations[creationId] = new Tracked { Bounds = bounds, Parked = true, Asleep = true, Idle = Settings.SleepDelay };

    /// <summary>Stops tracking a Creation (destroyed, or merged into another).</summary>
    public void Forget(int creationId) => _creations.Remove(creationId);

    /// <summary>
    /// Advances by <paramref name="dt"/> seconds. Creations not yet tracked start live and awake. A tracked
    /// live Creation missing from <paramref name="creations"/> keeps its last box and counts as still.
    /// </summary>
    public StreamingUpdate Update(float dt, IReadOnlyList<Vector3> players, IReadOnlyList<CreationSample> creations)
    {
        var s = Settings;
        var sampled = new HashSet<int>();
        var fellAsleep = new List<int>();
        foreach (var sample in creations)
        {
            sampled.Add(sample.Id);
            if (!_creations.TryGetValue(sample.Id, out var t))
                _creations[sample.Id] = t = new Tracked();
            t.HoldAwake = sample.HoldAwake;
            if (t.Parked)
                continue;
            t.Bounds = sample.Bounds;
            t.Moving = sample.LinearSpeed > s.SleepLinearSpeed || sample.AngularSpeed > s.SleepAngularSpeed;
            var still = !sample.HoldAwake && !t.Moving;
            t.Idle = still ? t.Idle + dt : 0f;
        }
        foreach (var (id, t) in _creations)
        {
            // A parked Creation that was moving stays awake to resume when there is room; a still one dozes off.
            if (t.Parked ? !t.Moving : !sampled.Contains(id))
                t.Idle += dt;
            if (t.HoldAwake)
                t.Idle = 0f;
            var asleep = t.Idle >= s.SleepDelay;
            if (asleep && !t.Asleep && !t.Parked)
                fellAsleep.Add(id);
            t.Asleep = asleep;
        }

        // Players first; attended Creations next; then awake unattended ones, nearest first, within budget.
        var desired = new HashSet<ChunkCoord>();
        var reach = new Vector3(s.PlayerLoadDistance, 0f, s.PlayerLoadDistance);
        foreach (var player in players)
            desired.UnionWith(ChunksOf(WorldBounds.Around(player, reach)));
        var kept = new HashSet<int>();
        var candidates = new List<(int Id, float Distance)>();
        foreach (var (id, t) in _creations)
        {
            var distance = players.Count == 0 ? float.PositiveInfinity : players.Min(t.Bounds.HorizontalDistanceTo);
            if (distance <= s.PlayerNearDistance)
            {
                kept.Add(id);
                desired.UnionWith(ForceLoaded(t));
            }
            else if (!t.Asleep)
                candidates.Add((id, distance));
        }
        var overBudget = new HashSet<int>();
        int keptUnattended = 0, extraChunks = 0;
        // Moving Creations come first, so ones only held awake (a lamp left on) never freeze one on its way.
        foreach (var (id, _) in candidates.OrderBy(c => _creations[c.Id].Moving ? 0 : 1).ThenBy(c => c.Distance).ThenBy(c => c.Id))
        {
            var extra = ForceLoaded(_creations[id]).Where(c => !desired.Contains(c)).ToList();
            if (keptUnattended < s.MaxUnattendedCreations && extraChunks + extra.Count <= s.MaxUnattendedChunks)
            {
                keptUnattended++;
                extraChunks += extra.Count;
                desired.UnionWith(extra);
                kept.Add(id);
            }
            else
                overBudget.Add(id);
        }

        var unload = new List<ChunkCoord>();
        foreach (var chunk in _loaded)
        {
            if (desired.Contains(chunk))
            {
                _unneededFor.Remove(chunk);
                continue;
            }
            var unneeded = _unneededFor[chunk] = _unneededFor.GetValueOrDefault(chunk) + dt;
            if (unneeded >= s.UnloadDelay)
                unload.Add(chunk);
        }
        var load = desired.Where(c => !_loaded.Contains(c)).ToList();
        var after = new HashSet<ChunkCoord>(_loaded);
        after.ExceptWith(unload);
        after.UnionWith(load);

        // A live Creation nothing keeps parks when over budget, or when the ground under it is going.
        var park = new List<int>();
        foreach (var (id, t) in _creations)
        {
            if (t.Parked || kept.Contains(id))
                continue;
            if (overBudget.Contains(id) || ChunksOf(t.Bounds).Any(c => !after.Contains(c)))
            {
                t.Parked = true;
                park.Add(id);
            }
        }
        var resume = new List<int>();
        foreach (var (id, t) in _creations)
        {
            if (t.Parked && kept.Contains(id) && !park.Contains(id) && ChunksOf(t.Bounds).All(after.Contains))
            {
                t.Parked = false;
                resume.Add(id);
            }
        }

        foreach (var chunk in unload)
        {
            _loaded.Remove(chunk);
            _unneededFor.Remove(chunk);
        }
        _loaded.UnionWith(load);
        unload.Sort(ChunkCoord.Compare);
        load.Sort(ChunkCoord.Compare);
        return new StreamingUpdate(park, unload, load, resume, fellAsleep);
    }

    private IEnumerable<ChunkCoord> ForceLoaded(Tracked t) => ChunksOf(t.Bounds.Expand(Settings.CreationBuffer));
}
