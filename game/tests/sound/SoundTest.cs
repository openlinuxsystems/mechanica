using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Player;
using Mechanica.Core.Sound;
using Mechanica.Core.World;
using Mechanica.Game.Building;
using Mechanica.Game.Mods;
using Mechanica.Game.Sound;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Sound, host plus clients (chunk 16, D10). Every vanilla clip must load through the runtime loaders.
/// A client builds with its tools and both peers must hear what the host decided (place, smash, break,
/// remove), while swings, sparks and refusals stay with the player who made them; a client out of earshot
/// is not sent a sound. A loop on a part follows it on the client, takes parameter changes, reaches a late
/// joiner and stops when the part goes. Uses vanilla plus the fixture mod in <c>tests/sound/mods</c>.
/// </summary>
public partial class SoundTest : StagedTest
{
    private const string Plank = "sound_test:plank";

    private readonly List<ActionResult> _results = [];
    private readonly Dictionary<Instance, List<(string Id, Vector3 At)>> _heard = [];
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Instance _late = null!;
    private int _creation;
    private int _loop;
    private Vector3? _lastCentre;

    protected override string Tag => "sound";

    protected override int StageTimeoutFrames => 600;

    private ToolBelt Tools => _client.Main.Tools!;

    public override void _Ready()
    {
        _mods = ModLoader.Load([Path.Combine(ModPaths.ModsRoot, Identifiers.VanillaModId), ProjectSettings.GlobalizePath("res://tests/sound/mods/sound_test")]);
        _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = Listen(_harness.StartHost(mods: _mods));
        _client = Listen(_harness.StartClient("Client", mods: _mods));

        Add("the client joins", _ =>
        {
            if (_client.Main.PlayerSync.Players.Count != 2 || _client.Main.Tools is null || _host.Main.PlayerSync.Players.Count != 2)
                return false;
            var registry = _host.Main.SoundEvents!;
            Check(!_mods.HasErrors && !registry.HasErrors, $"the mods and sounds load ({string.Join("; ", registry.Diagnostics)})");
            Check(registry.Find("place_wood") is not null && registry.Find("hum", "sound_test") is not null, "vanilla's and the fixture's events are registered");
            return true;
        });
        Add("every clip loads through the runtime loaders", _ =>
        {
            var player = _host.Main.Sounds.Player;
            var clips = 0;
            var bad = new List<string>();
            foreach (var sound in _host.Main.SoundEvents!.Events.Values)
            {
                foreach (var clip in sound.Clips)
                {
                    clips++;
                    if (player.Stream(clip, sound.Loop) is not { } stream || stream.GetLength() <= 0)
                        bad.Add(clip);
                }
            }
            Check(bad.Count == 0, $"all {clips} clips of {_host.Main.SoundEvents.Events.Count} events load ({string.Join(", ", bad)})");
            var wav = player.Stream(_host.Main.SoundEvents.Find("click_loop", "sound_test")!.Clips[0], loop: true) as AudioStreamWav;
            Check(wav is { LoopMode: AudioStreamWav.LoopModeEnum.Forward, LoopEnd: > 0 }, "a WAV loop is set to loop");
            var ogg = player.Stream(_host.Main.SoundEvents.Find("engine_idle")!.Clips[0], loop: true) as AudioStreamOggVorbis;
            Check(ogg is { Loop: true }, "an Ogg loop is set to loop");
            return true;
        });
        Add("each play varies its pitch and volume", _ =>
        {
            var player = _host.Main.Sounds.Player;
            var hit = _host.Main.SoundEvents!.Find("smash_hit")!;
            var voices = Enumerable.Range(0, 6).Select(_ => player.Play(hit, Vector3.Zero)).OfType<SoundPlayer.Voice>().ToList();
            Check(voices.Count == 6, "six copies play (the limit is eight)");
            Check(voices.All(v => Mathf.Abs(v.Roll.Pitch - 1) <= hit.PitchVariation + 1e-4f && Mathf.Abs(v.Roll.VolumeDb) <= hit.VolumeVariationDb + 1e-4f),
                $"within the event's variation ({string.Join(", ", voices.Select(v => $"{v.Roll.Pitch:F3}"))})");
            Check(voices.Select(v => v.Roll.Pitch).Distinct().Count() > 1, "and not all the same");
            Check(voices.All(v => v.Player is AudioStreamPlayer3D { AttenuationModel: AudioStreamPlayer3D.AttenuationModelEnum.Disabled }),
                "positional voices fade by Core's curve, not Godot's");
            player.StopAll();
            var ui = player.Play("ui_click", Identifiers.VanillaModId, Vector3.Zero);
            Check(ui?.Player is AudioStreamPlayer p && p.Bus == "Ui" && ui.Gain == 1, "a UI sound is not positional and plays on the Ui bus");
            player.StopAll();
            return true;
        });

        // A loop fired as a one-shot (a bot's cry) plays its clip once and ends on every peer: it never hums on where it played.
        Add("a loop played as a one-shot ends by itself on both peers", frame =>
        {
            var hum = _host.Main.SoundEvents!.Find("hum", "sound_test")!;
            if (frame == 0)
            {
                Check(hum.Loop, "the hum is a loop");
                Check(_host.Main.Sounds.Emit("hum", "sound_test", _client.Main.LocalPlayer!.GlobalPosition), "the host emits it");
                var voice = _host.Main.Sounds.Player.Voices.LastOrDefault(v => v.Event == hum);
                Check(voice is { Loops: false, Player: AudioStreamPlayer3D { Stream: AudioStreamOggVorbis { Loop: false } } }, "it plays once, not looping");
                return false;
            }
            if (frame == 10)
                Check(_client.Main.Sounds.Player.Voices.Any(v => v.Event.QualifiedId == hum.QualifiedId && !v.Loops), "the client plays it once too");
            return frame > 10 && _host.Main.Sounds.Player.Voices.Concat(_client.Main.Sounds.Player.Voices).All(v => v.Event.QualifiedId != hum.QualifiedId);
        });

        AddBuilding();
        AddEarshot();
        AddLoops();
    }

