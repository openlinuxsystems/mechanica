using Godot;
using Mechanica.Core.World;

namespace Mechanica.Game.World;

/// <summary>
/// The look of POI liquid pools (worldgen <c>liquids[]</c>): one material per liquid, drawn like the water
/// (<see cref="WaterSurface"/>) with tiled ripple normal maps scrolled in world space, plus slow swirls of shade, an
/// oil slick's drifting rainbow film, bubbles, and scum along the shore where the ground comes up to the surface. The
/// surface fades out over the last few centimetres of depth, so the shore follows the basin instead of a hard line.
/// Cosmetic and client-side.
/// </summary>
public static class LiquidSurface
{
    private static readonly Dictionary<LiquidDefinition, ShaderMaterial> Materials = [];
    private static Shader? _shader;

    /// <summary>The material a liquid's pools are drawn with.</summary>
    public static ShaderMaterial MaterialFor(LiquidDefinition liquid)
    {
        if (Materials.TryGetValue(liquid, out var material))
            return material;
        _shader ??= new Shader { Code = Code };
        material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("ripples", Ripples);
        material.SetShaderParameter("cells", Cells);
        material.SetShaderParameter("albedo", Color(liquid.Color));
        material.SetShaderParameter("glow", liquid.Glow);
        material.SetShaderParameter("roughness", liquid.Roughness);
        material.SetShaderParameter("ripple_size", liquid.RippleSize);
        material.SetShaderParameter("ripple_strength", liquid.Ripples);
        material.SetShaderParameter("flow", liquid.Flow);
        material.SetShaderParameter("sheen", liquid.Sheen);
        material.SetShaderParameter("bubbles", liquid.Bubbles);
        material.SetShaderParameter("edge", liquid.EdgeColor is null ? 0f : 1f);
        material.SetShaderParameter("edge_color", Color(liquid.EdgeColor));
        return Materials[liquid] = material;
    }

    private static Color Color(string? hex)
    {
        var c = WorldGenData.ParseColor(hex);
        return new Color(c.X, c.Y, c.Z);
    }

    // Soft ripples that tile, as on the water.
    private static readonly NoiseTexture2D Ripples = new()
    {
        Width = 256,
        Height = 256,
        Seamless = true,
        AsNormalMap = true,
        BumpStrength = 8f,
        GenerateMipmaps = true,
        Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.02f, FractalOctaves = 2, Seed = 23 },
    };

    // Cells: the swirls and film come from their smooth distance field, the bubbles from their centres.
    private static readonly NoiseTexture2D Cells = new()
    {
        Width = 256,
        Height = 256,
        Seamless = true,
        GenerateMipmaps = true,
        Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.03f, CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance, Seed = 31 },
    };

    private const string Code = """
        shader_type spatial;
        render_mode blend_mix, depth_draw_always, cull_back, specular_schlick_ggx;

        uniform sampler2D ripples : hint_normal, filter_linear_mipmap, repeat_enable;
        uniform sampler2D cells : hint_default_white, filter_linear_mipmap, repeat_enable;
        uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;
        uniform vec3 albedo : source_color;
        uniform vec3 edge_color : source_color;
        uniform float edge = 0.0;
        uniform float glow = 0.0;
        uniform float roughness = 0.1;
        uniform float ripple_size = 4.0;
        uniform float ripple_strength = 0.6;
        uniform float flow = 0.05;
        uniform float sheen = 0.0;
        uniform float bubbles = 0.0;

        varying vec3 world;

        vec3 ripple(vec2 uv) {
            return texture(ripples, uv).xyz * 2.0 - 1.0;
        }

        void vertex() {
            world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            float t = TIME * flow;
            vec2 p = world.xz / ripple_size;
            vec3 n1 = ripple(p + vec2(0.8, 0.6) * t / ripple_size);
            vec3 n2 = ripple(p * 2.3 + vec2(-0.5, 0.86) * t * 1.6 / ripple_size + 0.37);
            vec3 n = normalize(vec3(n1.xy + n2.xy * 0.6, n1.z * n2.z));

            // Depth of liquid under this point, from the scene behind it.
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
            float shore = 1.0 - smoothstep(0.0, 0.5, below);

            // Slow swirls of lighter and darker liquid.
            float swirl = texture(cells, world.xz / 11.0 + n.xy * 0.06 + vec2(0.7, 0.4) * t / 11.0).r;
            float swirl2 = texture(cells, world.xz / 5.0 - vec2(0.3, 0.9) * t / 5.0 + 0.5).r;
            vec3 color = albedo * mix(0.75, 1.2, swirl * 0.7 + swirl2 * 0.3);

            // An oil slick: a thin rainbow film in bands that drift and shift with the view angle.
            float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.0);
            float film = swirl * 1.1 + swirl2 * 0.4 + fresnel * 0.6;
            vec3 rainbow = 0.5 + 0.5 * cos(6.2831853 * (film + vec3(0.0, 0.33, 0.67)));
            float slick = sheen * smoothstep(0.5, 0.85, swirl);
            color += rainbow * 0.12 * slick;

            // Bubbles: small cell centres, in patches that drift and fade so they seem to rise and pop.
            float dots = 1.0 - smoothstep(0.12, 0.26, texture(cells, world.xz / 4.5 + vec2(0.13, 0.07) * TIME * 0.03).r);
            float patches = smoothstep(0.3, 0.6, texture(cells, world.xz / 8.0 - vec2(0.05, 0.11) * TIME * 0.03 + 0.21).r);
            float bubble = bubbles * dots * patches * (1.0 - shore);

            // Scum or crust along the shore, broken up by the swirls.
            float crust = edge * smoothstep(0.35, 0.6, swirl2 + shore * 0.55) * shore;
            color = mix(color, edge_color, crust);
            color = mix(color, albedo * 1.3 + vec3(0.3, 0.35, 0.2), bubble);

            NORMAL_MAP = n * 0.5 + 0.5;
            NORMAL_MAP_DEPTH = mix(ripple_strength, 0.15, crust) + bubble;
            ALBEDO = color;
            EMISSION = (albedo * (0.7 + 0.6 * swirl) + albedo * bubble * 2.0) * glow * (1.0 - crust);
            ROUGHNESS = mix(roughness, 0.85, crust);
            SPECULAR = mix(0.3, 0.5, slick);
            METALLIC = 0.0;
            ALPHA = max(smoothstep(0.0, 0.12, below) * 0.97, crust);
        }
        """;
}
