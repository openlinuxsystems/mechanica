using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Quests;
using Mechanica.Core.World;

namespace Mechanica.Core.Ui;

/// <summary>
/// The Logbook's numbers (<c>game/data/logbook.json</c>, UI spec §5, §10, §13): how finely the map is drawn, how far
/// it zooms, how close a click must be to pick a marker, and how many markers a player may set.
/// </summary>
public sealed record LogbookSettings
{
    /// <summary>Pixels across the whole-world map drawn when the Logbook first opens.</summary>
    public int MapResolution { get; init; } = 512;

    /// <summary>Pixels across the sharper map of the area in view, drawn once panning and zooming settle.</summary>
    public int DetailResolution { get; init; } = 384;

    /// <summary>Seconds the view must hold still before the sharper map is drawn.</summary>
    public double DetailDelaySeconds { get; init; } = 0.25;

    /// <summary>Closest zoom, in metres per screen pixel.</summary>
    public float MinMetresPerPixel { get; init; } = 0.25f;

    /// <summary>How much one wheel step zooms (1.25 is 25%).</summary>
    public float ZoomStep { get; init; } = 1.25f;

    /// <summary>Metres between the map's height lines.</summary>
    public float ContourInterval { get; init; } = 10f;

    /// <summary>Screen pixels within which a click picks a marker.</summary>
    public float PickRadius { get; init; } = 12f;

    /// <summary>Markers a player may set (the host refuses more).</summary>
    public int MaxMarkers { get; init; } = 20;

    /// <summary>Longest marker name, in characters.</summary>
    public int MarkerNameLength { get; init; } = 32;

    /// <summary>Reads <paramref name="json"/>; throws <see cref="FormatException"/> on bad data.</summary>
    public static LogbookSettings Parse(string json)
    {
        LogbookSettings? settings;
        try
        {
            settings = Data.DataJson.Read<LogbookSettings>(json);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Invalid logbook JSON: {e.Message}", e);
        }
        settings ??= new LogbookSettings();
        if (settings.MapResolution is < 16 or > 4096 || settings.DetailResolution is < 16 or > 4096)
            throw new FormatException("map_resolution and detail_resolution must be 16 to 4096.");
        if (!(settings.MinMetresPerPixel > 0) || !(settings.ZoomStep > 1) || !(settings.ContourInterval > 0) || settings.PickRadius <= 0
            || settings.DetailDelaySeconds < 0)
            throw new FormatException("min_metres_per_pixel, contour_interval and pick_radius must be above zero, zoom_step above 1 and detail_delay_seconds 0 or more.");
        if (settings.MaxMarkers < 1 || settings.MarkerNameLength < 1)
            throw new FormatException("max_markers and marker_name_length must be at least 1.");
        return settings;
    }
}

/// <summary>
/// Where the Logbook map looks (UI spec §10): a centre in world x/z and a zoom in metres per screen pixel. Dragging
/// pans, the wheel zooms about the cursor, and the view never leaves the world or zooms out past the whole of it.
/// It lives as long as the game does, so closing the Logbook or resizing the window keeps the pan and zoom (§11).
/// </summary>
public sealed class MapView(float worldSize, float minMetresPerPixel)
{
    /// <summary>Side of the square world in metres.</summary>
    public float WorldSize => worldSize;

    /// <summary>World x/z at the middle of the map.</summary>
    public Vector2 Centre { get; private set; }

    /// <summary>Metres per screen pixel.</summary>
    public float MetresPerPixel { get; private set; } = float.NaN;

    /// <summary>False until <see cref="Fit"/> or a zoom has set the scale.</summary>
    public bool Placed => float.IsFinite(MetresPerPixel);

    /// <summary>The widest zoom on a map <paramref name="size"/> pixels big: the whole world just fits.</summary>
    public float MaxMetresPerPixel(Vector2 size) => worldSize / MathF.Max(1f, MathF.Min(size.X, size.Y));

