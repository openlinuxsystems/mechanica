using System.Globalization;
using System.Numerics;
using System.Text;

namespace Mechanica.Core.Ui;

/// <summary>A colour-blind filter (UI spec §16 Interface).</summary>
public enum ColorBlindMode
{
    /// <summary>No filter.</summary>
    Off,

    /// <summary>For red-blindness (no long-wavelength cones).</summary>
    Protanopia,

    /// <summary>For green-blindness (no medium-wavelength cones).</summary>
    Deuteranopia,

    /// <summary>For blue-blindness (no short-wavelength cones).</summary>
    Tritanopia,
}

/// <summary>
/// Daltonization: simulate what a colour-blind eye sees (Machado, Oliveira and Fernandes 2009, full
/// severity), take the difference from the real colour, and shift that lost difference into channels the
/// eye still tells apart, so colours that would look alike (red and green; blue and green, yellow and
/// violet) stay distinct. Greys are left alone. The game's full-screen filter runs <see cref="Shader"/>,
/// built from the same matrices as <see cref="Correct"/>.
/// </summary>
public static class ColorBlindness
{
    // Red-green losses go into green and blue; a blue loss goes into red and green.
    private static readonly Matrix4x4 RedGreenShift = Rows(
        0f, 0f, 0f,
        0.7f, 1f, 0f,
        0.7f, 0f, 1f);

    private static readonly Matrix4x4 BlueShift = Rows(
        1f, 0f, 0.7f,
        0f, 1f, 0.7f,
        0f, 0f, 0f);

    private static Matrix4x4 Simulation(ColorBlindMode mode) => mode switch
    {
        ColorBlindMode.Protanopia => Rows(0.152286f, 1.052583f, -0.204868f, 0.114503f, 0.786281f, 0.099216f, -0.003882f, -0.048116f, 1.051998f),
        ColorBlindMode.Deuteranopia => Rows(0.367322f, 0.860646f, -0.227968f, 0.280085f, 0.672501f, 0.047413f, -0.011820f, 0.042940f, 0.968881f),
        ColorBlindMode.Tritanopia => Rows(1.255528f, -0.076749f, -0.178779f, -0.078411f, 0.930809f, 0.147602f, 0.004733f, 0.691367f, 0.303900f),
        _ => Matrix4x4.Identity,
    };

    /// <summary>What an eye with <paramref name="mode"/> sees of <paramref name="rgb"/> (0 to 1 each).</summary>
    public static Vector3 Simulate(Vector3 rgb, ColorBlindMode mode) =>
        mode == ColorBlindMode.Off ? rgb : Apply(Simulation(mode), rgb);

    /// <summary><paramref name="rgb"/> corrected for <paramref name="mode"/>, clamped to 0 to 1.</summary>
    public static Vector3 Correct(Vector3 rgb, ColorBlindMode mode) =>
        mode == ColorBlindMode.Off ? rgb : Vector3.Clamp(Apply(Correction(mode), rgb), Vector3.Zero, Vector3.One);

    /// <summary>The whole correction as one RGB matrix: <c>I + Shift × (I − Simulation)</c>.</summary>
    public static Matrix4x4 Correction(ColorBlindMode mode)
    {
        if (mode == ColorBlindMode.Off)
            return Matrix4x4.Identity;
        var lost = Matrix4x4.Identity - Simulation(mode);
        lost.M44 = 0;
        var m = Matrix4x4.Identity + Mul(mode == ColorBlindMode.Tritanopia ? BlueShift : RedGreenShift, lost);
        m.M44 = 1;
        return m;
    }

    /// <summary>The Godot canvas shader that corrects the screen for <paramref name="mode"/>.</summary>
    public static string Shader(ColorBlindMode mode)
    {
        var m = Correction(mode);
        var c = CultureInfo.InvariantCulture;
        // GLSL mat3 is column-major: each vec3 is a column.
        string Col(float a, float b, float d) => string.Create(c, $"vec3({a:0.######}, {b:0.######}, {d:0.######})");
        var sb = new StringBuilder();
        sb.AppendLine("shader_type canvas_item;");
        sb.AppendLine("uniform sampler2D screen : hint_screen_texture, filter_nearest;");
        sb.Append("const mat3 CORRECT = mat3(")
            .Append(Col(m.M11, m.M21, m.M31)).Append(", ")
            .Append(Col(m.M12, m.M22, m.M32)).Append(", ")
            .Append(Col(m.M13, m.M23, m.M33)).AppendLine(");");
        sb.AppendLine("void fragment() {");
        sb.AppendLine("    vec3 rgb = texture(screen, SCREEN_UV).rgb;");
        sb.AppendLine("    COLOR = vec4(clamp(CORRECT * rgb, 0.0, 1.0), 1.0);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // Row-major 3×3 product (the 4th row and column are unused).
    private static Matrix4x4 Mul(Matrix4x4 a, Matrix4x4 b)
    {
        float[,] x = { { a.M11, a.M12, a.M13 }, { a.M21, a.M22, a.M23 }, { a.M31, a.M32, a.M33 } };
        float[,] y = { { b.M11, b.M12, b.M13 }, { b.M21, b.M22, b.M23 }, { b.M31, b.M32, b.M33 } };
        var r = new float[3, 3];
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
            r[i, j] = x[i, 0] * y[0, j] + x[i, 1] * y[1, j] + x[i, 2] * y[2, j];
        return Rows(r[0, 0], r[0, 1], r[0, 2], r[1, 0], r[1, 1], r[1, 2], r[2, 0], r[2, 1], r[2, 2]);
    }

    private static Vector3 Apply(Matrix4x4 m, Vector3 v) => new(
        m.M11 * v.X + m.M12 * v.Y + m.M13 * v.Z,
        m.M21 * v.X + m.M22 * v.Y + m.M23 * v.Z,
        m.M31 * v.X + m.M32 * v.Y + m.M33 * v.Z);

    private static Matrix4x4 Rows(float a, float b, float c, float d, float e, float f, float g, float h, float i) =>
        new(a, b, c, 0, d, e, f, 0, g, h, i, 0, 0, 0, 0, 0);
}
