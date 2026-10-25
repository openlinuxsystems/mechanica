using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Scripting;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Pigment Flower (crops map, batch K2), host plus client, on the vanilla world. The client is taken to the nearest
/// wild Pigment Flower near the spawn and picks it with Use: one Pigment Flower and sometimes a seed, the clump gone on
/// both machines until it flowers again half an hour of world time later. Then the client plants a Pigment Flower Seed in a
/// soil mound beside it and waters it once; the plant (stake and tag, grow, wilt and sway clips) grows half way on the
/// host's clock, then ripe its heads open, and harvested it gives two Pigment Flowers and exactly one seed back.
/// Batch K3: in the same mound the client grows a Banana Seed (watered once, ripe after 1260 s on the host's clock, its bunch
/// yellow) and harvests two bananas and one to three seeds; eating a banana heals 35, and a chili eaten at full health
/// hurts 50 on the host and the client sees the drop. Beetroot, Banana and Chili each have their bitten models and a plant
/// that grows, wilts and sways.
/// Batch K4: in the same mound the client grows a thirsty Broccoli Seed (seed.thirst 2): one bucket carries it only half way
/// in 1260 s on the host's clock, the soil dry; a second bucket ripens it, and it gives two broccoli and one to three seeds.
/// Eating an orange heals 45. Blueberry, Orange, Broccoli and Pineapple each have their bitten models and a plant with its
/// stake that grows, wilts and sways.
/// </summary>
public partial class CropsTest : StagedTest
{
    private const string Flower = "vanilla:pigment_flower";
    private const string Seed = "vanilla:pigment_flower_seed";
    private const string Bucket = "vanilla:water_bucket_full";
    private const string Soil = "vanilla:soil";
    private const string Banana = "vanilla:banana";
    private const string BananaSeed = "vanilla:banana_seed";
    private const string Chili = "vanilla:chili";
    private const string Broccoli = "vanilla:broccoli";
    private const string BroccoliSeed = "vanilla:broccoli_seed";
    private const string Orange = "vanilla:orange";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private WorldObjectPlacement _wild = null!;
    private Vector3 _soilAt;
    private int _soil;
    private int _soilPart;
    private int _seedsPicked;
    private float _health;

    protected override string Tag => "crops";

    protected override int StageTimeoutFrames => 1500;

    private int Peer => _client.Peer;

