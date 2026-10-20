using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// A Workshop's Fabricator (a POI <c>part</c> marker), host plus a client, in the vanilla Survival world. The host puts
/// exactly one world-owned Fabricator at every Workshop's marker when the world starts. The client walks into the story's
/// Workshop: both peers show it standing on its marker, anchored, world-owned. The client opens it, crafts a chest and
/// takes it out; it cannot lift, hit, paint or build onto it, and the host's copy stays exactly as it was. A save of the
/// world holds each one once, so loading it adds none. Then the client picks it up: it is in their pockets, the marker is
/// taken, and loading the world again puts no other there.
/// </summary>
public partial class WorkshopTest : StagedTest
{
    private const string Fabricator = "vanilla:fabricator";
    private const string Chest = "vanilla:chest";

    private readonly List<ActionResult> _refusals = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private IReadOnlyList<WorldPartSpot> _spots = [];
    private WorldPartSpot _spot = null!;
    private int _id;
    private int _revision;
    private Pose _pose;
    private bool _craftSent;
    private ActionResult? _craft;
    private ActionResult? _take;
    private ActionResult? _pickUp;

    protected override string Tag => "workshop";

    protected override int StageTimeoutFrames => 1800;

    private Creation HostFabricator => _host.Main.Creations.Find(_id)!;