    private void AddBuilding()
    {
        Add("aim at the ground ahead", frame =>
        {
            var me = _client.Main.LocalPlayer!.GlobalPosition;
            return LookAt(_client, new Vector3(me.X, 0.02f, me.Z - 2f), frame);
        });
        Add("the client places a wood plank, heard on both peers", frame =>
        {
            if (frame == 0)
            {
                Clear();
                Check(Tools.Builder.Select(Plank), "the plank is in hand");
                Tools.Use(Tools.Builder);
                Tools.Builder.UpdateAim();
                Check(Tools.Builder.Place(_results.Add), $"placing sends PlacePart ({Tools.Builder.Aim})");
            }
            if (_results.Count == 0 || !Heard(_client, "vanilla:place_wood"))
                return false;
            Check(_results[0].Ok, $"the host places it ({_results[0]})");
            _creation = _results[0].Value!.Value.GetProperty("creation").GetInt32();
            Check(Heard(_host, "vanilla:place_wood"), "the host hears it placed");
            var at = _heard[_client].First(h => h.Id == "vanilla:place_wood").At;
            var centre = PartCentre(_host, _creation, 1);
            Check(at.DistanceTo(centre) < 0.01f, $"where the plank is ({at} vs {centre})");
            return true;
        });
        Add("the client sees the plank", _ => _client.Main.Creations.NodeOf(_creation) is not null);
        Add("a refused weld sparks and errors for the client only", frame =>
        {
            if (frame == 0)
            {
                Clear();
                if (!LookAt(_client, PartCentre(_client, _creation, 1), 10))
                    return false;
            }
            if (frame == 10)
            {
                Tools.Use(Tools.Weld);
                Check(Tools.Weld.Select(), "the plank is selected");
                Tools.Weld.UpdateAim();
                Check(Tools.Weld.Weld(_results.Add), "welding it to itself is sent");
            }
            if (_results.Count == 0 || frame < 20)
                return false;
            Check(!_results[0].Ok, $"the host refuses it ({_results[0]})");
            Check(Heard(_client, "vanilla:weld_start") && Heard(_client, "vanilla:ui_error"), "the client hears the spark and the refusal");
            Check(!Heard(_host, "vanilla:ui_error") && !Heard(_host, "vanilla:weld_start") && !Heard(_host, "vanilla:weld_complete"),
                "the host hears neither, and nothing was welded");
            Tools.Weld.Deselect();
            return true;
        });
        Add("a weld's blue sparks fly on every peer that hears it", frame =>
        {
            if (frame == 0)
            {
                Clear();
                var at = _client.Main.LocalPlayer!.GlobalPosition + new Vector3(0, 1, -1.5f);
                Check(_host.Main.Sounds.Emit("weld_complete", "vanilla", at), "the host plays weld_complete");
            }
            if (!Heard(_client, "vanilla:weld_complete") && frame < 60)
                return false;
            Check(_host.Main.WeldEffects.Bursts == 1 && _client.Main.WeldEffects.Bursts == 1,
                $"sparks on both peers ({_host.Main.WeldEffects.Bursts}, {_client.Main.WeldEffects.Bursts})");
            return true;
        });
        Add("a Sledgehammer hit plays smash_hit and the plank's impact", frame =>
        {
            if (frame == 0)
            {
                Clear();
                Request(ToolActions.Sledge, new SledgeArgs(_creation, 1));
            }
            if (_results.Count == 0 || !Heard(_client, "vanilla:impact_wood"))
                return false;
            Check(_results[0].Ok && !_results[0].Value!.Value.GetProperty("broken").GetBoolean(), $"the first hit only damages it ({_results[0]})");
            Check(Heard(_client, "vanilla:smash_hit") && Heard(_host, "vanilla:smash_hit") && Heard(_host, "vanilla:impact_wood"), "both peers hear the hit");
            Check(Bursts(_client) == 1 && Bursts(_host) == 1, $"dust and chips fly on both peers ({Bursts(_client)}, {Bursts(_host)})");
            return true;
        });
        Wait("the swing ends", 60);
        Add("the second hit breaks it: smash_break and break_wood", frame =>
        {
            if (frame == 0)
            {
                Clear();
                Request(ToolActions.Sledge, new SledgeArgs(_creation, 1));
            }
            if (_results.Count == 0 || !Heard(_client, "vanilla:break_wood"))
                return false;
            Check(_results[0].Ok && _results[0].Value!.Value.GetProperty("broken").GetBoolean(), $"the plank breaks ({_results[0]})");
            Check(Heard(_client, "vanilla:smash_break") && Heard(_host, "vanilla:break_wood") && !Heard(_host, "vanilla:smash_hit"),
                "both peers hear it break, not a plain hit");
            Check(Bursts(_client) == 2 && Bursts(_host) == 2, $"and again for the break ({Bursts(_client)}, {Bursts(_host)})");
            return true;
        });
    }

