using Mechanica.Core.Data;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Saves;

/// <summary>The world the host runs now, as <c>world.info</c> answers.</summary>
/// <param name="Name">The world's name.</param>
/// <param name="Seed">World seed.</param>
/// <param name="Mode">Game mode.</param>
/// <param name="Difficulty">Difficulty.</param>
/// <param name="LoadedFrom">The save file it was loaded from, or null for a new world.</param>
/// <param name="WorldTime">Seconds the world has run.</param>
/// <param name="Tick">Current physics tick.</param>
/// <param name="TimeScale">How fast the world runs (1 is real time).</param>
/// <param name="Paused">True while the world is paused.</param>
/// <param name="Clients">Other players joined.</param>
/// <param name="Changing">True while a load or new world is under way.</param>
public sealed record WorldStatus(string Name, long Seed, GameMode Mode, Difficulty Difficulty, string? LoadedFrom, double WorldTime, long Tick,
    double TimeScale, bool Paused, int Clients, bool Changing);

/// <summary>A new world to start.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Seed">World seed.</param>
/// <param name="Mode">Game mode.</param>
/// <param name="Difficulty">Difficulty.</param>
/// <param name="Character">The host player's character (qualified id), or null for the default.</param>
public sealed record WorldSpec(string Name, long Seed, GameMode Mode, Difficulty Difficulty, string? Character);

/// <summary>A saved world as <c>world.list</c> shows it.</summary>
/// <param name="Name">The world's name.</param>
/// <param name="File">Its file name.</param>
/// <param name="SavedAt">When it was saved.</param>
/// <param name="Mode">Game mode.</param>
/// <param name="PlaySeconds">Seconds the world has run.</param>
/// <param name="Error">Why it does not load, or null.</param>
public sealed record WorldListing(string Name, string File, DateTimeOffset SavedAt, GameMode Mode, double PlaySeconds, string? Error);

/// <summary>Where a save went.</summary>
/// <param name="Name">The world's name.</param>
/// <param name="File">The save file's name.</param>
/// <param name="Path">The save file's full path.</param>
public sealed record WorldSaved(string Name, string File, string Path);

/// <summary>What the host does for <see cref="WorldControl"/>: the game side of saving, loading and starting worlds.</summary>
public interface IWorldHost
{
    /// <summary>The world now.</summary>
    WorldStatus Status { get; }

    /// <summary>The save folder.</summary>
    SaveStore Store { get; }

    /// <summary>Saves the world as <paramref name="name"/>; returns the file's path, or null when it could not be written.</summary>
    string? Save(string name);

    /// <summary>Replaces the running world with <paramref name="save"/> (it finishes after this returns).</summary>
    void Load(SaveGame save, string path);

    /// <summary>Replaces the running world with a new one (it finishes after this returns).</summary>
    void Start(WorldSpec spec);

    /// <summary>Sets how fast the world runs.</summary>
    void SetTimeScale(double scale);

    /// <summary>Pauses or resumes the world.</summary>
    void SetPaused(bool paused);
}

/// <summary>Arguments of <c>world.info</c> and <c>world.list</c> (none).</summary>
public sealed record WorldNoParams;

/// <summary>Arguments of <c>world.save</c>.</summary>
/// <param name="Name">Name to save as; the world's own name when left out.</param>
public sealed record SaveWorldParams(string? Name = null);

/// <summary>Arguments of <c>world.load</c>.</summary>
/// <param name="Name">A saved world's name or file name, as <c>world.list</c> shows it.</param>
/// <param name="DisconnectClients">True to go ahead although players are joined; they are disconnected and can join the new world.</param>
public sealed record LoadWorldParams(string Name, bool DisconnectClients = false);

/// <summary>Arguments of <c>world.new</c>.</summary>
/// <param name="Name">The new world's name.</param>
/// <param name="Seed">World seed; random when left out.</param>
/// <param name="Mode">Game mode.</param>
/// <param name="Difficulty">Difficulty.</param>
/// <param name="Character">The host player's character (qualified id).</param>
/// <param name="DisconnectClients">True to go ahead although players are joined.</param>
public sealed record NewWorldParams(string Name = "World", long? Seed = null, GameMode Mode = GameMode.Survival, Difficulty Difficulty = Difficulty.Normal,
    string? Character = null, bool DisconnectClients = false);

/// <summary>Arguments of <c>world.reset</c>.</summary>
/// <param name="DisconnectClients">True to go ahead although players are joined.</param>
public sealed record ResetWorldParams(bool DisconnectClients = false);

/// <summary>Arguments of <c>world.time_scale</c>.</summary>
/// <param name="Scale">How fast the world runs, from <see cref="WorldControl.MinTimeScale"/> to <see cref="WorldControl.MaxTimeScale"/> (1 is real time).</param>
public sealed record TimeScaleParams(double Scale);

/// <summary>Arguments of <c>world.pause</c>.</summary>
/// <param name="Paused">True to pause, false to resume.</param>
public sealed record PauseParams(bool Paused);

/// <summary>Why a <see cref="WorldControl"/> call was refused.</summary>
public enum WorldRefusal
{
    /// <summary>The arguments are wrong.</summary>
    BadParams,

    /// <summary>The call is fine but the world says no (no such save, players joined).</summary>
    Rejected,

    /// <summary>A world change is already under way.</summary>
    Busy,
}

/// <summary>A refused <see cref="WorldControl"/> call; the message says why.</summary>
public sealed class WorldControlException(WorldRefusal refusal, string message) : Exception(message)
{
    /// <summary>Why.</summary>
    public WorldRefusal Refusal { get; } = refusal;
}

