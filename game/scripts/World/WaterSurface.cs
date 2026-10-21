using Godot;
using FileAccess = Godot.FileAccess;
using Mechanica.Core.Settings;
using Mechanica.Core.World;
using Mechanica.Game.Settings;

namespace Mechanica.Game.World;

/// <summary>
/// The look of lakes and the sea (<see cref="WaterSettings"/>, <c>game/data/water.json</c>): one shared material every
/// terrain chunk's water plane uses, whose shader follows the Water quality setting (Options, Graphics), and the tint
/// over the screen while the camera is under the surface. The surface is cosmetic and client-side: ripples are two or
/// three tiled normal maps scrolled in world space, so they run on across chunks; waves are a few slow sines on the
/// vertices; the shore foam comes from the depth of the scene under the water.
/// </summary>
public partial class WaterSurface : Node
{
    /// <summary>Where the tuning is read from.</summary>
    public const string DefaultSettingsPath = "res://data/water.json";

    private static readonly Dictionary<Quality, Shader> Shaders = [];
    private static WaterSettings? _configured;
    private static Quality? _quality;

    private ColorRect? _underwater;

    /// <summary>The material every chunk's water is drawn with.</summary>
    public static ShaderMaterial Material { get; } = new();

    /// <summary>The tuning; null loads <see cref="DefaultSettingsPath"/>.</summary>
    public WaterSettings? Settings { get; set; }

    /// <summary>Height of the water's surface over a position: the sea level, or a pond's.</summary>
    public Func<Vector3, float>? Level { get; init; }

    /// <summary>The quality level the material draws now.</summary>
    public static Quality Quality => _quality ?? Quality.High;

    /// <summary>Reads the tuning from <see cref="DefaultSettingsPath"/>.</summary>
    public static WaterSettings LoadDefaultSettings()
    {
        using var file = FileAccess.Open(DefaultSettingsPath, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Cannot open {DefaultSettingsPath}: {FileAccess.GetOpenError()}");
        return WaterSettings.FromJson(file.GetAsText());
    }

    /// <summary>Sets the material's tuning and quality level; does nothing when neither changed.</summary>
    public static void Configure(WaterSettings settings, Quality quality)
    {
        quality = quality is Quality.Low or Quality.Medium ? quality : Quality.High;
        if (ReferenceEquals(settings, _configured) && quality == _quality)
            return;
        var tier = settings.Tier(quality);
        if (!Shaders.TryGetValue(quality, out var shader))
            Shaders[quality] = shader = new Shader { Code = Code(tier) };
        Material.Shader = shader;
        if (!ReferenceEquals(settings, _configured))
        {
            Material.SetShaderParameter("ripples", Ripples);
            Material.SetShaderParameter("foam_noise", FoamNoise);
            Material.SetShaderParameter("shallow_color", Color(settings.ShallowColor));
            Material.SetShaderParameter("deep_color", Color(settings.DeepColor));
            Material.SetShaderParameter("depth_fade", settings.DepthFade);
            Material.SetShaderParameter("transparency", settings.Transparency);
            Material.SetShaderParameter("normal_strength", settings.NormalStrength);
            Material.SetShaderParameter("ripple_size", settings.RippleSize);
            Material.SetShaderParameter("scroll_primary", settings.ScrollPrimary);
            Material.SetShaderParameter("scroll_secondary", settings.ScrollSecondary);
            Material.SetShaderParameter("wave_frequency", settings.WaveFrequency);
            Material.SetShaderParameter("wave_speed", settings.WaveSpeed);
            Material.SetShaderParameter("foam_width", settings.FoamWidth);
            Material.SetShaderParameter("roughness", settings.Roughness);
            Material.SetShaderParameter("lod_start", settings.LodStart);
            Material.SetShaderParameter("lod_end", settings.LodEnd);
            Material.SetShaderParameter("lod_ripples", settings.LodRipples);
        }
        Material.SetShaderParameter("wave_amplitude", settings.WaveAmplitude * tier.Waves);
        Material.SetShaderParameter("foam_intensity", settings.FoamIntensity * tier.Foam);
        _configured = settings;
        _quality = quality;
    }

    /// <summary>A water plane of one chunk's size: subdivided every <see cref="WaterSettings.MeshSpacing"/> for the waves, or flat for far scenery.</summary>
    public static Mesh MeshFor(float size, bool scenery)
    {
        var key = (size, scenery);
        if (Meshes.TryGetValue(key, out var mesh))
            return mesh;
        var spacing = (_configured ?? new WaterSettings()).MeshSpacing;
        var cuts = scenery ? 0 : Math.Max(0, (int)MathF.Round(size / spacing) - 1);
        return Meshes[key] = new PlaneMesh { Size = new Vector2(size, size), SubdivideWidth = cuts, SubdivideDepth = cuts };
    }

    private static readonly Dictionary<(float, bool), Mesh> Meshes = [];

    public override void _Ready()
    {
        Settings ??= LoadDefaultSettings();
        Configure(Settings, GameSettings.Current.Graphics.Effective().Water);
        var layer = new CanvasLayer { Name = "Underwater", Layer = -1 };
        AddChild(layer);
        var tint = new ShaderMaterial { Shader = new Shader { Code = UnderwaterCode } };
        tint.SetShaderParameter("tint", Color(Settings.UnderwaterColor));
        _underwater = new ColorRect { Name = "Tint", Material = tint, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _underwater.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_underwater);
    }

    public override void _Process(double delta)
    {
        Configure(Settings!, GameSettings.Current.Graphics.Effective().Water);
        var camera = GetViewport()?.GetCamera3D();
        if (_underwater is not null)
            _underwater.Visible = camera is not null && Level is not null && camera.GlobalPosition.Y < Level(camera.GlobalPosition) - 0.02f;
    }

    /// <summary>True while the screen is tinted because the camera is under the surface.</summary>
    public bool Underwater => _underwater?.Visible == true;

    private static Color Color(string hex)
    {
        var (r, g, b) = WaterSettings.ParseColor(hex);
        return new Color(r, g, b);
    }

    // Soft, smooth ripples that tile; the normal map is made once, off the main thread.
    private static readonly NoiseTexture2D Ripples = new()
    {
        Width = 256,
        Height = 256,
        Seamless = true,
        AsNormalMap = true,
        BumpStrength = 10f,
        GenerateMipmaps = true,
        Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.016f, FractalOctaves = 2, Seed = 11 },
    };

