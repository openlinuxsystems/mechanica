using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Survival;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Bots in the wild, host plus client, in a generated Survival world: with both players on a shore, Blue Scuttlers
/// appear in the deep water off it and swim there on both peers; when the players go far inland they are taken away.
/// In a meadow, Grazers appear in herds in their biomes and show on the client. The client tames one with carrots, leads it
/// on a Lead and lets it go: when the players leave,
/// the wild ones go and it stays, and it comes back where it was when the world is saved and loaded.
/// </summary>
public partial class WildTest : StagedTest
{
    private const string Blue = "vanilla:scuttler_blue";
    private const string Grazer = "vanilla:grazer";
    private const string Carrot = "vanilla:carrot";
    private const string Lead = "vanilla:lead";

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Vector3 _shore;
    private int _kept;

    // A Grazer's collar on the client.
    private Node3D? Collar(int bot) => _client.Main.Bots.Bodies.GetValueOrDefault(bot)?.Model?.Scene.FindChild("Collar", true, false) as Node3D;
    private Vector3 _keptAt;

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
            var wild = _host.Main.Bots.Wild!.Bots.Where(id => _host.Main.Bots.Bots!.Find(id)?.Type.QualifiedId == Blue).ToList();
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
        var meadow = Vector3.Zero;
        Add("both players go to a meadow", frame =>
        {
            if (frame > 0)
                return frame > 30;
            var gen = _host.Main.Streamer.Generator;
            for (var r = 0f; r < 5000f && meadow == default; r += 25f)
            {
                for (var a = 0f; a < Mathf.Tau && meadow == default; a += 0.3f)
                {
                    var (x, z) = (r * Mathf.Cos(a), r * Mathf.Sin(a));
                    // deep in the biome, so a herd's spots round it are meadow too
                    if (new[] { (0f, 0f), (60f, 0f), (-60f, 0f), (0f, 60f), (0f, -60f) }.All(d => gen.BiomeAt(x + d.Item1, z + d.Item2).Id == "meadow")
                        && gen.HeightAt(x, z) > gen.SeaLevel + 2f)
                        meadow = new Vector3(x, gen.HeightAt(x, z), z);
                }
            }
            Check(meadow != default, $"a meadow is found at {meadow}");
            _host.Main.PlayerSync.HostTeleport(_host.Peer, meadow + Vector3.Up * 0.5f);
            _host.Main.PlayerSync.HostTeleport(_client.Peer, meadow + new Vector3(-2, 0.5f, 0));
            return false;
        });
        Add("Grazers appear in herds in their biomes, on both peers", _ =>
        {
            var grazers = _host.Main.Bots.Wild!.Bots.Select(id => _host.Main.Bots.Bots!.Find(id)).Where(b => b?.Type.QualifiedId == Grazer).ToList();
            if (grazers.Count < 4 || !grazers.All(b => _client.Main.Bots.Bodies.GetValueOrDefault(b!.Id)?.Model is not null))
                return false;
            var gen = _host.Main.Streamer.Generator;
            foreach (var bot in grazers)
                Check(gen.BiomeAt(bot!.Position.X, bot.Position.Z).Id is "meadow" or "field", $"Grazer {bot.Id} is in a meadow or field");
            // a herd appears within 5 m and each wanders up to 8 m round its own spot
            Check(grazers.All(a => grazers.Count(b => (a!.Position - b!.Position).Length() < 22f) >= 2), "each Grazer has herd mates near it");
            Check(grazers.All(b => Collar(b!.Id) is { Visible: false }), "wild Grazers wear no collar");
            _kept = grazers[0]!.Id;
            return true;
        });
        var fed = new List<ActionResult>();
        var carrots = 0;
        Add("the client tames a Grazer by feeding it three carrots", frame =>
        {
            if (frame == 0)
            {
                Check(_host.Main.Inventory.Items!.PlayerInventory(_client.Peer).Add(Carrot, 3) == 3, "the client gets three carrots");
                carrots = _host.Main.Inventory.Items!.PlayerInventory(_client.Peer).Count(Carrot);
            }
            if (frame % 20 == 0 && fed.Count == frame / 20 && fed.Count < 3)
            {
                // stand by it (it wanders) and hand it a carrot
                var at = _host.Main.Bots.Bots!.Find(_kept)!.Position.ToGodot();
                _host.Main.PlayerSync.HostTeleport(_client.Peer, at + new Vector3(1.5f, 0.3f, 0));
            }
            if (frame % 20 == 10 && fed.Count == frame / 20 && fed.Count < 3)
                _client.Main.Actions.Request(BotActions.FeedBot, new FeedBotArgs(_kept, Carrot), fed.Add);
            if (fed.Count < 3 || _client.Main.Bots.Bodies.GetValueOrDefault(_kept)?.View is not { Kept: true })
            {
                if (frame == 500)
                    GD.Print($"[wild] the feeds so far: {string.Join(", ", fed)}");
                return false;
            }
            Check(fed.All(r => r.Ok), $"every feed is taken ({string.Join(", ", fed)})");
            Check(_host.Main.Bots.Bots!.Find(_kept) is { Kept: true, Fed: 3 }, "three feeds tame it on the host");
            Check(Collar(_kept) is { Visible: true }, "the client shows its collar");
            Check(_host.Main.Inventory.Items!.PlayerInventory(_client.Peer).Count(Carrot) == carrots - 3, "three carrots were eaten");
            return true;
        });
        var led = new List<ActionResult>();
        var walkedTo = Vector3.Zero;
        var gap = 0f;
        static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();
        Add("the client leads the tame Grazer on a Lead and it follows, rope and all", frame =>
        {
            var host = _host.Main.Bots.Bots!;
            if (frame == 0)
            {
                Check(_host.Main.Inventory.Items!.PlayerInventory(_client.Peer).Add(Lead, 1) == 1, "the client gets a Lead");
                _host.Main.PlayerSync.HostTeleport(_client.Peer, host.Find(_kept)!.Position.ToGodot() + new Vector3(1.5f, 0.3f, 0));
            }
            if (frame == 10)
                _client.Main.Actions.Request(BotActions.LeadBot, new BotArgs(_kept), led.Add);
            if (led.Count == 0 || _client.Main.Bots.Bodies.GetValueOrDefault(_kept)?.View is not { LedBy: { } peer })
                return false;
            if (walkedTo == Vector3.Zero)
            {
                Check(led[0].Ok && peer == _client.Peer, $"the client leads it ({led[0]})");
                // walk off seven metres, the flattest way (the herd and so the ground differ from run to run): it
                // comes after the client
                var gen = _host.Main.Streamer.Generator;
                var from = host.Find(_kept)!.Position.ToGodot();
                var space = _host.Main.GetWorld3D().DirectSpaceState;
                var self = new Godot.Collections.Array<Rid> { _host.Main.Bots.Bodies[_kept].GetRid() };
                // nothing in the way from the bot itself, at its knees and its back
                bool Clear(Vector3 to) => new[] { 0.3f, 0.8f }.All(h =>
                {
                    var ray = PhysicsRayQueryParameters3D.Create(from + Vector3.Up * h, to with { Y = gen.HeightAt(to.X, to.Z) + h }, uint.MaxValue, self);
                    return space.IntersectRay(ray).Count == 0;
                });
                var at = Enumerable.Range(0, 8).Select(i => from + new Vector3(Mathf.Cos(i * Mathf.Pi / 4), 0, Mathf.Sin(i * Mathf.Pi / 4)) * 7f)
                    .OrderBy(p => Clear(p) ? 0 : 1).ThenBy(p => Mathf.Abs(gen.HeightAt(p.X, p.Z) - gen.HeightAt(from.X, from.Z))).First();
                walkedTo = at with { Y = gen.HeightAt(at.X, at.Z) + 0.5f };
                gap = Flat(from - walkedTo);
                _host.Main.PlayerSync.HostTeleport(_client.Peer, walkedTo);
                return false;
            }
            var feet = _host.Main.PlayerSync.Players[_client.Peer].GlobalPosition;
            var now = Flat(host.Find(_kept)!.Position.ToGodot() - feet);
            if (frame % 120 == 0)
                GD.Print($"[wild] following: {now:0.0} m of {gap:0.0} m to go (bot at {host.Find(_kept)!.Position}, led by {host.Find(_kept)!.LedBy})");
            if (now > 4f && now > gap - 2f)
                return false;
            Check(_client.Main.Bots.GetNode<Bots.BotLeads>("Leads").Drawn.Contains(_kept), "the client draws the rope");
            Check(_host.Main.Bots.GetNode<Bots.BotLeads>("Leads").Drawn.Contains(_kept), "the host draws the rope");
            return true;
        });
        Add("the client lets the Grazer go and it makes its home there", frame =>
        {
            var host = _host.Main.Bots.Bots!;
            if (frame == 0)
                _client.Main.Actions.Request(BotActions.LeadBot, new BotArgs(_kept), led.Add);
            if (led.Count < 2 || host.Find(_kept)!.LedBy is not null || _client.Main.Bots.Bodies.GetValueOrDefault(_kept)?.View is not { LedBy: null })
                return false;
            Check(led[1].Ok, $"letting go is taken ({led[1]})");
            var bot = host.Find(_kept)!;
            Check((bot.Home - bot.Position).Length() < 2f, $"its home is where it was let go ({bot.Home} vs {bot.Position})");
            Check(!_client.Main.Bots.GetNode<Bots.BotLeads>("Leads").Drawn.Contains(_kept), "the rope is gone");
            return true;
        });
        Add("a kept Grazer stays when the players leave and the wild ones go", frame =>
        {
            if (frame == 0)
            {
                var away = meadow + new Vector3(0, 0, 400);
                away.Y = _host.Main.Streamer.Generator.HeightAt(away.X, away.Z) + 0.5f;
                _host.Main.PlayerSync.HostTeleport(_host.Peer, away);
                _host.Main.PlayerSync.HostTeleport(_client.Peer, away + new Vector3(0, 0, 2));
            }
            var bots = _host.Main.Bots.Bots!;
            if (bots.Bots.Values.Any(b => b.Type.QualifiedId == Grazer && b.Id != _kept && (b.Position.ToGodot() - meadow).Length() < 100f))
                return false;
            Check(bots.Find(_kept) is { Dead: false, Kept: true }, "the kept Grazer is still there");
            Check(_client.Main.Bots.Bodies.ContainsKey(_kept), "the client still shows it");
            _keptAt = bots.Find(_kept)!.Position.ToGodot();
            return true;
        });
        Add("the world is saved and loads with the kept Grazer where it was", frame =>
        {
            if (frame == 0)
            {
                var save = _host.Main.Saves.Capture("Wild");
                Check(save.Bots is [{ Type: Grazer }], $"the save holds the one kept Grazer ({save.Bots.Count})");
                _harness.Stop(_client);
                _harness.Stop(_host);
                _harness.Save = save;
                _host = _harness.StartHost();
                _client = _harness.StartClient("Client");
            }
            var kept = _host.Main.Bots.Bots?.Bots.Values.FirstOrDefault(b => b.Kept);
            if (kept is null || !_client.Main.Bots.Bodies.ContainsKey(kept.Id))
                return false;
            Check(kept.Type.QualifiedId == Grazer, "it is a Grazer");
            Check((kept.Position.ToGodot() - _keptAt).Length() < 4f, $"it is where it was ({kept.Position.ToGodot()} vs {_keptAt})");
            return true;
        });
    }
}
