using System.Text.Json;
using Mechanica.Core.Data;

namespace Mechanica.Core.Ui;

/// <summary>The world loading screen's stages (UI spec §15), in order.</summary>
public enum LoadStage
{
    /// <summary>A client reaching the host.</summary>
    Connecting,

    /// <summary>A client connected and waiting on the host's world.</summary>
    WaitingForHost,

    /// <summary>The ground under the spawn.</summary>
    Terrain,

    /// <summary>The chunks around the spawn.</summary>
    Chunks,

    /// <summary>Scenery and saved Creations.</summary>
    Structures,

    /// <summary>The first physics steps on the new ground.</summary>
    Physics,

    /// <summary>The local player spawning with their inventory.</summary>
    Player,

    /// <summary>The last frame before the world shows.</summary>
    Finalizing,

    /// <summary>Loaded; the screen fades into the world.</summary>
    Entering,
}

/// <summary>
/// The loading screen's timings (<c>game/data/loading.json</c>): how long it stays up at least, how often tips
/// change, the fades, how long a stage may stall before the load counts as failed, and the boot splash.
/// </summary>
public sealed record LoadingSettings
{
    /// <summary>Seconds the loading screen shows at least, so fast loads don't flash.</summary>
    public double MinSeconds { get; init; } = 2.0;

    /// <summary>Seconds each tip shows.</summary>
    public double TipSeconds { get; init; } = 5.0;

    /// <summary>Seconds stage and tip text fade between changes.</summary>
    public double FadeSeconds { get; init; } = 0.2;

    /// <summary>Seconds of the fade to black, and of the fade back in, on entering the world.</summary>
    public double EnterFadeSeconds { get; init; } = 0.4;

    /// <summary>Seconds a stage may make no progress before the load fails.</summary>
    public double TimeoutSeconds { get; init; } = 60.0;

    /// <summary>Physics steps run on the new ground before the player spawns into it.</summary>
    public int PhysicsSteps { get; init; } = 3;

    /// <summary>Seconds of the boot splash before the home screen (§14: one to two).</summary>
    public double SplashSeconds { get; init; } = 1.5;

    /// <summary>Reads <paramref name="json"/>; throws <see cref="FormatException"/> on bad data.</summary>
    public static LoadingSettings Parse(string json)
    {
        LoadingSettings? settings;
        try
        {
            settings = DataJson.Read<LoadingSettings>(json);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid loading JSON: {e.Message}", e);
        }
        settings ??= new LoadingSettings();
        if (settings.MinSeconds < 0 || settings.TipSeconds <= 0 || settings.FadeSeconds < 0 || settings.EnterFadeSeconds < 0
            || settings.TimeoutSeconds <= 0 || settings.SplashSeconds < 0 || settings.PhysicsSteps < 0)
            throw new FormatException("Loading times must not be negative, and tip_seconds and timeout_seconds must be above zero.");
        return settings;
    }
}

/// <summary>
/// Where a world load stands (UI spec §15): the stage, how far through it, the progress bar's share, the tip, a
/// stall timeout, and failure. Progress follows the real stages and never goes back; reaching a later stage
/// completes the ones before it. The screen may leave only once the load has entered and the minimum time passed.
/// </summary>
public sealed class WorldLoading(LoadingSettings settings)
{
    // Stages that fill the bar; Connecting and WaitingForHost leave it empty.
    private const int FirstFilled = (int)LoadStage.Terrain;
    private const int Filled = (int)LoadStage.Entering - FirstFilled;

    private readonly List<LoadStage> _reached = [LoadStage.Connecting];
    private double _sinceProgress;
    private double _progress;

    /// <summary>The timings in use.</summary>
    public LoadingSettings Settings { get; } = settings;

    /// <summary>The stage now.</summary>
    public LoadStage Stage { get; private set; }

    /// <summary>Every stage the load has been at, in order.</summary>
    public IReadOnlyList<LoadStage> Reached => _reached;

    /// <summary>How far through <see cref="Stage"/>, 0 to 1.</summary>
    public double StageFraction { get; private set; }

    /// <summary>Seconds since the load started.</summary>
    public double Elapsed { get; private set; }

    /// <summary>Why the load failed, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>The progress bar, 0 to 1.</summary>
    public double Progress => _progress;

    /// <summary>The bar as a whole percentage.</summary>
    public int Percent => (int)Math.Floor(_progress * 100 + 1e-9);

    /// <summary>True once the world is loaded.</summary>
    public bool Entered => Stage == LoadStage.Entering;

    /// <summary>True when the screen can fade into the world: loaded, not failed, and up for the minimum time.</summary>
    public bool CanLeave => Entered && Error is null && Elapsed >= Settings.MinSeconds;

    /// <summary>
    /// Reports <paramref name="stage"/> at <paramref name="fraction"/> (0 to 1). An earlier stage than the one now,
    /// or less of the same one, changes nothing; nothing changes after a failure. True when it moved on.
    /// </summary>
    public bool Report(LoadStage stage, double fraction = 0)
    {
        if (Error is not null)
            return false;
        fraction = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1);
        if (stage < Stage || (stage == Stage && fraction <= StageFraction))
            return false;
        if (stage != Stage)
            _reached.Add(stage);
        Stage = stage;
        StageFraction = stage == LoadStage.Entering ? 1 : fraction;
        var bar = stage < (LoadStage)FirstFilled ? 0 : Math.Min(1, ((int)stage - FirstFilled + StageFraction) / Filled);
        _progress = Math.Max(_progress, bar);
        _sinceProgress = 0;
        return true;
    }

    /// <summary>Stops the load with <paramref name="message"/>; the first failure wins.</summary>
    public void Fail(string message)
    {
        if (Error is null && !Entered)
            Error = message;
    }

    /// <summary>
    /// Advances the clock by <paramref name="delta"/> seconds. A stage that makes no progress for
    /// <see cref="LoadingSettings.TimeoutSeconds"/> fails the load with <paramref name="timedOut"/>.
    /// </summary>
    public void Tick(double delta, string timedOut = "The world took too long to load.")
    {
        if (!(delta > 0))
            return;
        Elapsed += delta;
        if (Error is not null || Entered)
            return;
        _sinceProgress += delta;
        if (_sinceProgress >= Settings.TimeoutSeconds)
            Fail(timedOut);
    }

    /// <summary>Which of <paramref name="count"/> tips shows now: each for <see cref="LoadingSettings.TipSeconds"/>, in turn.</summary>
    public int TipIndex(int count) => count <= 0 ? -1 : (int)(Elapsed / Settings.TipSeconds) % count;

    /// <summary>The English text for a stage (the string table key).</summary>
    public static string Text(LoadStage stage) => stage switch
    {
        LoadStage.Connecting => "Connecting to host…",
        LoadStage.WaitingForHost => "Waiting for host…",
        LoadStage.Terrain => "Generating terrain…",
        LoadStage.Chunks => "Loading chunks…",
        LoadStage.Structures => "Spawning structures…",
        LoadStage.Physics => "Initializing physics…",
        LoadStage.Player => "Loading player data…",
        LoadStage.Finalizing => "Finalizing world…",
        _ => "Entering world…",
    };
}
