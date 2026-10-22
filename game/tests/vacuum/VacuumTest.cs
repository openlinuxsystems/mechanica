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
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Item transport (parts map E1, batch 17c), host plus a client in Survival on flat ground. The host builds a base beside
/// the client: a Small Chest piped through two Vacuum Pipes to a Vacuum Pump with a Switch linked to it, and a Resource
/// Collector piped to a Refiner's input port. The client switches the pump on: it sucks a pile in front of its mouth into
/// the chest (the client sees the pile go, the pump's ring light up, and the scrap in the chest). A loose Wood Rod dropped
/// over the collector is pulled into it; the client queues a Wood Rod at the Refiner and it refines the collector's rod into
/// Scrap Wood Blocks. The client sets the pump to Eject and it throws the chest's items out as a pile the client sees.
/// The host saves; a new host loading the save has the chest's contents (the network is derived, never saved).
/// </summary>
public partial class VacuumTest : StagedTest
{
    private const string Deck = "vanilla:metal_block";
    private const string Chest = "vanilla:small_chest";
    private const string Pipe = "vanilla:vacuum_pipe";
    private const string Pump = "vanilla:vacuum_pump";
    private const string Switch = "vanilla:switch";
    private const string Collector = "vanilla:resource_collector";
    private const string Refiner = "vanilla:refiner";
    private const string Rod = "vanilla:wood_rod";
    private const string Scrap = "vanilla:scrap_metal_block";
    private const string Blocks = "vanilla:scrap_wood_block";

    // Local +Z turned to world +X.
    private static readonly GridRotation AlongX = GridRotation.FromAxes(new GridCell(0, 0, -1), new GridCell(0, 1, 0));

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private SaveStore _store = null!;
    private SaveGame _save = null!;
    private int _base;
    private int _chest;
    private int _pump;
    private int _switch;
    private int _collector;
    private int _refiner;
    private int _pile;
    private int _rod;
    private int _kept;

    protected override string Tag => "vacuum";

    protected override int StageTimeoutFrames => 1500;

    private int Peer => _client.Peer;

    // Through the survival world, which brings the Creation's pose up to date with its bodies first.
    private Creation HostBase => _host.Main.Survival.FindCreation(_base)!;

