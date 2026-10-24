using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using Mechanica.Core.Saves;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Scripting;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Soil on a Creation (parts map E9, batch 17b): the Growbed, host plus a client in Survival on flat ground. The host
/// builds a cart a few metres short of a chunk border: a deck of blocks with a Growbed and a Battery Container holding
/// batteries. The client plants a carrot seed in the Growbed and throws one bucket of water on it, all the 1260 s crop
/// needs (crops map K1); the crop grows on the host's world
/// clock while the batteries power it (one is used, and the bed shows "on"), and the client sees each stage. The host
/// pushes the cart across the chunk border: the cart and the crop stay whole and the crop stands on the bed on both
/// machines all the way (the hard rule: a Creation never breaks, unloads or loses parts at a border). The client
/// harvests five carrots, plants again, the host saves, and a new host loading the save has the bed with its crop.
/// </summary>
public partial class GrowbedTest : StagedTest
{
    private const string Growbed = "vanilla:growbed";
    private const string Battery = "vanilla:battery";
    private const string Cell = "vanilla:battery_pack";
    private const string Deck = "vanilla:aluminum_block";
    private const string Seed = "vanilla:carrot_seed";
    private const string Carrot = "vanilla:carrot";
    private const string Bucket = "vanilla:water_bucket_full";
    private const float Push = 3f;

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private SaveStore _store = null!;
    private SaveGame _save = null!;
    private int _cart;
    private int _bed;
    private int _battery;
    private float _border;
    private ChunkCoord _startChunk;
    private Vector3 _start;
    private int _carrots;
    private (string? Seed, float Grown, int Stage) _saved;

    protected override string Tag => "growbed";

    protected override int StageTimeoutFrames => 1200;

    private int Peer => _client.Peer;

    private Vector3 BedOnHost => BedCentre(_host);