    private SoilState HostSoil => _host.Main.Creations.Find(_soil)!.SoilOf(_soilPart)!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins and is taken to the nearest wild Pigment Flower", frame =>
        {
            if (_client.Main.Survival.Own is null || _client.Main.Inventory.Own is null || _client.Main.LocalPlayer is not { } player
                || !_host.Main.PlayerSync.Players.ContainsKey(Peer))
                return false;
            if (_wild is null)
            {
                var gen = _host.Main.Streamer.Generator;
                var home = gen.Spawn ?? player.GlobalPosition.ToNumerics();
                for (var r = 200f; r <= 3200f && _wild is null; r *= 2)
                {
                    _wild = gen.ObjectsIn(home.X - r, home.Z - r, home.X + r, home.Z + r)
                        .Where(o => o.Definition.Id == "wild_pigment_flower" && gen.SlopeAt(o.Position.X, o.Position.Z) < 0.25f)
                        .OrderBy(o => (o.Position - home).Length())
                        .FirstOrDefault()!;
                }
                if (!Check(_wild is not null, "a wild Pigment Flower grows within 3 km of the spawn"))
                    return true;
                // Scattered by the meadow and forest rules (CropsTests); near a biome's edge the ground under it can be the next one.
                GD.Print($"[crops] wild Pigment Flower {_wild!.Id} at {_wild.Position} ({gen.BiomeAt(_wild.Position.X, _wild.Position.Z).Id})");
                var at = _wild.Position.ToGodot() + new Vector3(1.5f, 0, 0);
                _host.Main.PlayerSync.HostTeleport(Peer, at with { Y = gen.HeightAt(at.X, at.Z) + 1.2f });
                return false;
            }
            return frame > 60 && player.IsOnFloor() && player.GlobalPosition.DistanceTo(_wild.Position.ToGodot()) < 3f
                && _client.Main.Streamer.ChunkHolding(_wild.Id) is { } chunk && chunk.LiveObjects.Any(o => o.Id == _wild.Id);
        });
        Add("picking it gives a Pigment Flower, sometimes a seed, and it is gone on both machines", frame =>
        {
            if (frame == 0)
                _client.Main.Actions.Request(SurvivalActions.Harvest, new HarvestArgs(_wild.Id.ToString()), _results.Add);
            // The inventory and the world sync separately: wait for both.
            if (_results.Count == 0 || !_client.Main.Streamer.State.IsDestroyed(_wild.Id) || (_results[0].Ok && Count(Flower) == 0))
                return false;
            Check(_results[0].Ok, $"the pick is allowed ({_results[0].Message})");
            Check(Count(Flower) == 1, $"the client has one Pigment Flower ({Count(Flower)})");
            _seedsPicked = Count(Seed);
            Check(_seedsPicked is 0 or 1, $"and no seed or one ({_seedsPicked})");
            Check(_host.Main.Streamer.State.IsDestroyed(_wild.Id), "the host has it gone");
            Check(_host.Main.Streamer.State.Regrowing.ContainsKey(_wild.Id), "the host will regrow it");
            Check(!_client.Main.Streamer.ChunkHolding(_wild.Id)!.LiveObjects.Any(o => o.Id == _wild.Id), "the client no longer draws it");
            return true;
        });
        Add("half an hour of world time later it flowers again on both machines", frame =>
        {
            if (frame == 0)
                _host.Main.Scripts.Runtime.Time += 1801;
            if (_host.Main.Streamer.State.IsDestroyed(_wild.Id) || _client.Main.Streamer.State.IsDestroyed(_wild.Id))
                return false;
            return Check(_client.Main.Streamer.ChunkHolding(_wild.Id)!.LiveObjects.Any(o => o.Id == _wild.Id), "the client draws it again");
        });
        Add("the client places soil and plants a Pigment Flower Seed in it", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                var pockets = _host.Main.Inventory.Items!.PlayerInventory(Peer);
                if (_seedsPicked == 0)
                    pockets.Add(Seed, 1);
                pockets.Add(Bucket, 1);
                return false;
            }
            if (frame == 30)
            {
                // Two metres from the player, on ground clear of the flower and anything else growing there.
                var gen = _host.Main.Streamer.Generator;
                var feet = _client.Main.LocalPlayer!.GlobalPosition;
                var ahead = Enumerable.Range(0, 16).Select(k => feet + new Vector3(Mathf.Sin(k * Mathf.Tau / 16), 0, -Mathf.Cos(k * Mathf.Tau / 16)) * 2f)
                    .First(at => !gen.ObjectsIn(at.X - 1.5f, at.Z - 1.5f, at.X + 1.5f, at.Z + 1.5f).Any() && gen.SlopeAt(at.X, at.Z) < 0.25f);
                // Set on the highest corner of its footprint, so the mound clears the ground.
                var top = new[] { (-0.4f, -0.4f), (0.4f, -0.4f), (-0.4f, 0.4f), (0.4f, 0.4f), (0f, 0f) }.Max(d => gen.HeightAt(ahead.X + d.Item1, ahead.Z + d.Item2));
                _soilAt = ahead with { Y = top + 0.14f };
                Request(BuildActions.PlacePart, new PlacePartArgs(Soil, GridCell.Zero, Position: _soilAt.ToNumerics().ToVec3()));
                return false;
            }
            if (_results.Count == 1 && _results[0].Ok && _soil == 0)
            {
                var placed = _results[0].Value!.Value.Deserialize<PlacePartResult>(ActionJson.Options)!;
                (_soil, _soilPart) = (placed.Creation, placed.Part);
                _results.Clear();
                LookAt(_soilAt);
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Seed), Creation: _soil, Part: _soilPart));
                return false;
            }
            if (_results.Count == 1 && !_results[0].Ok)
                return Check(false, $"placing or planting failed ({_results[0]})");
            if (_soil == 0 || !_client.Main.Survival.CropNodes.TryGetValue((_soil, _soilPart), out var crop) || crop.View?.Seed != Seed || crop.Plant is null)
                return false;
            Check(crop.Plant.FindChild("*Stake*", true, false) is not null, "the stake and its tag stand in the soil from planting");
            var player = crop.Plant.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();
            Check(player is not null && new[] { "grow", "wilt", "sway" }.All(c => player.HasAnimation(c)), "the plant has its grow, wilt and sway clips");
            return Check(crop.GrowShown == 0f, $"it starts at the beginning of its grow clip ({crop.GrowShown})");
        });
        Add("watered once, it grows half way on the host's clock", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                var aim = (_soilAt - _client.Main.LocalPlayer!.Head.GlobalPosition).Normalized();
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Bucket), new Vec3(aim.X, aim.Y, aim.Z)));
            }
            if (_results.Count == 1 && !_results[0].Ok)
                return Check(false, $"the throw is refused ({_results[0]})");
            if (_results.Count != 1 || HostSoil.Water < 1200f)
                return false;
            _host.Main.Scripts.Runtime.Time += 630; // half its 1260 s
            return true;
        });
        Add("half grown on both machines", _ =>
        {
            if (HostSoil.Stage < 2 || _client.Main.Survival.CropNodes[(_soil, _soilPart)].View?.Stage < 2)
                return false;
            return Check(HostSoil.Stage == 2 && !HostSoil.Ripe, $"half grown, not ripe ({HostSoil.Stage})");
        });
        Add("ripe, its heads open", frame =>
        {
            if (frame == 0)
                _host.Main.Scripts.Runtime.Time += 640;
            var crop = _client.Main.Survival.CropNodes[(_soil, _soilPart)];
            if (crop.View is not { Ripe: true } || crop.GrowShown < SoilState.Stages)
                return false;
            // Each stalk's head (Bloom0 to Bloom6) is scaled up from nothing at the end of the grow clip.
            var open = crop.Plant!.FindChildren("Bloom*", nameof(Node3D), true, false).OfType<Node3D>()
                .Count(b => b.Name.ToString()[5..].All(char.IsAsciiDigit) && b.Scale.X > 0.5f);
            return Check(open >= 5, $"the flower heads are open ({open})");
        });
        Add("harvested, it gives two Pigment Flowers and exactly one seed", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Request(LogicActions.ActivatePart, new LogicPartArgs(_soil, _soilPart));
            }
            if (_results.Count == 0)
                return false;
            if (!_results[0].Ok)
                return Check(false, $"the harvest is refused ({_results[0]})");
            if (_client.Main.Survival.CropNodes.ContainsKey((_soil, _soilPart)) || Count(Flower) != 3)
                return false;
            return Check(Count(Seed) == 1, $"exactly one seed back ({Count(Seed)})");
        });
        Add("the client plants a Banana Seed in the same mound and waters it once", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                var pockets = _host.Main.Inventory.Items!.PlayerInventory(Peer);
                pockets.Add(BananaSeed, 1);
                pockets.Add(Bucket, 1);
                return false;
            }
            if (frame == 20)
            {
                LookAt(_soilAt);
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(BananaSeed), Creation: _soil, Part: _soilPart));
            }
            if (frame < 20 || _results.Count == 0)
                return false;
            if (!_results[^1].Ok)
                return Check(false, $"planting or watering failed ({_results[^1]})");
            if (_results.Count == 1)
            {
                if (HostSoil.Seed != BananaSeed)
                    return false;
                var aim = (_soilAt - _client.Main.LocalPlayer!.Head.GlobalPosition).Normalized();
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Bucket), new Vec3(aim.X, aim.Y, aim.Z)));
                return false;
            }
            if (HostSoil.Water < 1200f || !_client.Main.Survival.CropNodes.TryGetValue((_soil, _soilPart), out var crop) || crop.View?.Seed != BananaSeed
                || crop.Plant is null)
                return false;
            var player = crop.Plant.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();
            return Check(player is not null && new[] { "grow", "wilt", "sway" }.All(c => player.HasAnimation(c)), "the banana plant has its grow, wilt and sway clips");
        });
        Add("after 1260 s on the host's clock the banana plant is ripe, its bunch yellow", frame =>
        {
            if (frame == 0)
                _host.Main.Scripts.Runtime.Time += 1270;
            var crop = _client.Main.Survival.CropNodes[(_soil, _soilPart)];
            if (crop.View is not { Ripe: true } || crop.GrowShown < SoilState.Stages)
                return false;
            // Each hand of the bunch has a green and a ripe look, switched at the end of the grow clip.
            var ripe = crop.Plant!.FindChildren("Hand*_ripe", nameof(Node3D), true, false).OfType<Node3D>().Count(h => h.Scale.X > 0.5f);
            var green = crop.Plant!.FindChildren("Hand*_green", nameof(Node3D), true, false).OfType<Node3D>().Count(h => h.Scale.X > 0.5f);
            return Check(ripe >= 2 && green == 0, $"the ripe hands show and the green ones are gone ({ripe} ripe, {green} green)");
        });
        Add("harvested, it gives two bananas and one to three seeds", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Request(LogicActions.ActivatePart, new LogicPartArgs(_soil, _soilPart));
            }
            if (_results.Count == 0)
                return false;
            if (!_results[0].Ok)
                return Check(false, $"the harvest is refused ({_results[0]})");
            if (_client.Main.Survival.CropNodes.ContainsKey((_soil, _soilPart)) || Count(Banana) != 2)
                return false;
            return Check(Count(BananaSeed) is >= 1 and <= 3, $"one to three seeds back ({Count(BananaSeed)})");
        });
        Add("eating a banana heals 35, on the host and on the client", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                _host.Main.Survival.Actions!.Damage(Peer, 50, "test");
                return false;
            }
            if (frame == 20)
            {
                _health = _client.Main.Survival.Own!.Health;
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Banana)));
            }
            if (frame < 20 || _results.Count == 0)
                return false;
            if (!_results[0].Ok)
                return Check(false, $"eating is refused ({_results[0]})");
            if (_client.Main.Survival.Own!.Health == _health || Count(Banana) != 1) // the pockets sync on their own
                return false;
            Check(_host.Main.Survival.Actions!.HealthOf(Peer) == 85f, $"the host heals 35 (50 -> {_host.Main.Survival.Actions!.HealthOf(Peer)})");
            return Check(_client.Main.Survival.Own!.Health == _health + 35, $"the client sees it ({_health} -> {_client.Main.Survival.Own!.Health})");
        });
        Add("a chili eaten at full health hurts 50 on the host, and the client sees the drop", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                _host.Main.Survival.Actions!.Heal(Peer, 100);
                _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Chili, 1);
                return false;
            }
            if (frame == 30)
            {
                Check(_client.Main.Survival.Own!.Health == 100f, $"the client is at full health ({_client.Main.Survival.Own!.Health})");
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Chili)));
            }
            if (frame < 30 || _results.Count == 0)
                return false;
            if (!_results[0].Ok)
                return Check(false, $"the chili is refused at full health ({_results[0]})");
            if (_client.Main.Survival.Own!.Health == 100f || Count(Chili) != 0)
                return false;
            Check(_host.Main.Survival.Actions!.HealthOf(Peer) == 50f, $"the host takes 50 ({_host.Main.Survival.Actions!.HealthOf(Peer)})");
            Check(!_host.Main.Survival.Actions!.IsDead(Peer), "one chili does not kill from full health");
            return Check(_client.Main.Survival.Own!.Health == 50f, $"the client sees 50 ({_client.Main.Survival.Own!.Health})");
        });
        Add("Beetroot, Banana and Chili have their bitten models in hand and plants that grow, wilt and sway", _ =>
        {
            var survival = _client.Main.Survival;
            foreach (var crop in new[] { "beetroot", "banana", "chili" })
            {
                var food = survival.Catalog.FindDefinition("vanilla:" + crop);
                var seed = survival.Catalog.FindDefinition("vanilla:" + crop + "_seed");
                var held = food?.Held?.Mesh is { } mesh ? survival.Assets!.InstantiateScene(mesh, "vanilla") : null;
                Check(held is not null && Enumerable.Range(0, 3).All(k => held.FindChild($"eat_{k}", true, false) is Node3D),
                    $"the {crop} in hand has its whole and two bitten versions");
                var plant = seed?.Seed?.Plant is { } path ? survival.Assets!.SceneOf("vanilla:" + crop + "_seed", path) : null;
                var player = plant?.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();
                Check(player is not null && new[] { "grow", "wilt", "sway" }.All(c => player.HasAnimation(c)),
                    $"the {crop} plant has its grow, wilt and sway clips");
                Check(plant?.FindChild("*Stake*", true, false) is not null, $"the {crop} plant has its stake and tag");
                held?.Free();
                plant?.Free();
            }
            return true;
        });
        Add("the client plants a thirsty Broccoli Seed in the same mound and waters it once", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                var pockets = _host.Main.Inventory.Items!.PlayerInventory(Peer);
                pockets.Add(BroccoliSeed, 1);
                pockets.Add(Bucket, 1);
                return false;
            }
            if (frame == 20)
            {
                LookAt(_soilAt);
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(BroccoliSeed), Creation: _soil, Part: _soilPart));
            }
            if (frame < 20 || _results.Count == 0)
                return false;
            if (!_results[^1].Ok)
                return Check(false, $"planting or watering failed ({_results[^1]})");
            if (_results.Count == 1)
            {
                if (HostSoil.Seed != BroccoliSeed)
                    return false;
                Throw();
                return false;
            }
            if (HostSoil.Water < 1200f || !_client.Main.Survival.CropNodes.TryGetValue((_soil, _soilPart), out var crop) || crop.View?.Seed != BroccoliSeed
                || crop.Plant is null)
                return false;
            Check(crop.Plant.FindChild("*Stake*", true, false) is not null, "the broccoli has its stake and tag");
            return Check(_host.Main.Survival.Catalog.FindDefinition(BroccoliSeed)!.Seed!.Thirst == 2f, "the broccoli drinks twice as fast");
        });
        Add("after 1260 s on one bucket it is only half grown, and its soil is dry", frame =>
        {
            if (frame == 0)
                _host.Main.Scripts.Runtime.Time += 1270;
            var crop = _client.Main.Survival.CropNodes[(_soil, _soilPart)];
            if (HostSoil.Water > 0.5f || HostSoil.Stage < 2 || crop.View?.Stage < 2)
                return false;
            Check(HostSoil.Stage == 2 && !HostSoil.Ripe, $"half grown, not ripe ({HostSoil.Stage}, {HostSoil.Grown:0} s)");
            return Check(crop.View is { Ripe: false, Stage: 2 }, $"the client sees it half grown ({crop.View?.Stage})");
        });
        Add("a second bucket ripens it", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Bucket, 1);
                return false;
            }
            if (frame == 20)
                Throw();
            if (frame < 20 || _results.Count == 0)
                return false;
            if (!_results[0].Ok)
                return Check(false, $"the second throw is refused ({_results[0]})");
            if (_results.Count == 1 && HostSoil.Water < 600f)
                return false;
            if (_results.Count == 1)
            {
                _results.Add(_results[0]); // the clock moves once
                _host.Main.Scripts.Runtime.Time += 640;
            }
            var crop = _client.Main.Survival.CropNodes[(_soil, _soilPart)];
            if (crop.View is not { Ripe: true } || crop.GrowShown < SoilState.Stages)
                return false;
            return Check(HostSoil.Ripe, "ripe on the host");
        });
        Add("harvested, it gives two broccoli and one to three seeds", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Request(LogicActions.ActivatePart, new LogicPartArgs(_soil, _soilPart));
            }
            if (_results.Count == 0)
                return false;
            if (!_results[0].Ok)
                return Check(false, $"the harvest is refused ({_results[0]})");
            if (_client.Main.Survival.CropNodes.ContainsKey((_soil, _soilPart)) || Count(Broccoli) != 2)
                return false;
            return Check(Count(BroccoliSeed) is >= 1 and <= 3, $"one to three seeds back ({Count(BroccoliSeed)})");
        });
        Add("eating an orange heals 45, on the host and on the client", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                _host.Main.Survival.Actions!.Heal(Peer, 100);
                _host.Main.Survival.Actions!.Damage(Peer, 50, "test");
                _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Orange, 1);
                return false;
            }
            if (frame == 30)
            {
                _health = _client.Main.Survival.Own!.Health;
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Orange)));
            }
            if (frame < 30 || _results.Count == 0)
                return false;
            if (!_results[0].Ok)
                return Check(false, $"eating is refused ({_results[0]})");
            if (_client.Main.Survival.Own!.Health == _health || Count(Orange) != 0)
                return false;
            Check(_host.Main.Survival.Actions!.HealthOf(Peer) == 95f, $"the host heals 45 (50 -> {_host.Main.Survival.Actions!.HealthOf(Peer)})");
            return Check(_client.Main.Survival.Own!.Health == _health + 45, $"the client sees it ({_health} -> {_client.Main.Survival.Own!.Health})");
        });
        Add("Blueberry, Orange, Broccoli and Pineapple have their bitten models in hand and plants that grow, wilt and sway", _ =>
        {
            var survival = _client.Main.Survival;
            foreach (var crop in new[] { "blueberry", "orange", "broccoli", "pineapple" })
            {
                var food = survival.Catalog.FindDefinition("vanilla:" + crop);
                var seed = survival.Catalog.FindDefinition("vanilla:" + crop + "_seed");
                var held = food?.Held?.Mesh is { } mesh ? survival.Assets!.InstantiateScene(mesh, "vanilla") : null;
                Check(held is not null && Enumerable.Range(0, 3).All(k => held.FindChild($"eat_{k}", true, false) is Node3D),
                    $"the {crop} in hand has its whole and two bitten versions");
                var plant = seed?.Seed?.Plant is { } path ? survival.Assets!.SceneOf("vanilla:" + crop + "_seed", path) : null;
                var player = plant?.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();
                Check(player is not null && new[] { "grow", "wilt", "sway" }.All(c => player.HasAnimation(c)),
                    $"the {crop} plant has its grow, wilt and sway clips");
                Check(plant?.FindChild("*Stake*", true, false) is not null, $"the {crop} plant has its stake and tag");
                held?.Free();
                plant?.Free();
            }
            return true;
        });
    }

    private void Throw()
    {
        var aim = (_soilAt - _client.Main.LocalPlayer!.Head.GlobalPosition).Normalized();
        Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Bucket), new Vec3(aim.X, aim.Y, aim.Z)));
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    private int Count(string item) => _client.Main.Inventory.Own?.Items.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;

    private int SlotOf(string item) => _client.Main.Inventory.Own!.Items.First(s => s.Item == item).Slot;

    private void LookAt(Vector3 target)
    {
        var player = _client.Main.LocalPlayer!;
        var d = (target - player.Head.GlobalPosition).Normalized();
        player.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Asin(d.Y) };
    }
}
