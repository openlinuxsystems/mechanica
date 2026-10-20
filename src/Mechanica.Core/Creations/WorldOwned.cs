namespace Mechanica.Core.Creations;

/// <summary>
/// The rule for world-owned Creations (<see cref="Creation.WorldPart"/>, a POI's <c>part</c> marker): everyone may use one
/// (interact with it, open its station, craft, take its output), but nobody may change its structure, move it or damage
/// it. Every action that would refuses with <see cref="Refusal"/>; damage from hits, blasts, bots and fire is not dealt.
/// </summary>
public static class WorldOwned
{
    /// <summary>
    /// Why <paramref name="creation"/> cannot be <paramref name="change"/> (a past participle: "picked up", "painted"), or
    /// null when it is not world-owned (or null).
    /// </summary>
    public static string? Refusal(Creation? creation, string change) =>
        creation?.WorldPart is null ? null : $"The {NameOf(creation)} belongs to the world: it can't be {change}.";

    // The display name of its first part, which is what players see.
    private static string NameOf(Creation creation) =>
        creation.Parts.FirstOrDefault() is { } part ? creation.DefinitionOf(part.Id).Name ?? part.Part : "structure";
}
