using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Items;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Fabricator screen, linked storage and upgrades (UI spec PR 12), host plus a client in Survival. The host builds a
/// wooden floor with a Fabricator, a chest beside it (linked) and a chest at the far end (not linked). On the client's
/// screen the Fabricator opens as a main window and a detail card (UI spec §8), with the linked chest in the Storage drawer; search and the category
/// filters narrow the recipes; the preview counts the player's and the linked chest's scrap together and shows a short
/// input in red with Craft off and a reason; crafting takes the chest's scrap first, the queue shows a cyan bar with its percentage
/// and time left, and the finished chest lands in the linked chest while the output flashes. The upgrade dialog shows
/// the level, the Component Kit cost (red while short) and what changes; confirming raises the level on the host and
/// the screen. The client draws a pipe to the linked chest only, and closing the screen stops watching the chest.
/// </summary>
public partial class FabricatorTest : StagedTest
{
    private const string Fabricator = "vanilla:fabricator";
    private const string Chest = "vanilla:chest";
    private const string Floor = "vanilla:wood_block";
    private const string Wood = "vanilla:scrap_wood_block";
    private const string Metal = "vanilla:scrap_metal_block";
    private const string Kit = "vanilla:component_kit";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private InventoryScreen _screen = null!;
    private UpgradeDialog _upgrades = null!;
    private int _workshop;
    private int _fabricator;
    private int _near;
    private int _far;

    protected override string Tag => "fabricator";

    protected override int StageTimeoutFrames => 900;

    private InventoryRef FabricatorRef => InventoryRef.OfPart(_workshop, _fabricator);

    private InventoryRef NearRef => InventoryRef.OfPart(_workshop, _near);

    private Creation HostWorkshop => _host.Main.Creations.Find(_workshop)!;

    private Inventory HostPockets => _host.Main.Inventory.Items!.PlayerInventory(_client.Peer);

