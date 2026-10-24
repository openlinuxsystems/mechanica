using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Items;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Consumables batch C4, host plus a client in Survival on flat ground. The host puts a Field Kitchen beside the client.
/// The client opens it: its station screen lists the two burgers, as any station's. It cooks a Garden Burger (4 Carrots,
/// 2 Tomatoes, 10 Potatoes) and a Colony Burger (1 Grazer Steak, 5 Tomatoes, 10 Potatoes): the ingredients leave its
/// inventory, the kitchen works on both machines, and once done the burgers are in the kitchen. It takes them out and eats
/// a Colony Burger, which heals 30; a potato in hand eats nothing. Both burgers have their bitten models in hand.
/// </summary>
public partial class KitchenTest : StagedTest
{
    private const string Garden = "vanilla:garden_burger";
    private const string Colony = "vanilla:colony_burger";
    private const string Potato = "vanilla:potato";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private InventoryScreen _screen = null!;
    private UpgradeDialog _upgrades = null!;
    private int _kitchen;
    private float _health;
    private Dictionary<string, int> _before = [];

    protected override string Tag => "kitchen";

    protected override int StageTimeoutFrames => 900;

    private SurvivalActions HostSurvival => _host.Main.Survival.Actions!;

    private int Peer => _client.Peer;

