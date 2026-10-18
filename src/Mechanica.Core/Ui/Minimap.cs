using System.Numerics;

namespace Mechanica.Core.Ui;

/// <summary>
/// The minimap's geometry: a north-up square on the HUD centred on the player, a fixed number of metres to a pixel
/// (<see cref="LogbookSettings.MinimapMetresPerPixel"/>). The picture under it covers twice the view each way, so it
/// is drawn again only once the player has walked an eighth of it from where it was drawn. Pins past the edge stay
/// on the rim, pointing the way to go.
/// </summary>
public static class Minimap
{
    /// <summary>
    /// Where a pin at world x/z <paramref name="target"/> sits on a minimap <paramref name="size"/> pixels square
    /// centred on world x/z <paramref name="centre"/>, from its top-left corner (x east, y south). A pin past the edge
    /// is pulled in along its bearing to <paramref name="inset"/> pixels inside the rim, and <c>AtEdge</c> says so.
    /// </summary>
    public static (Vector2 At, bool AtEdge) Pin(Vector2 centre, Vector2 target, float metresPerPixel, float size, float inset)
    {
        var half = size / 2f;
        var offset = (target - centre) / metresPerPixel;
        var limit = MathF.Max(0f, half - inset);
        var reach = MathF.Max(MathF.Abs(offset.X), MathF.Abs(offset.Y));
        if (reach <= limit)
            return (new Vector2(half) + offset, false);
        return (new Vector2(half) + offset * (limit / reach), true);
    }

    /// <summary>
    /// The square of world to draw for a view centred on <paramref name="centre"/>: its north-west corner and its
    /// side in metres, twice the view's. The corner snaps to whole pixels, so pictures drawn from nearby spots line up.
    /// </summary>
    public static (Vector2 Min, float Metres) Cover(Vector2 centre, float metresPerPixel, float size)
    {
        var metres = size * metresPerPixel * 2f;
        var min = centre - new Vector2(metres / 2f);
        return (new Vector2(MathF.Round(min.X / metresPerPixel), MathF.Round(min.Y / metresPerPixel)) * metresPerPixel, metres);
    }

    /// <summary>True once a view centred on <paramref name="centre"/> has moved an eighth of the drawn square from its middle.</summary>
    public static bool NeedsRedraw(Vector2 drawnMin, float drawnMetres, Vector2 centre)
    {
        var middle = drawnMin + new Vector2(drawnMetres / 2f);
        var gap = centre - middle;
        return MathF.Max(MathF.Abs(gap.X), MathF.Abs(gap.Y)) > drawnMetres / 8f;
    }
}
