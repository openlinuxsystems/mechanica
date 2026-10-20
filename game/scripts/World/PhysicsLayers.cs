namespace Mechanica.Game.World;

/// <summary>
/// Collision layers. Everything solid is on <see cref="Solid"/>. Step ramps (<see cref="Creations.StepRamps"/>) are on a layer of their own that
/// only climbing wheels collide with, so world queries (aiming, placing, sensors) pass <see cref="Queries"/> to see through them.
/// </summary>
public static class PhysicsLayers
{
    /// <summary>Terrain, structures, creations, players and bots.</summary>
    public const uint Solid = 1u;

    /// <summary>The invisible ramps climbing wheels lay at step edges (<see cref="Creations.StepRamps"/>).</summary>
    public const uint StepRamps = 1u << 19;

    /// <summary>The mask for a world query: everything but the step ramps.</summary>
    public const uint Queries = ~StepRamps;
}
