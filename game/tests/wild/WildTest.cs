using Godot;
using Mechanica.Core.Survival;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Bots in the wild, host plus client, in a generated Survival world: with both players on a shore, Blue Scuttlers
/// appear in the deep water off it and swim there on both peers; when the players go far inland they are taken away.
/// </summary>
public partial class WildTest : StagedTest
{
    private const string Blue = "vanilla:scuttler_blue";

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Vector3 _shore;

    protected override string Tag => "wild";

    protected override int StageTimeoutFrames => 1800;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", Seed = 20261018, GameMode = GameMode.Survival };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null);
        Add("both players go to a shore", frame =>
        {
            if (frame > 0)
                return frame > 30;
            var gen = _host.Main.Streamer.Generator;
            for (var t = 0f; t < 6100f; t += 2f)
            {
                if (gen.HeightAt(t, 0) > gen.SeaLevel + 0.2f)
                    _shore = new Vector3(t, gen.HeightAt(t, 0), 0);
                else if (_shore != default && gen.HeightAt(t + 40f, 0) < gen.SeaLevel - 4f)
                    break;
            }
            Check(_shore != default, $"a shore with deep water off it is found at {_shore}");
            _host.Main.PlayerSync.HostTeleport(_host.Peer, _shore + Vector3.Up * 0.5f);
            _host.Main.PlayerSync.HostTeleport(_client.Peer, _shore + new Vector3(-2, 0.5f, 0));
            return false;
        });
        Add("Blue Scuttlers appear in the deep water and swim on both peers", _ =>
        {
            var wild = _host.Main.Bots.Wild!.Bots;
            if (wild.Count < 2 || !wild.All(id => _client.Main.Bots.Bodies.GetValueOrDefault(id)?.View is { Swimming: true }))
                return false;
            var gen = _host.Main.Streamer.Generator;
            foreach (var id in wild)
            {
                var bot = _host.Main.Bots.Bots!.Find(id)!;
                Check(bot.Type.QualifiedId == Blue, $"bot {id} is a Blue Scuttler ({bot.Type.QualifiedId})");
                var away = new Vector2(bot.Position.X - _shore.X, bot.Position.Z - _shore.Z).Length();
                Check(away is > 20 and < 66, $"bot {id} is 25 to 60 m from the players ({away:0} m)");
                Check(gen.SeaLevel - gen.HeightAt(bot.Position.X, bot.Position.Z) >= 2.5f, $"bot {id} is in deep water");
            }
            Check(_client.Main.Bots.Bodies.Values.Where(b => wild.Contains(b.BotId)).All(b => b.Model?.Clip is "swim" or "swim_idle"),
                "the client shows them swimming");
            return true;
        });
        Add("they go when the players head far inland", frame =>
        {
            if (frame == 0)
            {
                var inland = _shore - new Vector3(400, 0, 0);
                inland.Y = _host.Main.Streamer.Generator.HeightAt(inland.X, inland.Z) + 0.5f;
                _host.Main.PlayerSync.HostTeleport(_host.Peer, inland);
                _host.Main.PlayerSync.HostTeleport(_client.Peer, inland + new Vector3(0, 0, 2));
            }
            return _host.Main.Bots.Bots!.Bots.Values.All(b => b.Type.QualifiedId != Blue) && _client.Main.Bots.Bodies.Values.All(b => b.Type.QualifiedId != Blue);
        });
    }
}