    private void AddEarshot()
    {
        Add("a sound out of earshot is not sent", frame =>
        {
            if (frame == 0)
            {
                Clear();
                var sounds = _host.Main.Sounds;
                var sent = sounds.Sent;
                var me = _client.Main.LocalPlayer!.GlobalPosition;
                Check(sounds.Emit("place_metal", Identifiers.VanillaModId, me + new Vector3(500, 0, 0)), "the host emits far away");
                Check(sounds.Sent == sent, "and sends it to nobody");
                Check(sounds.Emit("place_glass", Identifiers.VanillaModId, me + new Vector3(3, 0, 0)), "then near the client");
                Check(sounds.Sent == sent + 1, "which is sent to the client");
                Check(!sounds.Emit("no_such_sound", Identifiers.VanillaModId, me), "an unknown sound plays nothing");
            }
            if (!Heard(_client, "vanilla:place_glass"))
                return false;
            Check(!Heard(_client, "vanilla:place_metal") && Heard(_host, "vanilla:place_metal"), "the far sound is the host's alone");
            var voice = _client.Main.Sounds.Player.Voices.FirstOrDefault(v => v.Event.QualifiedId == "vanilla:place_glass");
            Check(voice is { Gain: > 0 and < 1 }, $"the near one fades with distance (gain {voice?.Gain:F3})");
            return true;
        });
    }

