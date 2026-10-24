using Godot;
using Mechanica.Core.World;

namespace Mechanica.Game.World;

/// <summary>
/// The textured ground: one material per world that blends the world's ground textures (<see cref="WorldGenData.Grounds"/>)
/// by the weights each terrain vertex carries (<see cref="TerrainChunkData.Grounds"/>, in the mesh's CUSTOM0 and CUSTOM1).
/// Textures repeat in world space every <see cref="Ground.Tile"/> metres, mixed with a copy at a larger scale so the
/// repeat doesn't show from afar, and a slow drift of tint over tens of metres breaks up large fields. A texture's alpha is
/// its height: grounds blend by it (grass fills between stones, dirt shows through thin grass) and it bumps the lighting.
/// What no ground covers keeps the biome's plain vertex colour.
/// </summary>
public static class GroundMaterial
{
    /// <summary>Pixels per side every ground texture is drawn at.</summary>
    public const int TextureSize = 1024;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldGenData, ShaderMaterial> Cache = [];

    /// <summary>The material for a world's grounds; null when the world has none.</summary>
    public static ShaderMaterial? For(WorldGenData data)
    {
        if (data.Grounds.Count == 0)
            return null;
        if (Cache.TryGetValue(data, out var cached))
            return cached;
        var images = new Godot.Collections.Array<Image>();
        var slopes = new Godot.Collections.Array<Image>();
        var tiles = new float[TerrainChunkData.GroundLayers];
        for (var i = 0; i < TerrainChunkData.GroundLayers; i++)
        {
            var ground = i < data.Grounds.Count ? data.Grounds[i] : null;
            var image = Load(ground);
            slopes.Add(Slopes(image));
            image.GenerateMipmaps();
            images.Add(image);
            tiles[i] = ground?.Tile ?? 1f;
        }
        var array = new Texture2DArray();
        array.CreateFromImages(images);
        var slopeArray = new Texture2DArray();
        slopeArray.CreateFromImages(slopes);
        var material = new ShaderMaterial { Shader = new Shader { Code = Code } };
        material.SetShaderParameter("grounds", array);
        material.SetShaderParameter("slopes", slopeArray);
        material.SetShaderParameter("tiles", tiles);
        Cache.Add(data, material);
        return material;
    }

