namespace Mechanica.Core;

/// <summary>Engine-independent constants shared by every layer of the game.</summary>
public static class MechanicaInfo
{
    public const string GameName = "Mechanica";

    /// <summary>The game's version, shown on the home screen.</summary>
    public const string Version = "0.1.0";

    /// <summary>Building grid cell size in metres (SI units everywhere, decision D13).</summary>
    public const float GridSize = 0.25f;

    /// <summary>Snaps a coordinate in metres to the nearest grid cell index.</summary>
    public static int ToGridCell(float metres) => (int)MathF.Round(metres / GridSize);

    /// <summary>Converts a grid cell index back to metres.</summary>
    public static float FromGridCell(int cell) => cell * GridSize;
}
