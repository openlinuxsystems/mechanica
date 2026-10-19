using Godot;
using Mechanica.Core.World;

namespace Mechanica.Game.World;

/// <summary>
/// The flora shader and the meshes and materials built from the Core <see cref="FloraModels"/>. One material per kind
/// of flora, so each kind's Foliage density level sets its own wind and fade distances; the wind and the plants'
/// pushers (players, bots, vehicles) are global shader parameters that <see cref="FloraField"/> updates every frame.
/// <list type="bullet">
/// <item>Leaves take the plant's tint (multimesh custom data, linear RGB; A is a random phase for the wind). Flowers
/// and bolts keep their own vertex colour.</item>
/// <item>Wind: a low sine sway driven by world position and time, stronger towards the tips.</item>
/// <item>Distance: past the fade start each plant drops its higher-ranked blades and lumps first, then sinks away by
/// the fade end, so the edge of the drawn area thins out instead of popping; the ground colour carries on beyond.</item>
/// <item>Appearing: a chunk's plants grow in from nothing over a moment when first drawn (instance parameter
/// <see cref="Appear"/>), so a chunk built late never pops in.</item>
/// <item>Pushers: plants within the push radius bend away from a player, bot or vehicle and press down a little.</item>
/// </list>
/// </summary>
public static class FloraMaterials
{
    /// <summary>Global shader parameter: wind direction x, z, strength (m per m of height) and speed (rad/s).</summary>
    public const string Wind = "flora_wind";

    /// <summary>Global shader parameters: a pusher's position and radius (0: none).</summary>
    public static readonly string[] Pushers = ["flora_push_0", "flora_push_1", "flora_push_2", "flora_push_3"];

    /// <summary>Instance shader parameter: how far (0..1) a mesh's plants have grown in since they were first drawn.</summary>
    public const string Appear = "appear";

    /// <summary>Global shader parameter: how far plants bend from a pusher.</summary>
    public const string PushStrength = "flora_push_strength";

    private static readonly Dictionary<FloraKind, ShaderMaterial> KindMaterials = [];
    private static readonly Dictionary<string, (ArrayMesh Full, ArrayMesh Lod)> ModelMeshes = [];

    /// <summary>Forgets the flora models' meshes, so a new world builds them from its mods afresh.</summary>
    public static void ClearModels() => ModelMeshes.Clear();
    private static bool _globals;

    /// <summary>The material for a kind of flora.</summary>
    public static ShaderMaterial For(FloraKind kind)
    {
        EnsureGlobals();
        if (!KindMaterials.TryGetValue(kind, out var material))
        {
            var shader = new Shader { Code = Code(cullBack: kind == FloraKind.Bush) };
            KindMaterials[kind] = material = new ShaderMaterial { Shader = shader };
            Configure(material, windScale: 1f, speedScale: 1f, fadeStart: 40f, fadeEnd: 55f);
        }
        return material;
    }

    /// <summary>A plant model's full and far meshes.</summary>
    public static (ArrayMesh Full, ArrayMesh Lod) Meshes(FloraModel model)
    {
        if (!ModelMeshes.TryGetValue(model.Id, out var meshes))
            ModelMeshes[model.Id] = meshes = (ToArrayMesh(model.Mesh), ToArrayMesh(model.Lod));
        return meshes;
    }

    /// <summary>Sets a material's wind share and the distances its plants fade out over.</summary>
    public static void Configure(ShaderMaterial material, float windScale, float speedScale, float fadeStart, float fadeEnd)
    {
        material.SetShaderParameter("wind_scale", windScale);
        material.SetShaderParameter("speed_scale", speedScale);
        material.SetShaderParameter("fade_start", fadeStart);
        material.SetShaderParameter("fade_end", Mathf.Max(fadeEnd, fadeStart + 0.01f));
    }

    /// <summary>Registers the global shader parameters the flora shader reads. Safe to call more than once.</summary>
    public static void EnsureGlobals()
    {
        if (_globals)
            return;
        _globals = true;
        var existing = RenderingServer.GlobalShaderParameterGetList();
        void Add(string name, RenderingServer.GlobalShaderParameterType type, Variant value)
        {
            if (!existing.Contains(name))
                RenderingServer.GlobalShaderParameterAdd(name, type, value);
        }
        Add(Wind, RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(1f, 0f, 0f, 1f));
        foreach (var name in Pushers)
            Add(name, RenderingServer.GlobalShaderParameterType.Vec4, Vector4.Zero);
        Add(PushStrength, RenderingServer.GlobalShaderParameterType.Float, 0f);
    }

