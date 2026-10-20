using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Player;
using Mechanica.Game.Survival;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The water bucket, host plus client, on a flat world under shallow water: the client takes an empty bucket in hand
/// (held by the rim, handle down, seen on the host too), dips it in the water and it comes up full in the same hotbar
/// slot (handle up, water in it), throws the water (a blob of water flies and the bucket comes back empty) and puts the
/// bucket down with Force Build, where it stands as the bucket itself. Then one dip with three empty buckets fills
/// all three. Each step is checked on both machines.
/// </summary>
public partial class BucketTest : StagedTest
{
    private const string Empty = "vanilla:water_bucket";
    private const string Full = "vanilla:water_bucket_full";

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _hotbarSlot;
    private int _inventorySlot;
    private bool _sawFill;
    private bool _sawThrow;
    private bool _sawWater;
    private float _mostTilt;
    private Vector3 _restAxis;

    protected override string Tag => "bucket";

    protected override int StageTimeoutFrames => 900;

    private int Peer => _client.Peer;

    private Building.ItemTool Item => _client.Main.Tools!.Item;

    // Flat ground at 0 under 0.3 m of water: anywhere the client looks down is water to fill from.
    private static WorldGenData Shallows => WorldGenData.FromDefinition(new WorldGenDefinition
    {
        Terrain = new TerrainDefinition { CoastWidth = 0f, CoastBiomeWidth = 0f, SeaLevel = 0.3f, SeaFloor = -1f },
        Biomes = [new BiomeDefinition { Id = "flat", Name = "Flat", Weight = 1f, Color = "#5c7350" }],
    });

