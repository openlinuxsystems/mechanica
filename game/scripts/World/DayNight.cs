using Godot;
using FileAccess = Godot.FileAccess;
using Mechanica.Core.World;
using Mechanica.Game.Net;

namespace Mechanica.Game.World;

/// <summary>
/// The day and night cycle (<see cref="SkySettings"/>, <c>game/data/sky.json</c>): moves the sun and moon over the
/// world's clock and gives the sky, haze, sunlight, shadow fill, stars and exposure the colours of the hour. The clock
/// is the host's world time (<see cref="Clock"/>); the host sends it to every client as they join and every few seconds
/// after, and clients run it on between. Cosmetic: each client draws its own sky.
/// </summary>
public partial class DayNight : Node
{
    /// <summary>Where the settings are read from.</summary>
    public const string DefaultSettingsPath = "res://data/sky.json";

    /// <summary>Seconds between the host's clock updates.</summary>
    public const double SendInterval = 5;

    private static SkySettings? _loaded;

    /// <summary>The session, to tell host from client. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>The host's world time in seconds. Set before adding to the tree.</summary>
    public Func<double> Clock { get; init; } = () => 0;

    /// <summary>Seconds in one in-game day. Set before adding to the tree.</summary>
    public Func<double> DayLength { get; init; } = () => WorldGenDefinition.DefaultDayLength;

    /// <summary>The settings; null loads <see cref="DefaultSettingsPath"/>.</summary>
    public SkySettings? Settings { get; set; }

    /// <summary>Holds the sky at this hour in place of the clock's (captures and the AI's screenshots); null follows it.</summary>
    public double? Hour { get; set; }

    /// <summary>The hour every new sky holds at when its <see cref="Hour"/> is not set (captures pin it); null follows the clock.</summary>
    public static double? HoldHour { get; set; }

    /// <summary>The hour the sky shows now.</summary>
    public double Shown { get; private set; } = WorldClock.StartHour;

    /// <summary>The settings in use: <see cref="DisplayApplier"/> reads the haze from them.</summary>
    public static SkySettings Current => _loaded ??= LoadDefaultSettings();

    private WorldEnvironment? _environment;
    private DirectionalLight3D? _sun;
    private ProceduralSkyMaterial? _sky;
    private double _clientTime;
    private double _sinceSend;
    private double _lastSent;