    // A ground's texture at the drawn size; a flat grey square when it is missing or unreadable.
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
            image.Fill(new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }
        image.Convert(Image.Format.Rgba8);
        if (image.GetWidth() != TextureSize || image.GetHeight() != TextureSize)
            image.Resize(TextureSize, TextureSize, Image.Interpolation.Lanczos);
        return image;
    }

    // The height's slope across and down a texture (from its alpha, wrapping at the edges), in red and green around 0.5,
    // with mipmaps. A texture with no alpha is flat.
    private static Image Slopes(Image image)
    {
        var size = image.GetWidth();
        var source = image.GetData();
        var data = new byte[size * size * 4];
        float Height(int x, int y) => source[((((y % size) + size) % size) * size + ((x % size) + size) % size) * 4 + 3] / 255f;
        const float strength = 6f;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var i = (y * size + x) * 4;
                var dx = (Height(x + 1, y) - Height(x - 1, y)) * strength;
                var dy = (Height(x, y + 1) - Height(x, y - 1)) * strength;
                data[i] = (byte)Math.Clamp(128 + dx * 127, 0, 255);
                data[i + 1] = (byte)Math.Clamp(128 + dy * 127, 0, 255);
                data[i + 2] = 128;
                data[i + 3] = 255;
            }
        var slopes = Image.CreateFromData(size, size, false, Image.Format.Rgba8, data);
        slopes.GenerateMipmaps();
        return slopes;
    }

    private const string Code = """
        shader_type spatial;
        render_mode specular_disabled, diffuse_lambert_wrap;

        uniform sampler2DArray grounds : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2DArray slopes : filter_linear_mipmap_anisotropic, repeat_enable;
        uniform float tiles[8];
        // How far below the highest ground another still shows through (in the textures' height, 0..1).
        uniform float blend_depth = 0.22;
        // How strongly the textures' height bumps the light.
        uniform float bump = 0.55;
        // Brightness and warmth drift across the land, over about this many metres.
        uniform float drift_scale = 70.0;
        uniform float drift = 0.11;

        varying vec3 world;
        varying vec3 world_normal;
        varying vec4 w0;
        varying vec4 w1;

        void vertex() {
            world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
            w0 = CUSTOM0;
            w1 = CUSTOM1;
        }

        float hash(vec2 p) {
            return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
        }

        float noise(vec2 p) {
            vec2 i = floor(p);
            vec2 f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
        }

        // A ground's colour (rgb) and height (a), and its slope (xy).
        vec4 ground(int i, out vec2 slope) {
            vec2 uv = world.xz / tiles[i];
            vec2 uv2 = uv / 3.7 + vec2(0.37, 0.71);
            vec4 near = texture(grounds, vec3(uv, float(i)));
            vec4 wide = texture(grounds, vec3(uv2, float(i)));
            slope = (texture(slopes, vec3(uv, float(i))).rg - 0.5) * 2.0 + (texture(slopes, vec3(uv2, float(i))).rg - 0.5) * 0.7;
            return vec4(mix(near.rgb, wide.rgb, 0.35), mix(near.a, wide.a, 0.3));
        }

        void fragment() {
            // With multisampling, a pixel on a triangle's edge is shaded at the pixel centre, which can lie outside the
            // triangle: its colour and weights are then extrapolated, far past 0..1 on the thin far-off triangles along
            // the skyline. Clamp them, or pow() of a negative colour gives NaN and glow turns it into a white flash.
            vec4 a = clamp(w0, 0.0, 1.0);
            vec4 b = clamp(w1, 0.0, 1.0);
            float w[8] = float[8](a.x, a.y, a.z, a.w, b.x, b.y, b.z, b.w);
            vec4 g[8];
            vec2 s[8];
            float total = 0.0;
            float top = -1.0;
            for (int i = 0; i < 8; i++) {
                g[i] = vec4(0.0);
                s[i] = vec2(0.0);
                if (w[i] > 0.004) {
                    g[i] = ground(i, s[i]);
                    total += w[i];
                    top = max(top, g[i].a + w[i]);
                }
            }
            // Height blending: where grounds meet, the one standing higher at this pixel wins, so a mix of grass and
            // dirt reads as grass with dirt in its gaps rather than a muddy average of both.
            vec3 c = vec3(0.0);
            vec2 slope = vec2(0.0);
            float sum = 0.0;
            for (int i = 0; i < 8; i++) {
                if (w[i] > 0.004) {
                    float k = max(g[i].a + w[i] - top + blend_depth, 0.0) * w[i];
                    c += k * g[i].rgb;
                    slope += k * s[i];
                    sum += k;
                }
            }
            c /= max(sum, 0.0001);
            slope /= max(sum, 0.0001);
            float cover = min(total, 1.0);
            float d = noise(world.xz / drift_scale) * 0.65 + noise(world.xz / (drift_scale * 0.31) + 17.0) * 0.35 - 0.5;
            c *= vec3(1.0 + d * drift * 1.2, 1.0 + d * drift, 1.0 - d * drift * 0.6);
            // Ground no texture covers keeps the biome colour (sRGB vertex colour).
            vec3 srgb = clamp(COLOR.rgb, 0.0, 1.0);
            vec3 plain = mix(srgb / 12.92, pow((srgb + 0.055) / 1.055, vec3(2.4)), step(0.04045, srgb));
            ALBEDO = c * cover + plain * (1.0 - cover);
            // Bumps from the height, in the ground's plane (textures run along world x and z), fading out with distance
            // where they would only shimmer.
            vec3 n = normalize(world_normal);
            vec3 t = normalize(vec3(1.0, 0.0, 0.0) - n * n.x);
            vec3 bt = normalize(cross(t, n)) * sign(dot(cross(t, n), vec3(0.0, 0.0, 1.0)));
            float fade = cover * clamp(1.0 - length(VERTEX) / 90.0, 0.0, 1.0);
            vec3 bumped = normalize(n - (t * slope.x + bt * slope.y) * bump * fade);
            NORMAL = normalize((VIEW_MATRIX * vec4(bumped, 0.0)).xyz);
            ROUGHNESS = 0.95;
        }
        """;
}
