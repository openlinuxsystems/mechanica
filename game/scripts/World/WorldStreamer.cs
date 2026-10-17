using Godot;
using Mechanica.Core.World;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using FileAccess = Godot.FileAccess;

namespace Mechanica.Game.World;

/// <summary>
/// Streams the world in chunks and force-loads Creations (chunk 12), by applying a Core
/// <see cref="ChunkStreamer"/> to the scene every physics tick, before players and bodies move.
/// <list type="bullet">
/// <item>The host (and an offline game) streams for every player and parks and resumes the Creations it
/// is the authority for (D5), so a client's player always has ground under it on the host.</item>
/// <item>A client streams ground for its own player only. It never parks Creations: the host decides
/// what is simulated, and state sync (chunk 4b) shows the result.</item>
/// </list>
/// </summary>
public partial class WorldStreamer : Node3D
{
    /// <summary>Where the tuning is read from.</summary>
    public const string DefaultSettingsPath = "res://data/streaming.json";

    private readonly Dictionary<ChunkCoord, GroundChunk> _chunks = [];

    /// <summary>The session, for this peer's role. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players to stream around. Set before adding to the tree.</summary>
    public PlayerSync PlayerSync { get; init; } = null!;

    /// <summary>Creations to force-load, park and resume. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Streamed around while there is no player yet, so the spawn has ground.</summary>
    public Func<Vector3> SpawnPoint { get; init; } = () => Vector3.Zero;

    /// <summary>Tuning; null reads <see cref="DefaultSettingsPath"/> when the node starts.</summary>
    public StreamingSettings? Settings { get; set; }

    /// <summary>The streaming logic, once started.</summary>
    public ChunkStreamer Streamer { get; private set; } = null!;

    /// <summary>Ground nodes of the loaded chunks.</summary>
    public IReadOnlyDictionary<ChunkCoord, GroundChunk> Chunks => _chunks;

    /// <summary>True when this peer decides which Creations are simulated.</summary>
    public bool StreamsCreations => Session.IsHost;

    /// <summary>Reads the tuning from <see cref="DefaultSettingsPath"/>.</summary>
    public static StreamingSettings LoadDefaultSettings()
    {
        using var file = FileAccess.Open(DefaultSettingsPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {DefaultSettingsPath}: {FileAccess.GetOpenError()}");
        return StreamingSettings.FromJson(file.GetAsText());
    }

    public override void _Ready()
    {
        ProcessPhysicsPriority = -200;
        Settings ??= LoadDefaultSettings();
        Streamer = new ChunkStreamer(Settings);
        Creations.Removed += Streamer.Forget;
        Step(0f);
    }

    public override void _ExitTree() => Creations.Removed -= Streamer.Forget;

    public override void _PhysicsProcess(double delta) => Step((float)delta);

    /// <summary>The chunk holding a world position.</summary>
    public ChunkCoord ChunkAt(Vector3 position) => ChunkCoord.At(position.X, position.Z, Streamer.Settings.ChunkSize);

    /// <summary>Runs one streaming update now and applies it.</summary>
    public StreamingUpdate Step(float delta)
    {
        var local = Session.LocalPeer;
        IEnumerable<Player.PlayerController> streamed = StreamsCreations
            ? PlayerSync.Players.Values
            : PlayerSync.LocalPlayer is { } own ? [own] : [];
        var players = streamed.Select(p => p.GlobalPosition.ToNumerics()).ToList();
        if (players.Count == 0)
            players.Add(SpawnPoint().ToNumerics());
        var samples = StreamsCreations
            ? Creations.All.Where(c => c.AuthorityPeer == local).Select(c => Creations.Sample(c.Id)).ToList()
            : [];

        var update = Streamer.Update(delta, players, samples);
        foreach (var id in update.Park)
            Creations.Park(id);
        foreach (var chunk in update.Unload)
        {
            if (_chunks.Remove(chunk, out var node))
            {
                RemoveChild(node);
                node.QueueFree();
            }
        }
        foreach (var chunk in update.Load)
        {
            var node = new GroundChunk { Coord = chunk, Size = Streamer.Settings.ChunkSize };
            AddChild(node);
            _chunks[chunk] = node;
        }
        foreach (var id in update.Resume)
            Creations.Resume(id);
        foreach (var id in update.FellAsleep)
            Creations.Sleep(id);
        return update;
    }
}
