using Godot;
using Mechanica.Core.World;
using Mechanica.Game.Mods;

namespace Mechanica.Game.World;

/// <summary>Prototype: the textured ground material, blending up to eight ground layers per vertex.</summary>
public static class GroundMaterial
{
    private static readonly string[] Layers = ["grass", "forest", "ruin", "rock", "sand", "concrete", "scorched"];
    private static ShaderMaterial? _material;

    /// <summary>The shared ground material.</summary>
    public static ShaderMaterial Get()
    {
        if (_material is not null)
            return _material;
        var images = new Godot.Collections.Array<Image>();
        foreach (var layer in Layers.Append("scorched"))
        {
            var image = Image.LoadFromFile(System.IO.Path.Combine(ModPaths.ModsRoot, "vanilla", "textures", "terrain", layer + ".png"));
            image.Convert(Image.Format.Rgba8);
            image.GenerateMipmaps();
            images.Add(image);
        }
        var array = new Texture2DArray();
        array.CreateFromImages(images);
        _material = new ShaderMaterial { Shader = new Shader { Code = Code } };
        _material.SetShaderParameter("layers", array);
        return _material;
    }

    private const string Code = """
        shader_type spatial;
        render_mode specular_disabled, diffuse_lambert_wrap;

        uniform sampler2DArray layers : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform float tile = 4.0;

        varying vec3 world;
        varying vec4 w0;
        varying vec4 w1;

        void vertex() {
            world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            w0 = CUSTOM0;
            w1 = CUSTOM1;
        }

        vec3 layer(float i, vec2 uv, vec2 uv2) {
            // Two scales, so the tiling doesn't show from afar.
            return mix(texture(layers, vec3(uv, i)).rgb, texture(layers, vec3(uv2, i)).rgb, 0.35);
        }

        void fragment() {
            vec2 uv = world.xz / tile;
            vec2 uv2 = world.xz / (tile * 3.7) + vec2(0.37, 0.71);
            vec3 n = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
            float rock = smoothstep(0.82, 0.66, n.y);
            vec4 a = w0;
            vec4 b = w1;
            a *= 1.0 - rock;
            b *= 1.0 - rock;
            a.w += rock;
            vec3 c = vec3(0.0);
            float total = 0.0;
            if (a.x > 0.01) { c += a.x * layer(0.0, uv, uv2); total += a.x; }
            if (a.y > 0.01) { c += a.y * layer(1.0, uv, uv2); total += a.y; }
            if (a.z > 0.01) { c += a.z * layer(2.0, uv, uv2); total += a.z; }
            if (a.w > 0.01) { c += a.w * layer(3.0, uv, uv2); total += a.w; }
            if (b.x > 0.01) { c += b.x * layer(4.0, uv, uv2); total += b.x; }
            if (b.y > 0.01) { c += b.y * layer(5.0, uv, uv2); total += b.y; }
            if (b.z > 0.01) { c += b.z * layer(6.0, uv, uv2); total += b.z; }
            ALBEDO = c / max(total, 0.001);
            ROUGHNESS = 0.95;
        }
        """;
}
