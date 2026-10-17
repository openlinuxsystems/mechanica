using Mechanica.Core.Data;

namespace Mechanica.Core.Sound;

/// <summary>Checks a parsed <see cref="SoundBankFile"/> for one mod and reports every problem it finds.</summary>
internal sealed class SoundValidator(PartValidator assets, string modId, IReadOnlySet<string> declaredDependencies)
{
    private const string Code = DiagnosticCodes.SoundInvalid;

    /// <summary>Most clips one event may pick from.</summary>
    public const int MaxClips = 32;

    /// <summary>
    /// The mod whose event <paramref name="id"/> replaces (<c>other_mod:id</c>), or null for one of
    /// <paramref name="modId"/>'s own events.
    /// </summary>
    public static string? ReplacedMod(string? id, string modId)
    {
        var colon = id?.IndexOf(':') ?? -1;
        return colon < 0 || id![..colon] == modId ? null : id[..colon];
    }

    /// <summary>
    /// Validates every event. <paramref name="seen"/> holds qualified ids already defined by this mod and
    /// gets this file's added, so a repeat across files is caught too.
    /// </summary>
    public void Validate(SoundBankFile bank, DiagnosticSink sink, HashSet<string> seen)
    {
        if (bank.Events is null)
            sink.Error(Code, "$.events", "events (a list of sound events) is required.");
        for (var i = 0; i < (bank.Events?.Count ?? 0); i++)
            ValidateEvent(bank.Events![i], $"$.events[{i}]", sink, seen);
        KnownKeys.WarnUnknown(bank.Extra, "$", sink, typeof(SoundBankFile));
    }

    /// <summary>Absolute path of a clip that validated.</summary>
    public string Resolve(string clip, Func<string, string?> modDirectory)
    {
        AssetPath.TryParse(clip, modId, AssetKind.Sound, out var path, out _);
        return path!.Resolve(modDirectory(path.ModId)!);
    }

