using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Mods;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Loot in the world's containers (POI part markers with <c>loot</c>), host plus a client, in a Survival world of the
/// fixture mod <c>loot_yard</c>: a POI where players start, with a Locker filled once and a Ruin Chest that refills a
/// day (600 s there) after it is emptied. The host fills both when it places them; the client opens them and sees the
/// same items, then takes everything. Time passes: the Ruin Chest fills again with a new roll, which the client sees, and
/// the Locker stays empty. A save keeps the fills, and loading it fills nothing again.
/// </summary>
public partial class WorldLootTest : StagedTest
{
    private const string Locker = "vanilla:locker";
    private const string RuinChest = "vanilla:ruin_chest";

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private IReadOnlyList<WorldPartSpot> _spots = [];
    private int _locker;
    private int _chest;
    private List<ItemStack> _firstChest = [];
    private readonly List<ActionResult> _moves = [];
    private int _movesSent;
    private double _emptiedAt;
    private int _refilledFrame = -1;

    protected override string Tag => "worldloot";

    protected override int StageTimeoutFrames => 1200;

    private static InventoryRef Of(int creation) => InventoryRef.OfPart(creation, 1);

    private Inventory HostInventory(int creation) => _host.Main.Creations.Find(creation)!.InventoryOf(1)!;

    private static List<ItemStack> Stacks(Inventory inventory) => [.. inventory.Slots.OfType<ItemStack>()];

    private InventoryView? ClientView(int creation) => _client.Main.Inventory.Shown.GetValueOrDefault(Of(creation));

    // True when the client's view of a container holds exactly what the host's does.
    private bool ClientSees(int creation) =>
        ClientView(creation) is { } view
        && view.Items.Select(i => (i.Slot, i.Item, i.Count)).SequenceEqual(
            HostInventory(creation).Slots.Select((s, i) => (s, i)).Where(x => x.s is not null).Select(x => (x.i, x.s!.Item, x.s.Count)));

    public override void _Ready()
    {
        var mods = ModLoader.Load([Path.Combine(ModPaths.ModsRoot, "vanilla"), ProjectSettings.GlobalizePath("res://tests/worldloot/mods/loot_yard")]);
        Check(!mods.HasErrors, $"vanilla and the loot yard load ({string.Join("; ", mods.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))})");
        var world = WorldGenData.Load(mods);
        Check(world.Pois.Any(p => p.QualifiedId == "loot_yard:loot_yard"), "the loot yard POI loads");
        Check(!world.Diagnostics.Any(d => d.File.Contains("loot_yard", StringComparison.Ordinal)),
            $"with no problems ({string.Join("; ", world.Diagnostics.Where(d => d.File.Contains("loot_yard", StringComparison.Ordinal)))})");
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = world };
        AddChild(_harness);
        _host = _harness.StartHost(mods: mods);
        _client = _harness.StartClient("Client", mods: mods);

