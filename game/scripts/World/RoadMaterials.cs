using Godot;
using Mechanica.Core.World;

namespace Mechanica.Game.World;

/// <summary>
/// The materials roads are drawn with (<see cref="RoadMaterial"/>), matching the road references: dark asphalt with
/// white edge lines, a double yellow centre line and white stop lines where a road meets a junction; light concrete curbs,
/// decks and piers with block seams; yellow and black hazard stripes; a yellow caution diamond.
/// </summary>
public static class RoadMaterials
{
    private static readonly Dictionary<(RoadMaterial, float), Material> Cache = [];

    /// <summary>The material for <paramref name="material"/> on roads <paramref name="width"/> metres wide.</summary>
    public static Material For(RoadMaterial material, float width)
    {
        if (Cache.TryGetValue((material, width), out var cached))
            return cached;
        Material made = material switch
        {
            RoadMaterial.Asphalt => Shaded(AsphaltCode, width),
            RoadMaterial.Concrete => Shaded(ConcreteCode, width),
            RoadMaterial.Hazard => Shaded(HazardCode, width),
            RoadMaterial.Sign => Shaded(SignCode, width),
            _ => new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.57f, 0.6f), Metallic = 0.4f, Roughness = 0.5f },
        };
        Cache[(material, width)] = made;
        return made;
    }

    private static ShaderMaterial Shaded(string code, float width)
    {
        var material = new ShaderMaterial { Shader = new Shader { Code = code } };
        material.SetShaderParameter("width", width);
        return material;
    }

    // Hash noise shared by the shaders: a little grain so flat colours read as material up close.
    private const string Grain = """
        float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
        float grain(vec2 p) {
            vec2 i = floor(p);
            vec2 f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
        }
        """;

    private const string AsphaltCode = """
        shader_type spatial;
        render_mode specular_schlick_ggx;

        uniform float width = 10.0;

        varying vec3 world;

        void vertex() {
            world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        """ + Grain + """

        // 1 inside [a, b] with a soft edge a few centimetres wide.
        float band(float x, float a, float b) {
            float e = max(fwidth(x), 0.004);
            return smoothstep(a - e, a + e, x) * (1.0 - smoothstep(b - e, b + e, x));
        }

        void fragment() {
            vec3 c = vec3(0.17, 0.18, 0.19);
            float n = grain(world.xz * 3.0) * 0.6 + grain(world.xz * 0.35) * 0.4;
            c *= 0.85 + 0.3 * n;
            if (UV2.x >= 0.0) {
                float across = (UV.x - 0.5) * width;
                float hw = width * 0.5;
                // Worn wheel tracks in each lane.
                float lane = abs(abs(across) - hw * 0.5);
                c *= 1.0 - 0.08 * (1.0 - smoothstep(0.3, 0.9, abs(lane - 0.9)));
                float white = band(abs(across), hw - 0.55, hw - 0.35);
                float yellow = band(abs(across), 0.08, 0.2);
                // Stop lines across the lane that arrives at a junction, a little short of its plate.
                float toStart = UV2.x;
                float toEnd = UV2.y;
                white = max(white, band(toStart, 0.6, 1.1) * step(across, -0.05));
                white = max(white, band(toEnd, 0.6, 1.1) * step(0.05, across));
                // The centre line stops at the stop lines.
                yellow *= step(1.1, toStart) * step(1.1, toEnd);
                float wear = 0.8 + 0.2 * grain(world.xz * 1.7);
                c = mix(c, vec3(0.92, 0.92, 0.9) * wear, white);
                c = mix(c, vec3(0.95, 0.74, 0.12) * wear, yellow);
            }
            ALBEDO = c;
            ROUGHNESS = 0.85;
        }
        """;

    private const string ConcreteCode = """
        shader_type spatial;

        uniform float width = 10.0;

        """ + Grain + """

        void fragment() {
            vec3 c = vec3(0.74, 0.73, 0.69);
            c *= 0.9 + 0.15 * grain(UV * 4.0);
            // Block seams every two metres along.
            float seam = 1.0 - smoothstep(0.0, max(fwidth(UV.x) * 1.5, 0.02), abs(fract(UV.x / 2.0 + 0.5) - 0.5) * 2.0);
            c *= 1.0 - 0.25 * seam;
            ALBEDO = c;
            ROUGHNESS = 0.9;
        }
        """;

    private const string HazardCode = """
        shader_type spatial;

        uniform float width = 10.0;

        void fragment() {
            float stripe = step(0.5, fract((UV.x + UV.y) / 0.36));
            ALBEDO = mix(vec3(0.06, 0.06, 0.06), vec3(0.98, 0.78, 0.1), stripe);
            ROUGHNESS = 0.6;
        }
        """;

    private const string SignCode = """
        shader_type spatial;

        uniform float width = 10.0;

        void fragment() {
            vec2 p = UV - vec2(0.5);
            float diamond = abs(p.x) + abs(p.y);
            vec3 c = vec3(0.98, 0.8, 0.12);
            // A black border and an exclamation mark.
            float mark = step(0.43, diamond);
            mark = max(mark, step(abs(p.x), 0.035) * step(-0.05, p.y) * step(p.y, 0.25));
            mark = max(mark, step(length(p - vec2(0.0, -0.15)), 0.045));
            ALBEDO = mix(c, vec3(0.05), mark);
            ROUGHNESS = 0.5;
        }
        """;
}
