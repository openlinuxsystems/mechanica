using Godot;
using Mechanica.Core;

namespace Mechanica.Game.Creations;

/// <summary>
/// Materials for parts whose texture spans several grid cells (<c>visual.texture_span</c>): the texture is laid over
/// the creation's grid instead of each part's own UVs, so neighbouring blocks carry on one larger pattern. Each face
/// takes the texture from the two grid axes it lies along. A material is made per source material, span, part
/// rotation and position within one repeat, and shared by every part that matches.
/// </summary>
public static class SpanMaterials
{
    private const string Code = """
        shader_type spatial;
        render_mode CULL;
        uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform vec4 albedo : source_color = vec4(1.0);
        uniform float roughness = 1.0;
        uniform float metallic = 0.0;
        uniform vec3 emission : source_color = vec3(0.0);
        uniform sampler2D emission_texture : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
        // Part-local to creation grid: the mesh's turn and its origin within one repeat, in metres.
        uniform mat3 to_grid = mat3(1.0);
        uniform vec3 grid_origin = vec3(0.0);
        uniform float period = 1.0;
        varying vec2 grid_uv;

        void vertex() {
            vec3 p = to_grid * VERTEX + grid_origin;
            vec3 n = abs(to_grid * NORMAL);
            if (n.x >= n.y && n.x >= n.z)
                grid_uv = vec2(p.z, -p.y);
            else if (n.y >= n.z)
                grid_uv = vec2(p.x, p.z);
            else
                grid_uv = vec2(p.x, -p.y);
            grid_uv /= period;
        }

        void fragment() {
            vec4 t = texture(albedo_texture, grid_uv);
            ALBEDO = albedo.rgb * t.rgb;
            ROUGHNESS = roughness;
            METALLIC = metallic;
        #ifdef SCISSOR
            ALPHA = albedo.a * t.a;
            ALPHA_SCISSOR_THRESHOLD = 0.5;
        #endif
        #ifdef BLEND
            ALPHA = albedo.a * t.a;
        #endif
        #ifdef GLOW
            EMISSION = emission * texture(emission_texture, grid_uv).rgb;
        #endif
        }
        """;

    private static readonly Dictionary<(bool Scissor, bool Blend, bool Glow, bool TwoSided), Shader> Shaders = [];
    private static readonly Dictionary<(BaseMaterial3D Source, int Span, Basis Turn, Vector3I Origin), ShaderMaterial> Materials = [];

    /// <summary>
    /// The spanning material for <paramref name="source"/> (a part's own material, paint included) on a mesh placed at
    /// <paramref name="placed"/> in its body (identity for a part on its own: an icon, the build ghost, a drop), or the
    /// source itself when it has no texture to spread.
    /// </summary>
    public static Material For(BaseMaterial3D source, int span, Transform3D placed)
    {
        if (span <= 1 || source.AlbedoTexture is null)
            return source;
        var period = span * MechanicaInfo.GridSize;
        // Only the position within one repeat matters; snapped to millimetres so every copy of a part shares a material.
        var origin = new Vector3(Mathf.PosMod(placed.Origin.X, period), Mathf.PosMod(placed.Origin.Y, period), Mathf.PosMod(placed.Origin.Z, period));
        var key = (source, span, Snap(placed.Basis), (Vector3I)(origin * 1000).Round());
        if (Materials.TryGetValue(key, out var material))
            return material;
        var scissor = source.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor;
        var blend = source.Transparency == BaseMaterial3D.TransparencyEnum.Alpha;
        var glow = source.EmissionEnabled;
        material = new ShaderMaterial { Shader = ShaderFor(scissor, blend, glow, source.CullMode == BaseMaterial3D.CullModeEnum.Disabled) };
        material.SetShaderParameter("albedo_texture", source.AlbedoTexture);
        material.SetShaderParameter("albedo", source.AlbedoColor);
        material.SetShaderParameter("roughness", source.Roughness);
        material.SetShaderParameter("metallic", source.Metallic);
        if (glow)
        {
            material.SetShaderParameter("emission", source.Emission * source.EmissionEnergyMultiplier);
            if (source.EmissionTexture is { } glowMap)
                material.SetShaderParameter("emission_texture", glowMap);
        }
        material.SetShaderParameter("to_grid", key.Item3);
        material.SetShaderParameter("grid_origin", (Vector3)key.Item4 / 1000f);
        material.SetShaderParameter("period", period);
        return Materials[key] = material;
    }

    // A grid turn's basis with the float noise rounded off, so equal turns share a key.
    private static Basis Snap(Basis b) => new(Round(b.Column0), Round(b.Column1), Round(b.Column2));

    private static Vector3 Round(Vector3 v) => (v * 1000).Round() / 1000;

    private static Shader ShaderFor(bool scissor, bool blend, bool glow, bool twoSided)
    {
        var key = (scissor, blend, glow, twoSided);
        if (Shaders.TryGetValue(key, out var shader))
            return shader;
        var defines = (scissor ? "#define SCISSOR\n" : "") + (blend ? "#define BLEND\n" : "") + (glow ? "#define GLOW\n" : "");
        var code = Code.Replace("render_mode CULL;", $"render_mode {(twoSided ? "cull_disabled" : "cull_back")};");
        return Shaders[key] = new Shader { Code = code.Replace("shader_type spatial;\n", "shader_type spatial;\n" + defines) };
    }
}
