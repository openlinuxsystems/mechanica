using System.Numerics;
using Mechanica.Core.Sound;

namespace Mechanica.Scripting;

/// <summary>
/// The sound primitives scripts call (the game's <c>SoundSync</c>, D2): one-shots heard by everyone in
/// earshot, and loops that follow a part and can be bent by their parameters. Sound ids resolve like
/// mod data (unqualified ids are the part's own mod).
/// </summary>
public interface IScriptSound
{
    /// <summary>Plays a one-shot at a world point. Returns why it can't, or null.</summary>
    string? Play(string sound, string ownerModId, Vector3 position);

    /// <summary>
    /// Plays a one-shot at a world point with its pitch scaled by <paramref name="pitch"/> (already clamped to
    /// <see cref="SoundEvent.MinPitchScale"/>..<see cref="SoundEvent.MaxPitchScale"/>), as a Horn or a Scuttler Head
    /// note does. Returns why it can't, or null. Without an override the pitch is ignored and it plays as recorded.
    /// </summary>
    string? Play(string sound, string ownerModId, Vector3 position, float pitch) => Play(sound, ownerModId, position);

    /// <summary>
    /// Starts a loop that follows a part of a Creation. Returns its handle, or 0 with
    /// <paramref name="error"/> saying why not.
    /// </summary>
    int StartLoop(string sound, string ownerModId, int creation, int part, IReadOnlyDictionary<string, float>? parameters, out string? error);

    /// <summary>Sets a running loop's parameter. Returns why it can't, or null.</summary>
    string? SetLoopParameter(int handle, string name, float value);

    /// <summary>Stops a loop.</summary>
    void StopLoop(int handle);
}

/// <summary>No sound: one-shots and loops are refused.</summary>
public sealed class NoSound : IScriptSound
{
    /// <inheritdoc/>
    public string? Play(string sound, string ownerModId, Vector3 position) => "sound is not available here";

    /// <inheritdoc/>
    public int StartLoop(string sound, string ownerModId, int creation, int part, IReadOnlyDictionary<string, float>? parameters, out string? error)
    {
        error = "sound is not available here";
        return 0;
    }

    /// <inheritdoc/>
    public string? SetLoopParameter(int handle, string name, float value) => "sound is not available here";

    /// <inheritdoc/>
    public void StopLoop(int handle)
    {
    }
}
