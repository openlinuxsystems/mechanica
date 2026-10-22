using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Creations;
using Mechanica.Game.Mods;
using Mechanica.Game.Player;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Parts batch 17a, host plus client on the vanilla Survival world. A Ladder (E2) stands on a floating floor against a
/// deck as tall as it: the client walks into it and grabs it, climbs up and steps off the top onto the deck, gets back on
/// from the top and climbs down to the floor, jumps off part way up, and climbs it while the whole Creation moves. The host
/// simulates every step (its player is climbing too) and draws the client climbing. Then the Chemical Bucket (E11): an
/// empty bucket dipped in a chemical pool comes up a Chemical Bucket, and used on a Chemical Container it pours its
/// Chemical in and comes back empty.
/// </summary>
public partial class LadderTest : StagedTest
{
    private const int Rig = 9017;
    private const string Ladder = "vanilla:ladder";
    private const string Empty = "vanilla:water_bucket";
    private const string Full = "vanilla:water_bucket_full";
    private const string ChemicalBucket = "vanilla:chemical_bucket";
    private const string Chemical = "vanilla:chemical";
    private const float Drift = 3f;

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private ModLoadResult _mods = null!;
    private Vector3 _above;
    private int _ladder;
    private int _container;
    private float _height;
    private float _lowest;
    private bool _climbed;
    private PoiPoolPlacement _pool = null!;
    private int _seenAt;
    private int _ticks;

    protected override string Tag => "ladder";

    protected override int StageTimeoutFrames => 1500;

    private int Peer => _client.Peer;

    private PlayerController Me => _client.Main.LocalPlayer!;

    private PlayerController HostSide => _host.Main.PlayerSync.Players[Peer];

    private Building.ItemTool Item => _client.Main.Tools!.Item;

    private CreationBody HostBody => _host.Main.Creations.NodeOf(Rig)!.Bodies[0];

    // The ladder's middle and its half height, on the host, in world space.
    private (Vector3 Centre, float Half) LadderOnHost()
    {
        var creation = _host.Main.Creations.NodeOf(Rig)!.Creation;
        var part = creation.FindPart(_ladder)!;
        var definition = creation.DefinitionOf(_ladder);
        var centre = HostBody.GlobalTransform * PartGeometry.CentreMetres(definition, part.Cell, part.Rotation).ToGodot();
        return (centre, definition.Size!.Value.Y * Core.MechanicaInfo.GridSize / 2f);
    }

