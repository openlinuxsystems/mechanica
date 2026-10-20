using Godot;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;
using Mechanica.Game.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The opening cinematic on the vanilla world, host plus a client (played sped up). On the host its camera takes over, the
/// HUD hides and the player stands unseen while copies of the wreck fly; the real wreck and its spilled cargo stay out of
/// sight until the ship settles where they lie. Then the camera is the player's own again, the player gets up facing the
/// wreck and everything is as it was. A skip goes straight to the player. The client never sees any of it.
/// </summary>
public partial class OpeningTest : StagedTest
{
    private const double Speed = 6;

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private OpeningCinematic _opening = null!;
    private bool _finished;

    protected override string Tag => "opening";

    protected override int StageTimeoutFrames => 1800;

    private PlayerController Me => _host.Main.LocalPlayer!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null);
        Add("the host's player starts at the crash site and the cinematic takes over", _ =>
        {
            Check(_host.Main.PlayOpening() is { } opening, "it plays at the crash site's spawn");
            _opening = _host.Main.Opening!;
            _opening.Finished += () => _finished = true;
            Check(_opening.Playing && _opening.Camera.Current, "its camera is current");
            Check(HudRoot.Hidden, "the HUD is hidden");
            Check(!Me.Visible, "the player stands unseen");
            Check(_opening.Copies is { Fore: not null, Aft: not null, Nacelle: not null }, "copies of the three pieces fly");
            Check(_client.Main.Opening is null, "the client sees none of it");
            Engine.TimeScale = Speed;
            return true;
        });
        Add("the real wreck waits out of sight while the copies come down", _ =>
        {
            if (_opening.Time < _opening.Scene.Settings.Touchdown + 0.5f)
                return false;
            foreach (var index in _opening.Scene.HiddenModels)
                Check(Models(index).All(m => !m.Visible), $"model {index} is hidden");
            var (fore, _, nacelle) = _opening.Copies;
            Check(fore!.Visible && nacelle!.Visible, "the copies are drawn");
            Check(_opening.Camera.Current && _opening.Darkness < 0.01f, "the picture is clear");
            return true;
        });
        Add("when the ship settles, the copies give way to the wreck where it lies", _ =>
        {
            if (_opening.Time < _opening.Scene.Settings.Settle + 0.3f)
                return false;
            var scene = _opening.Scene;
            Check(Models(scene.ForeIndex).Any() && Models(scene.ForeIndex).All(m => m.Visible), "the wreck is drawn again");
            Check(Models(scene.ForeIndex).All(m => m.GlobalTransform.Origin.DistanceTo(_opening.Copies.Fore!.GlobalTransform.Origin) < 0.01f),
                "the fore section's copy came to rest exactly on it");
            Check(!_opening.Copies.Fore!.Visible, "the copies are no longer drawn");
            return true;
        });
        Add("it cuts to the player getting up, facing the wreck", _ =>
        {
            if (!_opening.HandedOff)
                return false;
            Check(Me.Camera.Current && Me.Visible, "the camera is the player's own again");
            Check(Me.Character is not CharacterModel model || model.Animator.Gesture?.StartsWith("get_up", StringComparison.Ordinal) == true,
                "the player gets up");
            Check(_opening.Copies is { Fore: null, Aft: null, Nacelle: null }, "the copies are gone");
            CheckQuiet();
            return true;
        });
        Add("when it ends everything is as it was", _ =>
        {
            if (!_finished)
                return false;
            Check(!HudRoot.Hidden, "the HUD is back");
            Check(_host.Main.Opening is null, "the cinematic is gone");
            Check(Me.Camera.Current, "the player's camera is current");
            return true;
        });
        Add("a skip goes to black and straight on to the player", frame =>
        {
            if (frame == 0)
            {
                Engine.TimeScale = 1;
                _finished = false;
                _opening = _host.Main.PlayOpening()!;
                _opening.Finished += () => _finished = true;
                _opening.Skip();
                return false;
            }
            if (!_opening.HandedOff)
                return false;
            Check(_opening.Time >= _opening.Scene.Settings.Black[1] && _opening.Time < _opening.Scene.Settings.Black[1] + 0.2f, $"it jumped to the cut ({_opening.Time:0.0} s)");
            Check(frame * (1 / 60f) < _opening.Scene.Settings.SkipFade + 0.5f, "after the short fade");
            foreach (var index in _opening.Scene.HiddenModels)
                Check(Models(index).All(m => m.Visible), $"model {index} is drawn");
            CheckQuiet();
            Engine.TimeScale = Speed;
            return true;
        });
        Add("the skipped cinematic ends too", _ =>
        {
            if (!_finished)
                return false;
            Engine.TimeScale = 1;
            Check(!HudRoot.Hidden && Me.Visible && Me.Camera.Current, "everything is back");
            return true;
        });
    }

    public override void _ExitTree() => Engine.TimeScale = 1;

    // The cinematic's loops (the ship's engine, the camera's air whoosh) stop at the cut: none hums on over the wreck.
    private void CheckQuiet()
    {
        var settings = _opening.Scene.Settings;
        var ids = settings.Sounds.Select(s => s.Event).Append(settings.EngineLoop)
            .Select(id => _host.Main.Sounds.Registry.Find(id)).OfType<Mechanica.Core.Sound.SoundEvent>().Where(e => e.Loop).ToList();
        Check(ids.Count >= 2, $"the cinematic plays loops ({ids.Count})");
        var playing = _host.Main.Sounds.Player.Voices.Where(v => ids.Contains(v.Event)).Select(v => v.Event.QualifiedId).ToList();
        Check(playing.Count == 0, $"none of them plays on after the cut ({string.Join(", ", playing)})");
    }

    private IEnumerable<Node3D> Models(int index) =>
        _host.Main.Streamer.Chunks.Values.Concat(_host.Main.Streamer.Scenery.Values)
            .Select(c => c.Model(_opening.Scene.Poi.Index, index)).OfType<Node3D>();
}