    /// <summary>Shows the whole world on a map <paramref name="size"/> pixels big.</summary>
    public void Fit(Vector2 size)
    {
        Centre = Vector2.Zero;
        MetresPerPixel = MaxMetresPerPixel(size);
    }

    /// <summary>Centres the map on <paramref name="world"/> x/z.</summary>
    public void CentreOn(Vector2 world) => Centre = Clamp(world);

    /// <summary>The screen point (from the map's top-left) of world x/z <paramref name="world"/>.</summary>
    public Vector2 ToScreen(Vector2 world, Vector2 size) => size / 2 + (world - Centre) / MetresPerPixel;

    /// <summary>The world x/z under screen point <paramref name="screen"/>.</summary>
    public Vector2 ToWorld(Vector2 screen, Vector2 size) => Centre + (screen - size / 2) * MetresPerPixel;

    /// <summary>Moves the map by a drag of <paramref name="pixels"/>: the ground follows the pointer.</summary>
    public void Pan(Vector2 pixels) => Centre = Clamp(Centre - pixels * MetresPerPixel);

    /// <summary>
    /// Zooms by <paramref name="steps"/> wheel steps of <paramref name="step"/> (positive zooms in) keeping the ground
    /// under <paramref name="screen"/> where it is, within the closest zoom and the whole world.
    /// </summary>
    public void ZoomAt(Vector2 screen, Vector2 size, float steps, float step)
    {
        if (!Placed)
            Fit(size);
        var anchor = ToWorld(screen, size);
        MetresPerPixel = Math.Clamp(MetresPerPixel / MathF.Pow(step, steps), minMetresPerPixel, MaxMetresPerPixel(size));
        Centre = Clamp(anchor - (screen - size / 2) * MetresPerPixel);
    }

    /// <summary>Keeps the zoom inside its limits after the map changed size (§11: the view is kept, not reset).</summary>
    public void Resized(Vector2 size)
    {
        if (!Placed)
            Fit(size);
        else
            MetresPerPixel = Math.Clamp(MetresPerPixel, minMetresPerPixel, MaxMetresPerPixel(size));
    }

    /// <summary>The world rectangle (min x/z, max x/z) a map <paramref name="size"/> pixels big shows.</summary>
    public (Vector2 Min, Vector2 Max) Visible(Vector2 size) => (ToWorld(Vector2.Zero, size), ToWorld(size, size));

    private Vector2 Clamp(Vector2 world)
    {
        var half = worldSize / 2;
        return new Vector2(Math.Clamp(world.X, -half, half), Math.Clamp(world.Y, -half, half));
    }
}

/// <summary>One quest in the Logbook's list.</summary>
/// <param name="Quest">The quest.</param>
/// <param name="Done">True once finished; the list shows it greyed out (§10).</param>
/// <param name="Step">The current step of an active quest.</param>
/// <param name="Progress">Its progress, null before it starts.</param>
public sealed record LogbookQuest(Quest Quest, bool Done, QuestStepInfo? Step, QuestProgress? Progress)
{
    /// <summary>The waypoint id the compass gives this quest's goal.</summary>
    public string WaypointId => Logbook.QuestWaypoint(Quest.QualifiedId);
}

/// <summary>What the Logbook lists (UI spec §5, §10).</summary>
public static class Logbook
{
    /// <summary>The compass waypoint id of a quest's current goal.</summary>
    public static string QuestWaypoint(string quest) => $"quest:{quest}";

    /// <summary>Every started quest: active ones first, in book order, then finished ones.</summary>
    public static IReadOnlyList<LogbookQuest> Quests(QuestBook book, QuestState state, Func<string, QuestStepInfo?> currentStep)
    {
        var active = new List<LogbookQuest>();
        var done = new List<LogbookQuest>();
        foreach (var quest in book.All)
        {
            switch (state.Of(quest.QualifiedId))
            {
                case { Status: QuestStatus.Done } progress:
                    done.Add(new LogbookQuest(quest, true, null, progress));
                    break;
                case { Status: QuestStatus.Active } progress:
                    active.Add(new LogbookQuest(quest, false, currentStep(quest.QualifiedId), progress));
                    break;
            }
        }
        return [.. active, .. done];
    }
}

