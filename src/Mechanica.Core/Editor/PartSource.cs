using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;

namespace Mechanica.Core.Editor;

/// <summary>What a <see cref="ModelPiece"/> is made of.</summary>
public enum ModelPieceKind
{
    /// <summary>An imported glTF file (<see cref="ModelPiece.Mesh"/>).</summary>
    Mesh,

    /// <summary>A box of <see cref="ModelPiece.Size"/> metres.</summary>
    Box,

    /// <summary>A cylinder along local Y with <see cref="ModelPiece.Radius"/> and <see cref="ModelPiece.Height"/>.</summary>
    Cylinder,

    /// <summary>A sphere of <see cref="ModelPiece.Radius"/>.</summary>
    Sphere,

    /// <summary>
    /// A right-angled wedge filling half of a <see cref="ModelPiece.Size"/> box: full height at local -Z,
    /// sloping down to nothing at +Z, like the block wedges.
    /// </summary>
    Wedge,
}

/// <summary>How a piece combines with the pieces above it in the list (constructive solid geometry).</summary>
public enum CsgOperation
{
    /// <summary>Adds the piece.</summary>
    Union,

    /// <summary>Cuts the piece out of what is above it.</summary>
    Subtract,

    /// <summary>Keeps only where the piece overlaps what is above it.</summary>
    Intersect,
}

/// <summary>
/// One piece of a part's editable model: an imported mesh or a primitive, placed relative to the part
/// centre in metres, combined with the pieces before it by <see cref="Operation"/>.
/// </summary>
public sealed record ModelPiece
{
    /// <summary>What the piece is.</summary>
    public ModelPieceKind? Kind { get; init; }

    /// <summary>How it combines with earlier pieces. Omitted means union.</summary>
    public CsgOperation? Operation { get; init; }

    /// <summary>glTF file for <see cref="ModelPieceKind.Mesh"/>, relative to the mod root or <c>other_mod:path</c>.</summary>
    public string? Mesh { get; init; }

    /// <summary>Box or wedge extents in metres.</summary>
    public Vec3? Size { get; init; }

    /// <summary>Cylinder or sphere radius in metres.</summary>
    public float? Radius { get; init; }

    /// <summary>Cylinder height in metres.</summary>
    public float? Height { get; init; }

    /// <summary>Centre relative to the part centre, in metres. Omitted means the part centre.</summary>
    public Vec3? Position { get; init; }

    /// <summary>Rotation in degrees about X, Y and Z, applied in Y, X, Z order (Godot's default). Omitted means none.</summary>
    public Vec3? Rotation { get; init; }

    /// <summary>Uniform scale. Omitted means 1.</summary>
    public float? Scale { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>True when the piece is a plain union with no transform, so a mesh piece can be used as is.</summary>
    [JsonIgnore]
    public bool IsUntransformed =>
        (Operation ?? CsgOperation.Union) == CsgOperation.Union
        && (Position ?? Vec3.Zero) == Vec3.Zero && (Rotation ?? Vec3.Zero) == Vec3.Zero && (Scale ?? 1f) == 1f;

    /// <summary>A box piece.</summary>
    public static ModelPiece NewBox(Vec3 size) => new() { Kind = ModelPieceKind.Box, Size = size };

    /// <summary>A mesh piece.</summary>
    public static ModelPiece NewMesh(string mesh) => new() { Kind = ModelPieceKind.Mesh, Mesh = mesh };
}

/// <summary>
/// The Part Editor's source for one part: what the part definition cannot hold, chiefly the model
/// pieces that are baked into the part's glTF mesh on export. Saved as JSON next to the mod's data at
/// <c>editor/&lt;same path as the part file&gt;</c> (for example <c>editor/parts/blocks/wood_block.json</c>),
/// so a part can be reopened and edited. The game never reads it; only the exported part file and mesh matter.
/// </summary>
public sealed record PartSource
{
    /// <summary>The source format this build writes.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Folder inside a mod that holds editor sources.</summary>
    public const string Folder = "editor";

    /// <summary>Format version of the file.</summary>
    public int? FormatVersion { get; init; }

