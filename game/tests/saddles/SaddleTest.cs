using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Core.World;
using Mechanica.Game.Mods;
using Mechanica.Game.Player;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The parts map's batch 4 seats with a host and a client, all through the client's Action API: the client upgrades a
/// Saddle in place (Creative: free) and both sides see Saddle 2 with its extra links; it sits astride the Driver's
/// Saddle, and the host and the client both play the <c>sit_saddle</c> clip with the hands on the bars; on the Saddle
/// the <c>sit_saddle_ride</c> clip (hands free); and in the Passenger Seat the chair's hands-free <c>sit_ride</c>.
/// </summary>
public partial class SaddleTest : StagedTest
{
    private const int RigId = 1;
    private static readonly Vector3 RigAt = new(0, 0.6f, -6);

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _driversSaddle, _saddle, _passengerSeat;

    protected override string Tag => "saddles";

    protected override int StageTimeoutFrames => 900;

    private static string VanillaDir => Path.Combine(ModPaths.ModsRoot, "vanilla");

    public override void _Ready()
    {
        Add("a host and a client start on vanilla in Creative", frame =>
        {
            if (frame == 0)
            {
                _mods = ModLoader.Load([VanillaDir]);
                Check(!_mods.HasErrors, "vanilla loads");
                _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
                AddChild(_harness);
                _host = _harness.StartHost(mods: _mods);
                _client = _harness.StartClient("Client", mods: _mods);
            }
            return frame > 0 && _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null;
        });
        Add("the host spawns a deck with a Driver's Saddle, a Saddle and a Passenger Seat, and the client walks up", frame =>
        {
            if (frame == 0)
            {
                Spawn();
                _host.Main.PlayerSync.HostTeleport(_client.Peer, RigAt + new Vector3(0, 0.2f, 2.5f));
            }
            return frame >= 30 && _client.Main.Creations.NodeOf(RigId) is not null;
        });
        Wait("the deck settles", 90);
        Add("the client upgrades the Saddle in place and both sides see Saddle 2", frame =>
        {
            if (frame == 0)
                Request(InventoryActions.UpgradePart, new UpgradePartArgs(RigId, _saddle));
            if (_results.Count < 1)
                return false;
            if (!_results[0].Ok)
                return !Check(false, $"upgrading succeeds ({_results[0]})");
            if (_client.Main.Creations.NodeOf(RigId)?.Creation.FindPart(_saddle)?.Level != 1)
                return false;
            var host = _host.Main.Creations.Find(RigId)!;
            var client = _client.Main.Creations.NodeOf(RigId)!.Creation;
            Check(host.FindPart(_saddle)!.Level == 1, "the host raised it a level");
            Check(host.DefinitionOf(_saddle).Name == "Saddle 2" && client.DefinitionOf(_saddle).Name == "Saddle 2", "both see Saddle 2");
            Check(client.DefinitionOf(_saddle).Logic!.Outputs == 4 && client.DefinitionOf(_saddle).Mesh == "meshes/parts/saddle_2.glb",
                "with its four links and its level 2 model");
            _results.Clear();
            return true;
        });
        Sit("the Driver's Saddle", () => _driversSaddle, saddle: true, handsFree: false, clip: "sit_saddle");
        Stand();
        Sit("the Saddle", () => _saddle, saddle: true, handsFree: true, clip: "sit_saddle_ride");
        Stand();
        Sit("the Passenger Seat", () => _passengerSeat, saddle: false, handsFree: true, clip: "sit_ride");
        Stand();
    }

    // The client sits in a seat; the host seats it with that seat's style, and both sides' models play the clip.
    private void Sit(string what, Func<int> seat, bool saddle, bool handsFree, string clip)
    {
        Add($"the client sits on {what}", frame =>
        {
            if (frame == 0)
                Request(LogicActions.EnterSeat, new LogicPartArgs(RigId, seat()));
            if (_results.Count < 1 || !_client.Main.LocalPlayer!.Seated)
                return false;
            Check(_results[0].Ok, $"sitting down succeeds ({_results[0]})");
            Check(_host.Main.Logic.Seats.Occupant(RigId, seat()) == _client.Peer, "the host seats the client");
            var style = HostPlayer().SeatStyle;
            Check(style.Saddle == saddle && style.HandsFree == handsFree, $"the host sits it {(saddle ? "astride" : "in a chair")} ({style})");
            Check(_client.Main.LocalPlayer.SeatStyle == style, "the client works out the same style");
            _results.Clear();
            return true;
        });
        Add($"both sides show the client on {what} in its pose", frame =>
        {
            if (frame < 45)
                return false;
            foreach (var (side, player) in new[] { ("host", HostPlayer()), ("client", _client.Main.LocalPlayer!) })
            {
                if (player.Character is not CharacterModel model)
                    continue;
                var body = model.Animator.Body;
                Check(body.Any(c => c.Clip == clip && c.Weight > 0.9f),
                    $"the {side} plays {clip} ({string.Join(", ", body.Select(c => $"{c.Clip} {c.Weight:0.00}"))})");
            }
            var feet = _host.Main.Logic.SeatPose(RigId, seat())!.Value.Feet;
            Check(HostPlayer().GlobalPosition.DistanceTo(feet) < 0.1f, "the client's player is on the seat");
            return true;
        });
    }

    private void Stand() => Add("the client stands up", frame =>
    {
        if (frame == 0)
            Request(LogicActions.LeaveSeat, new NoArgs());
        if (_results.Count < 1 || _client.Main.LocalPlayer!.Seated || HostPlayer().Seated || frame < 20)
            return false;
        Check(_results[0].Ok, $"standing up succeeds ({_results[0]})");
        _results.Clear();
        return true;
    });

    // A metal deck (x -4..5, z -2..2) with the Driver's Saddle (3 x 4 x 4) at its left, the Saddle (3 x 3 x 3) in the
    // middle and the Passenger Seat (4 x 4 x 3) at its right, all facing -Z.
    private void Spawn()
    {
        var rig = new Creation(RigId, _mods);
        for (var x = -4; x <= 5; x++)
            for (var z = -2; z <= 2; z++)
                Place(rig, "vanilla:metal_block", new GridCell(x, 0, z));
        _driversSaddle = Place(rig, "vanilla:drivers_saddle", new GridCell(-4, 1, -2));
        _saddle = Place(rig, "vanilla:saddle", new GridCell(-1, 1, -1));
        _passengerSeat = Place(rig, "vanilla:passenger_seat", new GridCell(2, 1, -1));
        rig.SetAllBodyPoses(new Pose(RigAt.ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Creations.Add(rig, _host.Main.Assets);
    }

    private static int Place(Creation creation, string part, GridCell cell)
    {
        if (!creation.TryPlacePart(part, cell, GridRotation.Identity, out var placed, out var error))
            throw new InvalidOperationException(error);
        return placed.Id;
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    private PlayerController HostPlayer() => _host.Main.PlayerSync.Players[_client.Peer];
}
