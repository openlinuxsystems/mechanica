using Godot;

namespace Mechanica.Game.Survival;

/// <summary>
/// The look of a soil mound as it gets wet and dries out (Soil design spec: Dry, Lightly Watered, Fully Wet, Drying),
/// drawn by one material parameter instead of a mesh morph. Each mound mesh keeps its model's texture; per mesh
/// instance, <c>wet</c> (0 dry to 1 soaked) darkens damp patches that spread until they cover it, adds sheen and,
/// near 1, puddles in the low dark hollows of the top; <c>drying</c> (0 or 1) brings up pale crusted patches; and
/// <c>flash</c> darkens it for a moment right after it is watered. <see cref="SoilLooks"/> sets them.
/// </summary>
public static class SoilMaterial
{
    /// <summary>The per-instance parameter for how wet the mound looks, 0 to 1.</summary>
    public const string Wet = "wet";

    /// <summary>The per-instance parameter for the pale crust of drying soil, 0 to 1.</summary>
    public const string Drying = "drying";

    /// <summary>The per-instance parameter for the moment's darkening after watering, 0 to 1.</summary>
    public const string Flash = "flash";

    private static readonly Shader Shader = new() { Code = Code };
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BaseMaterial3D, ShaderMaterial> Cache = [];

    /// <summary>True for a material made here.</summary>
    public static bool IsSoil(Material? material) => material is ShaderMaterial { Shader: var shader } && shader == Shader;

    /// <summary>The soil material drawing <paramref name="source"/>'s textures, shared by every mesh using it.</summary>
    public static ShaderMaterial For(BaseMaterial3D source)
    {
        if (Cache.TryGetValue(source, out var cached))
            return cached;
        var material = new ShaderMaterial { Shader = Shader };
        material.SetShaderParameter("albedo_color", source.AlbedoColor);
        material.SetShaderParameter("base_roughness", source.Roughness);
        if (source.AlbedoTexture is { } albedo)
            material.SetShaderParameter("albedo_tex", albedo);
        if (source.NormalEnabled && source.NormalTexture is { } normal)
        {
            material.SetShaderParameter("normal_tex", normal);
            material.SetShaderParameter("has_normal", true);
        }
        Cache.Add(source, material);
        return material;
    }

    private const string Code = """
        shader_type spatial;

        uniform vec4 albedo_color : source_color = vec4(1.0);
        uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, hint_default_white;
        uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap;
        uniform bool has_normal = false;
        uniform float base_roughness = 0.95;
        instance uniform float wet = 0.0;
        instance uniform float drying = 0.0;
        instance uniform float flash = 0.0;

        float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

        float noise(vec2 p) {
            vec2 i = floor(p);
            vec2 f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
        }

        void fragment() {
            vec3 colour = texture(albedo_tex, UV).rgb * albedo_color.rgb;
            float lum = dot(colour, vec3(0.299, 0.587, 0.114));
            float up = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).y;
            // Damp patches grow from a few spots until the whole mound is dark.
            float patches = noise(UV * 6.0) * 0.6 + noise(UV * 17.0 + 4.0) * 0.4;
            float reach = 1.0 - wet * 1.35;
            float damp = smoothstep(reach - 0.12, reach + 0.12, patches) * smoothstep(0.0, 0.25, wet);
            // Drying soil: lighter crusted patches where the water left first.
            float crust = drying * smoothstep(0.6, 0.78, noise(UV * 11.0 + 9.0));
            damp *= 1.0 - crust * 0.8;
            vec3 soaked = pow(colour, vec3(1.7)) * vec3(0.5, 0.44, 0.38);
            colour = mix(colour, soaked, damp);
            colour = mix(colour, colour * 1.12 + vec3(0.02, 0.016, 0.01), crust * 0.6);
            colour *= 1.0 - 0.28 * flash;
            // Fully wet: water stands in the dark hollows of the top.
            float puddle = smoothstep(0.8, 1.0, wet) * smoothstep(0.75, 0.95, up) * smoothstep(0.30, 0.22, lum)
                * smoothstep(0.35, 0.6, noise(UV * 9.0 + 2.0));
            colour = mix(colour, colour * 0.7, puddle);
            ALBEDO = colour;
            ROUGHNESS = mix(mix(base_roughness, 0.55, damp), 0.08, puddle);
            SPECULAR = 0.5 + 0.1 * damp + 0.3 * puddle;
            if (has_normal) {
                NORMAL_MAP = texture(normal_tex, UV).rgb;
                NORMAL_MAP_DEPTH = 1.0 - puddle * 0.9 - damp * 0.25;
            }
        }
        """;
}
