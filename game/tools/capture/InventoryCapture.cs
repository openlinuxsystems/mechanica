using Godot;
using Mechanica.Core.Items;
using Mechanica.Core.Net;
using Mechanica.Game.Items;
using Mechanica.Game.Menus;

namespace Mechanica.Game.Tools;

/// <summary>
/// Pictures of the Inventory window in a running world (<c>tools/capture-inventory.sh</c>): every tab, a search,
/// before and after Organize, a chest beside it, the Fabricator's tabs and its upgrade dialog (enough kits, short, upgraded). With <c>creative</c> after the folder it
/// starts a Creative world instead, which lists every part and tool under the grid.
/// </summary>
public partial class InventoryCapture : Node
{
    // Where stacks go in the player's pockets: a scattered, half-sorted backpack.
    private static readonly (int Slot, string Item, int Count)[] Pockets =
    [
        (0, "vanilla:brick_block", 40), (1, "vanilla:scrap_wood_block", 100), (2, "vanilla:concrete_block", 100), (3, "vanilla:concrete_block", 56),
        (7, "vanilla:component_kit", 20), (8, "vanilla:component_kit", 8), (9, "vanilla:scrap_metal_block", 40),
        (10, "vanilla:chest", 1), (11, "vanilla:gas_container", 1), (14, "vanilla:scrap_wood_block", 55), (18, "vanilla:circuit_board", 3),
        (19, "vanilla:bearing", 4), (20, "vanilla:connect_tool", 1), (21, "vanilla:paint_tool", 1), (23, "vanilla:gasoline", 8),
        (24, "vanilla:battery", 5), (26, "vanilla:carrot", 10), (27, "vanilla:carrot", 10), (28, "vanilla:potato", 10), (29, "vanilla:carrot", 6),
    ];

    private readonly Queue<(string Name, int Frames, Action Setup)> _shots = new();
    private Main _main = null!;
    private string _out = "";
    private int _wait;
    private (string Name, int Frames, Action Setup)? _shot;
    private InventoryRef? _chest;
    private InventoryRef? _fabricator;
    private (int Creation, int Part)? _fabricatorPart;

