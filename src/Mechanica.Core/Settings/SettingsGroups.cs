using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Settings;

/// <summary>How the game window is shown.</summary>
public enum DisplayMode
{
    /// <summary>A normal window.</summary>
    Windowed,

    /// <summary>A borderless window covering the screen.</summary>
    Borderless,

    /// <summary>Exclusive fullscreen.</summary>
    Fullscreen,
}

/// <summary>A level for one quality setting, or a whole preset.</summary>
public enum Quality
{
    /// <summary>Lowest cost.</summary>
    Low,

    /// <summary>Balanced.</summary>
    Medium,

    /// <summary>The default.</summary>
    High,

    /// <summary>Everything on.</summary>
    Ultra,

    /// <summary>A preset only: the individual levels are set by hand.</summary>
    Custom,
}

/// <summary>
/// How much decorative flora is drawn (Options, Graphics, Foliage density), set for grass, ground cover and bushes
/// apart. Each level scales the plant count and how far plants are drawn (<see cref="World.FloraSettings"/>).
/// </summary>
public enum FoliageLevel
{
    /// <summary>None at all.</summary>
    Off,

    /// <summary>A few, near the player, without wind.</summary>
    Low,

    /// <summary>Thinner and closer than High.</summary>
    Medium,

    /// <summary>The default.</summary>
    High,

    /// <summary>Denser and further than High.</summary>
    VeryHigh,
}

/// <summary>The Graphics tab (UI spec §16). Window changes need Apply and a confirm; the rest apply live.</summary>
public sealed record GraphicsSettings
{
    /// <summary>Window size in windowed mode, <c>1920x1080</c>; null keeps the size the window has.</summary>
    public string? Resolution { get; init; }

    /// <summary>How the window is shown.</summary>
    public DisplayMode DisplayMode { get; init; } = DisplayMode.Windowed;

    /// <summary>The quality preset; <see cref="Quality.Custom"/> uses the levels below.</summary>
    public Quality Preset { get; init; } = Quality.High;

    /// <summary>
    /// How far the world is drawn, in metres: the simulated ground, then scenery out to here
    /// (<see cref="World.FarChunks"/>), with fog at the edge.
    /// </summary>
    public float ViewDistance { get; init; } = DefaultViewDistance;

    /// <summary>Shadow quality.</summary>
    public Quality Shadows { get; init; } = Quality.High;

    /// <summary>Anti-aliasing quality.</summary>
    public Quality Antialiasing { get; init; } = Quality.High;

    /// <summary>Texture quality.</summary>
    public Quality Textures { get; init; } = Quality.High;

    /// <summary>Effects quality: glow from Medium, ambient occlusion from High, at full resolution on Ultra.</summary>
    public Quality Effects { get; init; } = Quality.High;

    /// <summary>
    /// Water quality (<see cref="World.WaterSettings"/>): Low, Medium or High. Ultra draws High, so the Ultra preset sets High.
    /// </summary>
    public Quality Water { get; init; } = Quality.High;

    /// <summary>Waits for the display's refresh.</summary>
    public bool VSync { get; init; } = true;

    /// <summary>Grass density. Not part of the quality preset, so it stays as the player set it.</summary>
    public FoliageLevel Grass { get; init; } = FoliageLevel.High;

    /// <summary>Ground cover (clover, small flowers) density.</summary>
    public FoliageLevel GroundCover { get; init; } = FoliageLevel.High;

    /// <summary>Bush density.</summary>
    public FoliageLevel Bushes { get; init; } = FoliageLevel.High;

    /// <summary>The foliage level of one kind of flora.</summary>
    public FoliageLevel Foliage(World.FloraKind kind) => kind switch
    {
        World.FloraKind.Grass => Grass,
        World.FloraKind.GroundCover => GroundCover,
        _ => Bushes,
    };

    /// <summary>Shortest and longest view distance offered.</summary>
    public const float MinViewDistance = World.FarChunks.MinViewDistance, MaxViewDistance = 1024f;

