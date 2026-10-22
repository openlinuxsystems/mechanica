using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Survival;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Items on the ground, host plus client: every item (each upgrade level too) has a model to show lying there, its
/// own or a tool's prop, never a crate. A bot's drop of three kinds of item (a Rakehand's Component Kit, Gasoline and
/// Scrap Metal Blocks) lies as three piles on both machines, each the item's model sized 0.3 m across, floating in
/// its glow with its beam, with no floating label; the Use prompt names the pile under the crosshair, and Use picks
/// up that one only. A dropped Weld Tool shows its prop.
/// </summary>
public partial class DropsTest : StagedTest
{
    private const string Kit = "vanilla:component_kit";
    private const string Gas = "vanilla:gasoline";
    private const string Scrap = "vanilla:scrap_metal_block";
    private const string Weld = "vanilla:weld_tool";

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private SurvivalHud _hud = null!;
    private Vector3 _at;
    private int _weld;

    protected override string Tag => "drops";

    protected override int StageTimeoutFrames => 900;

    private int Peer => _client.Peer;

    private SurvivalActions HostSurvival => _host.Main.Survival.Actions!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins", _ =>
        {
            if (_client.Main.Survival.Own is null || _client.Main.Inventory.Own is null || _client.Main.LocalPlayer is not { } player
                || !player.IsOnFloor() || !_host.Main.PlayerSync.Players.ContainsKey(Peer))
                return false;
            _hud = new SurvivalHud
            {
                Name = "TestSurvivalHud", Survival = _client.Main.Survival, Actions = _client.Main.Actions, Players = _client.Main.PlayerSync,
                Catalog = _client.Main.Survival.Catalog, UseHeld = () => false, Playing = () => true,
            };
            _client.Main.AddChild(_hud);
            return true;
        });
        Add("every item has a model to lie on the ground as", _ =>
        {
            var mods = _client.Main.Mods!;
            var items = mods.Parts.Values.SelectMany(p => Enumerable.Range(0, PartUpgrades.MaxLevel(p.Definition) + 1)
                .Select(level => PartUpgrades.ItemKey(p.QualifiedId, level))).ToList();
            var missing = items.Where(id => DropModels.Resolve(id, mods, _client.Main.Assets, _client.Main.Survival.ToolModel) is not (DropLook.Model or DropLook.Tool)).ToList();
            Check(items.Count > 400, $"every item is checked ({items.Count})");
            Check(DropModels.Resolve(Weld, mods, _client.Main.Assets, _client.Main.Survival.ToolModel) == DropLook.Tool, "the Weld Tool lies as its prop");
            return Check(missing.Count == 0, $"no item lies as a crate ({string.Join(", ", missing)})");
        });
        Add("a bot's drop of three kinds lies as three glowing piles, on the client too", frame =>
        {
            if (frame == 0)
            {
                var player = _client.Main.LocalPlayer!;
                _at = player.GlobalPosition + new Vector3(0, 0, -2.2f);
                HostSurvival.AddDrop(_at.ToNumerics() + new System.Numerics.Vector3(0, 0.8f, 0), [new ItemStack(Kit, 1), new ItemStack(Gas, 3), new ItemStack(Scrap, 8)]);
            }
            if (HostSurvival.Drops.Count != 3 || _client.Main.Survival.DropNodes.Count != 3 || _client.Main.Survival.DropNodes.Values.Any(p => p.Model is null))
                return false;
            Check(HostSurvival.Drops.Values.Select(d => d.Items.Single().Item).SequenceEqual([Kit, Gas, Scrap]), "the host lays one pile per kind");
            foreach (var pile in _client.Main.Survival.DropNodes.Values)
            {
                var item = pile.Drop!.Items.Single().Item;
                Check(!pile.Text.Contains('\n') && !pile.FindChild("Label", false, false)!.Get("visible").AsBool(), $"{item} has no floating label");
                Check(pile.Glow.Visible && pile.Glow.FindChild("Glow", false, false) is MeshInstance3D && pile.Glow.FindChild("Beam", false, false) is MeshInstance3D, $"{item} glows, with a beam");
                Check(!((Node3D)pile.FindChild("Box", false, false)!).Visible, $"{item} is not a crate");
                var across = Across(pile.Model!);
                Check(across is > 0.29f and < 0.31f, $"{item} is drawn 0.3 m across ({across:0.###} m)");
                var middle = Middle(pile.Model!) - pile.GlobalPosition;
                Check(Mathf.Abs(middle.Y - DropAim.Middle) < 0.05f, $"{item} floats in the middle of its glow ({middle.Y:0.###} m up)");
            }
            var piles = _client.Main.Survival.DropNodes.Values.Select(p => p.Position).ToList();
            Check(piles.SelectMany((a, i) => piles.Skip(i + 1).Select(b => a.DistanceTo(b))).All(d => d > SurvivalActions.StackSpacing - 0.05f), "the piles lie apart");
            return true;
        });
        Add("the Use prompt names the pile under the crosshair", frame =>
        {
            var gas = _client.Main.Survival.Drops.Values.Single(d => d.Items[0].Item == Gas);
            var kit = _client.Main.Survival.Drops.Values.Single(d => d.Items[0].Item == Kit);
            var aimed = frame < 30 ? gas : kit;
            LookAt(aimed.Position.ToGodot() + new Vector3(0, DropAim.Middle, 0));
            if (frame == 25)
                Check(_hud.Interaction()?.Label == "pick up Gasoline x3", $"the gasoline offers its name and count ({_hud.Interaction()?.Label})");
            if (frame < 55)
                return false;
            return Check(_hud.Interaction()?.Label == "pick up Component Kit", $"the kit offers its name alone ({_hud.Interaction()?.Label})");
        });
        Add("Use picks up the Component Kit only", frame =>
        {
            if (frame == 0)
                _hud.Interaction()!.Value.Run();
            if (Count(Kit) != 1 || _client.Main.Survival.DropNodes.Count != 2)
                return false;
            Check(HostSurvival.Drops.Count == 2 && Count(Scrap) == 0 && Count(Gas) == 0, "the other two still lie there");
            return true;
        });
        Add("a dropped Weld Tool shows its prop in the glow", frame =>
        {
            if (frame == 0)
                _weld = HostSurvival.AddDrop((_at + new Vector3(2, 0.5f, 0)).ToNumerics(), [new ItemStack(Weld, 1)])!.Id;
            if (!_client.Main.Survival.DropNodes.TryGetValue(_weld, out var pile) || pile.Model is null)
                return false;
            Check(pile.Glow.Visible && !((Node3D)pile.FindChild("Box", false, false)!).Visible, "the tool glows and is not a crate");
            var across = Across(pile.Model);
            return Check(across is > 0.29f and < 0.31f, $"the tool is drawn 0.3 m across ({across:0.###} m)");
        });
    }

    private static Aabb Bounds(Node3D model) => model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
        .Select(m => m.GlobalTransform * m.GetAabb()).Aggregate((a, b) => a.Merge(b));

    // Measured along the model's own (turned) axes, so its slow turn does not widen it.
    private static float Across(Node3D model)
    {
        var turn = model.GlobalBasis.Orthonormalized();
        var unturned = new Transform3D(turn.Inverse(), Vector3.Zero);
        var size = model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
            .Select(m => unturned * m.GlobalTransform * m.GetAabb()).Aggregate((a, b) => a.Merge(b)).Size;
        return Mathf.Max(size.X, Mathf.Max(size.Y, size.Z));
    }

    private static Vector3 Middle(Node3D model) => Bounds(model).GetCenter();

    private int Count(string item) => _client.Main.Inventory.Own?.Items.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;

    private void LookAt(Vector3 target)
    {
        var d = (target - _client.Main.LocalPlayer!.Head.GlobalPosition).Normalized();
        _client.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Asin(d.Y) };
    }
}