    private InventoryRef Station => InventoryRef.OfPart(_id, 1);

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world", _ =>
            _client.Main.Survival.Own is not null && _client.Main.LocalPlayer is { } player && player.IsOnFloor() && _host.Main.Inventory.Items is not null);
        Add("the host put one world-owned Fabricator at every Workshop marker", _ =>
        {
            _spots = WorldParts.SpotsOf(_host.Main.Streamer.Generator);
            Check(_spots.Count > 0 && _spots.All(s => s.Part == Fabricator), $"the vanilla Workshops have Fabricator markers ({_spots.Count})");
            var owned = _host.Main.Creations.All.Where(c => c.IsWorldOwned).ToList();
            Check(owned.Select(c => c.WorldPart).Order(StringComparer.Ordinal).SequenceEqual(_spots.Select(s => s.Key).Order(StringComparer.Ordinal)),
                $"exactly one per marker ({string.Join(", ", owned.Select(c => c.WorldPart))})");
            _spot = _spots.First(s => s.Poi == "vanilla:outpost");
            var fabricator = owned.Single(c => c.WorldPart == _spot.Key);
            _id = fabricator.Id;
            Check(fabricator.AuthorityPeer == Creation.HostPeer && fabricator.IsAnchored(1), "simulated by the host, anchored");
            return true;
        });
        Add("the client walks into the Workshop and both peers show the Fabricator on its marker", frame =>
        {
            if (frame == 0)
            {
                // In front of the Fabricator (its front is its local -Z), on the bay floor.
                var front = new Vector3(-Mathf.Sin(_spot.Yaw), 0, -Mathf.Cos(_spot.Yaw));
                _host.Main.PlayerSync.HostTeleport(_client.Peer, _spot.Floor.ToGodot() + front * 2.6f + Vector3.Up * 0.2f);
                return false;
            }
            if (_host.Main.Creations.NodeOf(_id) is not { } node || _client.Main.Creations.NodeOf(_id) is not { } shown || frame < 60)
                return false;
            var floor = _spot.Floor.ToGodot();
            foreach (var (who, main) in new[] { ("host", _host.Main), ("client", _client.Main) })
            {
                var creation = main.Creations.Find(_id)!;
                Check(creation.WorldPart == _spot.Key && creation.IsWorldOwned, $"{who}: it is world-owned ({creation.WorldPart})");
                Check(creation.Parts.Count == 1 && creation.Parts.First().Part == Fabricator, $"{who}: it is one Fabricator");
                var box = CreationBounds.World(creation);
                var bottom = new Vector3((box.Min.X + box.Max.X) / 2, box.Min.Y, (box.Min.Z + box.Max.Z) / 2);
                Check(bottom.DistanceTo(floor) < 0.02f, $"{who}: it stands on the marker ({bottom} vs {floor})");
                Check(ToolActions.LiftRefusal(creation, _host.Main.Build.Settings!.Lift, [], _client.Peer)?.Contains("belongs to the world") == true,
                    $"{who}: the Lift says it can't take it");
            }
            Check(node.IsBodyFrozen(0), "the host holds its body still: nothing knocks it over");
            var expected = new Vector3(-Mathf.Sin(_spot.Yaw), 0, -Mathf.Cos(_spot.Yaw));
            var facing = -node.Bodies[0].GlobalBasis.Z;
            Check(facing.Dot(expected) > 0.999f, $"it faces out of its bay ({facing} vs {expected})");
            Check(shown.Bodies.Count == 1, "the client draws it");
            _revision = HostFabricator.Revision;
            _pose = HostFabricator.BodyPose(0);
            return true;
        });
        Add("the client opens it and crafts a chest", frame =>
        {
            if (frame == 0)
            {
                var pockets = _host.Main.Inventory.Items!.PlayerInventory(_client.Peer);
                pockets.Add("vanilla:scrap_wood_block", 16);
                pockets.Add("vanilla:scrap_metal_block", 8);
                _client.Main.Inventory.Open(Station);
                return false;
            }
            if (!_client.Main.Inventory.Shown.ContainsKey(Station))
                return false;
            if (!_craftSent)
            {
                _craftSent = true;
                _client.Main.Actions.Request(InventoryActions.Craft, new CraftArgs(_id, 1, Chest), r => _craft = r);
                return false;
            }
            if (_craft is null)
                return false;
            if (!_craft.Ok)
                Check(false, $"the craft is queued ({_craft})");
            return HostFabricator.InventoryOf(1)!.Count(Chest) == 1
                && _client.Main.Inventory.Shown.GetValueOrDefault(Station) is { } view && view.Count(Chest) == 1;
        });
        Add("the client takes the chest out", frame =>
        {
            if (frame == 0)
            {
                var slot = HostFabricator.InventoryOf(1)!.Slots.Select((s, i) => (s, i)).First(x => x.s?.Item == Chest).i;
                _client.Main.Actions.Request(InventoryActions.MoveItems, new MoveItemsArgs(Station, slot, InventoryRef.OfPlayer(_client.Peer)), r => _take = r);
                return false;
            }
            if (_take is null)
                return false;
            Check(_take.Ok, $"the chest moves to the client ({_take})");
            return _host.Main.Inventory.Items!.PlayerInventory(_client.Peer).Count(Chest) == 1;
        });
        Add("the client can't lift, hit, paint, weld or build onto it", frame =>
        {
            if (frame == 0)
            {
                _client.Main.Inventory.Close(Station);
                // The tools the client would need, so each refusal is the Fabricator's own.
                _host.Main.Inventory.Items!.PlayerInventory(_client.Peer).Add(HotbarTools.Weld, 1);
                Request(ToolActions.CaptureLift, new CaptureLiftArgs(_id));
                Request(ToolActions.Sledge, new SledgeArgs(_id, 1));
                Request(ToolActions.Paint, new PaintArgs(_id, [1], "#ff0000"));
                Request(ToolActions.WeldParts, new WeldPartsArgs(_id, 1, 1));
                Request(BuildActions.PlacePart, new PlacePartArgs("vanilla:wood_block", new GridCell(0, 9, 0), Creation: _id));
                Request(InventoryActions.UpgradePart, new UpgradePartArgs(_id, 1));
                return false;
            }
            if (_refusals.Count < 6)
                return false;
            foreach (var result in _refusals)
                Check(!result.Ok && result.Code == ActionErrors.Rejected && result.Message!.Contains("belongs to the world"), $"refused: {result}");
            return true;
        });
        Add("the Fabricator is just as it was on both peers", frame =>
        {
            if (frame < 30)
                return false;
            var fabricator = HostFabricator;
            Check(fabricator.Parts.Count == 1 && fabricator.PartDamage(1) == 0 && fabricator.FindPart(1)!.Color is null && fabricator.LevelOf(1) == 0,
                "whole, undamaged, unpainted, not upgraded");
            Check(fabricator.Revision == _revision && !fabricator.OnLift && fabricator.IsAnchored(1), "its graph never changed and it is still anchored");
            _host.Main.Creations.NodeOf(_id)!.SyncToCreation();
            Check(fabricator.BodyPose(0).Position.ToGodot().DistanceTo(_pose.Position.ToGodot()) < 1e-3f, "it has not moved");
            Check(_client.Main.Creations.Find(_id) is { Parts.Count: 1, IsWorldOwned: true }, "the client still shows it");
            Check(_host.Main.Build.Tools!.LiftOf(_client.Peer)?.Creation != _id && _host.Main.Build.Tools.CaptureOf(_client.Peer) is null, "no Lift holds it");
            return true;
        });
        Add("a save holds each Fabricator once, so loading it adds none", _ =>
        {
            var save = _host.Main.Saves.Capture("workshop-test");
            var diagnostics = new List<DataDiagnostic>();
            var loaded = save.ReadCreations(_host.Main.Build.Catalog, save.Name, diagnostics);
            Check(loaded.Count(c => c.IsWorldOwned) == _spots.Count, $"the save holds {loaded.Count(c => c.IsWorldOwned)} of {_spots.Count}");
            var plan = WorldParts.Reconcile(_spots, loaded, _host.Main.Build.Catalog, () => 100000);
            Check(plan.Add.Count == 0 && plan.Remove.Count == 0 && plan.Moved.Count == 0, "and loading it places, moves and removes nothing");
            return true;
        });
        Add("the client picks it up, and loading the world puts no other there", frame =>
        {
            if (frame == 0)
            {
                _client.Main.Actions.Request(BuildActions.RemovePart, new RemovePartArgs(_id, 1), r => _pickUp = r);
                return false;
            }
            if (_pickUp is null)
                return false;
            Check(_pickUp.Ok, $"it comes up ({_pickUp})");
            if (_host.Main.Creations.Find(_id) is not null || _client.Main.Creations.Find(_id) is not null)
                return false;
            Check(_host.Main.Inventory.Items!.PlayerInventory(_client.Peer).Count(Fabricator) == 1, "the Fabricator is in the client's pockets");
            Check(_host.Main.Streamer.State.IsDestroyed(WorldParts.ObjectOf(_spot.Key)!.Value), "its marker is taken");
            var save = _host.Main.Saves.Capture("workshop-test");
            Check(save.Destroyed.Contains(WorldParts.ObjectOf(_spot.Key)!.Value), "the save says so");
            var diagnostics = new List<DataDiagnostic>();
            var loaded = save.ReadCreations(_host.Main.Build.Catalog, save.Name, diagnostics);
            var plan = WorldParts.Reconcile(_spots, loaded, _host.Main.Build.Catalog, () => 100000, save.Destroyed.Contains);
            Check(plan.Add.Count == 0 && plan.Remove.Count == 0, $"loading it places none ({plan.Add.Count} placed, {plan.Remove.Count} removed)");
            return true;
        });
    }

    private void Request<TArgs>(string action, TArgs args) => _client.Main.Actions.Request(action, args, _refusals.Add);
}
