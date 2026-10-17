using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Items;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Items;

/// <summary>
/// The local player's inventory screen. Inventory (<c>I</c> or <c>B</c>) shows the player's own inventory;
/// Use (<c>E</c>) on a container or station opens it beside the player's (through <see cref="Survival.SurvivalHud"/>). Click a stack to pick it up and click a slot to put
/// it there (right-click picks up half); shift-click sends a stack straight to the other side. A station
/// lists the recipes it runs, with Craft buttons, and its queue with progress and Cancel buttons. In
/// Creative a give box makes items (<c>scrap_metal 50</c>). Every change goes through the Action API.
/// </summary>
public partial class InventoryScreen : CanvasLayer
{
    private const int Columns = 6;

    private Control _root = null!;
    private GridContainer _ownGrid = null!;
    private GridContainer _otherGrid = null!;
    private Label _otherTitle = null!;
    private VBoxContainer _recipes = null!;
    private VBoxContainer _queue = null!;
    private Control _otherPanel = null!;
    private Label _status = null!;
    private LineEdit _give = null!;
    private InventoryRef? _open;
    private (InventoryRef Where, int Slot, int? Count)? _held;

    /// <summary>Inventory sync. Set before adding to the tree.</summary>
    public InventorySync Inventory { get; init; } = null!;

    /// <summary>Where requests go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>Players, for the local camera. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Item names. Set before adding to the tree.</summary>
    public IPartCatalog Catalog { get; init; } = null!;

    /// <summary>True while the screen is up.</summary>
    public bool IsOpen => _root.Visible;

    public override void _Ready()
    {
        Layer = 5;
        _root = UiInput.KeepMouse(new PanelContainer { Name = "Panel", Visible = false });
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.KeepSize);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        _root.AddChild(columns);

