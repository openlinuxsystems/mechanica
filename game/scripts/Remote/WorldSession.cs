using Godot;
using Mechanica.Core.Saves;
using Mechanica.Game.Saves;

namespace Mechanica.Game.Remote;

/// <summary>
/// The game side of session control for AI agents (<see cref="WorldControl"/>, the <c>world.*</c> methods):
/// saves through <see cref="SaveSync"/>, and loads or starts a world the way the menus do (through
/// <see cref="Main.MenuSave"/> and <see cref="Main.MenuOptions"/>), keeping the host's launch options (port,
/// AI endpoint, tokens) and carrying the AI endpoint over to the new world. Time scale and pause act on the
/// engine and the scene tree; a world change puts both back to normal.
/// </summary>
public sealed class WorldSession(Main main) : IWorldHost
{
    /// <summary>True from a load or new world until the new world has entered.</summary>
    public static bool Changing { get; private set; }

    /// <inheritdoc/>
    public WorldStatus Status => new(
        main.Saves.WorldName,
        main.Streamer.Seed,
        main.Options!.GameMode,
        main.Options.Difficulty,
        main.Save is null ? null : main.Options.LoadPath,
        main.Scripts.Runtime.Time,
        main.Recorder.Log.CurrentTick,
        Engine.TimeScale,
        main.GetTree().Paused,
        main.Session.ReadyPeers.Count,
        Changing);

    /// <inheritdoc/>
    public SaveStore Store => main.Saves.Store ?? SaveSync.UserStore;

    /// <inheritdoc/>
    public string? Save(string name)
    {
        var path = main.Saves.SaveNow(name);
        if (path is not null)
            main.Ai.Events.Publish("world", () => new { kind = "saved", path, status = Status });
        return path;
    }

    /// <inheritdoc/>
    public void Load(SaveGame save, string path)
    {
        Main.MenuSave = save;
        Main.MenuOptions = Carried() with { LoadPath = path };
        Replace();
    }

    /// <inheritdoc/>
    public void Start(WorldSpec spec)
    {
        Main.MenuSave = null;
        Main.MenuOptions = Carried() with { Seed = spec.Seed, GameMode = spec.Mode, Difficulty = spec.Difficulty, LoadPath = null };
        Main.MenuWorldName = spec.Name;
        Main.MenuCharacter = spec.Character;
        Replace();
    }

    /// <inheritdoc/>
    public void SetTimeScale(double scale) => Engine.TimeScale = scale;

    /// <inheritdoc/>
    public void SetPaused(bool paused) => main.GetTree().Paused = paused;

    /// <summary>Called by the new world each frame until it has entered: then the change is over and agents hear so.</summary>
    public void Entered()
    {
        if (!Changing)
            return;
        Changing = false;
        main.Ai.Events.Publish("world", () => new { kind = "loaded", status = Status });
        GD.Print($"[world] '{main.Saves.WorldName}' is ready for agents");
    }

    // The host's launch options without the one-off parts (a replay or recording file belongs to the old world),
    // hosting again on the same port when this world was opened to others after it started.
    private Core.Net.LaunchOptions Carried()
    {
        var options = main.Options! with { ReplayPath = null, RecordPath = null };
        if (main.Session.State == Net.NetSession.SessionState.Hosting && options.Mode != Core.Net.SessionMode.Host)
            options = options with { Mode = Core.Net.SessionMode.Host, Port = main.Session.Port, MaxPlayers = main.Session.MaxPlayers };
        return options;
    }

    private void Replace()
    {
        Changing = true;
        Engine.TimeScale = 1;
        main.GetTree().Paused = false;
        main.Ai.Carry();
        // Joined players are disconnected and the port is free for the next world to host on.
        Callable.From(() =>
        {
            main.Session.Leave("The host is changing worlds.");
            main.ReplaceWorld!();
        }).CallDeferred();
    }
}
