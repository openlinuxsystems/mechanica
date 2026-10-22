using Godot;
using Mechanica.Core.Survival;

namespace Mechanica.Game.Survival;

/// <summary>
/// The glow round a loose item on the ground, as in Scrap Mechanic: a soft green sphere, brightest at its rim, round
/// the item floating in its middle, and a thin beam of light rising from it so items can be found in grass and from
/// afar. The sphere is the pile's aim sphere (<see cref="DropAim"/>), so what the player sees is what they can pick up.
/// </summary>
public static class DropGlow
{
    /// <summary>The glow's colour.</summary>
    public static readonly Color Color = new(0.62f, 0.95f, 0.25f);

    /// <summary>How high the beam rises above the ground, in metres.</summary>
    public const float BeamHeight = 2.6f;

    private const float BeamRadius = 0.025f;

    private static ShaderMaterial? _sphere;
    private static ShaderMaterial? _beam;
    private static SphereMesh? _sphereMesh;
    private static CylinderMesh? _beamMesh;

    /// <summary>A new glow sphere, centred on its origin.</summary>
    public static MeshInstance3D Sphere()
    {
        _sphere ??= Material(SphereCode);
        _sphereMesh ??= new SphereMesh { Radius = DropAim.Radius, Height = DropAim.Radius * 2, RadialSegments = 32, Rings = 16, Material = _sphere };
        return new MeshInstance3D { Name = "Glow", Mesh = _sphereMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    /// <summary>A new beam, standing on its origin.</summary>
    public static MeshInstance3D Beam()
    {
        if (_beam is null)
        {
            _beam = Material(BeamCode);
            _beam.SetShaderParameter("beam_height", BeamHeight);
        }
        _beamMesh ??= new CylinderMesh
        {
            TopRadius = BeamRadius, BottomRadius = BeamRadius, Height = BeamHeight, RadialSegments = 8, Rings = 1,
            CapTop = false, CapBottom = false, Material = _beam,
        };
        return new MeshInstance3D
        {
            Name = "Beam", Mesh = _beamMesh, Position = new Vector3(0, BeamHeight / 2, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static ShaderMaterial Material(string code)
    {
        var material = new ShaderMaterial { Shader = new Shader { Code = code } };
        material.SetShaderParameter("glow", Color);
        return material;
    }

    // Brightest where the surface turns away from the eye (its rim), faintly tinted across the middle, breathing slowly.
    private const string SphereCode = """
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never, cull_back, shadows_disabled, fog_disabled;
        uniform vec3 glow : source_color;
        void fragment() {
            float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
            float rim = pow(1.0 - facing, 3.0);
            float breathe = 0.9 + 0.1 * sin(TIME * 2.4);
            ALBEDO = glow;
            ALPHA = clamp((rim * 1.6 + 0.06) * breathe, 0.0, 1.0);
        }
        """;

    // A soft line: brightest along its middle as seen, fading out towards its top.
    private const string BeamCode = """
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;
        uniform vec3 glow : source_color;
        uniform float beam_height = 1.0;
        varying float height;
        void vertex() {
            height = VERTEX.y / beam_height + 0.5;
        }
        void fragment() {
            float facing = abs(dot(NORMAL, VIEW));
            float fade = 1.0 - smoothstep(0.1, 1.0, height);
            ALBEDO = glow;
            ALPHA = clamp(facing * facing * fade * 0.75, 0.0, 1.0);
        }
        """;
}
