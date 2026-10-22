using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Fire;
using Mechanica.Core.Player;
using Mechanica.Core.Quests;
using Mechanica.Core.Saves;
using Mechanica.Game.Fire;
using Mechanica.Scripting;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Fire with a host plus a client (D13, D10), on the vanilla world. The crash site's fires burn on both
/// machines, with flames and the burning sound; a wooden block the client places in one catches, and its fire
/// shows on the client too; a bucket of water the client throws lands on that fire and puts it (and the block) out on both,
/// which counts for the first quest. A save keeps the put-out fire out: a host loading it and a client joining
/// that host see the rest still burning.
/// </summary>
public partial class FireTest : StagedTest
{
    private const string Bucket = "vanilla:water_bucket_full";
    private const string EmptyBucket = "vanilla:water_bucket";
    private const string Wood = "vanilla:wood_block";
    private const string GettingStarted = "vanilla:getting_started";

    // The crash site's fire markers: six at the wreck's hatches and four at burst containers (mods/vanilla/pois/crash_site.json).
    private const int Fires = 10;

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private List<QuestMarker> _fires = [];
    private SaveGame? _save;

    protected override string Tag => "fire";

    private FireSync HostFire => _host.Main.Fire;

    private FireSync ClientFire => _client.Main.Fire;

    // The burst containers' fires come last and stand in the open; the wreck's sit in its hatches.
    private Vector3 FirstFire => _fires[^1].Position.ToGodot();

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the crash site burns on both machines", _ =>
        {
            if (_host.Main.PlayerSync.Players.Count < 2 || ClientFire.Nodes.Count < Fires)
                return false;
            _fires = [.. _host.Main.Quests.SitesOf("vanilla:crash_site")[0].Markers.Where(m => m.Type == "fire")];
            Check(_fires.Count == Fires, $"{Fires} fire markers ({_fires.Count})");
            Check(ClientFire.Fires.Values.All(f => f.Strength == 1 && f.Poi == "vanilla:crash_site"), "the client's fires burn at full strength");
            foreach (var marker in _fires)
            {
                var node = ClientFire.Nodes[$"site:{_host.Main.Quests.SitesOf("vanilla:crash_site")[0].Index}:{marker.Index}"];
                Check(node.GlobalPosition.DistanceTo(marker.Position.ToGodot()) < 0.01f, $"a fire stands on its marker ({node.GlobalPosition})");
                Check(node.Flames.Emitting && node.Smoke.Emitting, "it shows flames and smoke");
            }
            Check(HostFire.Loops.Count == Fires, $"each fire plays its burning loop ({HostFire.Loops.Count})");
            Check(_client.Main.Sounds.Loops.Values.Count(l => l.Event.QualifiedId == "vanilla:burn_wood") == Fires, "the client hears them");
            return true;
        });
        Add("a wooden block placed in a fire catches, and the client sees it burn", frame =>
        {
            if (frame == 0)
                _host.Main.PlayerSync.HostTeleport(_client.Peer, FirstFire + new Vector3(2.5f, 0, 0));
            if (frame == 20)
                Request(BuildActions.PlacePart, new PlacePartArgs(Wood, GridCell.Zero, Position: (FirstFire + new Vector3(0.75f, 0.125f, 0)).ToNumerics().ToVec3()));
            if (_results.Count == 0)
                return false;
            if (!_results[0].Ok)
                Check(false, $"the block is placed ({_results[0]})");
            if (ClientFire.Fires.Values.FirstOrDefault(f => f.Part is not null) is not { } burning)
                return false;
            var node = ClientFire.Nodes[burning.Id];
            Check(node.Visible && node.GlobalPosition.DistanceTo(FirstFire) < 1.5f, $"its flames are on the block ({node.GlobalPosition})");
            Check(_host.Main.Creations.Find(burning.Creation!.Value)!.FireOf(burning.Part!.Value) > 0, "the host has it burning");
            return true;
        });
        Add("a bucket of water puts the fire out on both machines", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                _host.Main.Inventory.Items!.PlayerInventory(_client.Peer).Add(Bucket, 1);
                LookAt(FirstFire + new Vector3(0, 0.3f, 0));
            }
            if (frame == 30)
            {
                var aim = (FirstFire + new Vector3(0, 0.3f, 0) - Eye()).Normalized();
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Bucket), new Vec3(aim.X, aim.Y, aim.Z)));
            }
            if (_results.Count == 0)
                return false;
            if (!_results[0].Ok)
                Check(false, $"the bucket is used ({_results[0]})");
            if (ClientFire.Nodes.Count != Fires - 1 || HostFire.Actions!.SitesOut("vanilla:crash_site", "fire") != 1)
                return false;
            Check(ClientFire.Fires.Values.All(f => f.Part is null), "the block went out with it");
            Check(HostFire.Loops.Count == Fires - 1, $"its sound stopped ({HostFire.Loops.Count})");
            Check(_client.Main.Inventory.Own!.Count(Bucket) == 0 && _client.Main.Inventory.Own.Count(EmptyBucket) == 1, "the bucket comes back empty");
            return _client.Main.Quests.State.Of(GettingStarted) is { Step: 0, Count: 1 };
        });
        Add("a save keeps the fire out", frame =>
        {
            if (frame == 0)
            {
                var diagnostics = new List<DataDiagnostic>();
                _save = SaveGame.Read(_host.Main.Saves.Capture("Fire World").Write(), "fire-world.json", diagnostics);
                Check(_save?.Fires is { Count: Fires } fires && fires.Count(f => f.Strength == 0) == 1,
                    $"the save holds all the fires, one out ({string.Join("; ", diagnostics)})");
                _harness.Stop(_client);
                _harness.Stop(_host);
                _harness.Save = _save;
                _host = _harness.StartHost();
                _client = _harness.StartClient("Client");
            }
            if (frame < 2 || HostFire.Actions!.Fires().Count < Fires || ClientFire.Nodes.Count < Fires - 1 || _client.Main.Quests.State.Of(GettingStarted) is not { } progress)
                return false;
            Check(HostFire.Actions.SitesOut("vanilla:crash_site", "fire") == 1, "the loaded host keeps it out");
            Check(progress is { Step: 0, Count: 1 }, $"the quest still counts it ({progress.Count})");
            return true;
        });
        Add("the client of the loaded world sees two fires", frame =>
        {
            if (frame < 30)
                return false;
            Check(ClientFire.Nodes.Count == Fires - 1, $"the rest burn ({ClientFire.Nodes.Count})");
            return true;
        });
    }

    private void Request<TArgs>(string action, TArgs args) => _client.Main.Actions.Request(action, args, _results.Add);

    private int SlotOf(string item) => _client.Main.Inventory.Own!.Items.First(s => s.Item == item).Slot;

    private Vector3 Eye() => _client.Main.LocalPlayer!.Head.GlobalPosition;

    private void LookAt(Vector3 target)
    {
        var d = (target - Eye()).Normalized();
        _client.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Asin(d.Y) };
    }
}