    // Leaves' shade and accent channels stay as they are; flowers' and bolts' own colours are sRGB, so they go linear here.
    private static ArrayMesh ToArrayMesh(FloraMesh mesh)
    {
        var count = mesh.Positions.Length;
        var vertices = new Vector3[count];
        var normals = new Vector3[count];
        var colors = new Color[count];
        var uvs = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            vertices[i] = mesh.Positions[i].ToGodot();
            normals[i] = mesh.Normals[i].ToGodot();
            var c = mesh.Colors[i];
            colors[i] = c.W > 0.5f ? new Color(c.X, c.Y, c.Z, 1f) : new Color(c.X, c.Y, c.Z, 0f).SrgbToLinear();
            uvs[i] = new Vector2(mesh.Uvs[i].X, mesh.Uvs[i].Y);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        var result = new ArrayMesh();
        result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return result;
    }

    private static string Code(bool cullBack) => $$"""
        shader_type spatial;
        render_mode {{(cullBack ? "cull_back" : "cull_disabled")}}, specular_disabled, diffuse_lambert_wrap;

        global uniform vec4 flora_wind;
        global uniform vec4 flora_push_0;
        global uniform vec4 flora_push_1;
        global uniform vec4 flora_push_2;
        global uniform vec4 flora_push_3;
        global uniform float flora_push_strength;

        uniform float wind_scale = 1.0;
        uniform float speed_scale = 1.0;
        uniform float fade_start = 40.0;
        uniform float fade_end = 55.0;
        uniform float brightness = 1.1;
        uniform vec3 accent_color : source_color = vec3(0.98, 0.86, 0.2);
        instance uniform float appear = 1.0;

        varying vec3 tint;

        vec3 push(vec4 pusher, vec3 origin, float height) {
            if (pusher.w <= 0.0 || abs(origin.y - pusher.y) > 2.5) {
                return vec3(0.0);
            }
            vec2 away = origin.xz - pusher.xz;
            float d = length(away);
            float k = 1.0 - smoothstep(pusher.w * 0.3, pusher.w, d);
            vec2 dir = d > 0.001 ? away / d : vec2(1.0, 0.0);
            return vec3(dir.x, -0.6, dir.y) * (flora_push_strength * height * k);
        }

        void vertex() {
            tint = INSTANCE_CUSTOM.rgb;
            vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
            vec3 eye = INV_VIEW_MATRIX[3].xyz;
            float fade = 1.0 - smoothstep(fade_start, fade_end, distance(origin, eye));
            // Higher-ranked blades and lumps go first; the rest sink away over the last stretch.
            float kept = step(UV.x, fade * 1.6);
            float size = smoothstep(0.0, 0.35, fade) * kept * smoothstep(0.0, 1.0, appear);
            float scale = length(MODEL_MATRIX[0].xyz);
            float height = VERTEX.y * scale * UV.y;
            vec2 dir = flora_wind.xy;
            float phase = dot(origin.xz, dir) * 0.35 + INSTANCE_CUSTOM.a * 6.2832;
            float t = TIME * flora_wind.w * speed_scale;
            float sway = sin(t - phase) * 0.7 + sin(t * 1.7 + phase * 1.3) * 0.3;
            vec3 offset = vec3(dir.x, 0.0, dir.y) * (flora_wind.z * wind_scale * height * (0.55 + 0.45 * sway));
            offset += push(flora_push_0, origin, height) + push(flora_push_1, origin, height)
                + push(flora_push_2, origin, height) + push(flora_push_3, origin, height);
            VERTEX = (VERTEX + transpose(mat3(MODEL_MATRIX)) * offset / max(scale * scale, 0.0001)) * size;
        }

        void fragment() {
            vec3 leaf = tint * COLOR.r * brightness;
            vec3 tip = mix(tint, accent_color, 0.6) * max(COLOR.r, 0.85) * brightness;
            ALBEDO = COLOR.a > 0.5 ? mix(leaf, tip, COLOR.g) : COLOR.rgb;
            ROUGHNESS = 0.85;
        }
        """;
}