/// <summary>
/// Draws the Logbook's map from the world generator, in the blue theme (§13): sea in deep navy that lightens towards
/// the shore, land in blues whose lightness follows the biome and the height, lit from the north-west, with a thin
/// line every <see cref="LogbookSettings.ContourInterval"/> metres of height. Pure and thread-safe, so the game draws
/// it off the main thread.
/// </summary>
public static class WorldMap
{
    private static readonly Vector3 DeepSea = Rgb(UiPalette.DeepNavy);
    private static readonly Vector3 Shallows = new(0.10f, 0.20f, 0.32f);
    private static readonly Vector3 Lowland = new(0.16f, 0.27f, 0.40f);
    private static readonly Vector3 Highland = new(0.42f, 0.58f, 0.74f);

    /// <summary>
    /// RGB bytes (row by row from the north-west corner) of a <paramref name="width"/> by <paramref name="height"/>
    /// picture of the world from x/z <paramref name="min"/>, <paramref name="metresPerPixel"/> to a pixel.
    /// Outside the world is open sea.
    /// </summary>
    public static byte[] Render(WorldGenerator world, Vector2 min, float metresPerPixel, int width, int height, float contourInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        // Heights with a one-pixel border for the lighting.
        var w = width + 2;
        var heights = new float[w * (height + 2)];
        var half = world.WorldSize / 2;
        Parallel.For(0, height + 2, j =>
        {
            for (var i = 0; i < w; i++)
            {
                float x = min.X + (i - 0.5f) * metresPerPixel, z = min.Y + (j - 0.5f) * metresPerPixel;
                heights[j * w + i] = MathF.Abs(x) > half || MathF.Abs(z) > half ? world.SeaLevel - 20f : world.HeightAt(x, z);
            }
        });
        var high = Highest(world);
        var pixels = new byte[width * height * 3];
        Parallel.For(0, height, j =>
        {
            for (var i = 0; i < width; i++)
            {
                var h = heights[(j + 1) * w + i + 1];
                Vector3 colour;
                if (h < world.SeaLevel)
                {
                    colour = Vector3.Lerp(Shallows, DeepSea, Math.Clamp((world.SeaLevel - h) / 15f, 0f, 1f));
                }
                else
                {
                    float x = min.X + (i + 0.5f) * metresPerPixel, z = min.Y + (j + 0.5f) * metresPerPixel;
                    var biome = world.BiomeAt(x, z).Color;
                    var t = Math.Clamp((h - world.SeaLevel) / MathF.Max(1f, high - world.SeaLevel), 0f, 1f);
                    // Biomes differ by lightness only, so the map stays blue.
                    var lightness = 0.55f + 0.9f * Luminance(biome);
                    colour = Vector3.Lerp(Lowland, Highland, t) * lightness;
                    // Light from the north-west.
                    var dx = (heights[(j + 1) * w + i + 2] - heights[(j + 1) * w + i]) / (2 * metresPerPixel);
                    var dz = (heights[(j + 2) * w + i + 1] - heights[j * w + i + 1]) / (2 * metresPerPixel);
                    colour *= Math.Clamp(1f - 1.2f * (dx + dz), 0.5f, 1.4f);
                    if (Contour(h, heights[(j + 1) * w + i + 2], heights[(j + 2) * w + i + 1], contourInterval))
                        colour = Vector3.Lerp(colour, Highland * 1.3f, 0.35f);
                }
                var at = (j * width + i) * 3;
                pixels[at] = Byte(colour.X);
                pixels[at + 1] = Byte(colour.Y);
                pixels[at + 2] = Byte(colour.Z);
            }
        });
        return pixels;
    }

    // True where a height line crosses between this pixel and its east or south neighbour.
    private static bool Contour(float h, float east, float south, float interval) =>
        MathF.Floor(h / interval) != MathF.Floor(east / interval) || MathF.Floor(h / interval) != MathF.Floor(south / interval);