    // Broken cells the shore foam is cut from.
    private static readonly NoiseTexture2D FoamNoise = new()
    {
        Width = 128,
        Height = 128,
        Seamless = true,
        GenerateMipmaps = true,
        Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.06f, CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance, Seed = 5 },
    };

    // Seen from under the surface: the world wavers a little and fades into the water's colour.
    private const string UnderwaterCode = """
        shader_type canvas_item;

        uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap;
        uniform vec3 tint : source_color;
        uniform float strength = 0.55;

        void fragment() {
            vec2 wobble = vec2(sin(SCREEN_UV.y * 37.0 + TIME * 1.9), cos(SCREEN_UV.x * 29.0 + TIME * 1.6)) * 0.004;
            vec3 seen = texture(screen, SCREEN_UV + wobble).rgb;
            COLOR = vec4(mix(seen * vec3(0.7, 0.95, 1.0), tint, strength), 1.0);
        }
        """;

    // The surface shader for a quality level; the level's switches are compiled in, so Low pays for none of the rest.
    private static string Code(WaterTierSettings tier) => $$"""
        shader_type spatial;
        render_mode blend_mix, depth_draw_opaque, cull_disabled, specular_schlick_ggx;

        #define LAYERS {{tier.Layers}}
        #define WAVES {{(tier.Waves > 0f ? 1 : 0)}}
        #define FOAM {{(tier.Foam > 0f && tier.Depth ? 1 : 0)}}
        #define DEPTH {{(tier.Depth ? 1 : 0)}}

        uniform sampler2D ripples : hint_normal, filter_linear_mipmap, repeat_enable;
        uniform sampler2D foam_noise : hint_default_white, filter_linear_mipmap, repeat_enable;
        #if DEPTH
        uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;
        #endif
        uniform vec3 shallow_color : source_color = vec3(0.282, 0.792, 0.894);
        uniform vec3 deep_color : source_color = vec3(0.0, 0.588, 0.78);
        uniform float depth_fade = 3.0;
        uniform float transparency = 0.65;
        uniform float normal_strength = 0.5;
        uniform float ripple_size = 8.0;
        uniform float scroll_primary = 0.12;
        uniform float scroll_secondary = 0.07;
        uniform float wave_amplitude = 0.08;
        uniform float wave_frequency = 0.15;
        uniform float wave_speed = 0.25;
        uniform float foam_intensity = 0.4;
        uniform float foam_width = 0.35;
        uniform float roughness = 0.05;
        uniform float lod_start = 48.0;
        uniform float lod_end = 112.0;
        uniform float lod_ripples = 0.35;

        varying vec3 world;
        varying float near;

        // Height and slope of the waves: three slow sines in different directions, so no one direction reads as a current.
        vec3 waves(vec2 p, float t) {
            float k = 6.2831853 * wave_frequency;
            vec2 d1 = vec2(0.96, 0.28);
            vec2 d2 = vec2(-0.37, 0.93);
            vec2 d3 = vec2(0.6, -0.8);
            float p1 = k * (dot(d1, p) - wave_speed * t);
            float p2 = k * 1.37 * (dot(d2, p) - wave_speed * 0.8 * t) + 1.7;
            float p3 = k * 0.71 * (dot(d3, p) - wave_speed * 1.2 * t) + 4.1;
            float h = 0.5 * sin(p1) + 0.3 * sin(p2) + 0.2 * sin(p3);
            vec2 slope = 0.5 * k * cos(p1) * d1 + 0.3 * k * 1.37 * cos(p2) * d2 + 0.2 * k * 0.71 * cos(p3) * d3;
            return vec3(h, slope);
        }

        void vertex() {
            world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            near = 1.0 - smoothstep(lod_start, lod_end, distance(world.xz, CAMERA_POSITION_WORLD.xz));
        #if WAVES
            vec3 w = waves(world.xz, TIME) * wave_amplitude * near;
            VERTEX.y += w.x;
            world.y += w.x;
            NORMAL = normalize(vec3(-w.y, 1.0, -w.z));
        #endif
        }

        vec3 ripple(vec2 uv) {
            return texture(ripples, uv).xyz * 2.0 - 1.0;
        }

        void fragment() {
            vec2 p = world.xz / ripple_size;
            vec3 n = ripple(p + vec2(0.8, 0.6) * scroll_primary / ripple_size * TIME);
        #if LAYERS >= 2
            vec3 n2 = ripple(p * 1.9 + vec2(-0.5, 0.86) * scroll_secondary / ripple_size * 1.9 * TIME);
            n = vec3(n.xy + n2.xy, n.z * n2.z);
        #endif
        #if LAYERS >= 3
            vec3 n3 = ripple(p * 3.7 + vec2(0.2, -0.98) * scroll_secondary / ripple_size * 3.7 * TIME + 0.37);
            n = vec3(n.xy + n3.xy * 0.6, n.z * n3.z);
        #endif
            n = normalize(n);
            NORMAL_MAP = n * 0.5 + 0.5;
            NORMAL_MAP_DEPTH = normal_strength * mix(lod_ripples, 1.0, near);

            float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
            float deep = 0.6;
            float foam = 0.0;
        #if DEPTH
            float raw = texture(depth_texture, SCREEN_UV).r;
        #if CURRENT_RENDERER == RENDERER_COMPATIBILITY
            vec3 ndc = vec3(SCREEN_UV, raw) * 2.0 - 1.0;
        #else
            vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, raw);
        #endif
            vec4 view = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
            view.xyz /= view.w;
            vec3 floor_world = (INV_VIEW_MATRIX * vec4(view.xyz, 1.0)).xyz;
            float below = max(world.y - floor_world.y, 0.0);
            float through = max(length(view.xyz) - length(VERTEX), 0.0);
            deep = clamp(below / depth_fade, 0.0, 1.0);
        #if FOAM
            float edge = 1.0 - smoothstep(0.0, foam_width, min(below, through));
            float cells = texture(foam_noise, world.xz / 3.0 + n.xy * 0.05 + vec2(0.02, 0.015) * TIME).r;
            foam = edge * smoothstep(0.25, 0.55, cells + edge * 0.45) * foam_intensity;
        #endif
        #endif
            vec3 color = mix(shallow_color, deep_color, deep);
            color = mix(color, shallow_color, fresnel * 0.5);
            float alpha = mix(1.0 - transparency, 0.94, sqrt(deep));
            alpha = mix(alpha, 1.0, fresnel * 0.6);
            if (!FRONT_FACING) {
                color = deep_color;
                alpha = 0.8;
                foam = 0.0;
            }
            ALBEDO = mix(color, vec3(0.95, 0.98, 1.0), foam);
            ALPHA = max(alpha, foam);
            ROUGHNESS = mix(roughness, 0.6, foam);
            SPECULAR = 0.35;
            METALLIC = 0.0;
        }
        """;
}
