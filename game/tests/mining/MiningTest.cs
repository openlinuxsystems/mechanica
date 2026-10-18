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
/// Boulder Mining spec, host plus client, on the vanilla world: a rock outcrop streams in as cubes drawn with their
/// model; the client's Sledgehammer does nothing to a cube while a drill's damage breaks it into two loose Stone
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
    private int _rod;
    private string _block = "";

    protected override string Tag => "mining";

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
                // The nearest stone outcrop, and its ground cube farthest out from its middle, so the client stands clear of it.
                var objects = _host.Main.Streamer.Generator.ObjectsIn(feet.X - 800, feet.Z - 800, feet.X + 800, feet.Z + 800)
                    .Where(o => o.Definition.Id == "rock_outcrop").ToList();
                var nearest = objects.OrderBy(o => System.Numerics.Vector3.Distance(o.Position, feet)).First().Id with { Piece = 0 };
                var cubes = objects.Where(o => (o.Id with { Piece = 0 }) == nearest).ToList();
                var middle = new Vector3(cubes.Average(o => o.Position.X), 0, cubes.Average(o => o.Position.Z));
                var floor = cubes.Min(o => o.Position.Y);
                _cube = cubes.Where(o => o.Position.Y - floor < 0.01f).OrderByDescending(o => Flat(o.Position.ToGodot() - middle).Length()).First();
                var outward = (_cube.Position.ToGodot() - middle) with { Y = 0 };
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
                    .Any(m => m.Multimesh is { InstanceCount: > 0 } mm && mm.Mesh?.GetSurfaceCount() > 0 && m.Name == "rock_outcrop_mesh");
                Check(drawn, $"{who}: the cubes are a multimesh of the rock model");
            }
            return true;
        });
        Stage("the Sledgehammer does nothing to an intact cube", frame =>
        {
            if (frame % 30 == 0 && frame <= 60)
                _client.Main.Actions.Request(ToolActions.Sledge, new SledgeArgs(Object: _cube.Id.ToString(), At: At(_cube.Position)), _results.Add);
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
            if (frame % 20 == 0 && Loose(_host, Chunk).FirstOrDefault() is { } chunk)
            {
                _host.Main.PlayerSync.HostTeleport(Peer, Centre(_host, chunk) + new Vector3(1.5f, 1, 0));
                _client.Main.Actions.Request(ToolActions.Sledge, new SledgeArgs(chunk.Id, chunk.Parts.First().Id), _results.Add);
            }
            if (Loose(_host, Chunk).Count > 0 || Rods(_client).Count < 4 || Rods(_host).Count < 4)
                return frame > 1800 && Fail($"the chunks never broke ({Loose(_host, Chunk).Count} left, {Rods(_client).Count} rods; {string.Join("; ", _results.Where(r => !r.Ok))})");
            Check(Rods(_host).Count == 4, $"two chunks of two rods are four rods ({Rods(_host).Count})");
            return true;
        });
        Stage("a rod under the crosshair offers a refine", frame =>
        {
            var rod = Rods(_host).OrderBy(c => c.Id).First();
            if (frame == 0)
            {
                _rod = rod.Id;
                _block = Identifiers.Qualify(rod.DefinitionOf(rod.Parts.First().Id)!.Refine![0].Item!, "vanilla");
                _host.Main.PlayerSync.HostTeleport(Peer, Centre(_host, rod) + new Vector3(1.6f, 0.5f, 0));
            }
            if (frame >= 30)
                LookAt(_client, Centre(_client, _client.Main.Creations.Find(_rod)!));
            if (frame < 45 || _hud.AimedRefinable()?.Node.Creation.Id != _rod)
                return frame > 400 && Fail("the rod is never under the crosshair");
            var name = rod.DefinitionOf(rod.Parts.First().Id)!.Name;
            Check(_hud.Interaction()?.Label == $"hold to refine the {name}", $"Use offers to refine it ({_hud.Interaction()?.Label})");
            return true;
        });
        Stage("holding Use refines the rod into 20 blocks, the rod gone on both machines", frame =>
        {
            if (frame == 0)
            {
                _holding = true;
                Check(_hud.Interact(), "Use starts the refine");
            }
            if (frame > 0 && frame < 60)
                LookAt(_client, Centre(_client, _client.Main.Creations.Find(_rod)!));
            if (Count(_block) < 20 || _client.Main.Creations.Find(_rod) is not null || _host.Main.Creations.Find(_rod) is not null)
                return frame > 900 && Fail($"the rod was never refined ({Count(_block)} {_block})");
            Check(Count(_block) == 20, $"20 {_block} ({Count(_block)})");
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
