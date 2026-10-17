using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Survival;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// UI spec §9 and §10 survival interactions and weapons, host plus client, on a flat world with trees: the client's
/// Potato Blaster comes loaded and shows its rounds and the potatoes carried, aims with right click (zoom, host knows),
/// fires, reloads with R (the ring fills), places a carrot with Force Build where it aims, picks it up with right click,
/// and holds Use on a tree to refine it into the inventory (letting go early cancels). Each step is checked on both machines.
/// </summary>
public partial class WeaponsTest : StagedTest
{
    private const string Blaster = "vanilla:potato_blaster";
    private const string Potato = "vanilla:potato";
    private const string Carrot = "vanilla:carrot";
    private const string Wood = "vanilla:scrap_wood";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private SurvivalHud _hud = null!;
    private bool _holding;
    private int _placed;
    private float? _midway;
    private int _carrots;
    private WorldObjectPlacement? _tree;

    protected override string Tag => "weapons";

    private int Peer => _client.Peer;

    private Building.ItemTool Item => _client.Main.Tools!.Item;

    // A flat world with a tree every 12 m, so one stands near the spawn.
    private static WorldGenData Trees => WorldGenData.FromDefinition(new WorldGenDefinition
    {
        Terrain = new TerrainDefinition { CoastWidth = 0f, CoastBiomeWidth = 0f, SeaLevel = -100f, SeaFloor = -100f },
        Biomes = [new BiomeDefinition { Id = "flat", Name = "Flat", Weight = 1f, Color = "#5c7350", Scatter = [new ScatterRule { Object = "tree", Spacing = 12, Chance = 1 }] }],
        Objects =
        [
            new WorldObjectDefinition
            {
                Id = "tree",
                Collider = new ShapeDefinition { Shape = PrimitiveShape.Cylinder, Radius = 0.35f, Height = 9, At = new Vec3(0, 4.5f, 0) },
                Pieces = [new ShapeDefinition { Shape = PrimitiveShape.Cylinder, Radius = 0.35f, Height = 9, At = new Vec3(0, 4.5f, 0), Color = "#6b4a2e" }],
                Footprint = 1.5f,
                Health = 60,
                Drops = [new ObjectDrop { Item = "scrap_wood", Count = 6 }],
                Refine = true,
            },
        ],
    });

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = Trees };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Stage("the client joins with a Potato Blaster and potatoes", _ =>
        {
            if (_client.Main.Survival.Own is null || _client.Main.Inventory.Own is null || _client.Main.Tools is null
                || _client.Main.LocalPlayer is not { } player || !player.IsOnFloor())
                return false;
            var pockets = _host.Main.Inventory.Items!.PlayerInventory(Peer);
            pockets.Add(Blaster, 1);
            pockets.Add(Potato, 20);
            _hud = new SurvivalHud
            {
                Name = "TestSurvivalHud", Survival = _client.Main.Survival, Actions = _client.Main.Actions, Players = _client.Main.PlayerSync,
                Catalog = _client.Main.Survival.Catalog, UseHeld = () => _holding, Playing = () => true,
            };
            _client.Main.AddChild(_hud);
            return true;
        });
        Stage("the blaster in hand comes loaded and shows its ammo", frame =>
        {
            // The blaster goes in the first empty hotbar slot; picking that slot puts it in hand.
            var tools = _client.Main.Tools!;
            if (!Item.Active && tools.Slots.ToList().IndexOf(Blaster) is >= 0 and var slot)
                tools.Select(slot);
            if (!Item.Active || _client.Main.Survival.CounterOf(Blaster) is not { } view)
                return false;
            Check(view is { Value: 15, Max: 15, Ammo: Potato }, $"15 of 15 loaded, drawing potatoes ({view})");
            Check(_host.Main.Survival.CounterOf(Blaster) is null, "only the holder is sent the counter");
            Check(Item.CounterFor(Blaster)?.Text == "15 / 15", $"the hotbar slot shows the rounds ({Item.CounterFor(Blaster)})");
            Check(_client.Main.Tools!.AmmoText() == "15 / 15   Potato 20", $"the HUD counter shows rounds and potatoes ({_client.Main.Tools.AmmoText()})");
            return true;
        });
        Stage("right click aims: the view zooms and the host knows", frame =>
        {
            if (frame == 0)
                Check(Item.Aim(true), "the blaster aims");
            if (!_host.Main.Scripts.Runtime.IsAiming(Peer))
                return false;
            Check(Item.Aiming && Mathf.IsEqualApprox(_client.Main.LocalPlayer!.AimZoom, 0.8f), $"aiming zooms to the blaster's 0.8 ({_client.Main.LocalPlayer!.AimZoom})");
            Item.Release();
            return true;
        });
        Stage("letting go stops the aim", _ =>
        {
            if (_host.Main.Scripts.Runtime.IsAiming(Peer))
                return false;
            Check(!Item.Aiming && _client.Main.LocalPlayer!.AimZoom == 1f, "the view is back to normal");
            return true;
        });
        Stage("two shots take two rounds and two potatoes", frame =>
        {
            if (frame is 0 or 30)
                Item.Use(_results.Add);
            if (_results.Count < 2 || _client.Main.Survival.CounterOf(Blaster) is not { Value: 13 } || Count(Potato) != 18)
                return false;
            Check(_results.All(r => r.Ok), $"both shots fire ({string.Join("; ", _results)})");
            Check(_client.Main.Tools!.AmmoText() == "13 / 15   Potato 18", $"the counter follows ({_client.Main.Tools.AmmoText()})");
            return true;
        });
        Stage("R reloads the magazine and the ring fills while it does", frame =>
        {
            if (frame == 0)
                Check(Item.Reload(_results.Add), "a reload is asked for");
            if (_client.Main.Survival.CounterOf(Blaster) is not { Value: 15, Busy: > 0 })
                return false;
            Check(_results.Count == 1 && _results[0].Ok, $"the reload is accepted ({string.Join("; ", _results)})");
            Check(Item.Progress is >= 0 and < 1, $"the ring fills ({Item.Progress})");
            Check(_client.Main.Tools!.AmmoText()!.Contains("Reloading"), $"the HUD says it is reloading ({_client.Main.Tools.AmmoText()})");
            return true;
        });
        Stage("the reload finishes", _ => Item.Progress is null && !_client.Main.Tools!.AmmoText()!.Contains("Reloading"));
        Stage("Force Build places a carrot where the client aims, and it stays", frame =>
        {
            if (frame == 0)
                LookAt(_client, Feet(_client) + Forward(_client) * 2f);
            if (frame == 20)
            {
                _carrots = Count(Carrot);
                Item.Choose(Carrot);
                Check(Item.PlaceOne(_results.Add), "there is ground in reach to place it on");
            }
            if (_results.Count == 0 || _client.Main.Survival.Drops.Values.FirstOrDefault(d => d.Placed) is not { } drop || Count(Carrot) != _carrots - 1)
                return false;
            Check(_results[0].Ok, $"one carrot is placed ({_results[0]})");
            Check(drop.Position.ToGodot().DistanceTo(Feet(_client) + Forward(_client) * 2f) < 0.3f, $"where the client aimed ({drop.Position})");
            Check(_host.Main.Survival.Drops[drop.Id].Placed, "the host keeps it as placed");
            _placed = drop.Id;
            return true;
        });
        Stage("right click on the carrot picks it up", frame =>
        {
            if (frame == 0)
            {
                var drop = _client.Main.Survival.Drops[_placed];
                LookAt(_client, drop.Position.ToGodot() + new Vector3(0, DropAim.Middle, 0));
            }
            if (frame == 20)
            {
                Check(_hud.AimedDrop()?.Id == _placed, "the crosshair is on the carrot");
                Check(_hud.PickUpAimed(), "it is picked up");
            }
            return frame > 20 && Count(Carrot) == _carrots && !_client.Main.Survival.Drops.ContainsKey(_placed) && !_host.Main.Survival.Drops.ContainsKey(_placed);
        });
        Stage("a tree under the crosshair is outlined and offers a refine", frame =>
        {
            if (frame == 0)
            {
                var feet = Feet(_client).ToNumerics();
                _tree = _host.Main.Streamer.Generator.ObjectsIn(feet.X - 40, feet.Z - 40, feet.X + 40, feet.Z + 40)
                    .OrderBy(o => System.Numerics.Vector3.Distance(o.Position, feet)).First();
                var trunk = _tree.Position.ToGodot();
                _host.Main.PlayerSync.HostTeleport(Peer, trunk + new Vector3(1.4f, 0, 0));
            }
            if (frame == 30)
                LookAt(_client, _tree!.Position.ToGodot() + new Vector3(0, 1.4f, 0));
            if (frame < 45)
                return false;
            if (_hud.AimedNode()?.Id != _tree!.Id)
                return false;
            Check(_hud.Outlined == _tree.Id, "the tree has the blue outline");
            Check(_hud.Interaction()?.Label == "hold to refine the tree", $"Use offers to refine it ({_hud.Interaction()?.Label})");
            return true;
        });
        Stage("letting go of Use early cancels the refine", frame =>
        {
            if (frame == 0)
            {
                _holding = true;
                Check(_hud.Interact(), "Use starts the refine");
            }
            if (frame == 20)
            {
                Check(_hud.RefineProgress is > 0 and < 1, $"the ring fills ({_hud.RefineProgress})");
                _holding = false;
            }
            return frame > 30 && _hud.Refining is null && _host.Main.Survival.Actions!.RefiningOf(Peer) is null
                && !_host.Main.Streamer.State.IsDestroyed(_tree!.Id);
        });
        Stage("holding Use refines the tree into the inventory", frame =>
        {
            if (frame == 0)
            {
                _holding = true;
                _hud.Interact();
            }
            if (frame == 40)
                _midway = _hud.RefineProgress;
            if (Count(Wood) < 6 || !_client.Main.Streamer.State.IsDestroyed(_tree!.Id))
                return false;
            Check(_midway is > 0.3f and < 1, $"the ring was part full half way ({_midway})");
            Check(frame >= 60, $"it took the 1.2 s hold ({frame} frames)");
            Check(_hud.Refining is null && _hud.RefineProgress is null, "the ring is gone");
            Check(_host.Main.Streamer.State.IsDestroyed(_tree.Id), "the tree is gone on the host too");
            _holding = false;
            return true;
        });
    }

    private void Stage(string name, Func<int, bool> tick) => Add(name, frame =>
    {
        if (frame == 0)
            _results.Clear();
        return tick(frame);
    });

    private int Count(string item) => _client.Main.Inventory.Own?.Items.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;

    private static Vector3 Feet(Instance who) => who.Main.LocalPlayer!.GlobalPosition;

    private static Vector3 Forward(Instance who) => -who.Main.LocalPlayer!.GlobalBasis.Z with { Y = 0 };

    private static void LookAt(Instance who, Vector3 target)
    {
        var d = (target - who.Main.LocalPlayer!.Head.GlobalPosition).Normalized();
        who.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Asin(d.Y) };
    }
}
