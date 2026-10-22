using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Player;
using Mechanica.Game.Survival;
using Mechanica.Scripting;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Food and crops, host plus client, with vanilla's tomato (the food design). A tomato in hand rests on the open palm on
/// both machines; eating it plays the two bites (the held model goes from whole to one bite to two, then is gone and back)
/// on the client's arms and on the body the host draws, heals once, and a second click while chewing eats nothing. A tomato
/// seed planted in dry soil shows the tomato plant wilting, watered it picks up, ripe it is grown through to the end of its
/// grow clip, and harvested it gives two tomatoes and one or two seeds while the plant sinks away. The carrot and the
/// potato have the same bitten models in hand and plant clips.
/// </summary>
public partial class FoodTest : StagedTest
{
    private const string Tomato = "vanilla:tomato";
    private const string Seed = "vanilla:tomato_seed";
    private const string Bucket = "vanilla:water_bucket_full";
    private const string Soil = "vanilla:soil";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Vector3 _soilAt;
    private int _seedDrop;
    private int _soil;
    private int _soilPart;
    private readonly HashSet<int> _clientBites = [];
    private readonly HashSet<int> _hostBites = [];
    private float _health;
    private Node3D? _sinking;

    protected override string Tag => "food";

    protected override int StageTimeoutFrames => 900;

    private int Peer => _client.Peer;

    private CharacterModel HostView => (CharacterModel)_host.Main.PlayerSync.Players[Peer].Character!;

    private CharacterModel ClientView => (CharacterModel)_client.Main.LocalPlayer!.Arms!;