    private void ValidateEvent(SoundEventDefinition e, string path, DiagnosticSink sink, HashSet<string> seen)
    {
        if (e is null)
        {
            sink.Error(Code, path, "a sound event must be an object.");
            return;
        }
        CheckId(e.Id, $"{path}.id", sink, seen);

        if (e.Clips is not { Count: > 0 } clips)
        {
            sink.Error(Code, $"{path}.clips", "clips (at least one .ogg or .wav file) is required.");
        }
        else
        {
            if (clips.Count > MaxClips)
                sink.Error(Code, $"{path}.clips", $"an event may pick from at most {MaxClips} clips (got {clips.Count}).");
            for (var c = 0; c < clips.Count; c++)
                assets.CheckAsset(clips[c], $"{path}.clips[{c}]", AssetKind.Sound, sink);
        }

        if (e.VolumeDb is { } volume && !(volume >= -80 && volume <= 24))
            sink.Error(Code, $"{path}.volume_db", $"volume_db must be between -80 and 24 (got {volume}).");
        if (e.Pitch is { } pitch && !(pitch >= 0.01f && pitch <= 16))
            sink.Error(Code, $"{path}.pitch", $"pitch must be between 0.01 and 16 (got {pitch}).");
        if (e.VolumeVariationDb is { } vv && !(vv >= 0 && vv <= 24))
            sink.Error(Code, $"{path}.volume_variation_db", $"volume_variation_db must be between 0 and 24 (got {vv}).");
        if (e.PitchVariation is { } pv && !(pv >= 0 && pv < 1))
            sink.Error(Code, $"{path}.pitch_variation", $"pitch_variation must be 0 or more and less than 1 (got {pv}).");
        var min = e.MinDistance ?? SoundEvent.DefaultMinDistance;
        var max = e.MaxDistance ?? SoundEvent.DefaultMaxDistance;
        if (e.MinDistance is { } md && !(md > 0 && float.IsFinite(md)))
            sink.Error(Code, $"{path}.min_distance", "min_distance (m) must be greater than 0.");
        if (e.MaxDistance is { } xd && !(xd > 0 && float.IsFinite(xd)))
            sink.Error(Code, $"{path}.max_distance", "max_distance (m) must be greater than 0.");
        else if (min > 0 && max > 0 && max <= min)
            sink.Error(Code, $"{path}.max_distance", $"max_distance ({max} m) must be greater than min_distance ({min} m).");
        if (e.MaxInstances is < 1 or > 64)
            sink.Error(Code, $"{path}.max_instances", $"max_instances must be 1 to 64 (got {e.MaxInstances}).");
        if (e.Cooldown is { } cooldown && !(cooldown >= 0 && cooldown <= 60))
            sink.Error(Code, $"{path}.cooldown", $"cooldown must be 0 to 60 seconds (got {cooldown}).");
        if (e.Spatial == false && (e.MinDistance is not null || e.MaxDistance is not null || e.Rolloff is not null))
            sink.Warning(Code, path, "min_distance, max_distance and rolloff do nothing on a sound that is not spatial.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var p = 0; p < (e.Parameters?.Count ?? 0); p++)
            ValidateParameter(e.Parameters![p], $"{path}.parameters[{p}]", sink, names);
        if (e.Parameters is { Count: > 0 } && e.Loop != true)
            sink.Warning(Code, $"{path}.parameters", "parameters only change a sound while it plays; they are meant for loops.");

        KnownKeys.WarnUnknown(e.Extra, path, sink, typeof(SoundEventDefinition));
    }

    private void CheckId(string? id, string path, DiagnosticSink sink, HashSet<string> seen)
    {
        if (id is null)
        {
            sink.Error(Code, path, "id is required.");
            return;
        }
        var colon = id.IndexOf(':');
        var local = colon < 0 ? id : id[(colon + 1)..];
        if (colon >= 0 && !Identifiers.IsValid(id[..colon]) || !Identifiers.IsValid(local))
        {
            sink.Error(Code, path, $"id '{id}' must be lowercase snake_case, or 'mod_id:sound_id' to replace another mod's sound.");
            return;
        }
        if (ReplacedMod(id, modId) is { } other && !declaredDependencies.Contains(other))
        {
            sink.Error(Code, path, $"'{id}' replaces a sound of mod '{other}', which must be in this mod's dependencies so it loads first.");
            return;
        }
        if (!seen.Add(Identifiers.Qualify(id, modId)))
            sink.Error(DiagnosticCodes.SoundDuplicate, path, $"sound '{id}' is defined twice in this mod.");
    }

    private static void ValidateParameter(SoundParameter p, string path, DiagnosticSink sink, HashSet<string> names)
    {
        if (!Identifiers.IsValid(p.Name))
            sink.Error(Code, $"{path}.name", p.Name is null ? "parameter name is required." : $"parameter name '{p.Name}' must be lowercase snake_case.");
        else if (!names.Add(p.Name))
            sink.Error(Code, $"{path}.name", $"parameter '{p.Name}' is listed twice.");
        var min = p.Min ?? 0f;
        var max = p.Max ?? 1f;
        if (!float.IsFinite(min) || !float.IsFinite(max) || max <= min)
            sink.Error(Code, path, $"max ({max}) must be greater than min ({min}).");
        else if (p.Default is { } d && !(d >= min && d <= max))
            sink.Error(Code, $"{path}.default", $"default ({d}) must be between min ({min}) and max ({max}).");
        if (p.Pitch is { } pitch && (pitch.Count != 2 || pitch.Any(v => !(v >= 0.01f && v <= 16))))
            sink.Error(Code, $"{path}.pitch", "pitch must be [low, high], each between 0.01 and 16.");
        if (p.VolumeDb is { } volume && (volume.Count != 2 || volume.Any(v => !(v >= -80 && v <= 24))))
            sink.Error(Code, $"{path}.volume_db", "volume_db must be [low, high], each between -80 and 24.");
        if (p.Pitch is null && p.VolumeDb is null)
            sink.Warning(Code, path, "the parameter changes neither pitch nor volume_db.");
        KnownKeys.WarnUnknown(p.Extra, path, sink, typeof(SoundParameter));
    }
}