    private InventoryScreen Screen => _main.GetNode<InventoryScreen>("InventoryScreen");

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _out = args.Length > 0 ? args[0] : OS.GetUserDataDir();
        var creative = args.Length > 1 && args[1] == "creative";
        _main = GD.Load<PackedScene>(MainMenu.WorldScene).Instantiate<Main>();
        _main.Options = LaunchOptions.Offline with { Seed = 7, GameMode = creative ? Core.Survival.GameMode.Creative : LaunchOptions.Offline.GameMode };
        AddChild(_main);
        var tag = creative ? "creative" : "inventory";
        Shot($"{tag}_00_world", 90, () => _main.Characters.Window?.Confirm());
        Shot($"{tag}_01_all", 60, () =>
        {
            var items = _main.Inventory.Items!.PlayerInventory(_main.Inventory.Session.LocalPeer);
            items.Clear();
            foreach (var (slot, item, count) in Pockets)
            {
                if (_main.Inventory.Catalog.FindDefinition(item) is not null)
                    items.SetSlot(slot, new ItemStack(item, Math.Min(count, items.StackSizeOf(item))));
            }
            Spawn("vanilla:chest", new Vector3(1.2f, 1, -2.5f));
            Spawn("vanilla:fabricator", new Vector3(-3, 1, -4));
            Screen.Show(null);
        });
        foreach (var tab in Enum.GetValues<InventoryTab>().Skip(1))
            Shot($"{tag}_02_tab_{tab.ToString().ToLowerInvariant()}", 4, () => Screen.SetTab(tab));
        Shot($"{tag}_03_search", 4, () =>
        {
            Screen.SetTab(InventoryTab.All);
            Screen.SearchInventory("block");
        });
        Shot($"{tag}_04_search_none", 4, () => Screen.SearchInventory("zeppelin"));
        Shot($"{tag}_05_organized", 10, () =>
        {
            Screen.SearchInventory("");
            Screen.Organize(done: r => GD.Print($"[capture] organize: {r.Ok} {r.Message}"));
        });
        Shot($"{tag}_06_chest", 16, () =>
        {
            FindParts();
            if (_chest is { } chest)
            {
                var inventory = _main.Inventory.Items!.Find(chest)!;
                inventory.SetSlot(2, new ItemStack("vanilla:scrap_metal_block", 30));
                inventory.SetSlot(5, new ItemStack("vanilla:metal_block", 20));
                Screen.Show(chest);
            }
        });
        Shot($"{tag}_07_chest_organized", 10, () =>
        {
            if (_chest is { } chest)
                Screen.Organize(chest);
        });
        Shot($"{tag}_08_fabricator", 20, () =>
        {
            FindParts();
            if (_fabricator is { } station)
                Screen.Show(station);
        });
        // The recipe with the most inputs: the card's middle scrolls and the window keeps its size.
        Shot($"{tag}_08_fabricator_most_inputs", 10, () =>
        {
            var most = Screen.RecipeRows.Select(r => r.Recipe).MaxBy(r =>
            {
                Screen.Choose(r);
                return Screen.PreviewInputs.Count;
            });
            if (most is not null)
                Screen.Choose(most);
        });
        // The Fabricator's Upgrade dialog: with enough Component Kits, short of them, then after upgrading.
        Shot($"{tag}_09_upgrade", 20, () =>
        {
            if (_fabricatorPart is { } part)
                Screen.Upgrades?.Open(part);
        });
        Shot($"{tag}_10_upgrade_short", 10, () =>
        {
            var items = _main.Inventory.Items!.PlayerInventory(_main.Inventory.Session.LocalPeer);
            items.Remove("vanilla:component_kit", items.Count("vanilla:component_kit") - 3);
        });
        Shot($"{tag}_11_upgraded", 30, () =>
        {
            _main.Inventory.Items!.PlayerInventory(_main.Inventory.Session.LocalPeer).Add("vanilla:component_kit", 10);
            _main.GetTree().CreateTimer(0.1).Timeout += () => Screen.Upgrades?.Upgrade(r => GD.Print($"[capture] upgrade: {r.Ok} {r.Message}"));
        });
    }

    public override void _Process(double delta)
    {
        if (_main.LocalPlayer is null)
            return;
        // The pictures are of the window, not the first-open tip over it.
        if (_main.GetNodeOrNull<CanvasLayer>("TutorialHints") is { } tips)
            tips.Visible = false;
        if (_shot is { } shot)
        {
            if (--_wait > 0)
                return;
            var path = $"{_out}/{shot.Name}.png";
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"[capture] {path}");
            _shot = null;
        }
        if (!_shots.TryDequeue(out var next))
        {
            GetTree().Quit();
            return;
        }
        next.Setup();
        _shot = next;
        _wait = next.Frames;
    }

    // CAPTURE_ONLY=a,b takes only the pictures whose names contain one of those (and the setup ones, 00 and 01).
    private void Shot(string name, int frames, Action setup)
    {
        if (OS.GetEnvironment("CAPTURE_ONLY") is { Length: > 0 } only && !only.Split(',').Any(o => name.Contains(o, StringComparison.Ordinal))
            && !name.Contains("_00_", StringComparison.Ordinal) && !name.Contains("_01_", StringComparison.Ordinal))
            return;
        _shots.Enqueue((name, frames, setup));
    }

    private void Spawn(string part, Vector3 offset)
    {
        var at = _main.LocalPlayer!.GlobalPosition + offset;
        _main.Actions.Request(Core.Actions.CreationActions.SpawnPart, new Core.Actions.SpawnPartArgs(part, [at.X, at.Y, at.Z]), r => GD.Print($"[capture] {part}: {r.Ok} {r.Message}"));
    }

    private void FindParts()
    {
        foreach (var creation in _main.Creations.All)
        {
            foreach (var part in creation.Parts)
            {
                if (part.Part == "vanilla:chest")
                    _chest = InventoryRef.OfPart(creation.Id, part.Id);
                if (part.Part == "vanilla:fabricator")
                {
                    _fabricator = InventoryRef.OfPart(creation.Id, part.Id);
                    _fabricatorPart = (creation.Id, part.Id);
                }
            }
        }
    }
}