    public override void _Ready()
    {
        var folder = ProjectSettings.GlobalizePath("user://test_growbed_saves");
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        _store = new SaveStore(folder);
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world with seeds and water", _ =>
            _client.Main.Survival.Own is { Dead: false } && Count(Seed) > 0 && Count(Bucket) == 2
            && _client.Main.LocalPlayer is { } player && player.IsOnFloor());
        Stage("the client walks to a few metres short of a chunk border", frame =>
        {
            if (frame == 0)
            {
                var feet = Feet(_client);
                var size = _host.Main.Streamer.Streamer.Settings.ChunkSize;
                _startChunk = _host.Main.Streamer.ChunkAt(feet);
                _border = (_startChunk.X + 1) * size;
                _host.Main.PlayerSync.HostTeleport(Peer, new Vector3(_border - 9f, feet.Y, feet.Z + 3f));
            }
            return frame > 40 && Feet(_client).X > _border - 10f && _client.Main.LocalPlayer!.IsOnFloor();
        });
        Stage("the host builds a cart with a Growbed and a Battery Container of batteries beside the client", frame =>
        {
            if (frame == 0)
                BuildCart();
            if (frame < 20 || _client.Main.Creations.NodeOf(_cart) is not { } node)
                return false;
            var definition = _client.Main.Creations.Find(_cart)!.DefinitionOf(_bed);
            Check(definition.Soil is { Yield: 5 } && definition.GroundOnly != true, "the Growbed is soil that is not ground-only");
            Check(node.SoilParts.Contains(_bed), "the client's cart lists the bed as soil");
            return true;
        });
        Stage("the client plants a carrot seed in the Growbed", frame =>
        {
            if (frame == 0)
            {
                _carrots = Count(Carrot);
                LookAt(_client, BedCentre(_client));
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Seed), Creation: _cart, Part: _bed));
            }
            if (_results.Count == 0)
                return false;
            if (!Check(_results[0].Ok, $"the seed is planted ({_results[0]})"))
                return true;
            return ClientSoil() is { Seed: Seed } && _client.Main.Survival.CropNodes.ContainsKey((_cart, _bed));
        });
        Stage("the client throws a bucket of water on it: it looks wet on the client", frame =>
        {
            if (frame == 0)
            {
                var aim = (BedCentre(_client) + new Vector3(0, 0.2f, 0) - Eye(_client)).Normalized();
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Bucket), new Vec3(aim.X, aim.Y, aim.Z)));
            }
            if (_results.Count < 1 || ClientSoil() is not { Water: >= 1200 } soil)
                return frame < 600 ? false : Check(false, $"the bed is watered ({string.Join("; ", _results)}; {ClientSoil()})");
            Check(_results.All(r => r.Ok), $"the throw lands ({string.Join("; ", _results)})");
            return Check(soil.Wetness == SoilWetness.Wet, $"fully wet ({soil.Wetness})");
        });
        Stage("the crop grows on batteries and the client sees its stages; the bed shows it is powered", frame =>
        {
            // 630 s of the host's clock: half of a carrot's growing, two batteries of power (360 s each).
            if (frame == 0)
                _host.Main.Scripts.Runtime.Time += 630;
            if (ClientSoil() is not { Stage: >= 2 } soil)
                return false;
            Check(!soil.Ripe, "not ripe yet");
            var batteries = _host.Main.Creations.Find(_cart)!.InventoryOf(_battery)!.Count(Cell);
            Check(batteries == 4, $"two batteries of the six are used ({batteries} left)");
            if (Clip(_client) != "on")
                return false;
            Check(Clip(_host) == "on", $"the host's bed shows on too ({Clip(_host)})");
            return true;
        });
        Stage("the host pushes the cart across the chunk border: it stays whole and the crop rides on it", frame =>
        {
            if (frame == 0)
                _start = BedOnHost;
            if (_host.Main.Creations.NodeOf(_cart) is not { } node || _client.Main.Creations.NodeOf(_cart) is null)
                return Check(false, $"the cart stays live on both machines (frame {frame})");
            // Towards 4 m/s along +x (against the ground's friction) until the bed is 6 m past the border; then it slides to a stop.
            if (BedOnHost.X < _border + 6f)
            {
                foreach (var body in node.Bodies)
                    body.ApplyCentralForce(new Vector3(body.Mass * (Push * (4f - body.LinearVelocity.X) + 0.8f * 9.8f), 0, 0));
            }
            ExpectIntact(frame);
            if (BedOnHost.X < _border + 6f || node.Bodies[0].LinearVelocity.Length() > 0.3f)
                return frame < 1100 ? false : Check(false, $"the cart gets over the border ({BedOnHost.X:F1} vs {_border})");
            Check(_host.Main.Streamer.ChunkAt(BedOnHost) != _startChunk, $"the bed is in the next chunk ({_host.Main.Streamer.ChunkAt(BedOnHost)})");
            return true;
        });
        Stage("after the border the crop still stands on the bed on the client", frame =>
        {
            if (frame < 30)
                return false;
            var crop = _client.Main.Survival.CropNodes.GetValueOrDefault((_cart, _bed));
            var centre = BedCentre(_client);
            var above = crop is null ? Vector3.Zero : crop.GlobalPosition - centre;
            Check(crop is { Visible: true } && new Vector2(above.X, above.Z).Length() < 0.05f && above.Y is > 0.12f and < 0.26f,
                $"the crop stands on the bed's soil ({crop?.GlobalPosition} vs the bed's middle {centre})");
            Check(ClientSoil() is { Seed: Seed, Stage: >= 2 }, $"the client still has the crop ({ClientSoil()})");
            return true;
        });
        Stage("the crop ripens and the client harvests five carrots", frame =>
        {
            if (frame == 0)
            {
                var near = BedOnHost + new Vector3(0, 0, 2.6f);
                _host.Main.PlayerSync.HostTeleport(Peer, new Vector3(near.X, Feet(_client).Y, near.Z));
                _host.Main.Scripts.Runtime.Time += 700;
            }
            if (frame == 60)
                Request(LogicActions.ActivatePart, new LogicPartArgs(_cart, _bed));
            if (_results.Count == 0)
                return false;
            if (!Check(_results[0].Ok, $"the harvest goes through ({_results[0]})"))
                return true;
            if (Count(Carrot) < _carrots + 5 || ClientSoil() is { Seed: not null })
                return false;
            Check(Count(Carrot) == _carrots + 5, $"five carrots, the bed's yield ({Count(Carrot) - _carrots})");
            return true;
        });
        Stage("the client plants again and it grows a little", frame =>
        {
            if (frame == 0)
            {
                LookAt(_client, BedCentre(_client));
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Seed), Creation: _cart, Part: _bed));
            }
            if (frame == 30)
            {
                Check(_results.Count == 1 && _results[0].Ok, $"the seed is planted ({string.Join("; ", _results)})");
                // The first crop drank its bucket: a bucket's worth (a Water Cannon's ten jets) from the host's side, and a
                // quarter of the grow time for the first stage.
                _host.Main.Survival.Actions!.WaterSoil(new ActionCaller(0, ActionSource.Script), _cart, _bed, 1260);
                _host.Main.Scripts.Runtime.Time += 400;
            }
            return frame > 30 && ClientSoil() is { Seed: Seed, Stage: >= 1 };
        });
        Stage("the host saves the world with the cart and its crop", _ =>
        {
            var soil = _host.Main.Creations.Find(_cart)!.SoilOf(_bed)!;
            _saved = (soil.Seed, soil.Grown, soil.Stage);
            _host.Main.Saves.Store = _store;
            var path = _host.Main.Saves.SaveNow("Growbed World");
            var diagnostics = new List<DataDiagnostic>();
            _save = SaveStore.Read(path!, diagnostics)!;
            Check(_save is not null && diagnostics.Count == 0, $"the save reads back ({string.Join("; ", diagnostics)})");
            return true;
        });
        Stage("a new host loads the save: the bed keeps its crop, and the client sees it", frame =>
        {
            if (frame == 0)
            {
                _harness.Stop(_client);
                _harness.Stop(_host);
                _harness.Save = _save;
                _host = _harness.StartHost();
                _client = _harness.StartClient("Client");
            }
            if (_host.Main.Creations.Find(_cart) is not { } cart || ClientSoil() is not { Seed: Seed } seen)
                return false;
            var soil = cart.SoilOf(_bed)!;
            Check(cart.FindPart(_bed)?.Part == Growbed && cart.Parts.Count == PartCount, $"the cart is whole ({cart.Parts.Count} parts)");
            Check(soil.Seed == _saved.Seed && soil.Stage >= _saved.Stage && soil.Grown >= _saved.Grown,
                $"the crop is as saved ({soil.Seed} {soil.Grown} {soil.Stage} vs {_saved})");
            Check(seen.Stage == soil.Stage || seen.Stage == _saved.Stage, $"the client sees its stage ({seen.Stage})");
            return _client.Main.Survival.CropNodes.ContainsKey((_cart, _bed));
        });
    }

    private const int PartCount = 10 * 6 + 2;

    // A 10 x 6 deck of blocks with the Growbed (5 x 2 x 5) on one end and a Battery Container (4 x 3 x 3) of six
    // batteries on the other, beside the client.
    private void BuildCart()
    {
        var build = _host.Main.Build;
        var creation = new Creation(build.NextCreationId(), build.Catalog);
        for (var x = 0; x < 10; x++)
        {
            for (var z = 0; z < 6; z++)
                creation.TryPlacePart(Deck, new GridCell(x, 0, z), default, out _, out _);
        }
        if (!creation.TryPlacePart(Growbed, new GridCell(0, 1, 0), default, out var bed, out var why))
            Check(false, $"the Growbed fits ({why})");
        if (!creation.TryPlacePart(Battery, new GridCell(6, 1, 0), default, out var battery, out why))
            Check(false, $"the Battery Container fits ({why})");
        (_cart, _bed, _battery) = (creation.Id, bed?.Id ?? 0, battery?.Id ?? 0);
        creation.InventoryOf(_battery)!.Add(Cell, 6);
        var feet = _host.Main.PlayerSync.Players[Peer].GlobalPosition;
        creation.SetAllBodyPoses(new Pose((feet + new Vector3(-0.6f, 0.15f, -3.2f)).ToNumerics(), System.Numerics.Quaternion.Identity));
        build.Added(creation);
    }

    // The cart must stay whole, live and loaded on both machines, and keep its crop, while it crosses the border.
    private void ExpectIntact(int frame)
    {
        if (Done)
            return;
        foreach (var (who, name) in new[] { (_host, "host"), (_client, "client") })
        {
            if (who.Main.Creations.Find(_cart) is not { } cart || cart.Parts.Count != PartCount || cart.FindPart(_bed) is null)
                Check(false, $"the {name}'s cart keeps all its parts (frame {frame})");
        }
        if (_host.Main.Creations.Find(_cart)!.SoilOf(_bed) is not { Seed: Seed })
            Check(false, $"the host's bed keeps its crop (frame {frame})");
        if (ClientSoil() is not { Seed: Seed })
            Check(false, $"the client still shows the crop (frame {frame})");
        var chunk = _host.Main.Streamer.ChunkAt(BedOnHost);
        if (!_host.Main.Streamer.Chunks.ContainsKey(chunk))
            Check(false, $"the ground under the cart is loaded ({chunk}, frame {frame})");
        if (BedOnHost.Y < -0.5f)
            Check(false, $"the cart stays on the ground (y {BedOnHost.Y:F2}, frame {frame})");
    }

    // The Growbed's centre in the world as `who` shows it.
    private Vector3 BedCentre(Instance who)
    {
        var node = who.Main.Creations.NodeOf(_cart)!;
        var part = node.Creation.FindPart(_bed)!;
        var body = node.Bodies[node.Creation.BodyIndexOf(_bed)];
        return body.GlobalTransform * PartGeometry.CentreMetres(node.Creation.DefinitionOf(_bed), part.Cell, part.Rotation).ToGodot();
    }

    private SoilView? ClientSoil() => _client.Main.Survival.Soil.GetValueOrDefault((_cart, _bed));

    // The clip the bed's model shows (its "pose" readout), or null before it has one.
    private string? Clip(Instance who) =>
        who.Main.Creations.NodeOf(_cart)?.Displays.FirstOrDefault(d => d.Part == _bed)?.Clip;

    private void Stage(string name, Func<int, bool> tick) => Add(name, frame =>
    {
        if (frame == 0)
            _results.Clear();
        return tick(frame);
    });

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    private int Count(string item) => _client.Main.Inventory.Own?.Items.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;

    private int SlotOf(string item) => _client.Main.Inventory.Own!.Items.First(s => s.Item == item).Slot;

    private static Vector3 Feet(Instance who) => who.Main.LocalPlayer!.GlobalPosition;

    private static Vector3 Eye(Instance who) => who.Main.LocalPlayer!.Head.GlobalPosition;

    private static void LookAt(Instance who, Vector3 target)
    {
        var d = (target - Eye(who)).Normalized();
        who.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Asin(d.Y) };
    }
}
