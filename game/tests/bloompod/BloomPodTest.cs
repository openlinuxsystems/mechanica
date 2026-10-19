using Godot;
using Mechanica.Core.Building;
using Mechanica.Core.World;
using Mechanica.Game.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Bloom Pods (the loot pods), host plus a client, on the vanilla world. A green pod near the spawn is drawn on both
/// peers as its own animated model with its glow, settles shut and idles. The client's sledge makes it play its hit on
/// both peers; the blow that breaks it leaves a wreck playing the break on both, and one pile of 1 to 3 different items
/// from the Common loot table. The wreck shrinks away by itself.
/// </summary>
public partial class BloomPodTest : StagedTest
{
    private readonly List<Core.Actions.ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private WorldObjectPlacement _pod = null!;

    protected override string Tag => "bloompod";

    protected override int StageTimeoutFrames => 1200;

    private WorldStreamer HostWorld => _host.Main.Streamer;

    private WorldStreamer ClientWorld => _client.Main.Streamer;

    private IEnumerable<WorldStreamer> Worlds => [HostWorld, ClientWorld];

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", Seed = 20261017 };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null);
        Wait("players settle", 30);
        Add("find a green Bloom Pod", _ =>
        {
            var gen = HostWorld.Generator;
            _pod = gen.ObjectsIn(-1500, -1500, 1500, 1500)
                .Where(o => o.Definition.Id == "bloom_pod_green" && gen.SlopeAt(o.Position.X, o.Position.Z + 2f) < 0.3f)
                .OrderBy(o => o.Position.Length())
                .First();
            Check(_pod.Definition.Loot is { Table: "common", Picks: 1, PicksMax: 3 }, $"pod {_pod.Id} at {_pod.Position} rolls 1 to 3 Common picks");
            return true;
        });
        Add("the client's player walks up to it", frame =>
        {
            if (frame == 0)
            {
                var at = _pod.Position.ToGodot();
                var stand = new Vector3(at.X, HostWorld.Generator.HeightAt(at.X, at.Z + 1.8f) + 0.3f, at.Z + 1.8f);
                _host.Main.PlayerSync.HostTeleport(_client.Peer, stand, 0f);
                return false;
            }
            return frame > 30 && Worlds.All(w => w.ChunkHolding(_pod.Id)?.Animated(_pod.Id) is not null);
        });
        Add("both peers draw it as its own model, glowing, settling shut", _ =>
        {
            foreach (var world in Worlds)
            {
                var pod = world.ChunkHolding(_pod.Id)!.Animated(_pod.Id)!;
                Check(pod.Playing is "settle" or "idle", $"{Who(world)}: it settles or idles ({pod.Playing})");
                Check(pod.GetNodeOrNull<OmniLight3D>("Glow") is { OmniRange: > 1f } light && light.LightEnergy > 0f, $"{Who(world)}: it glows");
                Check(pod.FindChildren("Petal*", owned: false).Count == 4, $"{Who(world)}: it has its four petals");
            }
            return true;
        });
        Add("then idles", frame => frame > 150 && Worlds.All(w => w.ChunkHolding(_pod.Id)!.Animated(_pod.Id)!.Playing == "idle")
            || (frame > 400 && Fail("it never went back to idle")));
        Add("a blow plays its hit on both peers", frame =>
        {
            if (frame == 0)
            {
                Swing();
                return false;
            }
            if (HostWorld.Effects.Hits == 0 || ClientWorld.Effects.Hits == 0)
                return frame > 300 && Fail($"the blow never showed ({string.Join("; ", _results)})");
            Check(_results.All(r => r.Ok), $"the swing lands ({string.Join("; ", _results)})");
            Check(!HostWorld.State.IsDestroyed(_pod.Id), "one blow does not break it");
            foreach (var world in Worlds)
                Check(world.ChunkHolding(_pod.Id)!.Animated(_pod.Id)!.Playing == "hit", $"{Who(world)}: it plays its hit");
            return true;
        });
        Add("it breaks into a wreck and one pile of Common loot", frame =>
        {
            if (frame % 75 == 40 && !HostWorld.State.IsDestroyed(_pod.Id))
                Swing();
            if (!Worlds.All(w => w.State.IsDestroyed(_pod.Id)) || Worlds.Any(w => w.Effects.Wrecks == 0))
                return frame > 1000 && Fail($"it never broke on both peers ({string.Join("; ", _results)})");
            foreach (var world in Worlds)
            {
                Check(world.ChunkHolding(_pod.Id)!.Animated(_pod.Id) is null, $"{Who(world)}: the pod itself is gone");
                var wreck = world.Effects.GetChildren().OfType<AnimatedObject>().SingleOrDefault();
                Check(wreck?.Playing == "break", $"{Who(world)}: its wreck plays the break ({wreck?.Playing})");
            }
            var table = HostWorld.Generator.Data.Definition.LootTables!["common"].Items!.Select(e => Core.Data.Identifiers.Qualify(e.Item!)).ToHashSet();
            var pile = _host.Main.Survival.Drops.Values.Where(d => d.Items.All(i => table.Contains(i.Item))).ToList();
            Check(pile.Count == 1, $"one pile of loot ({_host.Main.Survival.Drops.Count} piles)");
            var items = pile.FirstOrDefault()?.Items ?? [];
            Check(items.Count is >= 1 and <= 3 && items.Select(i => i.Item).Distinct().Count() == items.Count,
                $"1 to 3 different items ({string.Join(", ", items)})");
            return true;
        });
        Add("the wreck shrinks away", frame => Worlds.All(w => !w.Effects.GetChildren().OfType<AnimatedObject>().Any())
            || (frame > 600 && Fail("the wreck stayed")));
    }

    private void Swing()
    {
        var at = new Core.Data.Vec3(_pod.Position.X, _pod.Position.Y + 0.7f, _pod.Position.Z);
        _client.Main.Actions.Request(ToolActions.Sledge, new SledgeArgs(Object: _pod.Id.ToString(), At: at), _results.Add);
    }

    private string Who(WorldStreamer world) => world == HostWorld ? "host" : "client";

    private bool Fail(string message)
    {
        Check(false, message);
        return true;
    }
}
