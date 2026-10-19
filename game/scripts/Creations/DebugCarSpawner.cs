using Godot;
using Mechanica.Core.Creations;

namespace Mechanica.Game.Creations;

/// <summary>
/// Puts the debug scrap car (<see cref="DebugCar"/>) down by the player spawn of a new world, wired, switched on and
/// fuelled, so the game can be driven round while it is in development. Only debug builds spawn it (the editor and
/// <c>godot --path</c> runs); release exports, which <c>tools/package.sh</c> makes with <c>--export-release</c>, never do.
/// Its settings are <c>game/data/debug_car.json</c>.
/// </summary>
public static class DebugCarSpawner
{
    /// <summary>The settings file.</summary>
    public const string DataPath = "res://data/debug_car.json";

    /// <summary>The settings, or the defaults without the file.</summary>
    public static DebugCarSettings LoadSettings() =>
        Godot.FileAccess.FileExists(DataPath) ? DebugCarSettings.Parse(Godot.FileAccess.GetFileAsString(DataPath)) : new DebugCarSettings();

    /// <summary>
    /// Builds the car and adds it to the host's world with its middle at <paramref name="spawn"/> plus the settings'
    /// offset, standing on the ground there. Returns its id and parts.
    /// </summary>
    public static (int Id, DebugCarParts Parts) Spawn(Main main, Vector3 spawn, DebugCarSettings settings)
    {
        var car = new Creation(main.Build.NextCreationId(), main.Build.Catalog);
        var parts = DebugCar.Build(car, settings);
        var middle = spawn + new Vector3(settings.Offset[0], settings.Offset[1], settings.Offset[2]);
        middle.Y += main.Streamer.Generator.HeightAt(middle.X, middle.Z) - spawn.Y + 0.05f;
        var rotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, Mathf.DegToRad(settings.Yaw));
        var ground = DebugCar.GroundPoint;
        var origin = middle.ToNumerics() - System.Numerics.Vector3.Transform(new System.Numerics.Vector3(ground.X, ground.Y, ground.Z), rotation);
        car.SetAllBodyPoses(new Pose(origin, rotation));
        main.Build.Added(car);
        GD.Print($"[debug] the debug scrap car {car.Id} is at {middle} with {settings.Gasoline} gasoline");
        return (car.Id, parts);
    }
}
