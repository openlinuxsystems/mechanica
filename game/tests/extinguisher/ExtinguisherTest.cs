using Godot;
using Mechanica.Core.Player;
using Mechanica.Core.Quests;
using Mechanica.Game.Building;
using Mechanica.Game.Fire;
using Mechanica.Game.Player;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Fire Extinguisher with a host plus a client (Consumables batch C2a, engine item C8: held Use), on the vanilla world.
/// The client holds Use with one in hand at one of the crash site's fires: the host runs its script every step
/// (<c>holder.using()</c>), the foam sprays on both machines, and the fire goes out on both. Held on, the extinguishers are
/// used up one every <c>spray_seconds</c>; letting go stops the spray and the using up. Then the host sprays another fire
/// out, and the client sees the host's foam.
/// </summary>
public partial class ExtinguisherTest : StagedTest
{
    private const string Extinguisher = "vanilla:fire_extinguisher";

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private List<QuestMarker> _fires = [];
    private int _site;
    private int _count;
    private int _frames;

    protected override string Tag => "extinguisher";

    protected override int StageTimeoutFrames => 1200;

    private FireSync HostFire => _host.Main.Fire;

    private FireSync ClientFire => _client.Main.Fire;

    private int Peer => _client.Peer;

    private ItemTool ClientItem => _client.Main.Tools!.Item;

    private ItemTool HostItem => _host.Main.Tools!.Item;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("both machines are in and the crash site burns", _ =>
        {
            if (_host.Main.PlayerSync.Players.Count < 2 || ClientFire.Nodes.Count < 3 || _client.Main.Inventory.Own is null || _client.Main.Tools is null)
                return false;
            var site = _host.Main.Quests.SitesOf("vanilla:crash_site")[0];
            _site = site.Index;
            _fires = [.. site.Markers.Where(m => m.Type == "fire")];
            Check(_fires.Count == 3, $"three fires ({_fires.Count})");
            _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Extinguisher, 3);
            _host.Main.Inventory.Items!.PlayerInventory(_host.Peer).Add(Extinguisher, 2);
            return true;
        });
        Add("the client takes one in hand", frame =>
        {
            var tools = _client.Main.Tools!;
            if (_client.Main.Inventory.Own!.Count(Extinguisher) != 3)
                return false;
            if (tools.Current != tools.Item || ClientItem.InHand?.Item != Extinguisher)
            {
                ClientItem.Choose(Extinguisher);
                if (frame % 30 == 0)
                    tools.Use(ClientItem);
                return false;
            }
            if (frame == 1 || frame % 60 == 0)
                Face(_client, Peer, Fire(0));
            var body = (CharacterModel)_host.Main.PlayerSync.Players[Peer].Character!;
            return frame > 60 && body.HeldNode is not null && _client.Main.LocalPlayer!.Arms is CharacterModel { HeldNode: not null };
        });
        Add("holding Use, the client sprays the fire out on both machines", frame =>
        {
            if (frame == 0)
            {
                _count = _client.Main.Inventory.Own!.Count(Extinguisher);
                Check(ClientItem.Hold(), "the extinguisher is held to use");
                return false;
            }
            Face(_client, Peer, Fire(0));
            if (frame == 45)
            {
                Check(_host.Main.Scripts.Runtime.IsUsing(Peer), "the host hears Use held");
                Check(_host.Main.Sprays.IsSpraying(Peer), "the host draws the client's foam");
                Check(_client.Main.Sprays.IsSpraying(Peer), "the client sees its own foam");
                var spray = _host.Main.Sprays.ParticlesOf(Peer)!;
                var look = -_host.Main.PlayerSync.Players[Peer].Head.GlobalBasis.Z;
                Check(spray.Direction.Dot(look) > 0.95f, $"the foam goes where the client looks ({spray.Direction}, {look})");
            }
            if (HostFire.Actions!.SitesOut("vanilla:crash_site", "fire") < 1 || ClientFire.Nodes.Count != 2)
                return false;
            _frames = frame;
            Check(frame < 300, $"it goes out within five seconds ({frame} frames)");
            return true;
        });
        Add("held on, the extinguishers are used up one at a time", frame =>
        {
            if (_client.Main.Inventory.Own!.Count(Extinguisher) > _count - 1)
                return false;
            Check(_frames + frame >= 100, $"one lasts about two seconds ({_frames + frame} frames)");
            Check(_client.Main.Inventory.Own.Count(Extinguisher) == _count - 1 && ClientItem.InHand?.Item == Extinguisher,
                "the next one in the stack carries on");
            Check(ClientItem.Holding && _host.Main.Scripts.Runtime.IsUsing(Peer), "still spraying");
            return true;
        });
        Add("letting go stops the spray and the using up", frame =>
        {
            if (frame == 0)
            {
                ClientItem.LetGo();
                _count = _client.Main.Inventory.Own!.Count(Extinguisher);
                return false;
            }
            if (frame < 200)
                return false;
            Check(!_host.Main.Scripts.Runtime.IsUsing(Peer), "the host hears Use let go");
            Check(!_host.Main.Sprays.IsSpraying(Peer) && !_client.Main.Sprays.IsSpraying(Peer), "the foam stops on both machines");
            Check(_client.Main.Inventory.Own!.Count(Extinguisher) == _count, $"no more is used ({_client.Main.Inventory.Own.Count(Extinguisher)} of {_count})");
            return true;
        });
        Add("the host sprays another fire out and the client sees its foam", frame =>
        {
            var tools = _host.Main.Tools!;
            if (frame == 0)
                _host.Main.PlayerSync.HostTeleport(_host.Peer, Fire(1) + new Vector3(2.2f, 0.2f, 0.6f));
            if (tools.Current != tools.Item || HostItem.InHand?.Item != Extinguisher)
            {
                HostItem.Choose(Extinguisher);
                if (frame % 30 == 0)
                    tools.Use(HostItem);
                return false;
            }
            if (frame < 40)
                return false;
            Face(_host, _host.Peer, Fire(1));
            if (!HostItem.Holding)
            {
                HostItem.Hold();
                _frames = frame;
            }
            if (frame == _frames + 40)
            {
                var shown = _client.Main.PlayerSync.Players[_host.Peer];
                Check(_client.Main.Sprays.IsSpraying(_host.Peer),
                    $"the client draws the host's foam (guard {shown.Pose.Guard}, body shown {shown.Character?.Visible})");
            }
            if (HostFire.Actions!.SitesOut("vanilla:crash_site", "fire") < 2 || ClientFire.Nodes.Count != 1)
                return false;
            HostItem.LetGo();
            return true;
        });
        Add("the host's spray stops on the client", frame =>
            frame > 30 && !_client.Main.Sprays.IsSpraying(_host.Peer) && !_host.Main.Scripts.Runtime.IsUsing(_host.Peer));
    }

    private Vector3 Fire(int i) => _fires[i].Position.ToGodot();

    // Teleports a player 2.5 m from the fire (the client on the first call) and turns them to look at its foot.
    private void Face(Instance who, int peer, Vector3 fire)
    {
        var player = who.Main.LocalPlayer!;
        if (who == _client && player.GlobalPosition.DistanceTo(fire) > 4f)
            _host.Main.PlayerSync.HostTeleport(peer, fire + new Vector3(2.5f, 0.2f, 0));
        var d = (fire + new Vector3(0, 0.3f, 0) - player.Head.GlobalPosition).Normalized();
        player.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Asin(d.Y) };
    }
}
