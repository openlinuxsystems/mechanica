using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Items;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Gas Engine's five levels, host plus a client in Survival. The host builds a floor with a Gas Engine 1 by the
/// client. The client sees its level 1 model, upgrades it with Component Kits in the upgrade dialog and sees the level 2
/// model and name; picking it up gives the client a <c>gas_engine@1</c> item with the level 2 icon (not stacking with Gas
/// Engine 1), and placing that item puts a Gas Engine 2 back on the host. <see cref="ElectricEngineTest"/> runs the same
/// stages on the Electric Engine.
/// </summary>
public partial class GasEngineTest : StagedTest
{
    private string Engine => $"vanilla:{PartId}";
    private string Upgraded => $"vanilla:{PartId}@1";
    private const string Floor = "vanilla:wood_block";
    private const string Kit = "vanilla:component_kit";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private UpgradeDialog _upgrades = null!;
    private int _floor;
    private int _engine;

    protected override string Tag => "gasengine";

    /// <summary>The engine's part id in the vanilla mod.</summary>
    protected virtual string PartId => "gas_engine";

    /// <summary>Its name without the level ("Gas Engine").</summary>
    protected virtual string Title => "Gas Engine";

    protected override int StageTimeoutFrames => 900;

    private Creation HostFloor => _host.Main.Creations.Find(_floor)!;