    /// <summary>The High preset's view distance.</summary>
    public const float DefaultViewDistance = 384f;

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>The levels a preset stands for; <see cref="Quality.Custom"/> keeps this record's own.</summary>
    public GraphicsSettings WithPreset(Quality preset)
    {
        if (preset == Quality.Custom)
            return this with { Preset = preset };
        var distance = preset switch { Quality.Low => MinViewDistance, Quality.Medium => 256f, Quality.High => DefaultViewDistance, _ => 640f };
        return this with
        {
            Preset = preset, ViewDistance = distance, Shadows = preset, Antialiasing = preset, Textures = preset, Effects = preset,
            Water = preset == Quality.Ultra ? Quality.High : preset,
        };
    }

    /// <summary>The levels in use: the preset's, or this record's own for Custom.</summary>
    public GraphicsSettings Effective() => Preset == Quality.Custom ? this : WithPreset(Preset);

    /// <summary>Parses <see cref="Resolution"/>; null when unset or not <c>WxH</c> of at least 1280×720.</summary>
    public (int Width, int Height)? ParseResolution()
    {
        var parts = Resolution?.Split('x');
        return parts is [var w, var h] && int.TryParse(w, out var width) && int.TryParse(h, out var height)
            && width >= Ui.UiPalette.MinWindowWidth && height >= Ui.UiPalette.MinWindowHeight
            ? (width, height)
            : null;
    }

    /// <summary>A copy with every value in range.</summary>
    public GraphicsSettings Clamped() => this with
    {
        ViewDistance = float.IsFinite(ViewDistance) ? Math.Clamp(ViewDistance, MinViewDistance, MaxViewDistance) : DefaultViewDistance,
        Resolution = ParseResolution() is { } r ? $"{r.Width}x{r.Height}" : null,
        Shadows = Shadows == Quality.Custom ? Quality.High : Shadows,
        Antialiasing = Antialiasing == Quality.Custom ? Quality.High : Antialiasing,
        Textures = Textures == Quality.Custom ? Quality.High : Textures,
        Effects = Effects == Quality.Custom ? Quality.High : Effects,
        Water = Water is Quality.Low or Quality.Medium ? Water : Quality.High,
        Grass = Enum.IsDefined(Grass) ? Grass : FoliageLevel.High,
        GroundCover = Enum.IsDefined(GroundCover) ? GroundCover : FoliageLevel.High,
        Bushes = Enum.IsDefined(Bushes) ? Bushes : FoliageLevel.High,
    };
}

/// <summary>The Audio tab (UI spec §16): volumes from 0 to 1, applied as they change.</summary>
public sealed record AudioSettings
{
    /// <summary>Everything.</summary>
    public float Master { get; init; } = 1f;

    /// <summary>Music and radio.</summary>
    public float Music { get; init; } = 0.8f;

    /// <summary>Effects and ambience in the world.</summary>
    public float Sfx { get; init; } = 1f;

    /// <summary>Menu and HUD sounds.</summary>
    public float Ui { get; init; } = 0.8f;

    /// <summary>Silences everything without losing the volumes.</summary>
    public bool Mute { get; init; }