/// <summary>
/// Session control for AI agents (AUDIT §5 PR 5): save the world, list saves, load one, start a new world or
/// the same world afresh, and run time faster, slower or not at all for tests. It checks every request and
/// leaves the work to the game's <see cref="IWorldHost"/>. Loading or starting a world with players joined
/// disconnects them, so it needs <c>disconnect_clients</c>.
/// </summary>
public sealed class WorldControl(IWorldHost host, Func<long>? randomSeed = null)
{
    /// <summary>Slowest time scale.</summary>
    public const double MinTimeScale = 0.05;

    /// <summary>Fastest time scale.</summary>
    public const double MaxTimeScale = 10;

    /// <summary>Longest world name.</summary>
    public const int MaxName = 64;

    private readonly Func<long> _randomSeed = randomSeed ?? (() => Random.Shared.NextInt64());

    /// <summary>The world now.</summary>
    public WorldStatus Info() => host.Status;

    /// <summary>Saves the world.</summary>
    /// <exception cref="WorldControlException">The name is bad, a world change is under way, or the file could not be written.</exception>
    public WorldSaved Save(SaveWorldParams p)
    {
        var status = host.Status;
        if (status.Changing)
            throw new WorldControlException(WorldRefusal.Busy, "The world is changing; save once it has loaded.");
        var name = CheckName(p.Name ?? status.Name);
        var path = host.Save(name) ?? throw new WorldControlException(WorldRefusal.Rejected, "The save could not be written; see the host's log.");
        return new WorldSaved(name, System.IO.Path.GetFileName(path), path);
    }

    /// <summary>Every saved world, newest first.</summary>
    public IReadOnlyList<WorldListing> List() =>
        [.. host.Store.List().Select(s => new WorldListing(s.Name, System.IO.Path.GetFileName(s.Path), s.SavedAt, s.Mode, s.PlaySeconds, s.Error))];

    /// <summary>Starts loading a saved world; returns its listing.</summary>
    /// <exception cref="WorldControlException">No such save, it doesn't read, players are joined, or a world change is under way.</exception>
    public WorldListing Load(LoadWorldParams p)
    {
        CheckChange(p.DisconnectClients);
        var saves = host.Store.List();
        var summary = saves.FirstOrDefault(s => s.Name == p.Name) ?? saves.FirstOrDefault(s => System.IO.Path.GetFileName(s.Path) == p.Name
            || System.IO.Path.GetFileNameWithoutExtension(s.Path) == p.Name)
            ?? throw new WorldControlException(WorldRefusal.Rejected, $"No saved world named '{Shorten(p.Name)}'; world.list shows them.");
        var diagnostics = new List<DataDiagnostic>();
        var save = SaveStore.Read(summary.Path, diagnostics)
            ?? throw new WorldControlException(WorldRefusal.Rejected, $"'{summary.Name}' does not load: {diagnostics.FirstOrDefault()?.Message ?? "unreadable"}");
        host.Load(save, summary.Path);
        return new WorldListing(summary.Name, System.IO.Path.GetFileName(summary.Path), summary.SavedAt, summary.Mode, summary.PlaySeconds, null);
    }

    /// <summary>Starts a new world; returns what it will be.</summary>
    /// <exception cref="WorldControlException">The name is bad, players are joined, or a world change is under way.</exception>
    public WorldSpec New(NewWorldParams p)
    {
        var name = CheckName(p.Name);
        if (p.Character is { } character && (character.Length > 128 || !character.Contains(':')))
            throw new WorldControlException(WorldRefusal.BadParams, "character is a qualified id such as vanilla:scrapper_female.");
        CheckChange(p.DisconnectClients);
        var spec = new WorldSpec(name, p.Seed ?? _randomSeed(), p.Mode, p.Difficulty, p.Character);
        host.Start(spec);
        return spec;
    }

    /// <summary>Starts the running world again from nothing: the same name, seed, mode and difficulty, no save.</summary>
    /// <exception cref="WorldControlException">Players are joined, or a world change is under way.</exception>
    public WorldSpec Reset(ResetWorldParams p)
    {
        CheckChange(p.DisconnectClients);
        var status = host.Status;
        var spec = new WorldSpec(status.Name, status.Seed, status.Mode, status.Difficulty, null);
        host.Start(spec);
        return spec;
    }

    /// <summary>Sets how fast the world runs; returns the status.</summary>
    /// <exception cref="WorldControlException">The scale is out of range.</exception>
    public WorldStatus SetTimeScale(TimeScaleParams p)
    {
        if (double.IsNaN(p.Scale) || p.Scale < MinTimeScale || p.Scale > MaxTimeScale)
            throw new WorldControlException(WorldRefusal.BadParams, $"scale must be from {MinTimeScale} to {MaxTimeScale}.");
        host.SetTimeScale(p.Scale);
        return host.Status;
    }

    /// <summary>Pauses or resumes the world; returns the status.</summary>
    public WorldStatus SetPaused(PauseParams p)
    {
        host.SetPaused(p.Paused);
        return host.Status;
    }

    private void CheckChange(bool disconnectClients)
    {
        var status = host.Status;
        if (status.Changing)
            throw new WorldControlException(WorldRefusal.Busy, "A world is already loading; wait for it (world.info shows changing).");
        if (status.Clients > 0 && !disconnectClients)
            throw new WorldControlException(WorldRefusal.Rejected,
                $"{status.Clients} other player(s) are joined and would be disconnected; pass disconnect_clients: true to go ahead.");
    }

    private static string CheckName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxName || trimmed.Any(char.IsControl))
            throw new WorldControlException(WorldRefusal.BadParams, $"A world name is one line of 1 to {MaxName} characters.");
        return trimmed;
    }

    private static string Shorten(string text) => text.Length <= 64 ? text : text[..64] + "…";
}
