using Godot;

namespace Mechanica.Game.Ui;

/// <summary>
/// The home screen's backdrop (UI spec §14): deep navy with a soft blue light, slowly turning gear outlines
/// and a conveyor line drifting across, all drawn in the theme's blue and cyan. Nothing here takes input.
/// </summary>
public partial class HomeBackground : Control
{
    private readonly (Vector2 At, float Radius, int Teeth, float Speed)[] _gears =
    [
        (new(0.12f, 0.78f), 150, 16, 0.05f),
        (new(0.27f, 0.62f), 80, 10, -0.09f),
        (new(0.86f, 0.24f), 190, 20, 0.035f),
        (new(0.74f, 0.44f), 70, 9, -0.1f),
        (new(0.92f, 0.86f), 110, 12, 0.06f),
    ];

    private double _time;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), UiTokens.DeepNavy);
        // The light: rings fading out from the upper middle.
        var light = new Vector2(size.X * 0.5f, size.Y * 0.32f);
        for (var i = 12; i > 0; i--)
            DrawCircle(light, size.Y * 0.07f * i, UiTokens.Blue with { A = 0.012f });
        var line = UiTokens.Blue with { A = 0.28f };
        foreach (var (at, radius, teeth, speed) in _gears)
            Gear(new Vector2(at.X * size.X, at.Y * size.Y), radius, teeth, (float)(_time * speed), line);
        // A conveyor: a belt with rollers and boxes sliding along it.
        var y = size.Y * 0.9f;
        DrawLine(new Vector2(0, y), new Vector2(size.X, y), line, 2);
        DrawLine(new Vector2(0, y + 18), new Vector2(size.X, y + 18), line, 2);
        var shift = (float)(_time * 24 % 80);
        for (var x = -80f + shift; x < size.X + 80; x += 80)
            DrawArc(new Vector2(x, y + 9), 8, 0, Mathf.Tau, 16, line, 1.5f);
        var box = UiTokens.Cyan with { A = 0.18f };
        for (var x = -240f + (float)(_time * 24 % 240); x < size.X + 240; x += 240)
            DrawRect(new Rect2(x, y - 34, 40, 34), box, false, 1.5f);
    }

    private void Gear(Vector2 center, float radius, int teeth, float angle, Color color)
    {
        var points = new Vector2[teeth * 4 + 1];
        for (var i = 0; i < teeth; i++)
        {
            var a = angle + i * Mathf.Tau / teeth;
            var step = Mathf.Tau / teeth / 4;
            points[i * 4] = center + Vector2.FromAngle(a) * radius * 0.85f;
            points[i * 4 + 1] = center + Vector2.FromAngle(a + step) * radius;
            points[i * 4 + 2] = center + Vector2.FromAngle(a + step * 2) * radius;
            points[i * 4 + 3] = center + Vector2.FromAngle(a + step * 3) * radius * 0.85f;
        }
        points[^1] = points[0];
        DrawPolyline(points, color, 2, true);
        DrawArc(center, radius * 0.35f, 0, Mathf.Tau, 32, color, 2);
        for (var i = 0; i < 4; i++)
            DrawLine(center + Vector2.FromAngle(angle + i * Mathf.Tau / 4) * radius * 0.35f, center + Vector2.FromAngle(angle + i * Mathf.Tau / 4) * radius * 0.75f, color, 2);
    }
}
