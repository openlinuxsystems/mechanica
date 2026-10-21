namespace Mechanica.Core.Sound;

/// <summary>What one play of a sound event uses: which clip, at what pitch scale and volume.</summary>
/// <param name="Clip">Index into <see cref="SoundEvent.Clips"/>.</param>
/// <param name="Pitch">Pitch scale, base pitch with its random variation applied.</param>
/// <param name="VolumeDb">Volume in dB, base volume with its random variation applied.</param>
public readonly record struct SoundVoice(int Clip, float Pitch, float VolumeDb);

/// <summary>
/// A sound event ready to play: a <see cref="SoundEventDefinition"/> that loaded cleanly, with every
/// default filled in and its clips resolved to files. Holds the playback maths (random variation, loop
/// parameters, distance fade), so every peer and the editor's preview play an event the same way.
/// </summary>
public sealed class SoundEvent
{
    /// <summary>Distance within which a positional sound is at full volume, when the data does not say.</summary>
    public const float DefaultMinDistance = 1f;

    /// <summary>Distance beyond which a positional sound is silent, when the data does not say.</summary>
    public const float DefaultMaxDistance = 40f;

    /// <summary>Copies of one event that may play at once, when the data does not say.</summary>
    public const int DefaultMaxInstances = 8;

    /// <summary>Lowest pitch scale a script may ask a one-shot for (two octaves down).</summary>
    public const float MinPitchScale = 0.25f;

    /// <summary>Highest pitch scale a script may ask a one-shot for (two octaves up).</summary>
    public const float MaxPitchScale = 4f;

    /// <summary>
    /// Wraps a definition that already validated. The registry builds every loaded event this way; the
    /// Sound Event Binder uses it to preview an event before it is saved.
    /// </summary>
    /// <param name="qualifiedId">Game-wide id, <c>mod_id:sound_id</c>.</param>
    /// <param name="modId">Mod whose file defines it.</param>
    /// <param name="filePath">File it comes from (or will be saved to).</param>
    /// <param name="definition">The data.</param>
    /// <param name="clips">Absolute paths of its clips, in data order.</param>
    public SoundEvent(string qualifiedId, string modId, string filePath, SoundEventDefinition definition, IReadOnlyList<string> clips)
    {
        QualifiedId = qualifiedId;
        ModId = modId;
        FilePath = filePath;
        Definition = definition;
        Clips = clips;
        Bus = definition.Bus ?? SoundBus.Sfx;
        Loop = definition.Loop ?? false;
        VolumeDb = definition.VolumeDb ?? 0f;
        Pitch = definition.Pitch ?? 1f;
        VolumeVariationDb = definition.VolumeVariationDb ?? 0f;
        PitchVariation = definition.PitchVariation ?? 0f;
        Spatial = definition.Spatial ?? Bus is not (SoundBus.Ui or SoundBus.Music);
        MinDistance = definition.MinDistance ?? DefaultMinDistance;
        MaxDistance = definition.MaxDistance ?? DefaultMaxDistance;
        Rolloff = definition.Rolloff ?? SoundRolloff.Inverse;
        Parameters = definition.Parameters ?? [];
        MaxInstances = definition.MaxInstances ?? DefaultMaxInstances;
        Cooldown = definition.Cooldown ?? 0f;
    }

    /// <summary>Game-wide id, <c>mod_id:sound_id</c>.</summary>
    public string QualifiedId { get; }

    /// <summary>Mod whose file defines it (differs from the id's prefix when another mod replaced the event).</summary>
    public string ModId { get; }

    /// <summary>Absolute path of the <c>sounds/*.json</c> file it came from.</summary>
    public string FilePath { get; }

    /// <summary>The data as written.</summary>
    public SoundEventDefinition Definition { get; }

    /// <summary>Absolute paths of its audio files, in data order.</summary>
    public IReadOnlyList<string> Clips { get; }

    /// <summary>Mixer bus.</summary>
    public SoundBus Bus { get; }

    /// <summary>True for a loop.</summary>
    public bool Loop { get; }

    /// <summary>Base volume, dB.</summary>
    public float VolumeDb { get; }

    /// <summary>Base pitch scale.</summary>
    public float Pitch { get; }

    /// <summary>Random volume shift either way, dB.</summary>
    public float VolumeVariationDb { get; }