    private SurvivalActions HostSurvival => _host.Main.Survival.Actions!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins and is given tomatoes and tomato seeds", _ =>
        {
            if (_client.Main.Inventory.Own is null || _client.Main.Tools is null || _client.Main.LocalPlayer is not { } player
                || !player.IsOnFloor() || !_host.Main.PlayerSync.Players.ContainsKey(Peer) || _client.Main.Survival.Own is null)
                return false;
            _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Tomato, 3);
            _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Seed, 1);
            return true;
        });
        Add("a tomato rests on the open palm on both machines", frame =>
        {
            var tools = _client.Main.Tools!;
            if (Count(Tomato) != 3)
                return false;
            if (tools.Current != tools.Item || tools.Item.InHand?.Item != Tomato)
            {
                tools.Item.Choose(Tomato);
                if (frame % 30 == 0)
                    tools.Use(tools.Item);
                return false;
            }
            if (HostView.HeldNode is not { } host || ClientView.HeldNode is not { } client || HostView.Animator.Prop != CharacterProp.Food)
                return false;
            if (frame < 60)
                return false; // the arm settles into the hold
            foreach (var (held, who) in new[] { (host, "host"), (client, "client") })
            {
                var bites = Bites(held);
                Check(bites.Length == 3, $"the {who} has the whole tomato and its two bitten versions ({bites.Length})");
                Check(bites.Length == 3 && bites[0].Visible && !bites[1].Visible && !bites[2].Visible, $"the {who} shows it whole");
            }
            Check(ClientView.Animator.Prop == CharacterProp.Food, $"the client's arms hold food ({ClientView.Animator.Prop})");
            // The tomato's red is in its vertex colours: drawn white unless they are used as albedo.
            var skin = client.FindChildren("*Body*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().FirstOrDefault();
            Check(skin?.GetActiveMaterial(0) is BaseMaterial3D { VertexColorUseAsAlbedo: true },
                $"the tomato's skin takes its colour from its vertex colours ({skin?.GetActiveMaterial(0)})");
            // Palm up: the tomato sits on top of the hand, not under it.
            var hand = HostView.Skeleton.GlobalTransform * HostView.Skeleton.GetBoneGlobalPose(HostView.Skeleton.FindBone(CharacterRig.PropBone));
            var centre = Centre(host);
            GD.Print($"[food] hand {hand.Origin}, tomato {centre}, offset {hand.Basis.Inverse() * (centre - hand.Origin)}");
            Check(centre.DistanceTo(hand.Origin) < 0.15f && centre.Y > hand.Origin.Y, $"the tomato is on the palm (hand {hand.Origin}, tomato {centre})");
            return true;
        });
        Add("eating it plays two bites on both machines and heals once", frame =>
        {
            var item = _client.Main.Tools!.Item;
            if (frame == 0)
            {
                HostSurvival.Damage(Peer, 40, "test");
                return false;
            }
            if (frame == 10)
            {
                _health = _client.Main.Survival.Own!.Health;
                item.Click(place: false);
            }
            if (frame == 14)
                item.Click(place: false); // still chewing: eats nothing
            if (frame < 10)
                return false;
            Note(ClientView, _clientBites);
            Note(HostView, _hostBites);
            if (frame < 150)
                return false;
            Check(_client.Main.Survival.Own!.Health == _health + 20 && Count(Tomato) == 2,
                $"one tomato is eaten and heals 20 ({_health} -> {_client.Main.Survival.Own!.Health}, {Count(Tomato)} left)");
            Check(_clientBites.SetEquals([0, 1, 2, -1]), $"the client's arms show it whole, bitten twice and gone ({string.Join(", ", _clientBites)})");
            Check(_hostBites.SetEquals([0, 1, 2, -1]), $"the host's view shows it whole, bitten twice and gone ({string.Join(", ", _hostBites)})");
            var held = ClientView.HeldNode!;
            return Check(held.Visible && Bites(held)[0].Visible, "once the clip is over the next tomato is in hand, whole");
        });
        Add("the client places soil and plants a tomato seed in it, dry", frame =>
        {
            if (frame == 0)
            {
                _client.Main.Tools!.Select(0);
                _soilAt = _client.Main.LocalPlayer!.GlobalPosition + new Vector3(0, 0.125f, -2);
                Request(BuildActions.PlacePart, new PlacePartArgs(Soil, GridCell.Zero, Position: _soilAt.ToNumerics().ToVec3()));
                return false;
            }
            if (_results.Count == 1 && _results[0].Ok && _soil == 0)
            {
                var placed = _results[0].Value!.Value.Deserialize<PlacePartResult>(ActionJson.Options)!;
                (_soil, _soilPart) = (placed.Creation, placed.Part);
                _results.Clear();
                LookAt(_soilAt);
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Seed), Creation: _soil, Part: _soilPart));
                return false;
            }
            if (_results.Count == 1 && !_results[0].Ok)
                return Check(false, $"placing or planting failed ({_results[0]})");
            if (_soil == 0 || !_client.Main.Survival.CropNodes.TryGetValue((_soil, _soilPart), out var crop) || crop.View?.Seed != Seed || crop.Plant is null)
                return false;
            Check(crop.Plant.FindChild("*Stake*", true, false) is not null, "the stake and its tag stand in the soil from planting");
            Check(crop.GrowShown == 0f, $"the plant starts at the beginning of its grow clip ({crop.GrowShown})");
            return Check(crop.WiltShown == 1f, $"dry soil wilts it ({crop.WiltShown})");
        });
        Add("watered, the plant picks up", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Water();
            }
            if (frame == 60)
                Water(); // a second bucket: enough for the whole grow time
            var crop = _client.Main.Survival.CropNodes[(_soil, _soilPart)];
            return frame > 60 && _results.Count == 2 && crop.View?.Wetness != SoilWetness.Dry && crop.WiltShown == 0f;
        });
        Add("ripe, the plant has grown through to the end of its grow clip", frame =>
        {
            // A tomato drinks more than full soil holds (360 s of growth, 300 s of water): skip ahead, top it up, skip again.
            if (frame is 0 or 60)
                _host.Main.Scripts.Runtime.Time += 300;
            if (frame == 30)
                HostSurvival.WaterSoil(new ActionCaller(Peer, ActionSource.Player), _soil, _soilPart, 300);
            var crop = _client.Main.Survival.CropNodes[(_soil, _soilPart)];
            if (frame % 100 == 99)
                GD.Print($"[food] waiting for ripe: {crop.View}, grow shown {crop.GrowShown}");
            if (crop.View is not { Ripe: true } || crop.GrowShown < SoilState.Stages)
                return false;
            var ripe = crop.Plant!.FindChildren("Fruit*_ripe", nameof(Node3D), true, false).OfType<Node3D>().Count(f => f.Scale.X > 0.5f);
            return Check(ripe >= 6, $"the ripe tomatoes show ({ripe})");
        });
        Add("harvested, it gives two tomatoes and one or two seeds, and the plant sinks away", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                _sinking = _client.Main.Survival.CropNodes[(_soil, _soilPart)];
                Request(LogicActions.ActivatePart, new LogicPartArgs(_soil, _soilPart));
            }
            if (_results.Count == 0)
                return false;
            if (!_results[0].Ok)
                return Check(false, $"the harvest is refused ({_results[0]})");
            if (_client.Main.Survival.CropNodes.ContainsKey((_soil, _soilPart)) || Count(Tomato) != 4)
                return false;
            Check(Count(Seed) is 1 or 2, $"one or two seeds back ({Count(Seed)})");
            Check(IsInstanceValid(_sinking) && _sinking!.IsInsideTree() && _sinking.FindChild("DirtPuff", false, false) is not null,
                "the plant sinks into the soil in a puff of dirt");
            return true;
        });
        Add("and is gone after", _ => !IsInstanceValid(_sinking));
        Add("every vanilla crop has its bitten food in hand and a plant that grows, wilts and sways", _ =>
        {
            var survival = _client.Main.Survival;
            foreach (var crop in new[] { "tomato", "carrot", "potato" })
            {
                var food = survival.Catalog.FindDefinition("vanilla:" + crop);
                var seed = survival.Catalog.FindDefinition("vanilla:" + crop + "_seed");
                var held = food?.Held?.Mesh is { } mesh ? survival.Assets!.InstantiateScene(mesh, "vanilla") : null;
                Check(held is not null && Enumerable.Range(0, 3).All(k => held.FindChild($"eat_{k}", true, false) is Node3D),
                    $"the {crop} in hand has its whole and two bitten versions");
                var plant = seed?.Seed?.Plant is { } path ? survival.Assets!.SceneOf("vanilla:" + crop + "_seed", path) : null;
                var player = plant?.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();
                Check(player is not null && new[] { "grow", "wilt", "sway" }.All(c => player.HasAnimation(c)),
                    $"the {crop} plant has its grow, wilt and sway clips");
                held?.Free();
                plant?.Free();
            }
            return true;
        });
        Add("a single seed packet on the ground is drawn big enough to see in the grass", frame =>
        {
            if (frame == 0)
                _seedDrop = HostSurvival.AddDrop((_soilAt + new Vector3(1, 0, 0)).ToNumerics(), [new ItemStack(Seed, 1)])!.Id;
            if (!_client.Main.Survival.DropNodes.TryGetValue(_seedDrop, out var pile) || pile.Model is not { } model)
                return false;
            var size = model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
                .Select(m => m.GlobalTransform * m.GetAabb()).Aggregate((a, b) => a.Merge(b)).Size;
            var across = Mathf.Max(size.X, Mathf.Max(size.Y, size.Z));
            return Check(across is > 0.29f and < 0.31f, $"the packet is drawn 0.3 m across ({across:0.###} m)");
        });
    }

    // Records which bitten version of the food in hand shows now (-1: none, the food is gone).
    private static void Note(CharacterModel view, HashSet<int> seen)
    {
        if (view.HeldNode is not { } held)
            return;
        var bites = Bites(held);
        seen.Add(!held.Visible ? -1 : Array.FindIndex(bites, b => b.Visible));
    }

    private static Node3D[] Bites(Node3D held) =>
        [.. Enumerable.Range(0, 8).Select(i => held.FindChild($"eat_{i}", true, false) as Node3D).TakeWhile(n => n is not null).Cast<Node3D>()];

    // The middle of what the whole tomato draws, in world space.
    private static Vector3 Centre(Node3D held)
    {
        var meshes = Bites(held)[0].FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().ToList();
        if (Bites(held)[0] is MeshInstance3D own)
            meshes.Add(own);
        return meshes.Select(m => m.GlobalTransform * m.GetAabb()).Aggregate((a, b) => a.Merge(b)).GetCenter();
    }

    // Throws a full bucket at the soil.
    private void Water()
    {
        var aim = (_soilAt - _client.Main.LocalPlayer!.Head.GlobalPosition).Normalized();
        Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Bucket), new Vec3(aim.X, aim.Y, aim.Z)));
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    private int Count(string item) => _client.Main.Inventory.Own?.Items.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;

    private int SlotOf(string item) => _client.Main.Inventory.Own!.Items.First(s => s.Item == item).Slot;

    private void LookAt(Vector3 target)
    {
        var player = _client.Main.LocalPlayer!;
        var d = (target - player.Head.GlobalPosition).Normalized();
        player.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Asin(d.Y) };
    }
}
