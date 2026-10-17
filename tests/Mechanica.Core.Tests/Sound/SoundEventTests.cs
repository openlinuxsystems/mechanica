using Mechanica.Core.Sound;

namespace Mechanica.Core.Tests.Sound;

public class SoundEventTests
{
    private static SoundEvent Event(SoundEventDefinition definition, int clips = 1) =>
        new($"vanilla:{definition.Id ?? "x"}", "vanilla", "bank.json", definition, [.. Enumerable.Range(0, clips).Select(i => $"clip{i}.ogg")]);

    [Fact]
    public void RollsStayWithinTheVariationAndUseEveryClip()
    {
        var sound = Event(new SoundEventDefinition { Id = "hit", VolumeDb = -6, Pitch = 1.2f, PitchVariation = 0.1f, VolumeVariationDb = 2 }, clips: 3);
        var random = new Random(7);

        var voices = Enumerable.Range(0, 500).Select(_ => sound.Roll(random)).ToList();

        Assert.All(voices, v => Assert.InRange(v.Pitch, 1.2f * 0.9f - 1e-5f, 1.2f * 1.1f + 1e-5f));
        Assert.All(voices, v => Assert.InRange(v.VolumeDb, -8f - 1e-5f, -4f + 1e-5f));
        Assert.Equal([0, 1, 2], voices.Select(v => v.Clip).Distinct().Order());
        // The variation is really used, not just allowed.
        Assert.True(voices.Max(v => v.Pitch) - voices.Min(v => v.Pitch) > 0.2f);
        Assert.True(voices.Max(v => v.VolumeDb) - voices.Min(v => v.VolumeDb) > 3f);
    }

    [Fact]
    public void RollsWithoutVariationAreExact()
    {
        var sound = Event(new SoundEventDefinition { Id = "click", VolumeDb = -3, Pitch = 0.5f });

        Assert.Equal(new SoundVoice(0, 0.5f, -3f), sound.Roll(new Random(1)));
    }

    [Fact]
    public void RollsRepeatForTheSameSeed()
    {
        var sound = Event(new SoundEventDefinition { Id = "hit", PitchVariation = 0.2f, VolumeVariationDb = 3 }, clips: 4);

        Assert.Equal(sound.Roll(new Random(42)), sound.Roll(new Random(42)));
    }

    [Fact]
    public void ParametersBendPitchAndVolumeAlongTheirRange()
    {
        var engine = Event(new SoundEventDefinition
        {
            Id = "engine_rev",
            Loop = true,
            Parameters =
            [
                new SoundParameter { Name = "rpm", Min = 0, Max = 6000, Default = 1000, Pitch = [0.5f, 2f], VolumeDb = [-12f, 0f] },
                new SoundParameter { Name = "load", Pitch = [1f, 1.2f] },
            ],
        });

        Assert.Equal((0.5f, -12f), engine.Shape(new Dictionary<string, float> { ["rpm"] = 0, ["load"] = 0 }));
        Assert.Equal((2f * 1.2f, 0f), engine.Shape(new Dictionary<string, float> { ["rpm"] = 6000, ["load"] = 1 }));
        var (pitch, volume) = engine.Shape(new Dictionary<string, float> { ["rpm"] = 3000, ["load"] = 0.5f });
        Assert.Equal(1.25f * 1.1f, pitch, 1e-5f);
        Assert.Equal(-6f, volume, 1e-5f);
        // Out of range values clamp; missing and non-finite ones use the default.
        Assert.Equal((2f, 0f), engine.Shape(new Dictionary<string, float> { ["rpm"] = 99999 }));
        var defaults = engine.Shape(new Dictionary<string, float> { ["rpm"] = float.NaN });
        Assert.Equal(0.75f, defaults.PitchScale, 1e-5f);
        Assert.Equal(-10f, defaults.VolumeDb, 1e-5f);
        Assert.Equal(new Dictionary<string, float> { ["rpm"] = 1000, ["load"] = 0 }, engine.DefaultParameters());
        Assert.NotNull(engine.FindParameter("load"));
        Assert.Null(engine.FindParameter("speed"));
    }

    [Fact]
    public void ALoopWithoutParametersIsUnchanged() =>
        Assert.Equal((1f, 0f), Event(new SoundEventDefinition { Id = "hum", Loop = true }).Shape(new Dictionary<string, float> { ["x"] = 5 }));

    [Theory]
    [InlineData(SoundRolloff.Inverse, 0.5f, 1f)]
    [InlineData(SoundRolloff.Inverse, 2f, 1f)]
    [InlineData(SoundRolloff.Inverse, 4f, 0.5f)]
    [InlineData(SoundRolloff.Inverse, 8f, 0.25f)]
    [InlineData(SoundRolloff.InverseSquare, 4f, 0.25f)]
    [InlineData(SoundRolloff.Linear, 11f, 0.5f)]
    [InlineData(SoundRolloff.Linear, 20f, 0f)]
    [InlineData(SoundRolloff.Inverse, 20f, 0.1f)]
    [InlineData(SoundRolloff.Inverse, 20.01f, 0f)]
    [InlineData(SoundRolloff.Inverse, float.NaN, 0f)]
    public void GainFallsWithDistance(SoundRolloff rolloff, float distance, float gain)
    {
        var sound = Event(new SoundEventDefinition { Id = "x", MinDistance = 2, MaxDistance = 20, Rolloff = rolloff });

        Assert.Equal(gain, sound.Gain(distance), 1e-5f);
        Assert.Equal(distance <= 20, sound.IsAudibleAt(distance));
    }

    [Fact]
    public void UiAndMusicAreHeardEverywhere()
    {
        foreach (var bus in new[] { SoundBus.Ui, SoundBus.Music })
        {
            var sound = Event(new SoundEventDefinition { Id = "x", Bus = bus });
            Assert.False(sound.Spatial);
            Assert.Equal(1f, sound.Gain(1000));
            Assert.True(sound.IsAudibleAt(1000));
        }
        Assert.True(Event(new SoundEventDefinition { Id = "x", Bus = SoundBus.Music, Spatial = true }).Spatial, "a radio is positional music");
    }

    [Fact]
    public void TheLimiterKeepsEventsWithinTheirInstancesAndCooldown()
    {
        var limiter = new SoundVoiceLimiter();
        var hit = Event(new SoundEventDefinition { Id = "hit", MaxInstances = 2, Cooldown = 0.1f });
        var loop = Event(new SoundEventDefinition { Id = "hum", Loop = true, MaxInstances = 1 });

        Assert.Equal(VoiceAdmission.Play, limiter.Admit(hit, 0, 1.0));
        Assert.Equal(VoiceAdmission.Drop, limiter.Admit(hit, 1, 1.05));
        Assert.Equal(VoiceAdmission.Play, limiter.Admit(hit, 1, 1.15));
        Assert.Equal(VoiceAdmission.ReplaceOldest, limiter.Admit(hit, 2, 1.3));
        Assert.Equal(VoiceAdmission.Play, limiter.Admit(loop, 0, 1.3));
        Assert.Equal(VoiceAdmission.Drop, limiter.Admit(loop, 1, 2.0));
        limiter.Clear();
        Assert.Equal(VoiceAdmission.Play, limiter.Admit(hit, 0, 1.31));
    }
}