    /// <summary>Output device name; null is the system default.</summary>
    public string? OutputDevice { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>A copy with every volume from 0 to 1.</summary>
    public AudioSettings Clamped() => this with { Master = Unit(Master), Music = Unit(Music), Sfx = Unit(Sfx), Ui = Unit(Ui) };

    /// <summary>Linear volume to decibels, with silence at 0.</summary>
    public static float ToDecibels(float volume) => volume <= 0.0001f ? -80f : 20f * MathF.Log10(volume);

    private static float Unit(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 1f;
}

/// <summary>The Interface tab (UI spec §16). Later HUD elements (compass, names, chat) add their toggles here.</summary>
public sealed record InterfaceSettings
{
    /// <summary>Shows the tool bar and its key hints at the bottom of the screen.</summary>
    public bool ShowHotbar { get; init; } = true;

    /// <summary>Shows the compass along the top of the screen.</summary>
    public bool ShowCompass { get; init; } = true;

    /// <summary>Shows the minimap in the top right corner (the full map opens with M either way).</summary>
    public bool ShowMinimap { get; init; } = true;

    /// <summary>Shows other players' names over their heads.</summary>
    public bool ShowPlayerNames { get; init; } = true;

    /// <summary>The colour-blind filter over the screen.</summary>
    public Ui.ColorBlindMode ColorBlind { get; init; } = Ui.ColorBlindMode.Off;

    /// <summary>How the HUD shows the containers of the Creation the player sits on (fuel, charge, …).</summary>
    public Ui.VehicleGaugeMode VehicleGauges { get; init; } = Ui.VehicleGaugeMode.PerContainer;

    /// <summary>How opaque the chat panel's background is, 0 to 1.</summary>
    public float ChatOpacity { get; init; } = 0.6f;

    /// <summary>The code of the language the UI shows (a file in <c>lang/</c>); <c>en</c> is the built-in English.</summary>
    public string Language { get; init; } = Ui.Language.English;

    /// <summary>The Handbook page read last (a file name in <c>docs/</c>), which the Handbook opens at (UI spec §10); null before any.</summary>
    public string? HandbookPage { get; init; }

    /// <summary>A copy with every value in range.</summary>
    public InterfaceSettings Clamped() => this with
    {
        Language = string.IsNullOrWhiteSpace(Language) ? Ui.Language.English : Language.Trim(),
        ChatOpacity = float.IsFinite(ChatOpacity) ? Math.Clamp(ChatOpacity, 0f, 1f) : 0.6f,
        ColorBlind = Enum.IsDefined(ColorBlind) ? ColorBlind : Ui.ColorBlindMode.Off,
        VehicleGauges = Enum.IsDefined(VehicleGauges) ? VehicleGauges : Ui.VehicleGaugeMode.PerContainer,
    };

        /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Counts play time towards the next automatic save (UI spec §16 "Auto-save interval").</summary>
public sealed class AutoSaveClock
{
    private double _since;

    /// <summary>Seconds since the last save.</summary>
    public double SinceLastSave => _since;

    /// <summary>Adds <paramref name="seconds"/>; true when a save is due every <paramref name="minutes"/> (0 never).</summary>
    public bool Tick(double seconds, int minutes)
    {
        _since += seconds;
        if (minutes <= 0 || _since < minutes * 60.0)
            return false;
        _since = 0;
        return true;
    }

    /// <summary>Starts counting again, after a save by hand.</summary>
    public void Reset() => _since = 0;
}

/// <summary>The Gameplay tab's own settings (UI spec §16); camera ones live on <see cref="UserSettings"/>.</summary>
public sealed record GameplaySettings
{
    /// <summary>Minutes between automatic saves while hosting; 0 is off.</summary>
    public int AutoSaveMinutes { get; init; } = 10;

    /// <summary>Most players a world this player hosts takes.</summary>
    public int MaxPlayers { get; init; } = Net.NetProtocol.DefaultMaxPlayers;

    /// <summary>Shows a short tip the first time each tool or screen is used.</summary>
    public bool ShowTutorialHints { get; init; } = true;

    /// <summary>The tips already shown, by id, so each shows once.</summary>
    public SortedSet<string>? SeenHints { get; init; }

    /// <summary>
    /// The password players need to join a world this player hosts; empty lets anyone on the network in.
    /// Kept on this machine only (<see cref="Net.NetPassword"/>).
    /// </summary>
    public string ServerPassword { get; init; } = "";

    /// <summary>The auto-save choices offered.</summary>
    public static readonly IReadOnlyList<int> AutoSaveChoices = [0, 5, 10, 15];

    /// <summary>Fewest and most players a host can allow.</summary>
    public const int MinPlayers = 2, MaxMaxPlayers = 16;

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>A copy with every value allowed.</summary>
    public GameplaySettings Clamped() => this with
    {
        AutoSaveMinutes = AutoSaveChoices.Contains(AutoSaveMinutes) ? AutoSaveMinutes : 10,
        MaxPlayers = Math.Clamp(MaxPlayers, MinPlayers, MaxMaxPlayers),
        ServerPassword = Net.NetPassword.Clamp(ServerPassword),
    };
}