    private InventoryRef KitchenRef => InventoryRef.OfPart(_kitchen, 1);

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world", _ =>
            _client.Main.Survival.Own is { Dead: false } && _client.Main.Inventory.Own is not null && _client.Main.Tools is not null
            && _client.Main.LocalPlayer is { } player && player.IsOnFloor() && _host.Main.PlayerSync.Players.ContainsKey(Peer));
        Stage("the host puts a Field Kitchen beside the client", frame =>
        {
            if (frame == 0)
                Spawn("vanilla:field_kitchen", Feet(_client) + new Vector3(-1.5f, 0.6f, 0), id => _kitchen = id);
            return frame > 30 && _kitchen != 0 && _client.Main.Creations.Find(_kitchen) is not null;
        });
        Stage("the client opens the Field Kitchen: its station screen lists the two burgers", frame =>
        {
            if (frame == 0)
            {
                var pockets = _host.Main.Inventory.Items!.PlayerInventory(Peer);
                pockets.Add("vanilla:carrot", 4);
                pockets.Add("vanilla:tomato", 7);
                pockets.Add(Potato, 21);
                pockets.Add("vanilla:grazer_steak", 1);
                _upgrades = new UpgradeDialog
                {
                    Name = "UpgradeDialog", Inventory = _client.Main.Inventory, Actions = _client.Main.Actions, Creations = _client.Main.Creations,
                    Catalog = _client.Main.Build.Catalog, OtherScreenOpen = () => _screen.IsOpen,
                };
                _screen = new InventoryScreen
                {
                    Name = "InventoryScreen", Inventory = _client.Main.Inventory, Actions = _client.Main.Actions, Players = _client.Main.PlayerSync,
                    Catalog = _client.Main.Build.Catalog, Mode = () => GameMode.Survival, Creations = _client.Main.Creations, Upgrades = _upgrades,
                };
                AddChild(_screen);
                AddChild(_upgrades);
                return false;
            }
            if (frame == 1)
                _screen.Show(KitchenRef);
            if (!_screen.StationMode || Shown(_client, KitchenRef) is not { } view || Count(Potato) < 21 || frame < 10)
                return false;
            Check(view.Part == "vanilla:field_kitchen" && view.Queue is { Count: 0 } && view.SlotCount == 6,
                $"the Field Kitchen's view arrives ({view.Part}, {view.SlotCount} slots)");
            Check(_screen.ListedRecipes.SequenceEqual([Garden, Colony]), $"it lists the burgers ({string.Join(", ", _screen.ListedRecipes)})");
            Check(Clip(_client, _kitchen) is null or "" or "idle_loop", $"it idles ({Clip(_client, _kitchen)})");
            return true;
        });
        Stage("the client cooks both burgers: the ingredients go in and the kitchen works on both machines", frame =>
        {
            if (frame == 0)
            {
                _before = new[] { "vanilla:carrot", "vanilla:tomato", Potato, "vanilla:grazer_steak" }.ToDictionary(i => i, Count);
                Request(InventoryActions.Craft, new CraftArgs(_kitchen, 1, Garden, 1));
                Request(InventoryActions.Craft, new CraftArgs(_kitchen, 1, Colony, 1));
            }
            if (_results.Count < 2)
                return false;
            if (!_results.All(r => r.Ok))
                return Check(false, $"the host queues them ({string.Join("; ", _results)})");
            if (Shown(_client, KitchenRef) is not { Queue: [var first, var second] } || Clip(_client, _kitchen)?.Contains("work_loop") != true)
                return false;
            Check((first.Recipe, first.Time, second.Recipe, second.Time) == (Garden, 10f, Colony, 13f),
                $"the orders show in its queue ({first.Recipe} {first.Time} s, {second.Recipe} {second.Time} s)");
            var used = _before.ToDictionary(p => p.Key, p => p.Value - Count(p.Key));
            Check(used["vanilla:carrot"] == 4 && used["vanilla:tomato"] == 7 && used[Potato] == 20 && used["vanilla:grazer_steak"] == 1,
                $"the ingredients left the client's inventory ({string.Join(", ", used.Select(p => $"{p.Key} {p.Value}"))})");
            Check(Clip(_host, _kitchen)?.Contains("work_loop") == true, $"the host's kitchen works too ({Clip(_host, _kitchen)})");
            return true;
        });
        Stage("23 s on, the kitchen holds both burgers, on the client too", frame =>
        {
            if (frame == 0)
                _host.Main.Inventory.Items!.Tick(24f);
            if (Shown(_client, KitchenRef) is not { Queue.Count: 0 } view || view.Count(Garden) != 1 || view.Count(Colony) != 1)
                return false;
            var held = _host.Main.Creations.Find(_kitchen)!.InventoryOf(1)!;
            Check(held.Count(Garden) == 1 && held.Count(Colony) == 1, "the host's kitchen holds them");
            Check(Clip(_client, _kitchen)?.StartsWith("complete", StringComparison.Ordinal) == true, $"it plays its complete clip ({Clip(_client, _kitchen)})");
            return true;
        });
        Stage("the client takes the burgers out", frame =>
        {
            if (frame == 0)
            {
                var slots = _host.Main.Creations.Find(_kitchen)!.InventoryOf(1)!.Slots;
                foreach (var slot in Enumerable.Range(0, slots.Count).Where(i => slots[i] is not null).Reverse())
                    Request(InventoryActions.MoveItems, new MoveItemsArgs(KitchenRef, slot, InventoryRef.OfPlayer(Peer)));
            }
            return Count(Garden) == 1 && Count(Colony) == 1;
        });
        Stage("a potato in hand eats nothing", frame =>
        {
            var tools = _client.Main.Tools!;
            if (frame == 0)
            {
                HostSurvival.Damage(Peer, 50, "test");
                return false;
            }
            if (frame == 10)
            {
                _health = _client.Main.Survival.Own!.Health;
                tools.Item.Choose(Potato);
                if (tools.Current != tools.Item)
                    tools.Use(tools.Item);
            }
            if (frame == 20)
                tools.Item.Click(place: false);
            if (frame < 80)
                return false;
            return Check(Count(Potato) == _before[Potato] - 20 && _client.Main.Survival.Own!.Health == _health, $"the potato is not eaten (health {_client.Main.Survival.Own!.Health})");
        });
        Stage("eating the Colony Burger heals 30", frame =>
        {
            var tools = _client.Main.Tools!;
            if (frame == 0)
            {
                tools.Item.Choose(Colony);
                if (tools.Current != tools.Item)
                    tools.Use(tools.Item);
                return false;
            }
            if (frame == 30)
            {
                _health = _client.Main.Survival.Own!.Health;
                tools.Item.Click(place: false);
            }
            if (frame < 150)
                return false;
            return Check(_client.Main.Survival.Own!.Health == _health + 30 && Count(Colony) == 0,
                $"one Colony Burger is eaten and heals 30 ({_health} -> {_client.Main.Survival.Own!.Health}, {Count(Colony)} left)");
        });
        Add("both burgers have their whole and two bitten versions in hand", _ =>
        {
            var survival = _client.Main.Survival;
            foreach (var id in new[] { Garden, Colony })
            {
                var held = survival.Catalog.FindDefinition(id)?.Held?.Mesh is { } mesh ? survival.Assets!.InstantiateScene(mesh, "vanilla") : null;
                Check(held is not null && Enumerable.Range(0, 3).All(k => held.FindChild($"eat_{k}", true, false) is Node3D),
                    $"{id} in hand has eat_0 to eat_2");
                held?.Free();
            }
            return true;
        });
    }

    private void Stage(string name, Func<int, bool> tick) => Add(name, frame =>
    {
        if (frame == 0)
            _results.Clear();
        return tick(frame);
    });

    private void Spawn(string part, Vector3 at, Action<int> created) =>
        _host.Main.Actions.Request(CreationActions.SpawnPart, new SpawnPartArgs(part, [at.X, at.Y, at.Z]), r =>
        {
            Check(r.Ok, $"{part} spawns ({r})");
            if (r.Ok)
                created(r.Value!.Value.GetInt32());
        });

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    // The clip a part's model shows on a machine (its display's clip readout), or null before it has one.
    private static string? Clip(Instance instance, int creation) =>
        instance.Main.Creations.NodeOf(creation)?.Displays.FirstOrDefault(d => d.Part == 1)?.Clip;

    private static InventoryView? Shown(Instance who, InventoryRef where) => who.Main.Inventory.Shown.GetValueOrDefault(where);

    private int Count(string item) => _client.Main.Inventory.Own?.Items.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;

    private static Vector3 Feet(Instance who) => who.Main.LocalPlayer!.GlobalPosition;
}
