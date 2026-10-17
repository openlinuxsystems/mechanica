using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Items;
using Mechanica.Game.Creations;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Items;

/// <summary>
/// The upgrade dialog (UI spec §4, §8), opened with Upgrade (U) on a part that has upgrade levels, or from the Fabricator
/// screen: the part's level, what the next level costs in Component Kits (or other items; red when the player is short,
/// free in Creative) and what it changes (speed, queue, slots, script properties), with Upgrade to confirm. The host
/// checks and applies it (<c>UpgradePart</c>). Esc, the X or Close shuts it.
/// </summary>
public partial class UpgradeDialog : CanvasLayer
{
    private ToolWindow _window = null!;
    private Label _level = null!;
    private VBoxContainer _cost = null!;
    private VBoxContainer _changes = null!;
    private Label _status = null!;
    private Button _confirm = null!;
    private int _shownLevel = -1;

    /// <summary>Inventory sync, for what the player holds. Set before adding to the tree.</summary>
    public InventorySync Inventory { get; init; } = null!;

    /// <summary>Where requests go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>The Creations this peer shows. Set before adding to the tree.</summary>
    public CreationWorld Creations { get; init; } = null!;

    /// <summary>Part definitions. Set before adding to the tree.</summary>
    public IPartCatalog Catalog { get; init; } = null!;

    /// <summary>The part under the crosshair in reach, or null. Set before adding to the tree.</summary>
    public Func<(int Creation, int Part)?> Aimed { get; init; } = () => null;

    /// <summary>Item pictures; null shows initials.</summary>
    public ItemIcons? Icons { get; init; }

    /// <summary>True while the dialog is up.</summary>
    public bool IsOpen => _window.Visible;

    /// <summary>The part shown, or null.</summary>
    public (int Creation, int Part)? Target { get; private set; }

    /// <summary>The window.</summary>
    public ToolWindow Window => _window;

    /// <summary>The Upgrade button.</summary>
    public Button Confirm => _confirm;

    /// <summary>The level line (<c>Level 1 of 3</c>).</summary>
    public string LevelText => _level.Text;

    /// <summary>The cost lines as shown, and whether each is short.</summary>
    public IReadOnlyList<(string Text, bool Missing)> CostLines =>
        [.. _cost.GetChildren().OfType<HBoxContainer>().Select(r => r.GetChild<Label>(1)).Select(l => (l.Text, l.ThemeTypeVariation == "ErrorLabel"))];

    /// <summary>The change lines as shown (<c>Speed: 1× → 1.25×</c>).</summary>
    public IReadOnlyList<string> ChangeLines => [.. _changes.GetChildren().OfType<Label>().Select(l => l.Text)];

    /// <summary>What the last upgrade did, or why not.</summary>
    public string Status => _status.Text;

    public override void _Ready()
    {
        Layer = 6;
        _window = new ToolWindow { Name = "Upgrade", Title = "Upgrade", Visible = false, CustomMinimumSize = new Vector2(420, 0) };
        _window.CloseRequested += Close;
        AddChild(_window);
        _level = Loc.Verbatim(new Label { Name = "Level", ThemeTypeVariation = "TitleLabel" });
        _window.Body.AddChild(_level);
        _window.Body.AddChild(new Label { Text = "Next level costs", ThemeTypeVariation = "SecondaryLabel" });
        _cost = new VBoxContainer { Name = "Cost" };
        _window.Body.AddChild(_cost);
        _window.Body.AddChild(new Label { Text = "It changes", ThemeTypeVariation = "SecondaryLabel" });
        _changes = new VBoxContainer { Name = "Changes" };
        _window.Body.AddChild(_changes);
        _status = Loc.Verbatim(new Label { Name = "Status", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(380, 0) });
        _window.Body.AddChild(_status);
        _window.AddAction("Close", Close);
        _confirm = _window.AddAction("Upgrade", () => Upgrade(), primary: true, tooltip: "Spend the cost and raise the part one level");
        Inventory.ViewChanged += OnViewChanged;
    }

    public override void _ExitTree() => Inventory.ViewChanged -= OnViewChanged;

