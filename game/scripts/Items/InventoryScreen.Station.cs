using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Items;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Items;

/// <summary>
/// The Fabricator screen (UI spec §4, §8): what Use (E) on any crafting station opens. Left 30%: the recipes the station
/// runs, with a search box and category filters. Centre 40%: the chosen recipe's output, inputs (icon and count, red when
/// there are not enough in the inventory and linked storage), time at the station's speed, and the station's own output
/// slots; along the bottom the quantity, Craft and the queue, each order with a cyan bar showing its percentage and time
/// left. Right 30%: every container linked to the station (<see cref="StorageLinks"/>), then the player's inventory.
/// The output flashes when a craft finishes. The station's level and an Upgrade button (U) sit under its name.
/// </summary>
public partial class InventoryScreen
{
    private const int StationColumns = 5;
    private const int StationSlot = 52;
    private const float FlashSeconds = 0.45f;
    private static readonly string[] IconStates = ["icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color", "icon_focus_color"];

    private Control _station = null!;
    private Label _stationTitle = null!;
    private Label _stationLevel = null!;
    private Button _upgrade = null!;
    private LineEdit _search = null!;
    private readonly List<Button> _filterButtons = [];
    private VBoxContainer _recipeList = null!;
    private readonly Dictionary<string, Button> _recipeButtons = [];
    private string _recipeKey = "";
    private ItemSlot _previewOutput = null!;
    private Label _previewName = null!;
    private Label _previewDescription = null!;
    private HFlowContainer _previewInputs = null!;
    private string _previewKey = "";
    private Label _previewTime = null!;
    private Control _outputPanel = null!;
    private readonly SlotGrid _outputGrid = new(StationColumns, StationSlot);
    private SpinBox _quantity = null!;
    private Button _craft = null!;
    private VBoxContainer _queueList = null!;
    private readonly List<(Label Name, ProgressBar Bar, Label Left, Button Cancel)> _queueRows = [];
    private VBoxContainer _storage = null!;
    private readonly SlotGrid _stationOwnGrid = new(StationColumns, StationSlot);
    private readonly List<InventoryRef> _linked = [];
    private readonly Dictionary<InventoryRef, (Label Title, SlotGrid Grid, Control Box)> _linkedGrids = [];
    private InventoryTab _filter;
    private string? _chosen;
    private bool _stationMode;
    private int _remainingCrafts = -1;
    private Tween? _flash;

    /// <summary>True while the screen shows a station (the Fabricator layout).</summary>
    public bool StationMode => _stationMode && _open is not null;

    /// <summary>The recipes listed, by qualified id, in order.</summary>
    public IReadOnlyList<string> ListedRecipes => [.. _recipeList.GetChildren().OfType<Button>().Select(b => (string)b.GetMeta("recipe"))];

    /// <summary>The recipe rows as shown: whether the player can make each, and whether its text is drawn at full strength.</summary>
    public IReadOnlyList<(string Recipe, bool CanMake, bool FullText)> RecipeRows =>
        [.. _recipeList.GetChildren().OfType<Button>().Select(b => ((string)b.GetMeta("recipe"), (bool)b.GetMeta("can_make"), b.Modulate == Colors.White && b.SelfModulate == Colors.White && !b.Disabled && !b.HasThemeColorOverride("font_color")))];

    /// <summary>The recipe chosen for the preview, or null.</summary>
    public string? ChosenRecipe => _chosen;

    /// <summary>The preview's input lines (<c>2 / 3 Scrap Metal</c>) and whether each is short.</summary>
    public IReadOnlyList<(string Text, bool Missing)> PreviewInputs =>
        [.. _previewInputs.GetChildren().OfType<VBoxContainer>().Select(b => b.GetChild<Label>(1)).Select(l => (l.Text, l.ThemeTypeVariation == "ErrorLabel"))];

    /// <summary>The Craft button.</summary>
    public Button CraftButton => _craft;

    /// <summary>The queue rows as shown: name, percent done and the time-left text.</summary>
    public IReadOnlyList<(string Name, double Percent, string Left)> QueueRows =>
        [.. _queueRows.Select(r => (r.Name.Text, r.Bar.Value, r.Left.Text))];

    /// <summary>The containers linked to the station, as the right side shows them.</summary>
    public IReadOnlyList<InventoryRef> LinkedShown => _linked;

    /// <summary>The station's own output slots.</summary>
    public IReadOnlyList<ItemSlot> OutputSlots => _outputGrid.Slots;