        var own = new VBoxContainer();
        own.AddChild(new Label { Text = "Inventory" });
        _ownGrid = new GridContainer { Columns = Columns };
        own.AddChild(_ownGrid);
        _give = new LineEdit { PlaceholderText = "give: item_id count", Visible = Inventory.Settings!.AllowGive };
        _give.TextSubmitted += OnGive;
        own.AddChild(_give);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(320, 0) };
        own.AddChild(_status);
        columns.AddChild(own);

        var other = new VBoxContainer();
        _otherPanel = other;
        _otherTitle = new Label();
        other.AddChild(_otherTitle);
        _otherGrid = new GridContainer { Columns = Columns };
        other.AddChild(_otherGrid);
        _queue = new VBoxContainer();
        other.AddChild(_queue);
        _recipes = new VBoxContainer();
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(360, 200) };
        scroll.AddChild(_recipes);
        _recipes.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        other.AddChild(scroll);
        columns.AddChild(other);

        Inventory.ViewChanged += _ => Refresh();
        Inventory.ViewClosed += OnViewClosed;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        var toggle = GameInput.Pressed(@event, InputActions.Inventory) && (IsOpen || Input.MouseMode == Input.MouseModeEnum.Captured);
        if (toggle || (IsOpen && (GameInput.Pressed(@event, InputActions.Interact) || GameInput.Pressed(@event, InputActions.Pause))))
        {
            if (IsOpen)
                Hide();
            else
                Show(null);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Shows the screen, with a container or station beside the player's inventory.</summary>
    public void Show(InventoryRef? where)
    {
        if (_open is { } previous && previous != where)
            Inventory.Close(previous);
        _open = where;
        if (where is not null)
            Inventory.Open(where);
        _held = null;
        _status.Text = "";
        _root.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
    }

    /// <summary>Hides the screen and stops watching the open container.</summary>
    public new void Hide()
    {
        if (_open is { } where)
            Inventory.Close(where);
        _open = null;
        _held = null;
        _root.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>The container or station part under the crosshair, within reach; Use (E) on it opens it.</summary>
    public InventoryRef? AimedInventory()
    {
        if (Players.LocalPlayer is not { } player)
            return null;
        var camera = player.Camera.GlobalTransform;
        var query = PhysicsRayQueryParameters3D.Create(camera.Origin, camera.Origin - camera.Basis.Z * Inventory.Settings!.Reach);
        query.Exclude = [player.GetRid()];
        var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is not CreationBody body || body.GetParent() is not CreationNode node)
            return null;
        var toGrid = body.GlobalTransform.AffineInverse();
        var (cell, _) = BuildAim.FromHit((toGrid * hit["position"].AsVector3()).ToNumerics(), (toGrid.Basis * hit["normal"].AsVector3()).ToNumerics());
        if (node.Creation.PartAt(cell) is not { } part || node.Creation.DefinitionOf(part.Id).Inventory is null)
            return null;
        return InventoryRef.OfPart(node.Creation.Id, part.Id);
    }

    private void Refresh()
    {
        if (!IsOpen)
            return;
        var own = Inventory.Own;
        Fill(_ownGrid, InventoryRef.OfPlayer(Inventory.Session.LocalPeer), own, own?.SlotCount ?? Inventory.Settings!.PlayerSlots);
        var view = _open is { } where ? Inventory.Shown.GetValueOrDefault(where) : null;
        _otherPanel.Visible = _open is not null;
        if (_open is null)
            return;
        _otherTitle.Text = view is null ? "Opening…" : ItemName(view.Part!) + (view.Blocked ? " (full: take the output)" : "");
        Fill(_otherGrid, _open, view, view?.SlotCount ?? 0);
        FillStation(view);
    }

    private void Fill(GridContainer grid, InventoryRef where, InventoryView? view, int slots)
    {
        foreach (var child in grid.GetChildren())
            child.QueueFree();
        for (var i = 0; i < slots; i++)
        {
            var slot = i;
            var stack = view?.At(i);
            var held = _held is { } h && h.Where == where && h.Slot == i;
            var button = new Button
            {
                Text = stack is null ? "" : $"{Short(ItemName(stack.Item))}\n{stack.Count}",
                TooltipText = stack is null ? "" : $"{ItemName(stack.Item)} × {stack.Count}",
                CustomMinimumSize = new Vector2(64, 52),
            };
            if (held)
                button.AddThemeStyleboxOverride("normal", MechanicaTheme.SelectedSlot());
            button.GuiInput += e => OnSlotInput(e, where, slot, stack);
            grid.AddChild(button);
        }
    }

    private void FillStation(InventoryView? view)
    {
        foreach (var child in _queue.GetChildren().Concat(_recipes.GetChildren()))
            child.QueueFree();
        if (view?.Queue is not { } queue)
            return;
        for (var i = 0; i < queue.Count; i++)
        {
            var job = queue[i];
            var index = i;
            var row = new HBoxContainer();
            var done = job.Time > 0 ? job.Progress / job.Time : 1f;
            row.AddChild(new Label { Text = $"{RecipeName(job.Recipe)} × {job.Count}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            row.AddChild(new ProgressBar { Value = done * 100, CustomMinimumSize = new Vector2(100, 0), ShowPercentage = false });
            var cancel = new Button { Text = "Cancel" };
            cancel.Pressed += () => Send(InventoryActions.CancelCraft, new CancelCraftArgs(_open!.Creation!.Value, _open.Part!.Value, index));
            row.AddChild(cancel);
            _queue.AddChild(row);
        }
        foreach (var recipe in Inventory.Recipes.ForStation(view.Part!))
        {
            var row = new HBoxContainer();
            var inputs = string.Join(", ", recipe.Inputs.Select(a => $"{a.Count} {ItemName(a.Item)}"));
            var outputs = string.Join(", ", recipe.Outputs.Select(a => $"{a.Count} {ItemName(a.Item)}"));
            row.AddChild(new Label
            {
                Text = $"{outputs}  ←  {inputs}  ({recipe.Time / (view.Speed ?? 1f):0.#} s)",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            });
            var count = new SpinBox { MinValue = 1, MaxValue = Inventory.Settings!.MaxCraftCount, Value = 1 };
            row.AddChild(count);
            var craft = new Button { Text = "Craft", Disabled = Inventory.Own is not { } own || !recipe.Inputs.All(a => own.Count(a.Item) >= a.Count) };
            var id = recipe.QualifiedId;
            craft.Pressed += () => Send(InventoryActions.Craft, new CraftArgs(_open!.Creation!.Value, _open.Part!.Value, id, (int)count.Value));
            row.AddChild(craft);
            _recipes.AddChild(row);
        }
    }

    private void OnSlotInput(InputEvent e, InventoryRef where, int slot, SlotView? stack)
    {
        if (e is not InputEventMouseButton { Pressed: true } click || click.ButtonIndex is not (MouseButton.Left or MouseButton.Right))
            return;
        if (click.ShiftPressed && stack is not null)
        {
            var other = where.IsPlayer ? _open : InventoryRef.OfPlayer(Inventory.Session.LocalPeer);
            if (other is not null)
                Send(InventoryActions.MoveItems, new MoveItemsArgs(where, slot, other));
            return;
        }
        if (_held is { } held)
        {
            _held = null;
            if (held.Where != where || held.Slot != slot)
                Send(InventoryActions.MoveItems, new MoveItemsArgs(held.Where, held.Slot, where, slot, held.Count));
            Refresh();
            return;
        }
        if (stack is null)
            return;
        _held = (where, slot, click.ButtonIndex == MouseButton.Right ? Math.Max(1, stack.Count / 2) : null);
        Refresh();
    }

    private void OnGive(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 1 or > 2 || !(words.Length == 1 || int.TryParse(words[1], out _)))
        {
            _status.Text = "Type an item id and a count, like scrap_metal 50.";
            return;
        }
        Send(InventoryActions.GiveItems, new GiveItemsArgs(words[0], words.Length == 2 ? int.Parse(words[1]) : 1));
        _give.Clear();
    }

    private void OnViewClosed(InventoryRef where, string reason)
    {
        if (where != _open)
            return;
        _open = null;
        _status.Text = reason;
        Refresh();
    }

    private void Send<TArgs>(string action, TArgs args) =>
        Actions.Request(action, args, result => _status.Text = result.Ok ? "" : result.Message ?? "");

    private string ItemName(string id) => Catalog.FindDefinition(id)?.Name ?? id;

    private string RecipeName(string id) => Inventory.Recipes.Find(id)?.Name ?? id;

    private static string Short(string name) => name.Length <= 10 ? name : name[..9] + "…";
}