    /// <summary>Model pieces in CSG order. Empty or omitted means the part uses its <c>mesh</c> (or a placeholder) as it is.</summary>
    public List<ModelPiece>? Model { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>An empty source.</summary>
    public static PartSource Empty { get; } = new() { FormatVersion = CurrentFormatVersion };

    /// <summary>The pieces, never null.</summary>
    [JsonIgnore]
    public IReadOnlyList<ModelPiece> Pieces => Model ?? [];

    /// <summary>
    /// True when exporting must bake the pieces into a new mesh: anything other than no pieces, or a
    /// single untransformed mesh piece (which the part can point at directly).
    /// </summary>
    [JsonIgnore]
    public bool NeedsBake => Pieces.Count > 1 || (Pieces.Count == 1 && !(Pieces[0].Kind == ModelPieceKind.Mesh && Pieces[0].IsUntransformed));

    /// <summary>Serializes in canonical form (same rules as part files).</summary>
    public static string Write(PartSource source) => JsonSerializer.Serialize(source with
    {
        FormatVersion = source.FormatVersion ?? CurrentFormatVersion,
        Extra = Sorted(source.Extra),
        Model = source.Model?.Select(p => p with { Extra = Sorted(p.Extra) }).ToList(),
    }, DataJson.WriteOptions) + "\n";

    /// <summary>Parses a source file, reporting problems instead of throwing.</summary>
    public static PartSource? Read(string json, string displayFile, List<DataDiagnostic> diagnostics) =>
        ModLoader.ParseJson<PartSource>(json, displayFile, diagnostics);

    /// <summary>Checks every piece and reports each problem.</summary>
    public static IReadOnlyList<DataDiagnostic> Validate(PartSource source, string modId, string displayFile)
    {
        var diagnostics = new List<DataDiagnostic>();
        void Error(string path, string message) =>
            diagnostics.Add(new DataDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.EditorSourceInvalid, displayFile, path, null, message));

        if (source.FormatVersion is { } format && (format < 1 || format > CurrentFormatVersion))
            Error("$.format_version", $"format_version {format} is not supported; this build reads 1 to {CurrentFormatVersion}.");
        for (var i = 0; i < source.Pieces.Count; i++)
        {
            var piece = source.Pieces[i];
            var p = $"$.model[{i}]";
            switch (piece.Kind)
            {
                case null:
                    Error($"{p}.kind", "kind is required: one of mesh, box, cylinder, sphere, wedge.");
                    break;
                case ModelPieceKind.Mesh:
                    if (piece.Mesh is null)
                        Error($"{p}.mesh", "a mesh piece needs a mesh file.");
                    else if (!AssetPath.TryParse(piece.Mesh, modId, AssetKind.Mesh, out _, out var error))
                        Error($"{p}.mesh", error);
                    break;
                case ModelPieceKind.Box or ModelPieceKind.Wedge:
                    if (piece.Size is not { } s || !(s.X > 0 && s.Y > 0 && s.Z > 0))
                        Error($"{p}.size", "size [x, y, z] in metres must be greater than 0 on every axis.");
                    break;
                case ModelPieceKind.Cylinder:
                    if (!(piece.Radius > 0))
                        Error($"{p}.radius", "radius (m) must be greater than 0.");
                    if (!(piece.Height > 0))
                        Error($"{p}.height", "height (m) must be greater than 0.");
                    break;
                case ModelPieceKind.Sphere:
                    if (!(piece.Radius > 0))
                        Error($"{p}.radius", "radius (m) must be greater than 0.");
                    break;
            }
            if (piece.Scale is { } scale && !(scale > 0 && float.IsFinite(scale)))
                Error($"{p}.scale", "scale must be greater than 0.");
        }
        if (source.Pieces.Count > 0 && (source.Pieces[0].Operation ?? CsgOperation.Union) != CsgOperation.Union)
            Error("$.model[0].operation", "the first piece must be a union; there is nothing above it to cut or intersect.");
        return diagnostics;
    }

    private static Dictionary<string, JsonElement>? Sorted(Dictionary<string, JsonElement>? extra) =>
        extra?.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value);
}
