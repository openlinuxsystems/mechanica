using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Inventories and crafting from data, host plus clients (chunk 18, D10). A client gets scrap and wood
/// (Creative give), places a bench and a chest, crafts two chests at the bench from the fixture recipe (hearing it start, work and finish),
/// takes them, and stores scrap in the chest. Each step goes through the host, and the client must see
/// every inventory it uses change. The host sees the same chest, a chest holding items cannot be picked
/// up, and a late joiner receives the chest's contents with the Creation. Uses the fixture mod in
/// <c>tests/items/mods</c>.
/// </summary>
public partial class ItemsTest : StagedTest
{
    private const string Scrap = "items_test:scrap";
    private const string Wood = "items_test:wood";
    private const string Chest = "items_test:chest";
    private const string Bench = "items_test:bench";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private readonly List<string> _clientHeard = [];
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Instance _late = null!;
    private int _bench;
    private int _chest;

    protected override string Tag => "items";

    private InventoryRef BenchRef => InventoryRef.OfPart(_bench, 1);

    private InventoryRef ChestRef => InventoryRef.OfPart(_chest, 1);

    private InventoryView? ClientOwn => _client.Main.Inventory.Own;

    public override void _Ready()
    {
        _mods = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/items/mods"));
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost(mods: _mods);
        _client = _harness.StartClient("Client", mods: _mods);
        _client.Main.Sounds.Player.VoiceStarted += v => _clientHeard.Add(v.Event.QualifiedId);

        Add("the client joins and sees its empty inventory", _ =>
        {
            if (_host.Main.PlayerSync.Players.Count != 2 || ClientOwn is not { } own)
                return false;
            Check(own.Inventory == InventoryRef.OfPlayer(_client.Peer) && own.SlotCount == 30 && own.Items.Count == 0,
                $"30 empty slots ({own.SlotCount}, {own.Items.Count} filled)");
            Check(_client.Main.Recipes!.Find("items_test:chest") is { Time: 1f }, "the client loaded the fixture recipe");
            return true;
        });
        Add("Creative gives the client scrap and wood", frame =>
        {
            if (frame == 0)
            {
                Request(InventoryActions.GiveItems, new GiveItemsArgs(Scrap, 9));
                Request(InventoryActions.GiveItems, new GiveItemsArgs(Wood, 2));
            }
            if (_results.Count < 2 || ClientOwn?.Count(Scrap) != 9 || ClientOwn.Count(Wood) != 2)
                return false;
            Check(_results.All(r => r.Ok), $"both gifts land ({string.Join("; ", _results)})");
            Check(_host.Main.Inventory.Items!.PlayerInventory(_client.Peer).Count(Scrap) == 9, "the host holds the client's scrap");
            return true;
        });
        Add("the client places a bench and a chest", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                var me = _client.Main.LocalPlayer!.GlobalPosition;
                var at = new Vector3(Mathf.Snapped(me.X, 0.25f), 0.375f, Mathf.Snapped(me.Z, 0.25f) - 3f);
                Request(BuildActions.PlacePart, new PlacePartArgs(Bench, GridCell.Zero, Position: at.ToNumerics().ToVec3()));
                Request(BuildActions.PlacePart, new PlacePartArgs(Chest, GridCell.Zero, Position: (at + new Vector3(2f, -0.125f, 0)).ToNumerics().ToVec3()));
            }
            if (_results.Count < 2)
                return false;
            Check(_results.All(r => r.Ok), $"both are placed ({string.Join("; ", _results)})");
            _bench = Value(_results[0], "creation");
            _chest = Value(_results[1], "creation");
            return true;
        });
        Wait("they settle", 20);
        Add("the client opens the bench", frame =>
        {
            if (frame == 0)
                _client.Main.Inventory.Open(BenchRef);
            if (Shown(_client, BenchRef) is not { } view)
                return false;
            Check(view.Part == Bench && view.SlotCount == 2 && view.Queue is { Count: 0 } && view.Speed == 2f,
                $"the bench view arrives ({view.Part}, {view.SlotCount} slots, speed {view.Speed})");
            return true;
        });
        Add("the client crafts two chests from data", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Request(InventoryActions.Craft, new CraftArgs(_bench, 1, "items_test:chest", 2));
            }
            if (_results.Count < 1)
                return false;
            var view = Shown(_client, BenchRef);
            if (view?.Count(Chest) != 2 || view.Queue!.Count != 0 || ClientOwn?.Count(Scrap) != 5)
                return false;
            Check(_results[0].Ok, $"the host queued it ({_results[0]})");
            Check(ClientOwn.Count(Wood) == 0, "the inputs left the client's inventory");
            Check(_host.Main.Creations.Find(_bench)!.InventoryOf(1)!.Count(Chest) == 2, "the host's bench holds two chests");
            Check(new[] { "items_test:bench_start", "items_test:bench_work", "items_test:bench_done" }.All(_clientHeard.Contains),
                $"the client hears the bench start, work and finish ({string.Join(", ", _clientHeard)})");
            return true;
        });
        Add("the bench's work loop stops with an empty queue", _ =>
        {
            return _host.Main.Sounds.Loops.Count == 0 && _client.Main.Sounds.Loops.Count == 0
                && _client.Main.Sounds.Player.Voices.All(v => v.Event.QualifiedId != "items_test:bench_work");
        });
        Add("the client sees the queue while it works", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Request(InventoryActions.GiveItems, new GiveItemsArgs(Wood, 1));
                Request(InventoryActions.Craft, new CraftArgs(_bench, 1, "items_test:chest", 1));
            }
            if (_results.Count < 2 || Shown(_client, BenchRef) is not { Queue: [var job] } view)
                return false;
            Check(_results.All(r => r.Ok) && job.Recipe == "items_test:chest" && job.Count == 1 && job.Time == 1f,
                $"the order shows in the queue ({string.Join("; ", _results)})");
            return true;
        });
        Add("the third chest stacks onto the others", _ => Shown(_client, BenchRef) is { Queue.Count: 0 } view && view.Count(Chest) == 3);
        Add("the client takes the chests from the bench", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Request(InventoryActions.MoveItems, new MoveItemsArgs(BenchRef, 0, InventoryRef.OfPlayer(_client.Peer)));
            }
            if (_results.Count < 1 || ClientOwn?.Count(Chest) != 3 || Shown(_client, BenchRef)?.Items.Count != 0)
                return false;
            Check(_results[0].Ok && Value(_results[0], "moved") == 3, $"three chests move ({_results[0]})");
            return true;
        });
        Add("the client stores scrap in the chest", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                _client.Main.Inventory.Close(BenchRef);
                _client.Main.Inventory.Open(ChestRef);
            }
            if (frame == 1)
            {
                var slot = ClientOwn!.Items.First(i => i.Item == Scrap).Slot;
                Request(InventoryActions.MoveItems, new MoveItemsArgs(InventoryRef.OfPlayer(_client.Peer), slot, ChestRef, 2, 2));
            }
            if (_results is [{ Ok: false } refused])
                return Check(false, $"the host refused the move ({refused})");
            if (_results.Count < 1 || Shown(_client, ChestRef)?.At(2) is not { Count: 2 })
                return false;
            Check(_results[0].Ok && ClientOwn?.Count(Scrap) == 1, $"two scrap move into slot 2 ({_results[0]})");
            Check(!_client.Main.Inventory.Shown.ContainsKey(BenchRef), "the closed bench is no longer shown");
            return true;
        });
        Add("the host sees the same chest", frame =>
        {
            if (frame == 0)
                _host.Main.Inventory.Open(ChestRef);
            if (Shown(_host, ChestRef) is not { } view)
                return false;
            Check(view.At(2) == new SlotView(2, Scrap, 2), "the host's view matches");
            return true;
        });
        Add("a chest holding items cannot be picked up", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Request(BuildActions.RemovePart, new RemovePartArgs(_chest, 1));
            }
            if (_results.Count < 1)
                return false;
            Check(!_results[0].Ok && _results[0].Message == "Empty the Test Chest before picking it up.", $"the host refuses ({_results[0]})");
            return true;
        });
        Add("another player cannot use the client's inventory", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                _host.Main.Actions.Request(InventoryActions.MoveItems,
                    new MoveItemsArgs(InventoryRef.OfPlayer(_client.Peer), 0, InventoryRef.OfPlayer(_host.Peer)), _results.Add);
            }
            if (_results.Count < 1)
                return false;
            Check(_results[0].Message == "You can only use your own inventory.", $"the host player is refused ({_results[0]})");
            return true;
        });
        Add("a late joiner gets the chest's contents with the Creation", frame =>
        {
            if (frame == 0)
                _late = _harness.StartClient("Late", mods: _mods);
            if (_late.Main.Creations.Find(_chest) is not { } chest)
                return false;
            Check(chest.InventoryOf(1)?.Count(Scrap) == 2, "the late joiner's chest holds two scrap");
            return true;
        });
    }

    private static InventoryView? Shown(Instance who, InventoryRef where) => who.Main.Inventory.Shown.GetValueOrDefault(where);

    private void Request<TArgs>(string action, TArgs args) => _client.Main.Actions.Request(action, args, _results.Add);

    private static int Value(ActionResult result, string name) => result.Value!.Value.GetProperty(name).GetInt32();
}
