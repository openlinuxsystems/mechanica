using Godot;
using Mechanica.Core.World;

namespace Mechanica.Game.World;

/// <summary>
/// The textured ground: one material per world that blends the world's ground textures (<see cref="WorldGenData.Grounds"/>)
/// by the weights each terrain vertex carries (<see cref="TerrainChunkData.Grounds"/>, in the mesh's CUSTOM0 and CUSTOM1).
/// Textures repeat in world space every <see cref="Ground.Tile"/> metres, mixed with a copy at a larger scale so the
/// repeat doesn't show from afar. What no ground covers keeps the biome's plain vertex colour.
/// </summary>
public static class GroundMaterial
{
    /// <summary>Pixels per side every ground texture is drawn at.</summary>
    public const int TextureSize = 512;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldGenData, ShaderMaterial> Cache = [];

    /// <summary>The material for a world's grounds; null when the world has none.</summary>
    public static ShaderMaterial? For(WorldGenData data)
    {
        if (data.Grounds.Count == 0)
            return null;
        if (Cache.TryGetValue(data, out var cached))
            return cached;
        var images = new Godot.Collections.Array<Image>();
        var tiles = new float[TerrainChunkData.GroundLayers];
        for (var i = 0; i < TerrainChunkData.GroundLayers; i++)
        {
            var ground = i < data.Grounds.Count ? data.Grounds[i] : null;
            images.Add(Load(ground));
            tiles[i] = ground?.Tile ?? 1f;
        }
        var array = new Texture2DArray();
        array.CreateFromImages(images);
        var material = new ShaderMaterial { Shader = new Shader { Code = Code } };
        material.SetShaderParameter("grounds", array);
        material.SetShaderParameter("tiles", tiles);
        Cache.Add(data, material);
        return material;
    }

    // A ground's texture at the drawn size with mipmaps; a grey square when it is missing or unreadable.
    private static Image Load(Ground? ground)
    {
        Image? image = null;
        if (ground?.TextureFile is { } file && System.IO.File.Exists(file))
        {
            image = Image.LoadFromFile(file);
            if (image is null || image.IsEmpty())
                GD.PushWarning($"Ground '{ground.Id}': could not read {file}.");
        }
        else if (ground is not null)
            GD.PushWarning($"Ground '{ground.Id}': texture not found ({ground.TextureFile ?? "no mod folder"}).");
        if (image is null || image.IsEmpty())
        {
            image = Image.CreateEmpty(TextureSize, TextureSize, false, Image.Format.Rgba8);
            image.Fill(new Color(0.5f, 0.5f, 0.5f));
        }
        image.Convert(Image.Format.Rgba8);
        if (image.GetWidth() != TextureSize || image.GetHeight() != TextureSize)
            image.Resize(TextureSize, TextureSize, Image.Interpolation.Lanczos);
        image.GenerateMipmaps();
        return image;
    }

    private const string Code = """
        shader_type spatial;
        render_mode specular_disabled, diffuse_lambert_wrap;

        uniform sampler2DArray grounds : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform float tiles[8];

        varying vec3 world;
        varying vec4 w0;
        varying vec4 w1;

        void vertex() {
            world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            w0 = CUSTOM0;
            w1 = CUSTOM1;
        }

        vec3 ground(int i) {
            vec2 uv = world.xz / tiles[i];
            vec3 near = texture(grounds, vec3(uv, float(i))).rgb;
            vec3 wide = texture(grounds, vec3(uv / 3.7 + vec2(0.37, 0.71), float(i))).rgb;
            return mix(near, wide, 0.35);
        }

        void fragment() {
            // With multisampling, a pixel on a triangle's edge is shaded at the pixel centre, which can lie outside the
            // triangle: its colour and weights are then extrapolated, far past 0..1 on the thin far-off triangles along
            // the skyline. Clamp them, or pow() of a negative colour gives NaN and glow turns it into a white flash.
            vec4 a = clamp(w0, 0.0, 1.0);
            vec4 b = clamp(w1, 0.0, 1.0);
            float w[8] = float[8](a.x, a.y, a.z, a.w, b.x, b.y, b.z, b.w);
            vec3 c = vec3(0.0);
            float total = 0.0;
            for (int i = 0; i < 8; i++) {
                if (w[i] > 0.004) {
                    c += w[i] * ground(i);
                    total += w[i];
                }
            }
            // Ground no texture covers keeps the biome colour (sRGB vertex colour).
            vec3 srgb = clamp(COLOR.rgb, 0.0, 1.0);
            vec3 plain = mix(srgb / 12.92, pow((srgb + 0.055) / 1.055, vec3(2.4)), step(0.04045, srgb));
            ALBEDO = c / max(total, 1.0) + plain * max(0.0, 1.0 - total);
            ROUGHNESS = 0.95;
        }
        """;
}