    private void AddLoops()
    {
        Add("the client builds a plank to put a loop on", frame =>
        {
            if (frame == 0)
            {
                Clear();
                var me = _client.Main.LocalPlayer!.GlobalPosition;
                Request(BuildActions.PlacePart, new PlacePartArgs(Plank, GridCell.Zero, Position: new Vector3(Mathf.Snapped(me.X, 0.25f), 0.125f, Mathf.Snapped(me.Z, 0.25f) - 2.5f).ToNumerics().ToVec3()));
            }
            if (_results.Count == 0 || !_results[0].Ok)
                return _results.Count > 0 && !Check(false, $"placed ({_results[0]})");
            _creation = _results[0].Value!.Value.GetProperty("creation").GetInt32();
            return _client.Main.Creations.NodeOf(_creation) is not null;
        });
        Add("a loop on the plank plays on both peers", frame =>
        {
            var sounds = _host.Main.Sounds;
            if (frame == 0)
            {
                Check(sounds.StartLoop("place_wood", Identifiers.VanillaModId, new SoundAnchor(Vector3.Zero)) == 0, "a one-shot cannot loop");
                Check(sounds.StartLoop("nope", Identifiers.VanillaModId, new SoundAnchor(Vector3.Zero)) == 0, "an unknown event cannot loop");
                Check(_client.Main.Sounds.StartLoop("hum", "sound_test", new SoundAnchor(Vector3.Zero)) == 0, "a client cannot start one");
                _loop = sounds.StartLoop("hum", "sound_test", SoundAnchor.OnPart(_creation, 1), new Dictionary<string, float> { ["speed"] = 0.25f });
                Check(_loop > 0, "the host starts it");
            }
            if (Loop(_client) is not { Voice: { } voice })
                return false;
            Check(voice.Event.QualifiedId == "sound_test:hum" && voice.Parameters["speed"] == 0.25f, "the client plays it at the host's speed");
            Check(Mathf.IsEqualApprox(voice.PitchScale, voice.Roll.Pitch * 1.25f, 1e-4f), $"so its pitch is up a quarter ({voice.PitchScale:F3})");
            Check(voice.Position.DistanceTo(PartCentre(_client, _creation, 1)) < 0.01f, "at the plank");
            return true;
        });
        Add("a parameter change reaches the client", frame =>
        {
            if (frame == 0)
                Check(_host.Main.Sounds.SetLoopParameter(_loop, "speed", 1f) && !_host.Main.Sounds.SetLoopParameter(_loop, "rpm", 1f), "the host sets speed");
            if (Loop(_client)?.Voice is not { } voice || voice.Parameters["speed"] != 1f)
                return false;
            Check(Mathf.IsEqualApprox(voice.PitchScale, voice.Roll.Pitch * 2f, 1e-4f), $"its pitch doubles ({voice.PitchScale:F3})");
            Check(Loop(_host)!.Voice!.Parameters["speed"] == 1f, "on the host too");
            return true;
        });
        Add("a late joiner gets the running loop", frame =>
        {
            if (frame == 0)
                _late = Listen(_harness.StartClient("Late", mods: _mods));
            if (Loop(_late)?.Voice is not { } voice)
                return false;
            Check(voice.Parameters["speed"] == 1f, "with its current speed");
            return true;
        });
        Add("the loop follows the plank as it is pushed", frame =>
        {
            if (frame == 0)
                Request(ToolActions.Sledge, new SledgeArgs(_creation, 1));
            // The voice moves on the process tick and the body on the physics tick (and a resync can snap
            // it), so they can differ by a frame while it moves: wait until the plank settles under it.
            if (frame < 30 || Loop(_client)?.Voice is not { } voice)
                return false;
            var centre = PartCentre(_client, _creation, 1);
            var still = _lastCentre is { } last && last.DistanceTo(centre) < 1e-4f;
            _lastCentre = centre;
            if (!still || voice.Position.DistanceTo(centre) >= 0.05f)
                return false;
            Check(true, "the voice stays on the part");
            return true;
        });
        Add("removing the plank stops the loop everywhere", frame =>
        {
            if (frame == 0)
            {
                Clear();
                Request(BuildActions.RemovePart, new RemovePartArgs(_creation, 1));
            }
            if (_results.Count == 0 || Loop(_host) is not null || Loop(_client) is not null || Loop(_late) is not null)
                return false;
            Check(_results[0].Ok, $"the plank is removed ({_results[0]})");
            Check(Heard(_host, "sound_test:plank_pop") && Heard(_client, "sound_test:plank_pop"), "with its own bound remove sound");
            Check(_client.Main.Sounds.Player.Voices.All(v => v.Event.QualifiedId != "sound_test:hum"), "no hum voice is left on the client");
            return true;
        });
    }

    private Instance Listen(Instance instance)
    {
        var heard = _heard[instance] = [];
        // The harness adds Main to the tree as it starts, so its sound player already exists.
        instance.Main.Sounds.Player.VoiceStarted += v => heard.Add((v.Event.QualifiedId, v.Position));
        return instance;
    }

    private void Clear()
    {
        _results.Clear();
        foreach (var heard in _heard.Values)
            heard.Clear();
    }

    private bool Heard(Instance who, string id) => _heard[who].Any(h => h.Id == id);

    private SoundSync.Loop? Loop(Instance who) => who.Main.Sounds.Loops.GetValueOrDefault(_loop);

    private void Request<TArgs>(string action, TArgs args) => _client.Main.Actions.Request(action, args, _results.Add);

    private static Vector3 PartCentre(Instance who, int creation, int partId)
    {
        var graph = who.Main.Creations.Find(creation)!;
        var part = graph.FindPart(partId)!;
        var body = who.Main.Creations.NodeOf(creation)!.Bodies[graph.BodyIndexOf(partId)];
        return body.GlobalTransform * PartGeometry.CentreMetres(graph.DefinitionOf(partId), part.Cell, part.Rotation).ToGodot();
    }

    private static int Bursts(Instance who) => who.Main.GetNode<SledgeImpacts>("SledgeImpacts").Bursts;

    private static bool LookAt(Instance who, Vector3 target, int frame)
    {
        var eye = who.Main.LocalPlayer!.Camera.GlobalPosition;
        var d = target - eye;
        who.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()) };
        return frame >= 10;
    }
}
