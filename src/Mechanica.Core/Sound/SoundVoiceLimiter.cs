namespace Mechanica.Core.Sound;

/// <summary>What <see cref="SoundVoiceLimiter.Admit"/> decided about a new play.</summary>
public enum VoiceAdmission
{
    /// <summary>Play it.</summary>
    Play,

    /// <summary>Play it, after stopping the oldest copy of the event to stay within its limit.</summary>
    ReplaceOldest,

    /// <summary>Drop it: the event is cooling down, or a loop is at its limit.</summary>
    Drop,
}

/// <summary>
/// Keeps each sound event within its <see cref="SoundEvent.MaxInstances"/> and <see cref="SoundEvent.Cooldown"/>
/// on one listener, so a burst of hits (a Sledgehammer on a row of blocks, a hundred blocks breaking)
/// stays a sound and not a roar. One-shots over the limit replace the oldest copy; loops over it are dropped,
/// because a loop belongs to something that keeps running.
/// </summary>
public sealed class SoundVoiceLimiter
{
    private readonly Dictionary<string, double> _lastStart = new(StringComparer.Ordinal);

    /// <summary>Decides whether a new play of <paramref name="sound"/> starts, given how many copies play now.</summary>
    /// <param name="sound">The event.</param>
    /// <param name="playing">Copies of it playing on this listener right now.</param>
    /// <param name="now">The listener's clock, in seconds.</param>
    public VoiceAdmission Admit(SoundEvent sound, int playing, double now)
    {
        if (sound.Cooldown > 0 && _lastStart.TryGetValue(sound.QualifiedId, out var last) && now - last < sound.Cooldown)
            return VoiceAdmission.Drop;
        var admission = playing < sound.MaxInstances ? VoiceAdmission.Play
            : sound.Loop ? VoiceAdmission.Drop
            : VoiceAdmission.ReplaceOldest;
        if (admission != VoiceAdmission.Drop)
            _lastStart[sound.QualifiedId] = now;
        return admission;
    }

    /// <summary>Forgets every cooldown.</summary>
    public void Clear() => _lastStart.Clear();
}