    // The client's player as the host draws it.
    private CharacterModel HostView => (CharacterModel)_host.Main.PlayerSync.Players[Peer].Character!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = Shallows };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins and is given an empty bucket", _ =>
        {
            if (_client.Main.Survival.Own is null || _client.Main.Inventory.Own is null || _client.Main.Tools is null
                || _client.Main.LocalPlayer is not { } player || !player.IsOnFloor() || !_host.Main.PlayerSync.Players.ContainsKey(Peer))
                return false;
            // Only the one bucket: the starting full ones go, and so does their hotbar slot.
            var pockets = _host.Main.Inventory.Items!.PlayerInventory(Peer);
            pockets.Remove(Full, pockets.Count(Full));
            pockets.Add(Empty, 1);
            return true;
        });
        Add("the empty bucket in hand hangs by its rim with the handle down", frame =>
        {
            var tools = _client.Main.Tools!;
            if (tools.Slots.ToList().IndexOf(Full) is >= 0 and var full)
            {
                if (frame % 10 == 0)
                    tools.Hotbar!.Set(full, null);
                return false;
            }
            if (!Item.Active && tools.Slots.ToList().IndexOf(Empty) is >= 0 and var slot)
            {
                _hotbarSlot = slot;
                tools.Select(slot);
            }
            if (Item.InHand?.Item != Empty || HostView.PropNode is not { } prop || prop.FindChild("water_bucket_body", true, false) is not Node3D body)
                return false;
            if (frame < 30)
                return false;
            _inventorySlot = Item.InHand!.Slot;
            Check(Item.Prop == CharacterProp.WaterBucket, $"the client holds the empty bucket ({Item.Prop})");
            Check(prop.Name == "water_bucket", $"the host draws the bucket in the client's hand ({prop.Name})");
            Check(!tools.Slots.Contains(Full), "no full bucket is on the hotbar");
            var handle = body.GetNode<Node3D>("water_bucket_body_handle");
            Check(handle.Rotation.X > Mathf.DegToRad(100), $"its handle hangs down ({Mathf.RadToDeg(handle.Rotation.X):F0}°)");
            Check(!body.GetNode<Node3D>("water_bucket_body_water").Visible, "it holds no water");
            return true;
        });
        Add("dipping it in the water fills it in the same hotbar slot", frame =>
        {
            var player = _client.Main.LocalPlayer!;
            if (frame == 0)
                player.CurrentInput = PlayerInput.None with { Pitch = -1.1f };
            if (frame == 10)
                Item.Click(place: false);
            _sawFill |= HostView.Animator.Gesture == "bucket_fill";
            if (_client.Main.Inventory.Own!.Items.FirstOrDefault(s => s.Slot == _inventorySlot)?.Item != Full || Item.InHand?.Item != Full)
                return false;
            // The hand changes at once; the hotbar changes when the host answers SetHotbarSlot.
            if (!_client.Main.Tools!.Slots.Contains(Full))
                return false;
            Check(_sawFill, "the host sees the client dip the bucket");
            Check(_client.Main.Tools!.Slots[_hotbarSlot] == Full, $"the full bucket is in the same hotbar slot ({string.Join(", ", _client.Main.Tools.Slots)})");
            Check(Item.Prop == CharacterProp.WaterBucketFull, $"the client holds the full bucket ({Item.Prop})");
            return true;
        });
        Add("the full bucket hangs from its raised handle with water in it", frame =>
        {
            if (frame < 90 || HostView.Animator.Gesture is not null)
                return false;
            var body = HostView.PropNode!.FindChild("water_bucket_body", true, false) as Node3D;
            Check(body is not null, "the host still draws the bucket");
            var handle = body!.GetNode<Node3D>("water_bucket_body_handle");
            Check(Mathf.Abs(handle.Rotation.X) < Mathf.DegToRad(15), $"its handle is up ({Mathf.RadToDeg(handle.Rotation.X):F0}°)");
            Check(body.GetNode<Node3D>("water_bucket_body_water").Visible, "the water shows");
            return true;
        });
        Add("throwing the water empties it and the water flies", frame =>
        {
            if (frame == 0)
            {
                _restAxis = (HostView.PropNode!.FindChild("water_bucket_body", true, false) as Node3D)!.Basis.Y;
                _client.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Pitch = -0.3f };
                Item.Click(place: false);
            }
            _sawThrow |= HostView.Animator.Gesture == "bucket_throw";
            _sawWater |= _client.Main.Survival.ProjectileNodes.Values.Any(n => n.Name.ToString().StartsWith("Projectile", StringComparison.Ordinal) && n.GetChildCount() > 0);
            if (HostView.PropNode?.FindChild("water_bucket_body", true, false) is Node3D body)
                _mostTilt = Mathf.Max(_mostTilt, body.Basis.Y.AngleTo(_restAxis));
            if (Item.InHand?.Item != Empty || _host.Main.Survival.Actions!.Projectiles.Any() || _client.Main.Survival.ProjectileNodes.Count > 0
                || !_client.Main.Tools!.Slots.Contains(Empty))
                return false;
            Check(_sawThrow, "the host sees the client throw");
            Check(_sawWater, "the client sees the water fly");
            Check(_mostTilt is > 1.4f and < 2.2f, $"the bucket swings up to pour ({Mathf.RadToDeg(_mostTilt):F0}°)");
            Check(_client.Main.Tools!.Slots[_hotbarSlot] == Empty, "the empty bucket is back in the same hotbar slot");
            return true;
        });
        Add("Force Build puts the bucket down, where it stands as itself", frame =>
        {
            if (frame == 60)
            {
                _client.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Pitch = -1.0f };
                Item.Click(place: true);
            }
            if (frame < 60 || _client.Main.Inventory.Own!.Count(Empty) != 0
                || _client.Main.Survival.DropNodes.Values.FirstOrDefault(d => d.Drop?.Items is [{ Item: Empty }]) is not { } pile)
                return false;
            Check(pile.FindChild("Model", false, false) is Node3D model && model.GetChildCount() > 0, "the pile shows the bucket's model");
            Check(_host.Main.Survival.Actions!.Drops.Values.Any(d => d.Placed && d.Items is [{ Item: Empty }]), "the host has it placed");
            return true;
        });
        Add("the client is given three more empty buckets", frame =>
        {
            if (frame == 0)
                _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Empty, 3);
            if (_client.Main.Inventory.Own!.Count(Empty) != 3 || _client.Main.Tools!.Slots.ToList().IndexOf(Empty) is not (>= 0 and var slot))
                return false;
            _client.Main.Tools.Select(slot);
            _client.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Pitch = -1.1f };
            return true;
        });
        Add("one dip fills every empty bucket the client carries", frame =>
        {
            if (frame == 30)
                Item.Click(place: false);
            var own = _client.Main.Inventory.Own!;
            if (frame < 30 || own.Count(Full) < 3)
                return frame < 600 ? false : Check(false, $"the buckets fill ({own.Count(Empty)} empty, {own.Count(Full)} full)");
            var host = _host.Main.Inventory.Items!.PlayerInventory(Peer);
            Check(own.Count(Empty) == 0 && host.Count(Full) == 3 && host.Count(Empty) == 0, "all three are full on both machines after one dip");
            return true;
        });
    }
}
