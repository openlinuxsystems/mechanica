using Godot;
using Mechanica.Core.Sound;

namespace Mechanica.Game.Sound;

/// <summary>
/// Plays sound events on this peer (chunk 16). Each play is a <see cref="Voice"/>: one Godot audio player
/// with the event's clip, its random pitch and volume (<see cref="SoundEvent.Roll"/>), and for a loop its
/// live parameters (<see cref="SoundEvent.Shape"/>). Distance fade is Core's (<see cref="SoundEvent.Gain"/>,
/// measured from <see cref="Listener"/>) rather than Godot's, so it matches the data exactly and the host's
/// earshot checks; Godot still pans positional sounds. <see cref="SoundVoiceLimiter"/> keeps each event
/// within its instance limit and cooldown. Clips load at runtime from the mod folders (D6).
/// </summary>
public partial class SoundPlayer : Node3D
{
    /// <summary>Bus names, by <see cref="SoundBus"/>; created under Master when the project lacks them.</summary>
    public static readonly IReadOnlyDictionary<SoundBus, string> BusNames = new Dictionary<SoundBus, string>
    {
        [SoundBus.Sfx] = "Sfx",
        [SoundBus.Ui] = "Ui",
        [SoundBus.Ambient] = "Ambient",
        [SoundBus.Music] = "Music",
    };

    private const float SilentDb = -80f;

    private readonly List<Voice> _voices = [];
    private readonly Dictionary<(string Path, bool Loop), AudioStream?> _streams = [];
    private readonly SoundVoiceLimiter _limiter = new();
    private readonly Random _random = new();

    /// <summary>The events to play. Swap it when the mods change.</summary>
    public SoundRegistry Registry { get; set; } = SoundRegistry.Empty;

    /// <summary>Where the listener's ears are; null uses the viewport's current camera, else this node's origin.</summary>
    public Func<Vector3?>? Listener { get; set; }

    /// <summary>Voices playing now, oldest first.</summary>
    public IReadOnlyList<Voice> Voices => _voices;

    /// <summary>Every voice started since this node entered the tree.</summary>
    public int Started { get; private set; }

    /// <summary>Raised when a voice starts, for tests and the editor's preview.</summary>
    public event Action<Voice>? VoiceStarted;

    /// <summary>Clock in seconds that voices and cooldowns use.</summary>
    public static double Now => Time.GetTicksUsec() / 1e6;

    public override void _Ready() => EnsureBuses();

    public override void _Process(double delta)
    {
        var ears = ListenerPosition();
        for (var i = _voices.Count - 1; i >= 0; i--)
        {
            var voice = _voices[i];
            if (!voice.Update(ears, Now))
                Remove(i);
        }
    }

    /// <summary>
    /// Plays a sound event once, as data names it (<see cref="SoundRegistry.Find"/>), at <paramref name="position"/>
    /// for a positional one. Null when the id is unknown or the limiter drops the play.
    /// </summary>
    public Voice? Play(string sound, string ownerModId, Vector3 position) =>
        Registry.Find(sound, ownerModId) is { } e ? Play(e, position, once: true) : null;

    /// <summary>
    /// Plays an event. <paramref name="track"/>, when given, moves a positional voice each frame; null from it keeps the last place.
    /// A loop event plays on until stopped unless <paramref name="once"/>: one-shot callers (a bot's cry, a world sound) pass
    /// it so a loop they fire plays its clip once and ends, rather than humming on where it was played.
    /// </summary>
    public Voice? Play(SoundEvent sound, Vector3 position, Func<Vector3?>? track = null, IReadOnlyDictionary<string, float>? parameters = null,
        bool once = false)
    {
        var playing = _voices.Where(v => v.Event == sound).ToList();
        switch (_limiter.Admit(sound, playing.Count, Now))
        {
            case VoiceAdmission.Drop:
                return null;
            case VoiceAdmission.ReplaceOldest:
                Remove(_voices.IndexOf(playing[0]));
                break;
        }
        var roll = sound.Roll(_random);
        var loops = sound.Loop && !once;
        var stream = Stream(sound.Clips[roll.Clip], loops);
        if (stream is null)
            return null;
        Node player;
        if (sound.Spatial)
        {
            player = new AudioStreamPlayer3D
            {
                Stream = stream,
                Bus = BusNames[sound.Bus],
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
                MaxDistance = 0,
                DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
            };
        }
        else
        {
            player = new AudioStreamPlayer { Stream = stream, Bus = BusNames[sound.Bus] };
        }
        var voice = new Voice(sound, player, roll, stream.GetLength(), Now, track, parameters, loops);
        AddChild(player);
        if (player is Node3D placed)
            placed.GlobalPosition = position;
        voice.Update(ListenerPosition(), Now);
        player.Call(AudioStreamPlayer.MethodName.Play);
        _voices.Add(voice);
        Started++;
        VoiceStarted?.Invoke(voice);
        return voice;
    }

    /// <summary>Stops a voice now.</summary>
    public void Stop(Voice voice)
    {
        var index = _voices.IndexOf(voice);
        if (index >= 0)
            Remove(index);
    }

    /// <summary>Stops every voice.</summary>
    public void StopAll()
    {
        for (var i = _voices.Count - 1; i >= 0; i--)
            Remove(i);
        _limiter.Clear();
    }

