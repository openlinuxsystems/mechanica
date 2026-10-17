using Godot;

namespace Mechanica.Game.Ui;

/// <summary>
/// The loading screen's illustration (UI spec §15): an original blueprint line drawing of a half-built vehicle
/// chassis, a frame of blocks on four wheels with a bearing and an engine, in the theme's blue with cyan
/// highlights, swaying slowly as a parallax shift. Nothing here takes input.
/// </summary>
public partial class BlueprintChassis : Control
{
    private double _time;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Process(double delta)
    {
        _time += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        var unit = Mathf.Min(size.X / 16f, size.Y / 9f);
        var sway = (float)Math.Sin(_time * 0.35);
        var centre = size / 2 + new Vector2(sway * unit * 0.25f, (float)Math.Cos(_time * 0.27) * unit * 0.12f);
        // A faint grid behind, moving less than the chassis for depth.
        var grid = UiTokens.Blue with { A = 0.08f };
        var shift = new Vector2(sway * unit * 0.08f, 0);
        for (var x = -8; x <= 8; x++)
            DrawLine(size / 2 + shift + new Vector2(x * unit, -4.5f * unit), size / 2 + shift + new Vector2(x * unit, 4.5f * unit), grid, 1);
        for (var y = -4; y <= 4; y++)
            DrawLine(size / 2 + shift + new Vector2(-8 * unit, y * unit), size / 2 + shift + new Vector2(8 * unit, y * unit), grid, 1);

        var line = UiTokens.Blue with { A = 0.55f };
        var glow = UiTokens.Cyan with { A = 0.7f };
        Vector2 P(float x, float y) => centre + new Vector2(x, y) * unit;
        // The frame: a row of blocks, the last few still dashed outlines (not built yet).
        for (var i = 0; i < 8; i++)
        {
            var at = P(-4 + i, -0.5f);
            var rect = new Rect2(at, new Vector2(unit, unit));
            if (i < 6)
                DrawRect(rect, line, false, 2);
            else
                Dashed(rect, glow);
        }
        // A cab on top at the front, an engine block behind it.
        DrawRect(new Rect2(P(1, -2.5f), new Vector2(2 * unit, 2 * unit)), line, false, 2);
        DrawLine(P(1.3f, -2.2f), P(2.7f, -2.2f), line, 1);
        DrawRect(new Rect2(P(-3, -1.6f), new Vector2(1.6f * unit, 1.1f * unit)), glow, false, 2);
        for (var i = 0; i < 4; i++)
            DrawLine(P(-2.8f + i * 0.4f, -1.6f), P(-2.8f + i * 0.4f, -0.5f), glow with { A = 0.35f }, 1);
        // Axles and four wheels (two drawn, two behind, offset), turning.
        var turn = (float)(_time * 0.6);
        foreach (var (x, depth) in new[] { (-3f, 0.6f), (2.5f, 0.6f), (-2.6f, 1f), (2.9f, 1f) })
        {
            var hub = P(x, 1.4f) + new Vector2(0, (1 - depth) * -unit * 0.4f);
            var radius = unit * 0.9f * depth;
            var colour = depth < 1 ? line with { A = 0.3f } : line;
            DrawArc(hub, radius, 0, Mathf.Tau, 40, colour, 2);
            DrawArc(hub, radius * 0.35f, 0, Mathf.Tau, 20, depth < 1 ? colour : glow, 2);
            for (var s = 0; s < 6; s++)
            {
                var a = turn + s * Mathf.Tau / 6;
                DrawLine(hub + Vector2.FromAngle(a) * radius * 0.35f, hub + Vector2.FromAngle(a) * radius, colour, 1);
            }
        }
        DrawLine(P(-3, 1.4f), P(2.5f, 1.4f), line, 2);
        DrawLine(P(-3, 0.5f), P(-3, 1.4f), line, 2);
        DrawLine(P(2.5f, 0.5f), P(2.5f, 1.4f), line, 2);
        // A bearing on a post with a connection line to the engine, the way the Connect Tool draws it.
        DrawArc(P(4.6f, -1.5f), unit * 0.3f, 0, Mathf.Tau, 24, glow, 2);
        DrawLine(P(4.6f, -1.2f), P(4.6f, -0.5f), line, 2);
        var pulse = 0.5f + 0.5f * (float)Math.Sin(_time * 2);
        DrawLine(P(-1.4f, -1.05f), P(4.3f, -1.5f), UiTokens.Cyan with { A = 0.25f + 0.35f * pulse }, 2);
        // Dimension marks along the bottom, as on a drawing.
        DrawLine(P(-4, 2.9f), P(4, 2.9f), line with { A = 0.35f }, 1);
        DrawLine(P(-4, 2.7f), P(-4, 3.1f), line with { A = 0.35f }, 1);
        DrawLine(P(4, 2.7f), P(4, 3.1f), line with { A = 0.35f }, 1);
    }

    private void Dashed(Rect2 rect, Color colour)
    {
        Vector2[] corners = [rect.Position, rect.Position + new Vector2(rect.Size.X, 0), rect.End, rect.Position + new Vector2(0, rect.Size.Y)];
        for (var i = 0; i < 4; i++)
            DrawDashedLine(corners[i], corners[(i + 1) % 4], colour, 2, 6);
    }
}