    /// <summary>The slots of a linked container on the right.</summary>
    public IReadOnlyList<ItemSlot> LinkedSlots(InventoryRef where) => _linkedGrids.TryGetValue(where, out var shown) ? shown.Grid.Slots : [];

    /// <summary>The player's slots on the right of the Fabricator layout.</summary>
    public IReadOnlyList<ItemSlot> StationOwnSlots => _stationOwnGrid.Slots;

    /// <summary>The station's level line, empty when it has no upgrades.</summary>
    public string StationLevel => _stationLevel.Visible ? _stationLevel.Text : "";

    /// <summary>True for a moment after a craft finishes (the output panel flashes).</summary>
    public bool Flashing => _flash?.IsRunning() == true;

    /// <summary>The three columns (left, centre, right).</summary>
    public IReadOnlyList<Control> StationColumnsShown => [.. _station.GetChildren().OfType<Control>()];

    /// <summary>Lists only recipes whose name or outputs contain every word of <paramref name="text"/>.</summary>
    public void Search(string text)
    {
        _search.Text = text;
        RefreshStation();
    }

    /// <summary>Lists only recipes whose main output is under <paramref name="tab"/>.</summary>
    public void Filter(InventoryTab tab)
    {
        _filter = tab;
        for (var i = 0; i < TabOrder.Length; i++)
            _filterButtons[i].SetPressedNoSignal(TabOrder[i] == tab);
        RefreshStation();
    }

    /// <summary>Shows <paramref name="recipe"/> (a qualified id) in the preview.</summary>
    public void Choose(string recipe)
    {
        _chosen = recipe;
        RefreshStation();
    }

    /// <summary>Sets how many crafts Craft queues.</summary>
    public void SetQuantity(int count) => _quantity.Value = count;

    /// <summary>Queues the chosen recipe at the station. False when nothing is chosen.</summary>
    public bool Craft(Action<Core.Actions.ActionResult>? done = null)
    {
        if (_chosen is null || _open is not { Creation: { } creation, Part: { } part })
            return false;
        Actions.Request(InventoryActions.Craft, new CraftArgs(creation, part, _chosen, (int)_quantity.Value), result =>
        {
            _status.Text = result.Ok ? "" : result.Message ?? "";
            done?.Invoke(result);
        });
        return true;
    }