        Add("the client joins a Survival world at the loot yard", _ =>
            _client.Main.Survival.Own is not null && _client.Main.LocalPlayer is { } player && player.IsOnFloor() && _host.Main.Inventory.Items is not null);
        Add("the host placed both containers, filled from their tables", _ =>
        {
            _spots = WorldParts.SpotsOf(_host.Main.Streamer.Generator);
            Check(_spots.Count == 2, $"two part markers ({_spots.Count})");
            var owned = _host.Main.Creations.All.Where(c => c.IsWorldOwned).ToList();
            Check(owned.Count == 2, $"two world-owned containers ({owned.Count})");
            var locker = owned.Single(c => c.Parts.First().Part == Locker);
            var chest = owned.Single(c => c.Parts.First().Part == RuinChest);
            _locker = locker.Id;
            _chest = chest.Id;
            var tables = _host.Main.Streamer.Generator.Data.Definition.LootTables!;
            foreach (var (creation, table) in new[] { (locker, "locker"), (chest, "ruin_chest") })
            {
                var items = Stacks(creation.InventoryOf(1)!);
                Check(items.Count is >= 2 and <= 4, $"{table}: 2 to 4 draws ({items.Count})");
                var allowed = tables[table].Items!.Select(e => Identifiers.Qualify(e.Item!)).ToHashSet();
                Check(items.All(s => allowed.Contains(s.Item)), $"{table}: only items of its table ({string.Join(", ", items)})");
                Check(creation.WorldLoot is { Fills: 1, EmptiedAt: null }, $"{table}: filled once");
            }
            _firstChest = Stacks(chest.InventoryOf(1)!);
            return true;
        });
        Add("the client opens both and sees what the host put in them", frame =>
        {
            if (frame == 0)
            {
                _client.Main.Inventory.Open(Of(_locker));
                _client.Main.Inventory.Open(Of(_chest));
                return false;
            }
            if (!ClientSees(_locker) || !ClientSees(_chest))
                return false;
            Check(_client.Main.Creations.Find(_locker) is { IsWorldOwned: true }, "the client shows the Locker as the world's");
            return true;
        });
        Add("the client takes everything out", frame =>
        {
            var full = new[] { _locker, _chest }.SelectMany(id => HostInventory(id).Slots.Select((s, i) => (id, s, i)).Where(x => x.s is not null)).ToList();
            if (frame == 0)
            {
                foreach (var (id, _, slot) in full)
                    _client.Main.Actions.Request(InventoryActions.MoveItems, new MoveItemsArgs(Of(id), slot, InventoryRef.OfPlayer(_client.Peer)), _moves.Add);
                _movesSent = full.Count;
                return false;
            }
            if (_moves.Count < _movesSent)
                return false;
            if (_moves.Any(m => !m.Ok))
                Check(false, $"every stack moves to the client ({string.Join("; ", _moves.Where(m => !m.Ok))})");
            if (!HostInventory(_locker).IsEmpty || !HostInventory(_chest).IsEmpty)
                return false;
            return ClientView(_locker) is { Items.Count: 0 } && ClientView(_chest) is { Items.Count: 0 };
        });
        Add("the host notes when the Ruin Chest was emptied, and only that one waits to refill", frame =>
        {
            var chest = _host.Main.Creations.Find(_chest)!;
            if (chest.WorldLoot?.EmptiedAt is not { } emptied)
                return false;
            _emptiedAt = emptied;
            // It stays empty until its day has passed.
            if (frame < 120)
                return false;
            Check(HostInventory(_chest).IsEmpty, "the Ruin Chest stays empty while it waits");
            Check(_host.Main.Creations.Find(_locker)!.WorldLoot is { Fills: 1, EmptiedAt: null }, "the Locker never refills, so it waits for nothing");
            return true;
        });
        Add("a day of world time later, the Ruin Chest fills again and the client sees it; the Locker stays empty", frame =>
        {
            if (frame == 0)
            {
                Check(HostInventory(_chest).IsEmpty, "still empty before the day is up");
                var day = _host.Main.Streamer.Generator.Data.Definition.DayLength;
                _host.Main.Scripts.Runtime.Time = _emptiedAt + day + 1;
                return false;
            }
            if (_refilledFrame < 0)
            {
                if (HostInventory(_chest).IsEmpty)
                    return false;
                _refilledFrame = frame;
                var chest = _host.Main.Creations.Find(_chest)!;
                Check(chest.WorldLoot is { Fills: 2, EmptiedAt: null }, $"its second fill ({chest.WorldLoot?.Fills})");
                var refill = Stacks(HostInventory(_chest));
                Check(refill.Count is >= 2 and <= 4, $"2 to 4 draws again ({string.Join(", ", refill)})");
                GD.Print($"[worldloot] first fill {string.Join(", ", _firstChest)}; refill {string.Join(", ", refill)}");
            }
            // Give the Locker a few more of the host's looks (one a second) to show it does not refill.
            if (!ClientSees(_chest) || frame < _refilledFrame + 180)
                return false;
            Check(HostInventory(_locker).IsEmpty && ClientView(_locker) is { Items.Count: 0 }, "the Locker stays empty for everyone");
            return true;
        });
        Add("a save keeps the fills, and loading it fills nothing again", _ =>
        {
            var save = _host.Main.Saves.Capture("worldloot-test");
            var diagnostics = new List<DataDiagnostic>();
            var loaded = save.ReadCreations(_host.Main.Build.Catalog, save.Name, diagnostics);
            Check(!diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), $"the save reads ({string.Join("; ", diagnostics)})");
            var locker = loaded.Single(c => c.Id == _locker);
            var chest = loaded.Single(c => c.Id == _chest);
            Check(locker.WorldLoot is { Fills: 1 } && locker.InventoryOf(1)!.IsEmpty, "the looted Locker is saved empty, filled once");
            Check(chest.WorldLoot is { Fills: 2 } && Stacks(chest.InventoryOf(1)!).SequenceEqual(Stacks(HostInventory(_chest))), "the Ruin Chest is saved with its refill");
            var plan = WorldParts.Reconcile(_spots, loaded, _host.Main.Build.Catalog, () => 100000, save.Destroyed.Contains);
            Check(plan.Add.Count == 0 && plan.Remove.Count == 0 && locker.InventoryOf(1)!.IsEmpty, "loading it places nothing new and the Locker stays empty");
            return true;
        });
    }
}