    // The player's own items decide whether the cost is short.
    private void OnViewChanged(InventoryView view)
    {
        if (IsOpen && view.Inventory == InventoryRef.OfPlayer(Inventory.Session.LocalPeer))
            Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!GameInput.Pressed(@event, InputActions.Upgrade) || GameInput.Typing(GetViewport()))
            return;
        if (IsOpen)
            Close();
        else if (Input.MouseMode == Input.MouseModeEnum.Captured && Aimed() is { } part && CanUpgrade(part))
            Open(part);
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (!IsOpen)
            return;
        if (Target is not { } target || Part(target) is not { } part)
        {
            Close();
            return;
        }
        if (part.Instance.Level != _shownLevel)
            Refresh();
        _window.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.KeepSize);
    }

    /// <summary>True when the part exists here and has upgrade levels.</summary>
    public bool CanUpgrade((int Creation, int Part) target) => Part(target) is { } part && PartUpgrades.MaxLevel(part.Written) > 0;

    /// <summary>Shows the dialog for a part.</summary>
    public void Open((int Creation, int Part) target)
    {
        Target = target;
        _status.Text = "";
        _shownLevel = -1;
        Refresh();
        _window.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Hides the dialog. The mouse goes back to the game unless another window is up.</summary>
    public void Close()
    {
        if (!IsOpen)
            return;
        _window.Visible = false;
        Target = null;
        if (ToolWindow.Topmost is null && !OtherScreenOpen())
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>A screen under the dialog that keeps the mouse (the Fabricator screen); null for none.</summary>
    public Func<bool> OtherScreenOpen { get; init; } = () => false;

    /// <summary>Asks the host to upgrade the part. False when there is nothing to upgrade.</summary>
    public bool Upgrade(Action<ActionResult>? done = null)
    {
        if (Target is not { } target || Part(target) is not { } part || part.Instance.Level >= PartUpgrades.MaxLevel(part.Written))
            return false;
        var name = part.Written.Name ?? part.Instance.Part;
        Actions.Request(InventoryActions.UpgradePart, new UpgradePartArgs(target.Creation, target.Part), result =>
        {
            if (!IsInstanceValid(this))
                return;
            _status.Text = result.Ok ? Loc.T("{0} upgraded.", name) : result.Message ?? "";
            done?.Invoke(result);
        });
        return true;
    }

    private void Refresh()
    {
        if (Target is not { } target || Part(target) is not { } part)
            return;
        var (instance, written) = part;
        var top = PartUpgrades.MaxLevel(written);
        _shownLevel = instance.Level;
        var name = written.Name ?? instance.Part;
        _level.Text = Loc.T("{0}: level {1} of {2}", name, instance.Level, top);
        foreach (var child in _cost.GetChildren().Concat(_changes.GetChildren()))
        {
            child.GetParent().RemoveChild(child);
            child.QueueFree();
        }
        _confirm.Disabled = instance.Level >= top;
        if (instance.Level >= top)
        {
            _cost.AddChild(new Label { Text = "None: this is the top level." });
            return;
        }
        var cost = written.Upgrades![instance.Level].Cost ?? [];
        var own = Inventory.Own;
        if (Inventory.Settings!.FreeUpgrades || cost.Count == 0)
            _cost.AddChild(new Label { Text = cost.Count == 0 ? "Nothing." : "Free in Creative." });
        else
        {
            var missing = false;
            foreach (var item in cost)
            {
                var id = Identifiers.Qualify(item.Item!);
                var need = item.Count ?? 1;
                var have = own?.Count(id) ?? 0;
                missing |= have < need;
                var row = new HBoxContainer();
                var slot = new ItemSlot { CustomMinimumSize = new Vector2(40, 40), MouseFilter = Control.MouseFilterEnum.Ignore };
                row.AddChild(slot);
                slot.Show(id, Icons?.For(id), ItemName(id), null);
                row.AddChild(Loc.Verbatim(new Label
                {
                    Text = Loc.T("{0} {1} (you have {2})", need, ItemName(id), have),
                    ThemeTypeVariation = have < need ? "ErrorLabel" : "",
                    VerticalAlignment = VerticalAlignment.Center,
                }));
                _cost.AddChild(row);
            }
            _confirm.Disabled = missing;
        }
        foreach (var change in PartUpgrades.Changes(PartUpgrades.At(written, instance.Level), PartUpgrades.At(written, instance.Level + 1)))
            _changes.AddChild(Loc.Verbatim(new Label { Text = $"{Loc.T(change.Label)}: {change.From} → {change.To}" }));
    }

    private (Core.Creations.PartInstance Instance, PartDefinition Written)? Part((int Creation, int Part) target) =>
        Creations.Find(target.Creation)?.FindPart(target.Part) is { } instance && Catalog.FindDefinition(instance.Part) is { } written
            ? (instance, written)
            : null;

    private string ItemName(string id) => Catalog.FindDefinition(id)?.Name ?? id;
}