    public override void _Ready()
    {
        // "Nothing is NEW the first time" needs a first time: forget what earlier runs saw at stations.
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://seen_recipes.json"));
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world", _ =>
            _client.Main.Inventory.Own is not null && _client.Main.LocalPlayer is { } player && player.IsOnFloor() && _host.Main.Inventory.Items is not null);
        Add("the host builds a workshop and stocks the chest", frame =>
        {
            if (frame == 0)
                Build();
            if (_client.Main.Creations.Find(_workshop) is not { } seen || seen.Parts.Count != HostWorkshop.Parts.Count)
                return false;
            Check(StorageLinks.Of(HostWorkshop, _fabricator).SequenceEqual([_near]), $"only the near chest is linked ({string.Join(", ", StorageLinks.Of(HostWorkshop, _fabricator))})");
            return true;
        });
        Add("the client's screen opens the Fabricator window and card", frame =>
        {
            if (frame == 0)
            {
                _upgrades = new UpgradeDialog
                {
                    Name = "UpgradeDialog", Inventory = _client.Main.Inventory, Actions = _client.Main.Actions, Creations = _client.Main.Creations,
                    Catalog = _client.Main.Build.Catalog, OtherScreenOpen = () => _screen.IsOpen,
                };
                _screen = new InventoryScreen
                {
                    Name = "InventoryScreen", Inventory = _client.Main.Inventory, Actions = _client.Main.Actions, Players = _client.Main.PlayerSync,
                    Catalog = _client.Main.Build.Catalog, Mode = () => GameMode.Survival, Creations = _client.Main.Creations, Upgrades = _upgrades,
                };
                AddChild(_screen);
                AddChild(_upgrades);
                return false;
            }
            if (frame == 1)
                _screen.Show(FabricatorRef);
            if (!_screen.StationMode || _client.Main.Inventory.Shown.GetValueOrDefault(NearRef) is not { } chest || chest.Count(Metal) != 10 || frame < 10)
                return false;
            var (main, card) = (_screen.StationPanels[0].GetGlobalRect(), _screen.StationPanels[1].GetGlobalRect());
            var narrow = GetViewport().GetVisibleRect().Size.X < 1280;
            Check(Mathf.IsEqualApprox(card.Size.X, narrow ? 280 : 302) && main.Size.X > 2 * card.Size.X, $"the main window and the card ({main.Size.X}, {card.Size.X})");
            Check(Mathf.Abs(card.Position.X - main.End.X - 12) < 1 && Mathf.Abs(card.Size.Y - main.Size.Y) < 1, "the card sits 12 px right of the window, as tall");
            Check(_screen.QueueCells == 2 && _screen.QueueRows.Count == 0, $"the queue strip shows the queue's 2 empty cells ({_screen.QueueCells})");
            // A tab shows when the station has a recipe under it (All always does); one without recipes is hidden.
            var withRecipes = Enum.GetValues<InventoryTab>().Where(t => _screen.ListedRecipes.Any(r => InventoryTabs.Shows(t, Definition(r), HotbarTools.IsTool(Output(r)))));
            Check(_screen.TabsShown.Contains(InventoryTab.All) && _screen.TabsShown.SequenceEqual(withRecipes), $"only tabs with recipes show ({string.Join(", ", _screen.TabsShown)})");
            Check(_screen.RecipeRows.All(r => !r.IsNew), "nothing is NEW the first time a station is opened");
            if (!_screen.StorageOpen)
                _screen.ToggleStorage();
            Check(_screen.StorageOpen, "the Storage button opens the drawer");
            Check(_screen.LinkedShown.SequenceEqual([NearRef]), "the linked chest is in the drawer");
            Check(_screen.LinkedSlots(NearRef).Any(s => s.Entry == Metal && s.Count == 10 && s.IsVisibleInTree()), "its scrap shows");
            Check(_screen.StationOwnSlots.Any(s => s.Entry == Wood), "and the player's inventory under it");
            Check(_screen.StationLevel == "Level 1 / 5", $"the station's level ({_screen.StationLevel})");
            return true;
        });
        Add("search and filters narrow the recipes", _ =>
        {
            var all = _screen.ListedRecipes.Count;
            _screen.Search("chest");
            var found = _screen.ListedRecipes;
            Check(found.Contains(Chest) && found.Count < all, $"searching 'chest' finds the chest ({found.Count} of {all})");
            Check(found.All(r => Matches(r, "chest")), "and only recipes that mention it");
            _screen.Search("");
            _screen.Filter(InventoryTab.Blocks);
            var blocks = _screen.ListedRecipes;
            Check(blocks.Count > 0 && blocks.All(r => Definition(r)?.Category == PartCategory.Block), $"Blocks lists only blocks ({blocks.Count})");
            _screen.Filter(InventoryTab.All);
            Check(_screen.ListedRecipes.Count == all, "All lists everything again");
            return true;
        });
        Add("recipes the player can't make still read as choosable", _ =>
        {
            var rows = _screen.RecipeRows;
            Check(rows.Any(r => !r.CanMake) && rows.Any(r => r.CanMake), $"some recipes are short and some are not ({rows.Count(r => !r.CanMake)} of {rows.Count} short)");
            Check(_screen.RecipeRows.Count == rows.Count, "every recipe has a tile");
            var shortOne = rows.First(r => !r.CanMake).Recipe;
            _screen.Choose(shortOne);
            Check(_screen.ChosenRecipe == shortOne && _screen.CraftButton.Disabled, $"a short recipe can still be chosen ({shortOne}), with Craft off");
            return true;
        });
        Add("the preview counts the linked chest and shows what is short", _ =>
        {
            _screen.Choose(Chest);
            _screen.SetQuantity(3);
            var inputs = _screen.PreviewInputs;
            Check(inputs.Any(i => i.Missing && i.Text.StartsWith("120 / 180")), $"180 scrap wood blocks is short in red ({Lines(inputs)})");
            Check(inputs.Any(i => !i.Missing && i.Text.StartsWith("20 / 30")) == false && inputs.Any(i => i.Missing && i.Text.StartsWith("20 / 30")), $"30 scrap metal blocks is short too ({Lines(inputs)})");
            Check(_screen.CraftButton.Disabled, "Craft is off");
            Check(_screen.Reason.StartsWith("Needs"), $"and says why ({_screen.Reason})");
            Check(_screen.StatRowsShown.Select(r => r.Name).SequenceEqual(["Mass", "Durability", "Friction", "Buoyancy"]), $"the chest's stats ({string.Join(", ", _screen.StatRowsShown)})");
            _screen.SetQuantity(2);
            inputs = _screen.PreviewInputs;
            Check(inputs.All(i => !i.Missing) && inputs.Any(i => i.Text.StartsWith("20 / 20")), $"two chests' worth counts the chest's scrap ({Lines(inputs)})");
            Check(!_screen.CraftButton.Disabled && _screen.Reason == "", "Craft is on, with no reason");
            return true;
        });
        Add("crafting takes the chest's scrap first and shows the queue", frame =>
        {
            if (frame == 0)
                _screen.Craft(_results.Add);
            if (_results.Count == 0 || _screen.QueueRows.Count == 0 || _screen.QueueRows[0].Percent <= 0)
                return frame < 300 ? false : Check(false, "the queue shows progress");
            Check(_results[0].Ok, $"the host queues it ({_results[0].Message})");
            Check(HostWorkshop.InventoryOf(_near)!.Count(Metal) == 0 && HostPockets.Count(Metal) == 0 && HostPockets.Count(Wood) == 0,
                $"the chest's scrap went first, then the pockets' ({HostWorkshop.InventoryOf(_near)!.Count(Metal)}, {HostPockets.Count(Metal)})");
            var row = _screen.QueueRows[0];
            Check(row.Name.Contains("× 2") && row.Left.EndsWith("left"), $"the order shows its count and time left ({row.Name}, {row.Left})");
            _results.Clear();
            return true;
        });
        var flashed = false;
        Add("the finished chest lands in the linked chest and the output flashes", frame =>
        {
            flashed |= _screen.Flashing;
            if (HostWorkshop.InventoryOf(_near)!.Count(Chest) < 1 || !flashed)
                return false;
            Check(HostWorkshop.InventoryOf(_fabricator)!.Count(Chest) == 0, "not in the Fabricator's own slots");
            return true;
        });
        Control? filler = null;
        var cardHeight = 0f;
        Add("a tall detail card scrolls instead of growing the window", frame =>
        {
            var details = (Control)_screen.DetailScroll.GetChild(0);
            if (frame == 0)
            {
                // Stands in for a recipe with many inputs and a long description.
                cardHeight = _screen.StationPanels[1].GetGlobalRect().Size.Y;
                filler = new Control { Name = "Filler", CustomMinimumSize = new Vector2(0, 1500) };
                details.AddChild(filler);
                return false;
            }
            if (frame < 3)
                return false;
            var (main, card) = (_screen.StationPanels[0].GetGlobalRect(), _screen.StationPanels[1].GetGlobalRect());
            var craft = _screen.CraftButton.GetGlobalRect();
            Check(Mathf.Abs(card.Size.Y - cardHeight) < 1 && Mathf.Abs(card.Size.Y - main.Size.Y) < 1, $"the card keeps its height ({cardHeight} → {card.Size.Y}, window {main.Size.Y})");
            Check(card.Encloses(craft) && GetViewport().GetVisibleRect().Encloses(craft), $"the Craft bar stays in the card, on screen ({craft})");
            var bar = _screen.DetailScroll.GetVScrollBar();
            Check(bar.MaxValue > bar.Page + 1000, $"the details scroll instead ({bar.Page} of {bar.MaxValue})");
            _screen.DetailScroll.ScrollVertical = 300;
            _screen.Choose(_screen.RecipeRows.First(r => r.Recipe != _screen.ChosenRecipe).Recipe);
            Check(_screen.DetailScroll.ScrollVertical == 0, "choosing another recipe scrolls back to the top");
            details.RemoveChild(filler!);
            filler!.QueueFree();
            _screen.Choose(Chest);
            return true;
        });
        Add("the upgrade dialog shows the cost in red while short", frame =>
        {
            if (frame == 0)
            {
                HostPockets.Add(Kit, 1);
                return false;
            }
            if (_client.Main.Inventory.Own?.Count(Kit) != 1)
                return false;
            if (!_upgrades.IsOpen)
                _upgrades.Open((_workshop, _fabricator));
            Check(_upgrades.TitleText == "Fabricator" && _upgrades.LevelText == "Level 1 → 2", $"the title and level ({_upgrades.TitleText}, {_upgrades.LevelText})");
            Check(_upgrades.CostLines.SequenceEqual([("1 / 5 Component Kit", true)]), $"the cost ({string.Join("; ", _upgrades.CostLines)})");
            Check(_upgrades.Status == "Needs 4 more Component Kit.", $"the reason line says why ({_upgrades.Status})");
            Check(_upgrades.ChangeLines.SequenceEqual(["Queue: 2 → 4", "Slots: 10 → 12"]), $"what changes ({string.Join("; ", _upgrades.ChangeLines)})");
            Check(_upgrades.Confirm.Disabled, "Upgrade is off");
            return true;
        });
        Add("with enough kits the upgrade goes through", frame =>
        {
            if (frame == 0)
            {
                HostPockets.Add(Kit, 4);
                return false;
            }
            if (_results.Count == 0)
            {
                // The cost turns white and Upgrade comes on once the other four kits reach the client.
                if (_upgrades.CostLines is [(_, false)] && !_upgrades.Confirm.Disabled)
                    _upgrades.Upgrade(_results.Add);
                return false;
            }
            if (_screen.StationLevel != "Level 2 / 5" || _upgrades.LevelText != "Level 2 → 3")
                return false;
            Check(_results[0].Ok, $"the host upgrades it ({_results[0].Message})");
            Check(HostWorkshop.LevelOf(_fabricator) == 1 && HostWorkshop.InventoryOf(_fabricator)!.SlotCount == 12, "the host's Fabricator is level 1 with 12 slots");
            Check(HostPockets.Count(Kit) == 0, "all five kits are spent");
            Check(_upgrades.ChangeLines.SequenceEqual(["Queue: 4 → 6", "Slots: 12 → 15"]), $"the next level's changes ({string.Join("; ", _upgrades.ChangeLines)})");
            Check(_upgrades.TitleText == "Fabricator" && _upgrades.Status == "Needs 5 more Component Kit.", $"the next level's title and reason ({_upgrades.TitleText}, {_upgrades.Status})");
            _upgrades.CloseButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(!_upgrades.IsOpen && Input.MouseMode != Input.MouseModeEnum.Captured, "closing the dialog leaves the screen in charge of the mouse");
            return true;
        });
        Add("the client draws a pipe to the linked chest only", _ =>
        {
            var node = _client.Main.Creations.NodeOf(_workshop)!;
            Check(node.Pipes.SequenceEqual([(_fabricator, _near)]), $"one pipe ({string.Join(", ", node.Pipes)})");
            return true;
        });
        Add("closing the screen stops watching the chest", frame =>
        {
            if (frame == 0)
                _screen.Hide();
            if (_client.Main.Inventory.Shown.ContainsKey(NearRef) || _client.Main.Inventory.Shown.ContainsKey(FabricatorRef))
                return false;
            Check(_screen.LinkedShown.Count == 0, "nothing linked is shown");
            return true;
        });
    }

    // A wooden floor 20 by 6 blocks under the client, a Fabricator at one end, a chest beside it and one at the far end.
    private void Build()
    {
        var catalog = _host.Main.Build.Catalog;
        var creation = new Creation(_host.Main.Build.NextCreationId(), catalog);
        for (var x = 0; x < 24; x++)
        {
            for (var z = 0; z < 8; z++)
            {
                if (!creation.TryPlacePart(Floor, new GridCell(x, 0, z), default, out _, out var error))
                    Check(false, $"the floor builds ({error})");
            }
        }
        _fabricator = PlaceOnFloor(creation, Fabricator, 0);
        var fabricatorEnd = creation.CellsOf(_fabricator).Max(c => c.X);
        _near = PlaceOnFloor(creation, Chest, fabricatorEnd + 2);
        _far = PlaceOnFloor(creation, Chest, 20);
        var feet = _host.Main.PlayerSync.Players[_client.Peer].GlobalPosition;
        creation.SetAllBodyPoses(new Pose((feet + new Vector3(-1, 0, -4)).ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Build.Added(creation);
        _workshop = creation.Id;
        HostPockets.Add(Wood, 120);
        HostPockets.Add(Metal, 10);
        creation.InventoryOf(_near)!.Add(Metal, 10);
    }

    // Puts a part on top of the floor with its lowest x cell at x, trying cells until its base socket attaches.
    private int PlaceOnFloor(Creation creation, string part, int x)
    {
        var definition = _host.Main.Build.Catalog.FindDefinition(part)!;
        var origin = PartGeometry.Cells(definition, GridCell.Zero, default).ToList();
        var (minX, minY, minZ) = (origin.Min(c => c.X), origin.Min(c => c.Y), origin.Min(c => c.Z));
        for (var z = 0; z < 4; z++)
        {
            var cell = new GridCell(x - minX, 1 - minY, z - minZ);
            if (creation.TryPlacePart(part, cell, default, out var placed, out _))
                return placed.Id;
        }
        Check(false, $"{part} fits on the floor at x {x}");
        return 0;
    }

    private bool Matches(string recipe, string word)
    {
        var found = _client.Main.Recipes!.Find(recipe)!;
        var catalog = _client.Main.Build.Catalog;
        return found.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
            || found.Outputs.Any(o => o.Item.Contains(word, StringComparison.OrdinalIgnoreCase) || catalog.FindDefinition(o.Item)?.Name?.Contains(word, StringComparison.OrdinalIgnoreCase) == true);
    }

    private string Output(string recipe) => _client.Main.Recipes!.Find(recipe)!.Outputs[0].Item;

    private PartDefinition? Definition(string recipe) => _client.Main.Build.Catalog.FindDefinition(Output(recipe));

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.03f;

    private static string Lines(IEnumerable<(string Text, bool Missing)> lines) => string.Join("; ", lines.Select(l => $"{l.Text}{(l.Missing ? " (red)" : "")}"));
}