    private void BuildStation()
    {
        var station = new HBoxContainer { Name = "Station", Visible = false };
        station.AddThemeConstantOverride("separation", 16);
        _root.AddChild(station);
        _station = station;

        // Left: recipe browser.
        var left = Column("Recipes", 3);
        station.AddChild(left);
        left.AddChild(new Label { Text = "Recipes", ThemeTypeVariation = "TitleLabel" });
        _search = new LineEdit { Name = "Search", PlaceholderText = "Search recipes", ClearButtonEnabled = true };
        _search.TextChanged += _ => RefreshStation();
        left.AddChild(_search);
        var filters = new HFlowContainer { Name = "Filters" };
        var group = new ButtonGroup();
        foreach (var tab in TabOrder)
        {
            var button = new Button { Name = tab.ToString(), Text = TabName(tab), ToggleMode = true, ButtonGroup = group, ButtonPressed = tab == _filter };
            button.Pressed += () => Filter(tab);
            filters.AddChild(button);
            _filterButtons.Add(button);
        }
        left.AddChild(filters);
        var recipes = new ScrollContainer { Name = "RecipeScroll", SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _recipeList = new VBoxContainer { Name = "RecipeList", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        recipes.AddChild(_recipeList);
        left.AddChild(recipes);

        // Centre: the station, the chosen recipe, its output slots, then quantity, Craft and the queue.
        var centre = Column("Preview", 4);
        station.AddChild(centre);
        var head = new HBoxContainer();
        _stationTitle = Loc.Verbatim(new Label { Name = "StationTitle", ThemeTypeVariation = "TitleLabel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        head.AddChild(_stationTitle);
        _stationLevel = Loc.Verbatim(new Label { Name = "StationLevel", ThemeTypeVariation = "SecondaryLabel", VerticalAlignment = VerticalAlignment.Center });
        head.AddChild(_stationLevel);
        _upgrade = new Button { Name = "Upgrade", TooltipText = "Spend Component Kits to make the station faster and bigger" };
        _upgrade.Pressed += OpenUpgrade;
        head.AddChild(_upgrade);
        centre.AddChild(head);
        var preview = new PanelContainer { Name = "RecipePreview" };
        var previewBox = new VBoxContainer();
        preview.AddChild(previewBox);
        var output = new HBoxContainer();
        output.AddThemeConstantOverride("separation", 12);
        _previewOutput = new ItemSlot { Name = "PreviewOutput", CustomMinimumSize = new Vector2(88, 88), MouseFilter = Control.MouseFilterEnum.Ignore };
        output.AddChild(_previewOutput);
        var names = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        _previewName = Loc.Verbatim(new Label { Name = "PreviewName", ThemeTypeVariation = "TitleLabel" });
        names.AddChild(_previewName);
        _previewDescription = Loc.Verbatim(new Label { Name = "PreviewDescription", ThemeTypeVariation = "SecondaryLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        names.AddChild(_previewDescription);
        _previewTime = Loc.Verbatim(new Label { Name = "PreviewTime" });
        names.AddChild(_previewTime);
        output.AddChild(names);
        previewBox.AddChild(output);
        previewBox.AddChild(new Label { Text = "Needs", ThemeTypeVariation = "SecondaryLabel" });
        _previewInputs = new HFlowContainer { Name = "PreviewInputs" };
        _previewInputs.AddThemeConstantOverride("h_separation", 10);
        previewBox.AddChild(_previewInputs);
        centre.AddChild(preview);
        var outputPanel = new VBoxContainer { Name = "Output" };
        outputPanel.AddChild(new Label { Text = "Output", ThemeTypeVariation = "SecondaryLabel" });
        outputPanel.AddChild(_outputGrid.Build("OutputGrid", rows: 2, this));
        _outputPanel = outputPanel;
        centre.AddChild(outputPanel);
        var bar = new HBoxContainer { Name = "CraftBar" };
        bar.AddChild(new Label { Text = "Quantity", VerticalAlignment = VerticalAlignment.Center });
        _quantity = new SpinBox { Name = "Quantity", MinValue = 1, MaxValue = Inventory.Settings!.MaxCraftCount, Value = 1 };
        _quantity.ValueChanged += _ => RefreshStation();
        bar.AddChild(_quantity);
        _craft = new Button { Name = "Craft", Text = "Craft", ThemeTypeVariation = "PrimaryButton", CustomMinimumSize = new Vector2(120, 36), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _craft.Pressed += () => Craft();
        bar.AddChild(_craft);
        centre.AddChild(bar);
        centre.AddChild(new Label { Text = "Queue", ThemeTypeVariation = "SecondaryLabel" });
        var queue = new ScrollContainer { Name = "QueueScroll", SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _queueList = new VBoxContainer { Name = "QueueList", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        queue.AddChild(_queueList);
        centre.AddChild(queue);

        // Right: linked storage, then the player's own inventory.
        var right = Column("Storage", 3);
        station.AddChild(right);
        var scroll = new ScrollContainer { Name = "StorageScroll", SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _storage = new VBoxContainer { Name = "StorageList", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_storage);
        right.AddChild(scroll);
        _storage.AddChild(new Label { Name = "LinkedTitle", Text = "Linked storage", ThemeTypeVariation = "TitleLabel" });
        _storage.AddChild(new Label { Name = "NoLinks", Text = "None. Place a chest next to the station to link it.", ThemeTypeVariation = "SecondaryLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _storage.AddChild(new Label { Name = "OwnTitle", Text = "Inventory", ThemeTypeVariation = "TitleLabel" });
        _storage.AddChild(_stationOwnGrid.Build("StationOwnGrid", rows: 6, this));
    }

    private static VBoxContainer Column(string name, float ratio)
    {
        var column = new VBoxContainer { Name = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = ratio, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        return column;
    }

    // Whether the screen being opened is for a station, known from the Creation before its view arrives.
    private void StartStation(InventoryRef? where)
    {
        var station = where is { Creation: { } c, Part: { } p } && Creations?.Find(c) is { } creation && creation.FindPart(p) is not null
            && creation.DefinitionOf(p).Crafting is not null;
        _stationMode = station || (where is not null && Inventory.Shown.GetValueOrDefault(where)?.Queue is not null);
        _remainingCrafts = -1;
        if (_stationMode)
            _status.Reparent(_station.GetChild(1));
        else if (_status.GetParent() != _classic.GetChild(0))
            _status.Reparent(_classic.GetChild(0));
    }

    // 30/40/30 of most of the screen, above the hotbar (UI spec §11: the proportions hold at any size).
    private void SizeStation(Vector2 screen, Vector2 margin, float bar)
    {
        var width = Mathf.Clamp(screen.X - 2 * margin.X - 32, 720, 1600);
        var height = Mathf.Max(420, screen.Y - margin.Y * 2 - bar - 24);
        _station.CustomMinimumSize = new Vector2(width, height);
        var share = width - 2 * 16;
        foreach (var (column, ratio) in StationColumnsShown.Zip(new[] { 0.3f, 0.4f, 0.3f }))
            column.CustomMinimumSize = new Vector2(Mathf.Floor(share * ratio) - 24, 0);
    }

    private void RefreshStation()
    {
        if (!StationMode)
            return;
        var view = Inventory.Shown.GetValueOrDefault(_open!);
        if (view is not null && view.Queue is null)
        {
            // Not a station after all: back to the container layout.
            _stationMode = false;
            StartStation(_open);
            Refresh();
            return;
        }
        var stationId = view?.Part ?? Creations?.Find(_open!.Creation!.Value)?.FindPart(_open.Part!.Value)?.Part;
        _stationTitle.Text = stationId is null ? Loc.T("Opening…") : view?.Blocked == true ? Loc.T("{0} (full: take the output)", ItemName(stationId)) : ItemName(stationId);
        _stationLevel.Visible = view is { MaxLevel: > 0 };
        _stationLevel.Text = view is null ? "" : Loc.T("Level {0} of {1}", view.Level, view.MaxLevel);
        _upgrade.Visible = view is { MaxLevel: > 0 } && Upgrades is not null;
        _upgrade.Text = Loc.T("Upgrade [{0}]", GameInput.Label(InputActions.Upgrade));
        _upgrade.Disabled = view is null || view.Level >= view.MaxLevel;
        SyncLinks(view?.Linked ?? []);

        var me = InventoryRef.OfPlayer(Inventory.Session.LocalPeer);
        var own = Inventory.Own;
        _stationOwnGrid.Fill(me, [.. Enumerable.Range(0, own?.SlotCount ?? Inventory.Settings!.PlayerSlots).Select(i => (i, own?.At(i)))]);
        _outputGrid.Fill(_open!, [.. Enumerable.Range(0, view?.SlotCount ?? 0).Select(i => (i, view?.At(i)))]);
        foreach (var where in _linked)
        {
            var shown = _linkedGrids[where];
            var linked = Inventory.Shown.GetValueOrDefault(where);
            shown.Title.Text = linked?.Part is { } part ? ItemName(part) : Loc.T("Opening…");
            shown.Grid.Fill(where, [.. Enumerable.Range(0, linked?.SlotCount ?? 0).Select(i => (i, linked?.At(i)))]);
        }
        _storage.GetNode<Control>("NoLinks").Visible = _linked.Count == 0;

        if (stationId is null)
            return;
        var recipes = RecipeBrowser.Filter(Inventory.Recipes.ForStation(stationId), _search.Text, _filter, Catalog);
        if (_chosen is not null && Inventory.Recipes.Find(_chosen)?.RunsAt(stationId) != true)
            _chosen = null;
        _chosen ??= recipes.FirstOrDefault()?.QualifiedId;
        FillRecipes(recipes);
        FillPreview(view);
        FillQueue(view);
    }

    // How many of an item the player and the linked storage hold, as last sent.
    private int Have(string item) =>
        (Inventory.Own?.Count(item) ?? 0) + _linked.Sum(l => Inventory.Shown.GetValueOrDefault(l)?.Count(item) ?? 0);

    private void FillRecipes(IReadOnlyList<Recipe> recipes)
    {
        var rows = recipes.Select(r => (r.QualifiedId, CanMake: r.Inputs.All(i => Have(i.Item) >= i.Count))).ToList();
        var key = string.Join('|', rows.Select(r => $"{r.QualifiedId}:{r.CanMake}"));
        if (key != _recipeKey)
        {
            _recipeKey = key;
            foreach (var child in _recipeList.GetChildren())
            {
                _recipeList.RemoveChild(child);
                child.QueueFree();
            }
            _recipeButtons.Clear();
            foreach (var (id, canMake) in rows)
            {
                var recipe = Inventory.Recipes.Find(id)!;
                var output = recipe.Outputs.FirstOrDefault().Item;
                var button = Loc.Verbatim(new Button
                {
                    Name = $"Recipe_{id.Replace(':', '_')}",
                    Text = recipe.Outputs.FirstOrDefault().Count > 1 ? $"{recipe.Name} × {recipe.Outputs[0].Count}" : recipe.Name,
                    Icon = output is null ? null : Icons?.For(output),
                    ExpandIcon = false,
                    ToggleMode = true,
                    Alignment = HorizontalAlignment.Left,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    TooltipText = canMake ? "" : Loc.T("Missing inputs"),
                });
                button.AddThemeConstantOverride("icon_max_width", 32);
                // Recipes the player can't make yet keep full-strength text, so they still read as choosable; only
                // the icon dims.
                if (!canMake)
                    foreach (var state in IconStates)
                        button.AddThemeColorOverride(state, UiTokens.Disabled);
                button.SetMeta("can_make", canMake);
                button.SetMeta("recipe", id);
                button.Pressed += () => Choose(id);
                _recipeList.AddChild(button);
                _recipeButtons[id] = button;
            }
            if (rows.Count == 0)
                _recipeList.AddChild(new Label { Name = "NoRecipes", Text = "No recipes match.", ThemeTypeVariation = "SecondaryLabel" });
        }
        foreach (var (id, button) in _recipeButtons)
            button.SetPressedNoSignal(id == _chosen);
    }

    private void FillPreview(InventoryView? view)
    {
        var recipe = _chosen is null ? null : Inventory.Recipes.Find(_chosen);
        var count = (int)_quantity.Value;
        var inputs = recipe is null ? [] : RecipeBrowser.Inputs(recipe, count, Have);
        var key = recipe is null ? "" : $"{recipe.QualifiedId}:{count}:{string.Join(',', inputs.Select(i => i.Have))}:{view?.Speed}";
        _craft.Disabled = recipe is null || inputs.Any(i => i.Missing) || view?.Queue is { } queue && view.QueueSize is { } size && queue.Count >= size;
        _craft.TooltipText = view?.Queue is { } q && view.QueueSize is { } limit && q.Count >= limit ? Loc.T("The queue is full ({0}).", limit) : "";
        if (key == _previewKey)
            return;
        _previewKey = key;
        foreach (var child in _previewInputs.GetChildren())
        {
            _previewInputs.RemoveChild(child);
            child.QueueFree();
        }
        if (recipe is null)
        {
            _previewOutput.Show(null, null, "", null);
            _previewName.Text = Loc.T("Choose a recipe");
            _previewDescription.Text = "";
            _previewTime.Text = "";
            return;
        }
        var output = recipe.Outputs.FirstOrDefault();
        _previewOutput.Show(output.Item, output.Item is null ? null : Icons?.For(output.Item), output.Item is null ? "" : ItemName(output.Item), output.Count * count);
        _previewOutput.Details = () => output.Item is null ? null : DetailsOf(output.Item, output.Count * count);
        _previewName.Text = output.Count * count > 1 ? $"{recipe.Name} × {output.Count * count}" : recipe.Name;
        _previewDescription.Text = output.Item is null ? "" : Catalog.FindDefinition(output.Item)?.Description ?? "";
        var speed = view?.Speed ?? 1f;
        _previewTime.Text = Loc.T("Takes {0}", Seconds(recipe.Time * count / speed));
        foreach (var input in inputs)
        {
            var box = new VBoxContainer { Name = $"Input_{input.Item.Replace(':', '_')}" };
            var slot = new ItemSlot { CustomMinimumSize = new Vector2(StationSlot, StationSlot) };
            slot.Show(input.Item, Icons?.For(input.Item), ItemName(input.Item), input.Need);
            var item = input.Item;
            slot.Details = () => DetailsOf(item, null);
            box.AddChild(slot);
            box.AddChild(Loc.Verbatim(new Label
            {
                Text = $"{input.Have} / {input.Need} {ItemName(input.Item)}",
                ThemeTypeVariation = input.Missing ? "ErrorLabel" : "SecondaryLabel",
                HorizontalAlignment = HorizontalAlignment.Center,
            }));
            _previewInputs.AddChild(box);
        }
    }

    private void FillQueue(InventoryView? view)
    {
        var jobs = view?.Queue ?? [];
        var speed = view?.Speed ?? 1f;
        // A craft finished: the orders left hold fewer crafts than before.
        var remaining = jobs.Sum(j => j.Count);
        if (_remainingCrafts >= 0 && remaining < _remainingCrafts)
            Flash();
        _remainingCrafts = remaining;
        while (_queueRows.Count > jobs.Count)
        {
            var last = _queueRows[^1];
            var row = last.Name.GetParent().GetParent();
            _queueList.RemoveChild(row);
            row.QueueFree();
            _queueRows.RemoveAt(_queueRows.Count - 1);
        }
        while (_queueRows.Count < jobs.Count)
        {
            var index = _queueRows.Count;
            var row = new VBoxContainer { Name = $"Job{index}" };
            var top = new HBoxContainer();
            var name = Loc.Verbatim(new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            top.AddChild(name);
            var left = Loc.Verbatim(new Label { ThemeTypeVariation = "SecondaryLabel" });
            top.AddChild(left);
            var cancel = new Button { Text = "Cancel", TooltipText = "Cancel the order and get its inputs back" };
            cancel.Pressed += () =>
            {
                if (_open is { Creation: { } c, Part: { } p })
                    Send(InventoryActions.CancelCraft, new CancelCraftArgs(c, p, index));
            };
            top.AddChild(cancel);
            row.AddChild(top);
            var bar = new ProgressBar { Name = "Progress", ShowPercentage = true, MaxValue = 100, CustomMinimumSize = new Vector2(0, 18) };
            row.AddChild(bar);
            _queueList.AddChild(row);
            _queueRows.Add((name, bar, left, cancel));
        }
        for (var i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            var (name, bar, left, _) = _queueRows[i];
            name.Text = $"{RecipeName(job.Recipe)} × {job.Count}";
            var current = job.Time > 0 ? Math.Clamp(job.Progress / job.Time, 0, 1) : 1f;
            bar.Value = Math.Round(current * 100);
            left.Text = i == 0 && view!.Blocked
                ? Loc.T("Output full")
                : i == 0 ? Loc.T("{0} left", Seconds(RecipeBrowser.Remaining(job.Time, job.Progress, job.Count, speed))) : Loc.T("Waiting");
        }
        if (jobs.Count == 0 && _queueList.GetNodeOrNull("Empty") is null)
            _queueList.AddChild(new Label { Name = "Empty", Text = "Nothing queued.", ThemeTypeVariation = "SecondaryLabel" });
        else if (jobs.Count > 0 && _queueList.GetNodeOrNull("Empty") is { } empty)
        {
            _queueList.RemoveChild(empty);
            empty.QueueFree();
        }
    }

    // The output panel glows cyan and fades back when a craft finishes.
    private void Flash()
    {
        _flash?.Kill();
        _outputPanel.Modulate = UiTokens.Cyan.Lightened(0.4f);
        _flash = CreateTween();
        _flash.TweenProperty(_outputPanel, "modulate", Colors.White, FlashSeconds);
    }

    // Opens or closes linked containers' views to match the station's links.
    private void SyncLinks(IReadOnlyList<InventoryRef> wanted)
    {
        foreach (var gone in _linked.Where(l => !wanted.Contains(l)).ToList())
        {
            Inventory.Close(gone);
            _linked.Remove(gone);
            var shown = _linkedGrids[gone];
            _storage.RemoveChild(shown.Box);
            shown.Box.QueueFree();
            _linkedGrids.Remove(gone);
        }
        foreach (var where in wanted.Where(w => !_linked.Contains(w)))
        {
            _linked.Add(where);
            Inventory.Open(where);
            var box = new VBoxContainer { Name = $"Linked{where.Part}" };
            var title = Loc.Verbatim(new Label { Text = Loc.T("Opening…") });
            box.AddChild(title);
            var grid = new SlotGrid(StationColumns, StationSlot);
            box.AddChild(grid.Build($"LinkedGrid{where.Part}", rows: 2, this));
            _storage.AddChild(box);
            // Linked storage sits under its heading, above the player's own inventory.
            _storage.MoveChild(box, _storage.GetNode("OwnTitle").GetIndex());
            _linkedGrids[where] = (title, grid, box);
        }
    }

    private void CloseLinks() => SyncLinks([]);

    private void LinkClosed(InventoryRef where)
    {
        if (_linked.Contains(where))
            _status.Text = Loc.T("A linked container is out of reach.");
    }

    // Shift+click on the player's own stack: into the first linked container, else the station (or the open container).
    private InventoryRef? ShiftTarget() => StationMode && _linked.Count > 0 ? _linked[0] : _open;

    private void OpenUpgrade()
    {
        if (_open is { Creation: { } c, Part: { } p } && Upgrades is not null)
            Upgrades.Open((c, p));
    }

    private static string Seconds(float seconds)
    {
        if (!float.IsFinite(seconds))
            return "-";
        var whole = (int)Math.Ceiling(seconds - 0.05);
        return whole < 60 ? Loc.T("{0} s", whole) : Loc.T("{0} min {1} s", whole / 60, whole % 60);
    }
}
