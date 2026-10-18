using Godot;
using Mechanica.Core.Bots;
using Mechanica.Core.Survival;
using Mechanica.Game.Bots;
using Mechanica.Game.Mods;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// POI guards, host plus client, in the vanilla Survival world. Nobody is near a POI at the spawn, so no
/// bots exist; the host moves the client next to the nearest guarded POI and its guards appear on the
/// client where the POI's bot markers stand; moved back to the spawn, the guards leave again.
/// </summary>
public partial class GuardTest : StagedTest
{
    // How far from the post the client is put: inside the wake distance, outside a Scuttler's sight of players.
    private const float Visit = 30f;

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private GuardPost _post = null!;
    private Vector3 _spawn;

    protected override string Tag => "guards";

    protected override int StageTimeoutFrames => 900;

    private BotSync ClientBots => _client.Main.Bots;

    private BotSync HostBots => _host.Main.Bots;

    public override void _Ready()
    {
        var mods = ModPaths.LoadContent();
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival };
        AddChild(_harness);
        _host = _harness.StartHost(mods: mods);
        _client = _harness.StartClient("Client", mods: mods);

        Add("the client joins a Survival world", _ =>
            _client.Main.Survival.Own is not null && _client.Main.LocalPlayer is { } player && player.IsOnFloor());
        Add("the host knows the world's guard posts and none is on duty at the spawn", frame =>
        {
            if (HostBots.Guards is not { } guards || frame < 30)
                return false;
            _spawn = _client.Main.LocalPlayer!.GlobalPosition;
            var posts = guards.Posts.ToList();
            Check(posts.Count > 0, "the vanilla world has guard posts");
            _post = posts.MinBy(p => Flat(p.Feet.ToGodot() - _spawn))!;
            Check(Flat(_post.Feet.ToGodot() - _spawn) > HostBots.Settings!.GuardSleepDistance, $"the nearest post ({_post.Poi}) is far from the spawn");
            Check(ClientBots.Bodies.Count == 0 && HostBots.Bots!.Bots.Count == 0, "no bots exist yet");
            return true;
        });
        Add("the client walks up to a POI and its guards appear there", frame =>
        {
            if (frame == 0)
            {
                var at = _post.Feet.ToGodot() + new Vector3(Visit, 0, 0);
                at.Y = _host.Main.Streamer.Generator.HeightAt(at.X, at.Z) + 1f;
                _host.Main.PlayerSync.HostTeleport(_client.Peer, at);
            }
            var onDuty = HostBots.Guards!.OnDuty.Count(id => id is not null);
            if (onDuty == 0 || ClientBots.Bodies.Count != onDuty)
                return false;
            Check(HostBots.Bots!.Bots.Values.All(b => Flat(b.Position.ToGodot() - _post.Feet.ToGodot()) < Visit + HostBots.Settings!.GuardWakeDistance),
                "every guard on duty stands near the visited POI");
            var types = _client.Main.Bots.Bodies.Values.Select(b => b.Type.QualifiedId).ToHashSet();
            Check(types.Contains(_post.Bot), $"the client shows the post's {_post.Bot} (shows {string.Join(", ", types)})");
            Check(HostBots.Bots!.Bots.Values.All(b => b.Raid is null), "guards are not raiders");
            return true;
        });
        Add("the client goes back to the spawn and the guards leave", frame =>
        {
            if (frame == 0)
                _host.Main.PlayerSync.HostTeleport(_client.Peer, _spawn + new Vector3(0, 0.5f, 0));
            return ClientBots.Bodies.Count == 0 && HostBots.Bots!.Bots.Count == 0;
        });
    }

    private static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();
}
