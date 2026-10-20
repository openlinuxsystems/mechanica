using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Items;
using Mechanica.Game.Ui;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Tossing items on the ground (Scrap Mechanic's drag out of an item window), host plus a client in Survival. The client
/// tosses one carrot out of its inventory window: the host makes a pile <c>toss_distance</c> ahead of it, and the client
/// sees it fly from its hand before it lies there. A hotbar slot let go over the world tosses the client's whole stack
/// of seeds and empties. A stack of metal chunks tossed out of a chest's window leaves the chest. The client then picks every pile
/// back up.
/// </summary>
public partial class TossTest : StagedTest
{
    private const string Carrot = "vanilla:carrot";
    private const string Seed = "vanilla:carrot_seed";
    private const string Chunk = "vanilla:metal_chunk";
    private const string Chest = "vanilla:chest";
    private const string Floor = "vanilla:wood_block";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private InventoryScreen _screen = null!;
    private int _floor;
    private int _chest;
    private int _drop;
    private bool _sawFlying;
    private int _carrots;

    protected override string Tag => "toss";

    protected override int StageTimeoutFrames => 900;

    private SurvivalActions HostSurvival => _host.Main.Survival.Actions!;

    private Creation HostFloor => _host.Main.Creations.Find(_floor)!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Stage("the client joins with the starting items", _ =>
        {
            if (_client.Main.Inventory.Own is null || Count(Carrot) == 0 || Count(Seed) == 0 || _client.Main.LocalPlayer is not { } player
                || !player.IsOnFloor() || _client.Main.Tools is null)
                return false;
            _screen = new InventoryScreen
            {
                Name = "InventoryScreen", Inventory = _client.Main.Inventory, Actions = _client.Main.Actions, Players = _client.Main.PlayerSync,
                Catalog = _client.Main.Build.Catalog, Hotbar = _client.Main.Hotbar, Creations = _client.Main.Creations,
            };
            AddChild(_screen);
            _carrots = Count(Carrot);
            return true;
        });
        Stage("a carrot dragged out of the inventory window flies from the hand and lands ahead", frame =>
        {
            if (frame == 0)
            {
                _screen.Show(null);
                var stack = Own.First(s => s.Item == Carrot);
                Check(_screen.Toss(new ItemDrag { Entry = Carrot, From = InventoryRef.OfPlayer(_client.Peer), FromSlot = stack.Slot, Count = 1 }), "the drag is a stack to toss");
            }
            if (_client.Main.Survival.Drops.Values.FirstOrDefault(d => d.Items.Any(s => s.Item == Carrot)) is { } shown
                && _client.Main.Survival.DropNodes.TryGetValue(shown.Id, out var node))
            {
                _drop = shown.Id;
                _sawFlying |= node.Flying;
                if (node.Flying)
                    return false;
                var feet = Feet(_client);
                var host = HostSurvival.Drops[_drop];
                var ahead = new Vector2(host.Position.X - feet.X, host.Position.Z - feet.Z).Length();
                Check(_sawFlying, "the client saw the carrot in the air");
                Check(Mathf.Abs(ahead - HostSurvival.Settings.TossDistance) < 0.3f, $"it lands toss_distance ahead ({ahead:F2} m)");
                Check(host.Items.Single() == new ItemStack(Carrot, 1) && !host.Placed, $"one carrot, which will despawn ({string.Join(", ", host.Items)})");
                Check(host.TossedFrom is null, "the pile the host keeps has no toss, so a later joiner sees it lying still");
                Check(node.Position.DistanceTo(host.Position.ToGodot()) < 0.01f, $"the client's pile lies where the host's does ({node.Position} vs {host.Position})");
                Check(Count(Carrot) == _carrots - 1, $"one carrot left the inventory ({Items()})");
                return true;
            }
            return false;
        });
        Stage("a hotbar slot let go over the world tosses its whole stack and empties", frame =>
        {
            if (frame == 0)
            {
                _client.Main.Hotbar.Set(9, Seed);
                return false;
            }
            if (frame == 10)
                Check(_client.Main.Tools!.TossSlot(9), "the seeds are tossed");
            if (frame < 10 || HostSurvival.Drops.Values.FirstOrDefault(d => d.Items.Any(s => s.Item == Seed)) is not { } pile)
                return false;
            if (Count(Seed) != 0 || _client.Main.Hotbar.Own[9] is not null)
                return false;
            Check(pile.Items.Single().Count == 4, $"all four seeds ({string.Join(", ", pile.Items)})");
            Check(!_client.Main.Tools!.TossSlot(9), "an empty slot has nothing to toss");
            return true;
        });
        Stage("a stack dragged out of a chest's window leaves the chest", frame =>
        {
            if (frame == 0)
            {
                BuildChest();
                return false;
            }
            if (_client.Main.Creations.Find(_floor) is null)
                return false;
            if (frame < 30)
                return false;
            if (frame == 30)
            {
                Check(HostFloor.InventoryOf(_chest)!.Add(Chunk, 4) == 4, "the chest takes a stack of four metal chunks");
                _screen.Show(InventoryRef.OfPart(_floor, _chest));
                return false;
            }
            if (frame == 60)
                Check(_screen.Toss(new ItemDrag { Entry = Chunk, From = InventoryRef.OfPart(_floor, _chest), FromSlot = 0 }), "the chest's stack is tossed");
            if (frame < 60 || HostSurvival.Drops.Values.FirstOrDefault(d => d.Items.Any(s => s.Item == Chunk)) is not { } pile)
                return frame < 600 ? false : Check(false, "the metal chunks land");
            Check(pile.Items.Single() == new ItemStack(Chunk, 4), $"all four metal chunks ({string.Join(", ", pile.Items)})");
            Check(HostFloor.InventoryOf(_chest)!.Count(Chunk) == 0, "the chest is empty");
            _screen.Hide();
            return true;
        });
        Stage("the client picks every pile back up", frame =>
        {
            // Stand by the next pile, then pick it up.
            if (_client.Main.Survival.Drops.Values.FirstOrDefault() is { } next)
            {
                if (frame % 40 == 0)
                    _host.Main.PlayerSync.HostTeleport(_client.Peer, next.Position.ToGodot() + new Vector3(0, 0.1f, 0.5f));
                if (frame % 40 == 20)
                    Request(SurvivalActions.PickUp, new PickUpArgs(next.Id));
                return false;
            }
            // The inventory view follows the piles a moment later.
            if (HostSurvival.Drops.Count > 0 || _client.Main.Survival.DropNodes.Count > 0 || Count(Chunk) != 4)
                return frame < 600 ? false : Check(false, $"everything comes back ({Items()})");
            Check(_results.All(r => r.Ok), $"every pick-up succeeds ({string.Join("; ", _results)})");
            Check(Count(Carrot) == _carrots && Count(Seed) == 4 && Count(Chunk) == 4, $"everything is back ({Items()})");
            return true;
        });
    }

    private void Stage(string name, Func<int, bool> tick) => Add(name, frame =>
    {
        if (frame == 0)
            _results.Clear();
        return tick(frame);
    });

    // A chest on a small wooden floor two metres to the client's side, built by the host.
    private void BuildChest()
    {
        var creation = new Creation(_host.Main.Build.NextCreationId(), _host.Main.Build.Catalog);
        for (var x = 0; x < 3; x++)
        {
            for (var z = 0; z < 3; z++)
                creation.TryPlacePart(Floor, new GridCell(x, 0, z), default, out _, out _);
        }
        if (!creation.TryPlacePart(Chest, new GridCell(1, 1, 1), default, out var chest, out var why))
            Check(false, $"the chest fits ({why})");
        _chest = chest?.Id ?? 0;
        _floor = creation.Id;
        var feet = _host.Main.PlayerSync.Players[_client.Peer].GlobalPosition;
        creation.SetAllBodyPoses(new Pose((feet + new Vector3(1.5f, 0.25f, 0)).ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Build.Added(creation);
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    private IReadOnlyList<SlotView> Own => _client.Main.Inventory.Own?.Items ?? [];

    private int Count(string item) => Own.Where(s => s.Item == item).Sum(s => s.Count);

    private string Items() => string.Join(", ", Own.Select(s => $"{s.Item} x{s.Count}"));

    private static Vector3 Feet(Instance who) => who.Main.LocalPlayer!.GlobalPosition;
}
