using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Data;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Survival;
using Mechanica.Game.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Boulder Mining spec, host plus client, on the vanilla world: a rock deposit streams in as blocks drawn with their
/// models; the client's Sledgehammer does nothing to a medium block while a drill's damage breaks it into two loose Stone
/// Chunks that both machines see; the client's Sledgehammer breaks the chunks into rods; and holding Use on a rod
/// refines it into 20 blocks, the rod gone on both machines.
/// </summary>
public partial class MiningTest : StagedTest
{
    private const string Chunk = "vanilla:stone_chunk";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private SurvivalHud _hud = null!;
    private bool _holding;
    private WorldObjectPlacement _cube = null!;
    private Vector3 _middle;
    private int _rod;
    private string _block = "";
    private int _swings;
    private int _refines;
    private int _misses;
    private List<Vector3> _rest = [];
    private double _lastSwing = double.NegativeInfinity;

    protected override string Tag => "mining";

    // Walking round the rods and the 5.2 s refine take longer than the default.
    protected override int StageTimeoutFrames => 3000;

    private int Peer => _client.Peer;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, Seed = 20261018 };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Stage("the client joins and walks up to a rock outcrop", frame =>
        {
            if (frame == 0)
                return false;
            if (_client.Main.Survival.Own is null || _client.Main.Inventory.Own is null || _client.Main.Tools is null
                || _client.Main.LocalPlayer is not { } player || !player.IsOnFloor())
                return false;
            if (_cube is null)
            {
                var feet = player.GlobalPosition.ToNumerics();
                // The nearest stone deposit with a medium block on the ground, and the outermost such block, so the client
                // stands clear of the deposit.
                var gen = _host.Main.Streamer.Generator;
                var deposits = gen.ObjectsIn(feet.X - 800, feet.Z - 800, feet.X + 800, feet.Z + 800)
                    .Where(o => !o.Id.IsPoi && gen.Data.ScatterRuleOf(o.Id)!.Object.Id == "rock_outcrop")
                    .GroupBy(o => o.Id with { Piece = 0 }).Select(g => g.ToList())
                    .OrderBy(d => System.Numerics.Vector3.Distance(d[0].Position, feet));
                foreach (var blocks in deposits)
                {
                    var middle = new Vector3(blocks.Average(o => o.Position.X), 0, blocks.Average(o => o.Position.Z));
                    // On near-level ground, so the chunks and rods settle beside the deposit instead of rolling away.
                    if (new[] { (0f, 0f), (6f, 0f), (-6f, 0f), (0f, 6f), (0f, -6f) }.Any(d => gen.SlopeAt(middle.X + d.Item1, middle.Z + d.Item2) > 0.12f))
                        continue;
                    // On the ground: nothing of the deposit lower within reach under it.
                    var grounded = blocks.Where(o => o.Definition.Id == "rock_outcrop"
                        && !blocks.Any(b => b != o && b.Position.Y < o.Position.Y - 0.3f && Flat(b.Position.ToGodot() - o.Position.ToGodot()).Length() < 1.2f));
                    if (grounded.OrderByDescending(o => Flat(o.Position.ToGodot() - middle).Length()).FirstOrDefault() is { } block)
                    {
                        _cube = block;
                        _middle = middle;
                        break;
                    }
                }
                if (_cube is null)
                    return false;
                var outward = (_cube.Position.ToGodot() - _middle) with { Y = 0 };
                outward = outward.LengthSquared() < 1e-4f ? Vector3.Right : outward.Normalized();
                _host.Main.PlayerSync.HostTeleport(Peer, _cube.Position.ToGodot() + outward * (0.5f * _cube.Scale + 1.2f) + new Vector3(0, 1.5f, 0));
                _hud = new SurvivalHud
                {
                    Name = "TestSurvivalHud", Survival = _client.Main.Survival, Actions = _client.Main.Actions, Players = _client.Main.PlayerSync,
                    Catalog = _client.Main.Survival.Catalog, UseHeld = () => _holding, Playing = () => true,
                };
                _client.Main.AddChild(_hud);
                return false;
            }
            return frame > 60 && Flat(Feet(_client) - _cube.Position.ToGodot()).Length() < 0.5f * _cube.Scale + 2f && player.IsOnFloor();
        });
        Stage("the outcrop's cubes are drawn with their model on both machines", frame =>
        {
            if (frame < 30)
                return false;
            foreach (var who in new[] { _host, _client })
            {
                var drawn = who.Main.Streamer.FindChildren("*", nameof(MultiMeshInstance3D), true, false).OfType<MultiMeshInstance3D>()
                    .Any(m => m.Multimesh is { InstanceCount: > 0 } mm && mm.Mesh?.GetSurfaceCount() > 0 && m.Name.ToString().StartsWith("rock_outcrop_mesh", StringComparison.Ordinal));
                Check(drawn, $"{who}: the cubes are a multimesh of the rock model");
            }
            return true;
        });
        Stage("the Sledgehammer does nothing to an intact cube", frame =>
        {
            if (_swings < 3 && SwingReady())
                Swing(new SledgeArgs(Object: _cube.Id.ToString(), At: At(_cube.Position)));
            if (_results.Count < 3)
                return false;
            Check(_results.All(r => r.Ok), $"the swings land ({string.Join("; ", _results)})");
            Check(_host.Main.Streamer.State.DamageOf(_cube.Id) == 0, $"the cube takes no damage ({_host.Main.Streamer.State.DamageOf(_cube.Id)})");
            return true;
        });
        Stage("a drill's damage breaks the cube into two loose chunks on both machines", frame =>
        {
            if (frame % 10 == 0 && !_host.Main.Streamer.State.IsDestroyed(_cube.Id))
                _host.Main.Survival.Actions!.StrikeObject(_cube.Id.ToString(), 40, DamageKinds.Drill);
            if (!_client.Main.Streamer.State.IsDestroyed(_cube.Id) || Loose(_client, Chunk).Count < 2 || Loose(_host, Chunk).Count < 2)
                return frame > 900 && Fail($"the cube never broke into chunks ({Loose(_client, Chunk).Count})");
            Check(Loose(_host, Chunk).All(c => !c.IsGrounded), "the chunks are loose, not anchored");
            Check(!TerrainCollides(_client, _cube.Position.ToGodot() + new Vector3(0, 0.5f * _cube.Scale, 0), _cube.Id), "the cube's collider is gone on the client");
            return true;
        });
        Stage("the client's Sledgehammer breaks the chunks into rods", frame =>
        {
            if (SwingReady() && Loose(_host, Chunk).FirstOrDefault() is { } chunk)
            {
                _host.Main.PlayerSync.HostTeleport(Peer, Centre(_host, chunk) + new Vector3(1.5f, 1, 0));
                Swing(new SledgeArgs(chunk.Id, chunk.Parts.First().Id));
            }
            if (Loose(_host, Chunk).Count > 0 || Rods(_client).Count < 4 || Rods(_host).Count < 4)
                return _swings > 40 && SwingReady() && Fail($"the chunks never broke ({Loose(_host, Chunk).Count} left, {Rods(_client).Count} rods; {string.Join("; ", _results.Where(r => !r.Ok))})");
            Check(Rods(_host).Count == 4, $"two chunks of two rods are four rods ({Rods(_host).Count})");
            return true;
        });
        Stage("the rods come to rest", frame =>
        {
            // A rod still rolling off the outcrop would leave the player's reach mid-refine.
            if (frame % 30 != 0)
                return false;
            var now = Rods(_host).Select(c => Centre(_host, c)).ToList();
            var still = _rest.Count == now.Count && now.Zip(_rest).All(p => p.First.DistanceTo(p.Second) < 0.01f);
            _rest = now;
            return still;
        });
        Stage("a rod under the crosshair offers a refine", frame =>
        {
            // Rods land every which way, some against the outcrop or each other, so walk round each in turn until the
            // crosshair finds one: every 60 frames the next of eight spots round it on bare ground (not on a rod or a cube),
            // then the next rod.
            var rods = Rods(_host).OrderBy(c => c.Id).ToList();
            var attempt = frame / 60;
            if (attempt >= rods.Count * 8)
                return Fail("no rod is ever under the crosshair");
            var rod = rods[attempt / 8];
            if (frame % 60 == 0)
            {
                _rod = rod.Id;
                if (!StandBeside(rod, attempt))
                    return false;
            }
            if (frame % 60 >= 30 && _client.Main.Creations.Find(_rod) is { } seen)
                LookAt(_client, Centre(_client, seen));
            // The client's prediction settles onto the teleport over a few snapshots; aim only once it has.
            if (frame % 60 < 45 || !Settled() || _hud.AimedRefinable()?.Node.Creation.Id != _rod)
                return false;
            _block = Identifiers.Qualify(rod.DefinitionOf(rod.Parts.First().Id)!.Refine![0].Item!, "vanilla");
            var name = rod.DefinitionOf(rod.Parts.First().Id)!.Name;
            Check(_hud.Interaction()?.Label == $"hold to refine the {name}", $"Use offers to refine it ({_hud.Interaction()?.Label})");
            return true;
        });
        Stage("holding Use refines the rod into 20 blocks, the rod gone on both machines", frame =>
        {
            // Use is held from the first frame. A refine the HUD let go of early (the player nudged off by a late
            // snapshot) is started again, as a player would by pressing Use again.
            // A nudge can also leave the player where the crosshair no longer finds the rod; after a second of that, step
            // to the next spot round it, as the stage before does.
            _holding = true;
            if (frame % 30 == 0 && _hud.RefiningTarget is null && Count(_block) < 20 && _client.Main.Creations.Find(_rod) is { } target)
            {
                LookAt(_client, Centre(_client, target));
                if (Settled() && _hud.AimedRefinable() is { } aimed && _hud.Interact())
                {
                    _rod = aimed.Node.Creation.Id;
                    _block = Identifiers.Qualify(aimed.Node.Creation.DefinitionOf(aimed.Part)!.Refine![0].Item!, "vanilla");
                    _refines++;
                    _misses = 0;
                }
                else if (++_misses % 2 == 0 && _host.Main.Creations.Find(_rod) is { } rod)
                    StandBeside(rod, _misses / 2);
            }
            if (Count(_block) < 20 || _client.Main.Creations.Find(_rod) is not null || _host.Main.Creations.Find(_rod) is not null)
                return frame > 2400 && Fail($"the rod was never refined ({Count(_block)} {_block}, {_refines} refines started; refining {_hud.RefiningTarget}, "
                    + $"rod on host {_host.Main.Creations.Find(_rod) is not null}, on client {_client.Main.Creations.Find(_rod) is not null}, settled {Settled()}, "
                    + $"aimed {_hud.AimedRefinable()?.Node.Creation.Id}, feet {Feet(_client)})");
            Check(Count(_block) == 20, $"20 {_block} ({Count(_block)})");
            _holding = false;
            return true;
        });
    }

    private void Stage(string name, Func<int, bool> tick) => Add(name, frame =>
    {
        if (frame == 0)
        {
            _results.Clear();
            _swings = 0;
        }
        return tick(frame);
    });

    // The host refuses a swing sooner than swing_seconds of real time after the last, so the next one waits for the
    // last one's answer and for the swing to finish, however fast frames come.
    private bool SwingReady() =>
        _results.Count == _swings && Time.GetTicksMsec() / 1000.0 - _lastSwing > _host.Main.Build.Settings!.Sledge.SwingSeconds + 0.1;

    private void Swing(SledgeArgs args)
    {
        _swings++;
        _lastSwing = Time.GetTicksMsec() / 1000.0;
        _client.Main.Actions.Request(ToolActions.Sledge, args, _results.Add);
    }

    private bool Fail(string message)
    {
        Check(false, message);
        return true;
    }

    private static Core.Data.Vec3 At(System.Numerics.Vector3 v) => new(v.X, v.Y + 0.5f, v.Z);

    private static List<Core.Creations.Creation> Loose(Instance who, string part) =>
        [.. who.Main.Creations.All.Where(c => c.Parts.Count == 1 && Identifiers.Qualify(c.Parts.First().Part, "vanilla") == part)];

    private static List<Core.Creations.Creation> Rods(Instance who) =>
        [.. Loose(who, "vanilla:stone_rod"), .. Loose(who, "vanilla:metal_rod")];

    private static Vector3 Centre(Instance who, Core.Creations.Creation creation) =>
        who.Main.Creations.NodeOf(creation.Id)?.PartNode(creation.Parts.First().Id)?.GlobalPosition ?? Vector3.Zero;

    // Whether nothing but the ground itself is under a spot (no rod, chunk or cube to stand on), and where the ground is.
    // Teleports the client to the attempt'th of eight spots round the rod, on bare ground (not on a rod or a cube).
    private bool StandBeside(Core.Creations.Creation rod, int attempt)
    {
        var angle = attempt % 8 * Mathf.Pi / 4f;
        var spot = Centre(_host, rod) + new Vector3(Mathf.Cos(angle) * 1.6f, 0, Mathf.Sin(angle) * 1.6f);
        if (!BareGround(_host, spot, out var ground))
            return false;
        _host.Main.PlayerSync.HostTeleport(Peer, ground + new Vector3(0, 0.2f, 0));
        return true;
    }

    private static bool BareGround(Instance who, Vector3 spot, out Vector3 ground)
    {
        var height = who.Main.Streamer.Generator.HeightAt(spot.X, spot.Z);
        ground = new Vector3(spot.X, height, spot.Z);
        var query = PhysicsRayQueryParameters3D.Create(ground + new Vector3(0, 4, 0), ground - new Vector3(0, 1, 0));
        var hit = who.Main.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 && Mathf.Abs(hit["position"].AsVector3().Y - height) < 0.15f;
    }

    // Whether the client's own player stands where the host has it.
    private bool Settled() => _host.Main.PlayerSync.Players[Peer].GlobalPosition.DistanceTo(Feet(_client)) < 0.1f;

    private static bool TerrainCollides(Instance who, Vector3 point, WorldObjectId id)
    {
        var shape = new PhysicsShapeQueryParameters3D { Shape = new SphereShape3D { Radius = 0.1f }, Transform = new Transform3D(Basis.Identity, point) };
        return who.Main.GetWorld3D().DirectSpaceState.IntersectShape(shape, 8)
            .Any(h => TerrainChunk.ObjectHit(h["collider"].AsGodotObject(), h["shape"].AsInt32()) == id);
    }

    private int Count(string item) => _client.Main.Inventory.Own?.Items.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    private static Vector3 Feet(Instance who) => who.Main.LocalPlayer!.GlobalPosition;

    private static void LookAt(Instance who, Vector3 target)
    {
        var d = (target - who.Main.LocalPlayer!.Head.GlobalPosition).Normalized();
        who.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Asin(d.Y) };
    }
}