    /// <summary>Reads <see cref="DefaultSettingsPath"/>, failing loudly on bad data.</summary>
    public static SkySettings LoadDefaultSettings()
    {
        using var file = FileAccess.Open(DefaultSettingsPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {DefaultSettingsPath}: {FileAccess.GetOpenError()}");
        return SkySettings.FromJson(file.GetAsText());
    }

    public override void _Ready()
    {
        Settings ??= Current;
        _environment = GetParent().GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        _sun = GetParent().GetNodeOrNull<DirectionalLight3D>("Sun");
        if (_environment?.Environment is { } env)
        {
            env.TonemapMode = Settings.Grade.Tonemap switch
            {
                "aces" => Godot.Environment.ToneMapper.Aces,
                "agx" => Godot.Environment.ToneMapper.Agx,
                _ => Godot.Environment.ToneMapper.Filmic,
            };
            env.TonemapWhite = Settings.Grade.WhitePoint;
            env.AdjustmentEnabled = true;
            env.AdjustmentContrast = Settings.Grade.Contrast;
            env.AdjustmentSaturation = Settings.Grade.Saturation;
            env.AdjustmentBrightness = Settings.Grade.Brightness;
            env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
            env.AmbientLightSkyContribution = 0.5f;
            _sky = env.Sky?.SkyMaterial as ProceduralSkyMaterial;
            if (_sky is not null)
            {
                _sky.SkyCover = Stars();
                _sky.SkyCurve = 0.12f;
                _sky.GroundCurve = 0.04f;
                _sky.SunAngleMax = 8f;
            }
        }
        Session.PeerReady += SendTo;
        Apply();
    }

    public override void _ExitTree() => Session.PeerReady -= SendTo;

    public override void _Process(double delta)
    {
        if (Session.IsHost)
        {
            _sinceSend += delta;
            var now = Clock();
            // Every few seconds, and at once when the clock jumps (a world loaded, a night slept through).
            var jumped = Math.Abs(now - _lastSent - _sinceSend) > 2;
            if ((_sinceSend >= SendInterval || jumped) && Multiplayer.HasMultiplayerPeer() && Multiplayer.GetPeers().Length > 0)
            {
                _sinceSend = 0;
                _lastSent = now;
                Rpc(MethodName.SetClock, now);
            }
        }
        else
            _clientTime += delta;
        Apply();
    }

    // Host → a client that just joined: the world's time.
    private void SendTo(int peer) => RpcId(peer, MethodName.SetClock, Clock());

    /// <summary>Host → client: the world's time in seconds.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SetClock(double time)
    {
        if (double.IsFinite(time) && time >= 0)
            _clientTime = time;
    }

    private void Apply()
    {
        var settings = Settings ??= Current;
        Shown = Hour ?? HoldHour ?? WorldClock.HourAt(Session.IsHost ? Clock() : _clientTime, DayLength());
        var state = settings.At(Shown);
        if (_sky is not null)
        {
            _sky.SkyTopColor = Rgb(state.SkyTop);
            _sky.SkyHorizonColor = Rgb(state.SkyHorizon);
            _sky.GroundHorizonColor = Rgb(state.SkyHorizon);
            _sky.GroundBottomColor = Rgb(state.SkyHorizon).Darkened(0.35f);
            _sky.SkyCoverModulate = new Color(1, 1, 1, state.Stars);
        }
        if (_environment?.Environment is { } env)
        {
            // Haze is the horizon's colour exactly, so land fading out at the view distance meets the sky without a seam.
            env.FogLightColor = Rgb(state.SkyHorizon);
            env.FogLightEnergy = 1f;
            env.AmbientLightColor = Rgb(state.Ambient);
            env.AmbientLightEnergy = state.AmbientEnergy;
            env.TonemapExposure = state.Exposure;
        }
        if (_sun is not null)
        {
            var toward = state.LightDirection.ToGodot();
            var up = Mathf.Abs(toward.Y) > 0.99f ? Vector3.Forward : Vector3.Up;
            _sun.GlobalBasis = Basis.LookingAt(toward, up);
            _sun.LightColor = Rgb(state.Light);
            _sun.LightEnergy = state.LightEnergy;
            _sun.Visible = state.LightEnergy > 0.001f;
            // The sky draws the sun's disc, not the moon's (a dim light would draw a dark smudge).
            _sun.SkyMode = state.Moon ? DirectionalLight3D.SkyModeEnum.LightOnly : DirectionalLight3D.SkyModeEnum.LightAndSky;
        }
    }

    private static Color Rgb((float R, float G, float B) c) => new(c.R, c.G, c.B);

    // A star field for the sky's cover (an equirectangular panorama; only the top half shows): seeded, so every peer
    // sees the same stars.
    private static ImageTexture Stars()
    {
        const int width = 2048, height = 1024;
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));
        var rng = new Random(424242);
        for (var i = 0; i < 2600; i++)
        {
            var x = rng.Next(width);
            // Up to just above the horizon; denser overhead does not matter at this count.
            var y = rng.Next(height / 2 - 20);
            var bright = (float)Math.Pow(rng.NextDouble(), 3) * 0.85f + 0.15f;
            var tint = rng.NextDouble() switch { < 0.15 => new Color(0.75f, 0.85f, 1f), < 0.25 => new Color(1f, 0.9f, 0.75f), _ => Colors.White };
            image.SetPixel(x, y, new Color(tint.R * bright, tint.G * bright, tint.B * bright, 1f));
            if (bright > 0.8f && x + 1 < width)
                image.SetPixel(x + 1, y, new Color(tint.R * bright * 0.5f, tint.G * bright * 0.5f, tint.B * bright * 0.5f, 1f));
        }
        return ImageTexture.CreateFromImage(image);
    }
}