    // The highest ground the biomes reach, for the land's lightness.
    private static float Highest(WorldGenerator world) =>
        world.Data.Biomes.Count == 0 ? world.SeaLevel + 1f : world.Data.Biomes.Max(b => b.Height + b.Relief);

    private static float Luminance(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;

    private static byte Byte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private static Vector3 Rgb(string hex)
    {
        var (r, g, b) = UiPalette.Parse(hex);
        return new Vector3(r, g, b);
    }
}

/// <summary>One piece of a Handbook page: Markdown text, or a diagram on a line of its own (<c>![caption](images/x.svg)</c>).</summary>
/// <param name="Markdown">The text, or null for a diagram.</param>
/// <param name="Image">The diagram's path relative to the docs folder, or null for text.</param>
/// <param name="Caption">The diagram's caption.</param>
public sealed record HandbookBlock(string? Markdown, string? Image = null, string? Caption = null);

/// <summary>The Handbook's pages (UI spec §5, §10): turning pages, and the diagrams in them.</summary>
public static class Handbook
{
    /// <summary>Image files a page can show.</summary>
    public static IReadOnlyList<string> ImageExtensions { get; } = [".svg", ".png", ".jpg", ".jpeg"];

    /// <summary>
    /// The page <paramref name="step"/> pages on from <paramref name="current"/> in <paramref name="pages"/>, stopping at
    /// the first and last. Not listed (a search hid it) starts from the first page.
    /// </summary>
    public static string? Turn(IReadOnlyList<string> pages, string? current, int step)
    {
        if (pages.Count == 0)
            return null;
        var index = current is null ? -1 : IndexOf(pages, current);
        if (index < 0)
            return pages[0];
        return pages[Math.Clamp(index + step, 0, pages.Count - 1)];
    }

    /// <summary>"3 / 19" for <paramref name="current"/>'s place in <paramref name="pages"/>, or empty when it is not listed.</summary>
    public static string Position(IReadOnlyList<string> pages, string? current) =>
        current is not null && IndexOf(pages, current) is var index and >= 0 ? $"{index + 1} / {pages.Count}" : "";

    /// <summary>Splits a page into its text and the diagrams on lines of their own (outside code blocks).</summary>
    public static IReadOnlyList<HandbookBlock> Split(string markdown)
    {
        var blocks = new List<HandbookBlock>();
        var text = new List<string>();
        var code = false;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
                code = !code;
            var image = code ? null : ImageLine(line);
            if (image is null)
            {
                text.Add(line);
                continue;
            }
            Flush();
            blocks.Add(image);
        }
        Flush();
        return blocks;

        void Flush()
        {
            if (text.Any(l => l.Trim().Length > 0))
                blocks.Add(new HandbookBlock(string.Join("\n", text)));
            text.Clear();
        }
    }

    /// <summary>
    /// A diagram's path made safe: relative to the docs folder, no <c>..</c>, no absolute path or URL, and an image
    /// extension; otherwise null.
    /// </summary>
    public static string? SafeImagePath(string path)
    {
        var clean = path.Trim().Replace('\\', '/');
        if (clean.Length == 0 || clean.StartsWith('/') || clean.Contains(':') || clean.Split('/').Any(p => p is ".." or "."))
            return null;
        return ImageExtensions.Contains(Path.GetExtension(clean).ToLowerInvariant()) ? clean : null;
    }

    private static HandbookBlock? ImageLine(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("![", StringComparison.Ordinal) || !trimmed.EndsWith(')'))
            return null;
        var close = trimmed.IndexOf("](", StringComparison.Ordinal);
        if (close < 0)
            return null;
        var caption = trimmed[2..close];
        var path = trimmed[(close + 2)..^1];
        return SafeImagePath(path) is { } safe ? new HandbookBlock(null, safe, caption) : null;
    }

    private static int IndexOf(IReadOnlyList<string> pages, string page)
    {
        for (var i = 0; i < pages.Count; i++)
        {
            if (pages[i] == page)
                return i;
        }
        return -1;
    }
}
