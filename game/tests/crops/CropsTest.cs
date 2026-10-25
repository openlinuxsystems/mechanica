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
/// </summary>
public partial class CropsTest : StagedTest
{
    private const string Flower = "vanilla:pigment_flower";
    private const string Seed = "vanilla:pigment_flower_seed";
    private const string Bucket = "vanilla:water_bucket_full";
    private const string Soil = "vanilla:soil";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private WorldObjectPlacement _wild = null!;
    private Vector3 _soilAt;
    private int _soil;
    private int _soilPart;
    private int _seedsPicked;

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
