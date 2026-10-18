using System.Globalization;
using Godot;
using Mechanica.Core.World;
using Mechanica.Game.Mods;

namespace Mechanica.Game.World;

/// <summary>
/// The <c>--world-export DIR [SEED]</c> command line (World Map spec §7 output requirements): generates the world from the
/// enabled mods and writes its placement manifest (<c>manifest.json</c>), heightmap (<c>heightmap.png</c>, and the
/// heights as little-endian floats in <c>heightmap.f32</c>), biome map (<c>biomes.png</c>) and an overview
/// (<c>overview.png</c>) into DIR, then quits.
/// </summary>
public static class WorldExportRunner
{
    /// <summary>The command-line flag.</summary>
    public const string Flag = "--world-export";

    /// <summary>Metres per pixel of the exported pictures.</summary>
    public const float MetresPerPixel = 8f;

    /// <summary>Exports the world with <paramref name="seed"/> into <paramref name="directory"/>; false (with the reason printed) on failure.</summary>
    public static bool Run(string directory, long seed)
    {
        var mods = ModPaths.LoadContent();
        var data = WorldGenData.Load(mods);
        foreach (var diagnostic in data.Diagnostics)
            GD.PrintErr($"[world-export] {diagnostic}");
        if (data.HasErrors)
            return false;
        var settings = WorldStreamer.LoadDefaultSettings();
        var gen = new WorldGenerator(data, seed, settings.WorldSize, settings.Depth);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), WorldExport.ToJson(WorldExport.Manifest(gen)));
        var heights = WorldExport.Heights(gen, MetresPerPixel, out var w, out var h);
        using (var file = File.Create(Path.Combine(directory, "heightmap.f32")))
        {
            var bytes = new byte[heights.Length * sizeof(float)];
            Buffer.BlockCopy(heights, 0, bytes, 0, bytes.Length);
            file.Write(bytes);
        }
        var grey = WorldExport.HeightImage(heights, out var low, out var high);
        var saved = Save(Image.CreateFromData(w, h, false, Image.Format.L8, grey), directory, "heightmap.png")
            & Save(Image.CreateFromData(w, h, false, Image.Format.Rgb8, WorldExport.BiomeImage(gen, MetresPerPixel, out _, out _)), directory, "biomes.png")
            & Save(Image.CreateFromData(w, h, false, Image.Format.Rgb8, WorldExport.OverviewImage(gen, heights, MetresPerPixel, w, h)), directory, "overview.png");
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"[world-export] seed {seed}: {gen.Pois.Count} POIs, {gen.Roads.LengthOf(RoadKind.Road):0} m of road, {gen.Roads.LengthOf(RoadKind.Track):0} m of track; " +
            $"pictures {w}×{h} at {MetresPerPixel} m a pixel, heights {low:0.0} to {high:0.0} m; written to {directory}"));
        return saved;
    }

    private static bool Save(Image image, string directory, string name)
    {
        var error = image.SavePng(Path.Combine(directory, name));
        if (error != Error.Ok)
            GD.PrintErr($"[world-export] could not write {name}: {error}");
        return error == Error.Ok;
    }
}