    public override void _Ready()
    {
        var folder = ProjectSettings.GlobalizePath("user://test_vacuum_saves");
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        _store = new SaveStore(folder);
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world", _ =>
            _client.Main.Survival.Own is { Dead: false } && _client.Main.LocalPlayer is { } player && player.IsOnFloor());
        Stage("the host builds the base beside the client; both see its pipe network", frame =>
        {
            if (frame == 0)
                BuildBase();
            if (frame < 20 || _client.Main.Creations.Find(_base) is not { } seen)
                return false;
            Check(TransportNetwork.Into(HostBase, _pump).SequenceEqual([_chest]), "the host's pump is piped to the chest");
            Check(TransportNetwork.Into(seen, _pump).SequenceEqual([_chest]), "the client works out the same network");
            Check(StorageLinks.InputsOf(HostBase, _refiner).Contains(_collector), "the Refiner takes its inputs from the collector");
            return true;
        });
        Stage("a pile lies in front of the pump's mouth; nothing happens while it is off", frame =>
        {
            if (frame == 0)
            {
                var (at, normal, _) = TransportActions.IntakeOf(HostBase, _pump)!.Value;
                var drop = _host.Main.Survival.Actions!.AddDrop(at + normal * 0.5f, [new ItemStack(Scrap, 30)], placed: true)!;
                _pile = drop.Id;
            }
            if (frame < 90 || !_client.Main.Survival.Drops.ContainsKey(_pile))
                return false;
            Check(_host.Main.Survival.Drops.ContainsKey(_pile), "the pile is still there: the pump is off");
            return Check(Clip(_client, _pump) is null or "off", $"the pump shows off ({Clip(_client, _pump)})");
        });
        Stage("the client switches the pump on: the pile goes into the chest and the pump lights up", frame =>
        {
            if (frame == 0)
            {
                Teleport(Beside(_switch));
            }
            if (frame == 30)
                Request(LogicActions.ActivatePart, new LogicPartArgs(_base, _switch));
            if (frame < 30 || _results.Count == 0)
                return false;
            if (!_results[0].Ok)
                return Check(false, $"the switch is used ({_results[0]})");
            if (frame == 900)
                return Check(false, Diagnose());
            if (_client.Main.Survival.Drops.ContainsKey(_pile) || Clip(_client, _pump) != "on")
                return false;
            Check(HostBase.InventoryOf(_chest)!.Count(Scrap) == 30, $"the chest holds the pile ({HostBase.InventoryOf(_chest)!.Count(Scrap)})");
            return true;
        });
        Stage("the client opens the chest and sees the scrap", frame =>
        {
            if (frame == 0)
                RequestView(_chest);
            if (_results.Count == 0)
                return false;
            var view = View(0);
            return Check(view?.Count(Scrap) == 30, $"the client sees 30 scrap in the chest ({_results[0]})");
        });
        Stage("a loose Wood Rod over the collector is pulled into it, and goes from the client's world", frame =>
        {
            if (frame == 0)
                DropRod();
            if (frame < 30 || HostBase.InventoryOf(_collector)!.Count(Rod) == 0)
                return false;
            return _host.Main.Creations.Find(_rod) is null && _client.Main.Creations.Find(_rod) is null;
        });
        Stage("the client queues a Wood Rod at the Refiner: it refines the collector's rod", frame =>
        {
            if (frame == 0)
                Teleport(Beside(_refiner));
            if (frame == 30)
                Request(InventoryActions.Craft, new CraftArgs(_base, _refiner, Rod));
            if (frame < 30 || _results.Count == 0)
                return false;
            if (_results.Count == 1)
            {
                if (!_results[0].Ok)
                    return Check(false, $"the Refiner takes the order ({_results[0]})");
                if (HostBase.InventoryOf(_collector)!.Count(Rod) != 0)
                    return Check(false, "the rod came from the collector");
                // The blocks go to the Refiner's linked storage (the collector stands next to it), then the Refiner.
                if (Made(HostBase) < 20)
                    return false;
                RequestView(_refiner);
                RequestView(_collector);
                return false;
            }
            if (_results.Count < 3)
                return false;
            var seen = (View(1)?.Count(Blocks) ?? 0) + (View(2)?.Count(Blocks) ?? 0);
            return Check(seen == 20, $"the client sees 20 Scrap Wood Blocks made ({seen})");
        });
        Stage("the client sets the pump to Eject: it throws the chest's scrap out as a pile the client sees", frame =>
        {
            if (frame == 0)
            {
                Teleport(Beside(_pump));
                _kept = _client.Main.Survival.Drops.Count;
            }
            if (frame == 30)
            {
                Request(LogicActions.ConfigurePart, new ConfigurePartArgs(_base, _pump,
                    new Dictionary<string, JsonElement?> { ["mode"] = JsonSerializer.SerializeToElement("eject") }));
            }
            if (frame < 30 || _results.Count == 0)
                return false;
            if (!_results[0].Ok)
                return Check(false, $"the pump is set to eject ({_results[0]})");
            var left = HostBase.InventoryOf(_chest)!.Count(Scrap);
            if (left > 26 || _client.Main.Survival.Drops.Count <= _kept)
                return false;
            var (at, normal, _) = TransportActions.IntakeOf(HostBase, _pump)!.Value;
            var thrown = _client.Main.Survival.Drops.Values.Where(d => d.Items.Any(i => i.Item == Scrap)).ToList();
            Check(thrown.Count > 0, "the client sees the thrown scrap");
            Check(thrown.All(d => (d.Position.ToNumerics() - at).Length() < 2.5f), "it lands in front of the pump's mouth");
            return true;
        });
        Stage("the host saves; a new host loading it has the chest's contents and the network again", frame =>
        {
            // The client switches the pump off first, so the chest keeps the rest.
            if (frame == 0)
            {
                Teleport(Beside(_switch));
                Request(LogicActions.ActivatePart, new LogicPartArgs(_base, _switch));
            }
            if (frame == 30 && (_results.Count == 0 || Clip(_host, _pump) != "off"))
                return Check(false, $"the pump is off again ({string.Join("; ", _results)}, {Clip(_host, _pump)})");
            if (frame == 60)
            {
                _kept = HostBase.InventoryOf(_chest)!.Count(Scrap);
                _host.Main.Saves.Store = _store;
                var path = _host.Main.Saves.SaveNow("Vacuum World");
                var diagnostics = new List<DataDiagnostic>();
                _save = SaveStore.Read(path!, diagnostics)!;
                Check(_save is not null && diagnostics.Count == 0, $"the save reads back ({string.Join("; ", diagnostics)})");
                _harness.Stop(_client);
                _harness.Stop(_host);
                _harness.Save = _save;
                _host = _harness.StartHost();
                _client = _harness.StartClient("Client");
            }
            if (frame < 60 || _host.Main.Creations.Find(_base) is not { } loaded)
                return false;
            Check(loaded.InventoryOf(_chest)!.Count(Scrap) == _kept, $"the chest keeps its {_kept} scrap ({loaded.InventoryOf(_chest)!.Count(Scrap)})");
            Check(Made(loaded) == 20, "the made blocks are kept");
            Check(TransportNetwork.Into(loaded, _pump).SequenceEqual([_chest]), "the network is worked out again");
            return true;
        });
    }

    // A deck of blocks (x -2 to 14, z -2 to 14) 6 m in front of the client with: a Small Chest, two Vacuum Pipes on
    // blocks and a Vacuum Pump turned along +x (its back port toward the chest), a Switch on the far edge linked to the pump; and a
    // Resource Collector piped by one Vacuum Pipe to a Refiner's input port.
    private void BuildBase()
    {
        var build = _host.Main.Build;
        var creation = new Creation(build.NextCreationId(), build.Catalog);
        for (var x = -2; x <= 14; x++)
        {
            for (var z = -2; z <= 14; z++)
                creation.TryPlacePart(Deck, new GridCell(x, 0, z), default, out _, out _);
        }
        int Place(string part, GridCell cell, GridRotation rotation = default)
        {
            if (!creation.TryPlacePart(part, cell, rotation, out var placed, out var why))
                Check(false, $"{part} fits at {cell} ({why})");
            return placed?.Id ?? 0;
        }
        _chest = Place(Chest, new GridCell(0, 1, 0));
        Place(Deck, new GridCell(3, 1, 1));
        Place(Deck, new GridCell(4, 1, 1));
        Place(Pipe, new GridCell(3, 2, 1), AlongX);
        Place(Pipe, new GridCell(4, 2, 1), AlongX);
        // Turned along x, its 3-block width runs from its origin toward -z: z 0 to 2, the back port at z 1.
        _pump = Place(Pump, new GridCell(5, 1, 2), AlongX);
        _switch = Place(Switch, new GridCell(1, 1, 14));
        if (!creation.TryLink(_switch, _pump, out var linkError))
            Check(false, $"the switch links to the pump ({linkError})");
        _collector = Place(Collector, new GridCell(0, 1, 8));
        Place(Deck, new GridCell(4, 1, 10));
        Place(Pipe, new GridCell(4, 2, 10), AlongX);
        _refiner = Place(Refiner, new GridCell(5, 1, 8));
        _base = creation.Id;
        var feet = _host.Main.PlayerSync.Players[Peer].GlobalPosition;
        creation.SetAllBodyPoses(new Pose((feet + new Vector3(-1f, 0.15f, -6f)).ToNumerics(), System.Numerics.Quaternion.Identity));
        build.Added(creation);
    }

    // The Scrap Wood Blocks the Refiner made, wherever they went (itself or the collector next to it).
    private int Made(Creation creation) =>
        creation.InventoryOf(_refiner)!.Count(Blocks) + creation.InventoryOf(_collector)!.Count(Blocks);

    // A loose Wood Rod a metre over the collector's top.
    private void DropRod()
    {
        var build = _host.Main.Build;
        var rod = new Creation(build.NextCreationId(), build.Catalog);
        if (!rod.TryPlacePart(Rod, GridCell.Zero, default, out _, out var why))
            Check(false, $"the rod is made ({why})");
        var top = PartCentre(_host, _collector) + new Vector3(0, 1.5f, 0);
        rod.SetAllBodyPoses(new Pose(top.ToNumerics(), System.Numerics.Quaternion.Identity));
        _rod = rod.Id;
        build.Added(rod);
    }

    // What the host's pump sees, for a failure message.
    private string Diagnose()
    {
        var intake = TransportActions.IntakeOf(HostBase, _pump);
        var pickups = _host.Main.Survival.Transport!.Pickups(HostBase, _pump);
        var pile = _host.Main.Survival.Drops.GetValueOrDefault(_pile);
        var messages = string.Join(" | ", _host.Main.Scripts.Runtime.Messages.TakeLast(5).Select(m => m.Text));
        return $"pump clip {Clip(_host, _pump)}, intake {intake?.Position}, pile {pile?.Position}, pickups {pickups.Count}, " +
            $"chest {HostBase.InventoryOf(_chest)!.Count(Scrap)}, into [{string.Join(",", TransportNetwork.Into(HostBase, _pump))}], scripts: {messages}";
    }

    // A part's centre in the world as `who` shows it.
    private Vector3 PartCentre(Instance who, int part)
    {
        var node = who.Main.Creations.NodeOf(_base)!;
        var placed = node.Creation.FindPart(part)!;
        var body = node.Bodies[node.Creation.BodyIndexOf(part)];
        return body.GlobalTransform * PartGeometry.CentreMetres(node.Creation.DefinitionOf(part), placed.Cell, placed.Rotation).ToGodot();
    }

    // Just off the deck's far edge (the Switch stands on it), level with a part: the client never stands in the base.
    private Vector3 Beside(int part) => new(PartCentre(_host, part).X, 0, PartCentre(_host, _switch).Z + 1.2f);

    private void Teleport(Vector3 at) => _host.Main.PlayerSync.HostTeleport(Peer, new Vector3(at.X, Feet().Y, at.Z));

    private Vector3 Feet() => _client.Main.LocalPlayer!.GlobalPosition;

    // The clip a part's model shows (its "pose" readout) as `who` sees it, or null before it has one.
    private string? Clip(Instance who, int part) =>
        who.Main.Creations.NodeOf(_base)?.Displays.FirstOrDefault(d => d.Part == part)?.Clip;

    private void RequestView(int part) =>
        Request(InventoryActions.GetInventory, new GetInventoryArgs(InventoryRef.OfPart(_base, part)));

    private InventoryView? View(int index) =>
        _results[index] is { Ok: true, Value: { } value } ? value.Deserialize<InventoryView>(ActionJson.Options) : null;

    private void Stage(string name, Func<int, bool> tick) => Add(name, frame =>
    {
        if (frame == 0)
            _results.Clear();
        return tick(frame);
    });

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);
}