    private Inventory HostPockets => _host.Main.Inventory.Items!.PlayerInventory(_client.Peer);

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world", _ =>
            _client.Main.Inventory.Own is not null && _client.Main.LocalPlayer is { } player && player.IsOnFloor() && _host.Main.Inventory.Items is not null);
        Add($"the client sees {Title} 1 with its level 1 model", frame =>
        {
            if (frame == 0)
                Build();
            if (!Shows(1))
                return false;
            var seen = _client.Main.Creations.Find(_floor)!;
            Check(seen.DefinitionOf(_engine).Name == $"{Title} 1", $"the name ({seen.DefinitionOf(_engine).Name})");
            Check(_host.Main.Recipes!.All.Where(r => r.Outputs.Any(o => o.Item.StartsWith(Engine, StringComparison.Ordinal))).All(r => r.Outputs.All(o => o.Item == Engine)),
                $"only {Title} 1 has a recipe");
            return true;
        });
        Add($"the client upgrades it to {Title} 2", frame =>
        {
            if (frame == 0)
            {
                _upgrades = new UpgradeDialog
                {
                    Name = "UpgradeDialog", Inventory = _client.Main.Inventory, Actions = _client.Main.Actions, Creations = _client.Main.Creations,
                    Catalog = _client.Main.Build.Catalog,
                };
                AddChild(_upgrades);
                HostPockets.Add(Kit, 2);
                return false;
            }
            if (!_upgrades.IsOpen)
            {
                if (_client.Main.Inventory.Own?.Count(Kit) != 2)
                    return false;
                _upgrades.Open((_floor, _engine));
                Check(_upgrades.TitleText == Title && _upgrades.LevelText == "Level 1 → 2", $"the title and level ({_upgrades.TitleText}, {_upgrades.LevelText})");
                Check(_upgrades.ChangeLines.Contains("Durability: 6 → 7") && _upgrades.ChangeLines.Contains("Outputs: 2 → 4"),
                    $"what changes ({string.Join("; ", _upgrades.ChangeLines)})");
                Check(_upgrades.CostLines.SequenceEqual([("2 / 2 Component Kit", false)]) && !_upgrades.Confirm.Disabled,
                    $"the cost ({string.Join("; ", _upgrades.CostLines)}), Upgrade {(_upgrades.Confirm.Disabled ? "off" : "on")}");
                Check(_upgrades.Upgrade(_results.Add), "the dialog asks the host");
                return false;
            }
            if (_results.Count == 0)
                return false;
            if (!Shows(2))
                return false;
            Check(_results[0].Ok, $"the host upgrades it ({_results[0].Message})");
            Check(HostFloor.LevelOf(_engine) == 1 && HostPockets.Count(Kit) == 0, "the host's engine is level 1 and the kits are spent");
            Check(_client.Main.Creations.Find(_floor)!.DefinitionOf(_engine).Name == $"{Title} 2", $"the client shows {Title} 2");
            _upgrades.Close();
            return true;
        });
        Add($"picking it up gives a {Title} 2 item with its own icon", frame =>
        {
            if (frame == 0)
            {
                HostPockets.Add(Engine, 1);
                _results.Clear();
                _client.Main.Actions.Request(BuildActions.RemovePart, new RemovePartArgs(_floor, _engine), _results.Add);
                return false;
            }
            if (_results.Count == 0 || _client.Main.Inventory.Own?.Count(Upgraded) != 1)
                return false;
            Check(_results[0].Ok, $"the host removes it ({_results[0].Message})");
            var own = _client.Main.Inventory.Own!;
            Check(own.Count(Engine) == 1, $"it does not stack with {Title} 1");
            Check(_client.Main.Build.Catalog.FindDefinition(Upgraded)?.Name == $"{Title} 2", $"the item is named {Title} 2");
            // The hotbar's and inventory's pictures: each level's own icon file.
            var icons = new Ui.ItemIcons { Catalog = _client.Main.Build.Catalog, Assets = _client.Main.Assets };
            var (first, second) = (icons.For(Engine)?.GetImage(), icons.For(Upgraded)?.GetImage());
            Check(first is not null && second is not null, "both levels have an icon");
            Check(first is not null && second is not null && !first.GetData().SequenceEqual(second.GetData()), "and they differ");
            return true;
        });
        Add($"placing the item puts a {Title} 2 back", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                var cell = new GridCell(1, 1, 1);  // where it stood
                _client.Main.Actions.Request(BuildActions.PlacePart, new PlacePartArgs(Upgraded, cell, Creation: _floor), _results.Add);
                return false;
            }
            if (_results.Count == 0)
                return false;
            Check(_results[0].Ok, $"the host places it ({_results[0].Message})");
            if (!_results[0].Ok)
                return true;
            var placed = HostFloor.Parts.Single(p => p.Part == Engine);
            Check(placed.Level == 1, $"at level 1 ({placed.Level})");
            Check(HostPockets.Count(Upgraded) == 0 && HostPockets.Count(Engine) == 1, $"the upgraded item is used up, {Title} 1 is kept");
            return true;
        });
    }

    // True once the client's floor has the engine drawn with level's model.
    private bool Shows(int level)
    {
        if (_client.Main.Creations.Find(_floor) is not { } seen || seen.Parts.Count != HostFloor.Parts.Count || seen.LevelOf(_engine) != level - 1)
            return false;
        return _client.Main.Creations.NodeOf(_floor)?.PartNode(_engine)?.FindChild($"{Title.Replace(" ", "", StringComparison.Ordinal)}{level}", true, false) is not null;
    }

    // A wooden floor 6 by 6 blocks in front of the client with the engine at level 1 on it.
    private void Build()
    {
        var catalog = _host.Main.Build.Catalog;
        var creation = new Creation(_host.Main.Build.NextCreationId(), catalog);
        for (var x = 0; x < 6; x++)
        {
            for (var z = 0; z < 6; z++)
            {
                if (!creation.TryPlacePart(Floor, new GridCell(x, 0, z), default, out _, out var error))
                    Check(false, $"the floor builds ({error})");
            }
        }
        if (!creation.TryPlacePart(Engine, new GridCell(1, 1, 1), default, out var engine, out var why))
            Check(false, $"the engine fits ({why})");
        _engine = engine?.Id ?? 0;
        var feet = _host.Main.PlayerSync.Players[_client.Peer].GlobalPosition;
        creation.SetAllBodyPoses(new Pose((feet + new Vector3(-0.75f, 0.25f, -2.5f)).ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Build.Added(creation);
        _floor = creation.Id;
    }
}