    // Where the client stands in the rig's frame (the host's), as the host has it.
    private Vector3 InRig(PlayerController player) => HostBody.GlobalTransform.AffineInverse() * player.GlobalPosition;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins", _ =>
        {
            if (_client.Main.Survival.Own is null || _client.Main.Inventory.Own is null || _client.Main.Tools is null
                || _client.Main.LocalPlayer is not { } player || !player.IsOnFloor() || !_host.Main.PlayerSync.Players.ContainsKey(Peer))
                return false;
            _mods = ModLoader.Load([System.IO.Path.Combine(ModPaths.ModsRoot, "vanilla")]);
            Check(!_mods.HasErrors, "vanilla loads");
            return true;
        });
        // ---- The Chemical Bucket ----
        Add("the client is given an empty bucket and taken to a chemical pool", frame =>
        {
            Hold(Vector3.Zero);
            Me.CurrentInput = PlayerInput.None;
            if (frame == 0)
            {
                var pockets = _host.Main.Inventory.Items!.PlayerInventory(Peer);
                pockets.Remove(Full, pockets.Count(Full));
                pockets.Add(Empty, 1);
                var gen = _host.Main.Streamer.Generator;
                var feet = Me.GlobalPosition.ToNumerics();
                var poi = gen.Pois.Where(p => p.Poi.Prefab.Id == "chemical_pool").OrderBy(p => System.Numerics.Vector2.Distance(p.Centre, new(feet.X, feet.Z))).First();
                _pool = gen.PoolsAt(poi.Centre.X, poi.Centre.Y).Single();
                _host.Main.PlayerSync.HostTeleport(Peer, _pool.Centre.ToGodot() + new Vector3(0, 0.5f, 0));
            }
            var tools = _client.Main.Tools!;
            if (!Item.Active && tools.Slots.ToList().IndexOf(Empty) is >= 0 and var slot)
                tools.Select(slot);
            if (frame % 200 == 100)
                GD.Print($"[ladder] at {Me.GlobalPosition} (pool {_pool.Centre}), floor {Me.IsOnFloor()}, hand {Item.InHand?.Item}, hotbar {string.Join(",", tools.Slots)}");
            return frame > 60 && Me.IsOnFloor() && Me.GlobalPosition.DistanceTo(_pool.Centre.ToGodot()) < 3f && Item.InHand?.Item == Empty;
        });
        Add("dipping it in the pool fills it with chemical", frame =>
        {
            Hold(Vector3.Zero);
            Me.CurrentInput = PlayerInput.None with { Pitch = -1.1f };
            if (frame == 10)
                Item.Click(place: false);
            var host = _host.Main.Inventory.Items!.PlayerInventory(Peer);
            if (_client.Main.Inventory.Own!.Count(ChemicalBucket) == 0 || host.Count(ChemicalBucket) == 0)
                return frame >= 600 && Fail("the bucket fills");
            Check(host.Count(Empty) == 0 && host.Count(Chemical) == 0, "the bucket itself is filled; no loose Chemical");
            return true;
        });

        // ---- The Ladder ----
        Add("a ladder on a floating floor against a deck as tall as it", _ =>
        {
            // High above the pool, where nothing gets in its way.
            _above = Me.GlobalPosition + new Vector3(0, 40, 0);
            var rig = new Creation(Rig, _mods);
            // The floor (3 m by 5 m), the ladder on it facing +z, and behind it a deck whose top is the ladder's top, held up
            // by a column at its back corner.
            for (var x = -6; x < 6; x++)
            {
                for (var z = -9; z < 12; z++)
                    Place(rig, "vanilla:metal_block", new GridCell(x, 0, z));
            }
            for (var y = 1; y < 8; y++)
                Place(rig, "vanilla:metal_block", new GridCell(-6, y, -9));
            for (var x = -6; x < 6; x++)
            {
                for (var z = -9; z < -1; z++)
                    Place(rig, "vanilla:metal_block", new GridCell(x, 8, z));
            }
            _ladder = Place(rig, Ladder, new GridCell(-1, 1, -1));
            _container = Place(rig, "vanilla:chemical_container", new GridCell(2, 1, 6));
            rig.SetAllBodyPoses(new Transform3D(Basis.Identity, _above).ToPose());
            var node = _host.Main.Creations.Add(rig, _host.Main.Assets);
            foreach (var body in node.Bodies)
                body.GravityScale = 0;
            return true;
        });
        Add("the client sees the rig and is put on its floor in front of the ladder", frame =>
        {
            Hold(Vector3.Zero);
            if (frame == 0)
                _seenAt = -1;
            if (_client.Main.Creations.NodeOf(Rig) is null)
                return false;
            if (_seenAt < 0)
            {
                _seenAt = frame;
                var (centre, half) = LadderOnHost();
                _host.Main.PlayerSync.HostTeleport(Peer, centre + new Vector3(0, -half + 0.1f, 0.9f));
            }
            Me.CurrentInput = PlayerInput.None;
            if (frame % 200 == 100)
                GD.Print($"[ladder] client at {Me.GlobalPosition} (host {HostSide.GlobalPosition}), floor {Me.IsOnFloor()}, ground {Me.Ground}, ladder {LadderOnHost()}");
            return frame > _seenAt + 60 && Me.IsOnFloor() && Me.Ground?.Creation == Rig;
        });
        Add("the Chemical Bucket is in hand, back on the rig by the Chemical Container", frame =>
        {
            Hold(Vector3.Zero);
            Me.CurrentInput = PlayerInput.None;
            if (frame == 0)
            {
                // On the floor half a metre in front of the container (its face is at z = 1.375); 1.4 put the player into it.
                var at = HostBody.GlobalTransform * new Vector3(0.6f, 0.3f, 0.9f);
                _host.Main.PlayerSync.HostTeleport(Peer, at);
            }
            var tools = _client.Main.Tools!;
            if (Item.InHand?.Item != ChemicalBucket && tools.Slots.ToList().IndexOf(ChemicalBucket) is >= 0 and var slot)
                tools.Select(slot);
            return frame > 60 && Me.IsOnFloor() && Me.Ground?.Creation == Rig && Item.InHand?.Item == ChemicalBucket;
        });
        Add("used on the Chemical Container it pours the Chemical in and comes back empty", frame =>
        {
            Hold(Vector3.Zero);
            var creation = _host.Main.Creations.NodeOf(Rig)!.Creation;
            var part = creation.FindPart(_container)!;
            var target = HostBody.GlobalTransform * PartGeometry.CentreMetres(creation.DefinitionOf(_container), part.Cell, part.Rotation).ToGodot();
            var look = target - Me.Camera.GlobalPosition;
            Me.CurrentInput = PlayerInput.None with
            {
                Yaw = Mathf.Atan2(-look.X, -look.Z),
                Pitch = Mathf.Atan2(look.Y, new Vector2(look.X, look.Z).Length()),
            };
            if (frame == 30)
                Item.Click(place: false);
            var tank = _host.Main.Inventory.Items!.Find(InventoryRef.OfPart(Rig, _container));
            if (tank is null || tank.Count(Chemical) == 0 || _client.Main.Inventory.Own!.Count(Empty) == 0)
                return frame >= 600 && Fail($"it pours ({tank?.Count(Chemical)} in the tank)");
            Check(tank.Count(Chemical) == 5, $"5 Chemical went in ({tank.Count(Chemical)})");
            Check(_client.Main.Inventory.Own!.Count(ChemicalBucket) == 0, "the Chemical Bucket is gone");
            Check(_host.Main.Survival.Actions!.Projectiles.Count() == 0, "nothing was thrown");
            return true;
        });
        Add("back in front of the ladder", frame =>
        {
            Hold(Vector3.Zero);
            Me.CurrentInput = PlayerInput.None;
            if (frame == 0)
            {
                var (centre, half) = LadderOnHost();
                _host.Main.PlayerSync.HostTeleport(Peer, centre + new Vector3(0, -half + 0.1f, 0.9f));
            }
            return frame > 60 && Me.IsOnFloor() && Me.Ground?.Creation == Rig;
        });
        Add("walking into the ladder grabs it, on the client and on the host", frame =>
        {
            Hold(Vector3.Zero);
            Me.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            if (!Me.Climbing || !HostSide.Climbing)
                return false;
            Check(Me.ClimbingOn == (Rig, _ladder), $"the client holds the ladder ({Me.ClimbingOn})");
            _height = InRig(HostSide).Y;
            return true;
        });
        Add("forward climbs it and the top steps off onto the deck", frame =>
        {
            Hold(Vector3.Zero);
            Me.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            _climbed |= Me.Climbing && InRig(HostSide).Y > _height + 1f;
            var (centre, half) = LadderOnHost();
            var top = centre.Y + half;
            if (Me.Climbing || HostSide.Climbing || !Me.IsOnFloor() || HostSide.GlobalPosition.Z > centre.Z - 0.3f)
                return false;
            Check(_climbed, "it climbed a metre and more on the ladder");
            Check(Mathf.Abs(HostSide.GlobalPosition.Y - top) < 0.15f, $"the client stands on the deck at the ladder's top ({HostSide.GlobalPosition.Y:F2} vs {top:F2})");
            Check(Me.Ground?.Creation == Rig, "on the rig");
            return true;
        });
        // Compared once the client stops: while it walks, the host's copy trails it by the input delay.
        Add("standing still on the deck, the client and the host agree where", frame =>
        {
            Hold(Vector3.Zero);
            Me.CurrentInput = PlayerInput.None;
            var apart = Me.GlobalPosition.DistanceTo(HostSide.GlobalPosition);
            if (apart < 0.3f)
                return Check(true, "the client and the host agree where");
            return frame >= 60 && Fail($"the client and the host agree where ({apart:F2} m apart)");
        });
        Add("walking back onto the ladder from the deck gets on it to climb down", frame =>
        {
            Hold(Vector3.Zero);
            Me.CurrentInput = frame < 30 ? PlayerInput.None with { Yaw = Mathf.Pi } : new PlayerInput(0, 1, Mathf.Pi, 0, false, false);
            if (!Me.Climbing || !HostSide.Climbing)
                return false;
            _lowest = InRig(HostSide).Y;
            return true;
        });
        Add("back climbs down, and back on the floor lets go", frame =>
        {
            Hold(Vector3.Zero);
            Me.CurrentInput = new PlayerInput(0, -1, Mathf.Pi, 0, false, false);
            if (Me.Climbing || HostSide.Climbing || !Me.IsOnFloor())
                return false;
            var (centre, half) = LadderOnHost();
            Check(InRig(HostSide).Y < _lowest - 1f, $"it came down the ladder ({InRig(HostSide).Y:F2} from {_lowest:F2})");
            Check(Mathf.Abs(HostSide.GlobalPosition.Y - (centre.Y - half)) < 0.15f, "it stands on the floor");
            return true;
        });
        Add("grabbing it again and jumping lets go and pushes off", frame =>
        {
            Hold(Vector3.Zero);
            if (frame == 0)
                _climbed = false;
            if (!_climbed)
            {
                Me.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
                if (Me.Climbing && InRig(HostSide).Y > 0.7f)
                    _climbed = true;
                return false;
            }
            Me.CurrentInput = PlayerInput.None with { Jump = true };
            if (Me.Climbing || HostSide.Climbing)
                return false;
            var (centre, _) = LadderOnHost();
            Check(HostSide.GlobalPosition.Z > centre.Z + 0.4f || HostSide.Velocity.Z > 0.5f, "it pushes off away from the ladder");
            return true;
        });
        Add("back on the floor", frame =>
        {
            Hold(Vector3.Zero);
            Me.CurrentInput = PlayerInput.None;
            return frame > 30 && Me.IsOnFloor() && !Me.Climbing;
        });
        Add("the rig drifts sideways and the client climbs its ladder all the same", frame =>
        {
            Hold(new Vector3(Drift, 0, 0));
            if (frame == 0)
                _climbed = false;
            Me.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            if (!Me.Climbing || !HostSide.Climbing)
                return frame >= 600 && Fail("the client grabs the moving ladder");
            if (!_climbed)
            {
                _climbed = true;
                _height = InRig(HostSide).Y;
            }
            var (centre, _) = LadderOnHost();
            var across = Mathf.Abs(HostSide.GlobalPosition.X - centre.X);
            if (across > 0.4f)
                return Fail($"the climber stays on the moving ladder ({across:F2} m off its middle)");
            if (InRig(HostSide).Y < _height + 0.8f)
                return false;
            Check(HostSide.Ground?.Creation == Rig, "the ladder's Body carries the climber");
            Check(Me.Body.HasFlag(Core.Net.BodyFlags.Climbing), "the client's snapshot says it climbs");
            return true;
        });
        Add("the host draws the client in the climb", _ =>
        {
            Hold(Vector3.Zero);
            var model = (CharacterModel)HostSide.Character!;
            return model.Animator.Mode == Core.Characters.CharacterMode.Climb || Fail($"the climb clip plays ({model.Animator.Mode})");
        });
        Add("jump off and stop the rig", frame =>
        {
            Hold(Vector3.Zero);
            Me.CurrentInput = PlayerInput.None with { Jump = frame < 5 };
            return frame > 60 && !Me.Climbing && Me.IsOnFloor();
        });
    }

    // Records a failed check and ends the stage.
    private bool Fail(string message)
    {
        Check(false, message);
        return true;
    }

    // Keeps the rig still (or drifting at velocity) in the air on the host.
    private void Hold(Vector3 velocity)
    {
        if (_host.Main.Creations.NodeOf(Rig) is not { } node)
            return;
        // Where the client is now and then, for a stage that times out.
        if (++_ticks % 240 == 0 && _client.Main.LocalPlayer is { } me && _host.Main.PlayerSync.Players.TryGetValue(Peer, out var host))
            GD.Print($"[ladder] client {InRig(me)} climbing {me.Climbing}, host {InRig(host)} climbing {host.Climbing}, floor {me.IsOnFloor()}");
        foreach (var body in node.Bodies)
        {
            body.LinearVelocity = velocity;
            body.AngularVelocity = Vector3.Zero;
        }
    }

    private static int Place(Creation creation, string part, GridCell cell)
    {
        if (!creation.TryPlacePart(part, cell, GridRotation.Identity, out var placed, out var error))
            throw new InvalidOperationException(error);
        return placed.Id;
    }
}