    /// <summary>Forgets loaded clips, after an editor saved new audio over them.</summary>
    public void ClearCache() => _streams.Clear();

    /// <summary>Loads one clip (<c>.ogg</c> or <c>.wav</c>) from disk, looping or not; null when it cannot be read.</summary>
    public AudioStream? Stream(string path, bool loop)
    {
        if (_streams.TryGetValue((path, loop), out var cached))
            return cached;
        AudioStream? stream = null;
        if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            var ogg = AudioStreamOggVorbis.LoadFromFile(path);
            if (ogg is not null)
                ogg.Loop = loop;
            stream = ogg;
        }
        else if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            var wav = AudioStreamWav.LoadFromFile(path);
            if (wav is not null && loop)
            {
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                wav.LoopBegin = 0;
                wav.LoopEnd = (int)Math.Round(wav.GetLength() * wav.MixRate);
            }
            stream = wav;
        }
        if (stream is null || stream.GetLength() <= 0)
        {
            GD.PushWarning($"[sound] could not load '{path}'.");
            stream = null;
        }
        _streams[(path, loop)] = stream;
        return stream;
    }

    /// <summary>Adds the four sound buses under Master unless the project already has them.</summary>
    public static void EnsureBuses()
    {
        foreach (var name in BusNames.Values)
        {
            if (AudioServer.GetBusIndex(name) >= 0)
                continue;
            AudioServer.AddBus();
            var index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }
    }

    private Vector3 ListenerPosition() =>
        Listener?.Invoke() ?? GetViewport()?.GetCamera3D()?.GlobalPosition ?? GlobalPosition;

    private void Remove(int index)
    {
        var voice = _voices[index];
        _voices.RemoveAt(index);
        voice.Release();
    }

    /// <summary>One sound event playing: its Godot player and live state.</summary>
    public sealed class Voice
    {
        private readonly Func<Vector3?>? _track;
        private readonly Dictionary<string, float> _parameters;
        private readonly double _length;
        private readonly double _started;

        internal Voice(SoundEvent sound, Node player, SoundVoice roll, double length, double now, Func<Vector3?>? track,
            IReadOnlyDictionary<string, float>? parameters, bool loops)
        {
            Event = sound;
            Loops = loops;
            Player = player;
            Roll = roll;
            _length = length;
            _started = now;
            _track = track;
            _parameters = sound.DefaultParameters();
            foreach (var (name, value) in parameters ?? new Dictionary<string, float>())
                SetParameter(name, value);
        }

        /// <summary>The event playing.</summary>
        public SoundEvent Event { get; }

        /// <summary>True when it plays on until stopped; false for a one-shot, a loop event played once included.</summary>
        public bool Loops { get; }

        /// <summary>The <see cref="AudioStreamPlayer3D"/> (positional) or <see cref="AudioStreamPlayer"/> playing it.</summary>
        public Node Player { get; }

        /// <summary>This play's clip, pitch and volume before parameters and distance.</summary>
        public SoundVoice Roll { get; }

        /// <summary>The loop's parameter values, by name.</summary>
        public IReadOnlyDictionary<string, float> Parameters => _parameters;

        /// <summary>World position of a positional voice.</summary>
        public Vector3 Position => (Player as Node3D)?.GlobalPosition ?? Vector3.Zero;

        /// <summary>Pitch scale now: rolled pitch times the parameters'.</summary>
        public float PitchScale { get; private set; }

        /// <summary>Volume in dB now: rolled volume, plus the parameters', plus distance fade.</summary>
        public float VolumeDb { get; private set; }

        /// <summary>Distance gain from the listener now, 0 to 1.</summary>
        public float Gain { get; private set; }

        /// <summary>
        /// Sets a loop parameter by name (clamped to its range when shaped). False for a name the event does
        /// not have.
        /// </summary>
        public bool SetParameter(string name, float value)
        {
            if (Event.FindParameter(name) is null || !float.IsFinite(value))
                return false;
            _parameters[name] = value;
            return true;
        }

        // Moves, shapes and fades the voice; false once a one-shot has finished.
        internal bool Update(Vector3 ears, double now)
        {
            if (Player is Node3D node && _track?.Invoke() is { } at)
                node.GlobalPosition = at;
            var (pitch, volume) = Event.Shape(_parameters);
            PitchScale = Roll.Pitch * pitch;
            Gain = Event.Gain(Event.Spatial ? Position.DistanceTo(ears) : 0f);
            VolumeDb = Gain > 0 ? Roll.VolumeDb + volume + (float)Mathf.LinearToDb(Gain) : SilentDb;
            switch (Player)
            {
                case AudioStreamPlayer3D p3:
                    p3.PitchScale = PitchScale;
                    p3.VolumeDb = VolumeDb;
                    break;
                case AudioStreamPlayer p2:
                    p2.PitchScale = PitchScale;
                    p2.VolumeDb = VolumeDb;
                    break;
            }
            // Our own clock decides when a one-shot is over: headless runs have no audio device to report it.
            return Loops || now - _started < _length / Math.Max(0.01, Roll.Pitch * pitch) + 0.05;
        }

        internal void Release()
        {
            if (!GodotObject.IsInstanceValid(Player))
                return;
            Player.Call(AudioStreamPlayer.MethodName.Stop);
            Player.GetParent()?.RemoveChild(Player);
            Player.QueueFree();
        }
    }
}