    /// <summary>Random pitch scale change either way, as a fraction.</summary>
    public float PitchVariation { get; }

    /// <summary>True when heard from a place in the world.</summary>
    public bool Spatial { get; }

    /// <summary>Full-volume distance, metres.</summary>
    public float MinDistance { get; }

    /// <summary>Distance beyond which it is silent, metres.</summary>
    public float MaxDistance { get; }

    /// <summary>Fade curve between the two distances.</summary>
    public SoundRolloff Rolloff { get; }

    /// <summary>Loop controls.</summary>
    public IReadOnlyList<SoundParameter> Parameters { get; }

    /// <summary>Most copies at once.</summary>
    public int MaxInstances { get; }

    /// <summary>Seconds before it may start again.</summary>
    public float Cooldown { get; }

    /// <summary>Picks a clip and this play's pitch and volume.</summary>
    public SoundVoice Roll(Random random)
    {
        var clip = Clips.Count > 1 ? random.Next(Clips.Count) : 0;
        var pitch = Pitch * (1f + PitchVariation * Spread(random));
        var volume = VolumeDb + VolumeVariationDb * Spread(random);
        return new SoundVoice(clip, pitch, volume);
    }

    /// <summary>
    /// A pitch scale a script asked a one-shot for, kept within <see cref="MinPitchScale"/> and
    /// <see cref="MaxPitchScale"/>; anything that is not a finite number plays as recorded (1).
    /// </summary>
    public static float ClampPitch(float scale) => float.IsFinite(scale) ? Math.Clamp(scale, MinPitchScale, MaxPitchScale) : 1f;

    /// <summary>The parameter called <paramref name="name"/>, or null.</summary>
    public SoundParameter? FindParameter(string name) => Parameters.FirstOrDefault(p => p.Name == name);

    /// <summary>Starting values of every parameter, by name.</summary>
    public Dictionary<string, float> DefaultParameters() =>
        Parameters.ToDictionary(p => p.Name!, p => p.Default ?? p.Min ?? 0f, StringComparer.Ordinal);

    /// <summary>
    /// How the loop's parameters change a voice: the pitch scale to multiply by and the dB to add. Values are
    /// clamped to each parameter's range; parameters missing from <paramref name="values"/> use their default.
    /// </summary>
    public (float PitchScale, float VolumeDb) Shape(IReadOnlyDictionary<string, float> values)
    {
        var pitch = 1f;
        var volume = 0f;
        foreach (var p in Parameters)
        {
            var min = p.Min ?? 0f;
            var max = p.Max ?? 1f;
            var value = values.TryGetValue(p.Name!, out var v) && float.IsFinite(v) ? v : p.Default ?? min;
            var t = max > min ? Math.Clamp((value - min) / (max - min), 0f, 1f) : 0f;
            if (p.Pitch is [var low, var high])
                pitch *= low + (high - low) * t;
            if (p.VolumeDb is [var quiet, var loud])
                volume += quiet + (loud - quiet) * t;
        }
        return (pitch, volume);
    }

    /// <summary>
    /// Distance gain, 0 to 1, of a positional sound heard <paramref name="distance"/> metres away: 1 up to
    /// <see cref="MinDistance"/>, falling by <see cref="Rolloff"/> after it, and 0 beyond <see cref="MaxDistance"/>.
    /// A sound that is not <see cref="Spatial"/> always gives 1.
    /// </summary>
    public float Gain(float distance)
    {
        if (!Spatial)
            return 1f;
        if (!(distance <= MaxDistance))
            return 0f;
        if (distance <= MinDistance)
            return 1f;
        var ratio = MinDistance / distance;
        return Rolloff switch
        {
            SoundRolloff.InverseSquare => ratio * ratio,
            SoundRolloff.Linear => (MaxDistance - distance) / (MaxDistance - MinDistance),
            _ => ratio,
        };
    }

    /// <summary>True when a listener <paramref name="distance"/> metres away can hear it at all.</summary>
    public bool IsAudibleAt(float distance) => !Spatial || distance <= MaxDistance;

    /// <inheritdoc />
    public override string ToString() => QualifiedId;

    // Uniform in [-1, 1].
    private static float Spread(Random random) => (float)(random.NextDouble() * 2 - 1);
}
