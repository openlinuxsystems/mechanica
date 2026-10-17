using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Logic;
using Mechanica.Core.Quests;
using Mechanica.Core.Survival;
using Mechanica.Game.Building;
using Mechanica.Game.Items;
using Mechanica.Game.Logic;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Quests;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Survival;

/// <summary>
/// The local player's survival HUD (chunk 20): a blue health bar at the top left inside the safe area (Survival
/// only, UI spec §6), the death screen with its respawn countdown, and Use to interact with what is in front of the
/// player: harvest a ripe
/// crop (the soil's <c>on_activate</c>, through <c>ActivatePart</c>), sleep in a bed to respawn there, or pick up
/// the nearest pile of items.
/// </summary>
public partial class SurvivalHud : CanvasLayer
{
    private readonly StyleBoxFlat _fill = MechanicaTheme.Box(UiTokens.Blue, margin: 0);
    private ProgressBar _health = null!;
    private Label _healthText = null!;
    private Label _hint = null!;
    private ColorRect _deathScreen = null!;
    private Label _deathText = null!;
    private Label _status = null!;

    /// <summary>Survival state. Set before adding to the tree.</summary>
    public SurvivalSync Survival { get; init; } = null!;

    /// <summary>Where requests go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>Players, for the local one. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Item names. Set before adding to the tree.</summary>
    public IPartCatalog Catalog { get; init; } = null!;

    /// <summary>Seats and usable parts (chunk 10); optional.</summary>
    public LogicSync? Logic { get; init; }

    /// <summary>The inventory screen, which Use on a container or station opens. Optional.</summary>
    public InventoryScreen? Containers { get; init; }

    /// <summary>The upgrade dialog, for the Upgrade (U) hint on an upgradable part; null shows none.</summary>
    public UpgradeDialog? Upgrades { get; init; }

    /// <summary>The part settings panel, which Use on a part with settings opens. Optional.</summary>
    public PartSettingsPanel? PartSettings { get; init; }

    /// <summary>Quests, for using quest markers and talking. Optional.</summary>
    public QuestSync? Quests { get; init; }

    /// <summary>True while the world tooltip shows, which then carries the Use hint. Optional.</summary>
    public Func<bool> TooltipShowing { get; init; } = () => false;

    /// <summary>What the tool in hand does with Use (E), ahead of the rest (the Lift's blueprint menu), or null.</summary>
    public Func<(string Label, Action Run)?> ToolInteraction { get; init; } = () => null;

    /// <summary>The health bar.</summary>
    public ProgressBar HealthBar => _health;

    public override void _Ready()
    {
        _health = new ProgressBar { Name = "Health", CustomMinimumSize = new Vector2(240, 18), ShowPercentage = false, MaxValue = 100 };
        // The theme's bar fills cyan; health is blue (§6).
        _health.AddThemeStyleboxOverride("fill", _fill);
        AddChild(_health);
        Layout();
        GetViewport().SizeChanged += Layout;
        _healthText = new Label { Name = "HealthText", HorizontalAlignment = HorizontalAlignment.Center };
        _health.AddChild(_healthText);
        _healthText.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _hint = new Label { Name = "Hint", HorizontalAlignment = HorizontalAlignment.Center };
        AddChild(_hint);
        _hint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.KeepSize);
        _hint.Position += new Vector2(0, 40);
        _status = new Label { Name = "Status", HorizontalAlignment = HorizontalAlignment.Center };
        AddChild(_status);
        _status.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.KeepSize, 80);
        _deathScreen = new ColorRect { Name = "DeathScreen", Color = new Color(0.35f, 0f, 0f, 0.55f), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_deathScreen);
        _deathScreen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _deathText = new Label { Name = "DeathText", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _deathScreen.AddChild(_deathText);
        _deathText.SetAnchorsPreset(Control.LayoutPreset.FullRect);
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    // Top left, inside the safe area.
    private void Layout()
    {
        var margin = HudRoot.Margin(GetViewport());
        _health.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _health.Position = margin;
    }

    public override void _Process(double delta)
    {
        var own = Survival.Own;
        var survival = own?.Mode == GameMode.Survival;
        var hud = !HudRoot.Hidden;
        _health.Visible = survival && hud;
        _status.Visible = hud;
        if (own is not null)
        {
            _health.MaxValue = own.MaxHealth;
            _health.Value = own.Health;
            _fill.BgColor = own.Health <= own.MaxHealth * (Survival.Settings?.LowHealth ?? 0) ? UiTokens.Error : UiTokens.Blue;
            _healthText.Text = $"{own.Health:0} / {own.MaxHealth:0}";
        }
        _deathScreen.Visible = own?.Dead == true;
        if (own is { Dead: true })
        {
            var wait = Survival.OwnRespawnIn;
            var where = own.HasBed ? "your bed" : "the spawn";
            _deathText.Text = wait > 0 ? Loc.T("You died.\nRespawn in {0} s", $"{Math.Ceiling(wait):0}") : Loc.T("You died.\n[{0}] respawn at {1}", GameInput.Label(InputActions.Interact), where);
        }
        var hints = new List<string>();
        if (own is not { Dead: true } && Interaction() is { } what)
            hints.Add($"[{GameInput.Label(InputActions.Interact)}] {what.Label}");
        if (own is not { Dead: true } && Upgrades is { IsOpen: false } upgrades && upgrades.Aimed() is { } part && upgrades.CanUpgrade(part))
            hints.Add($"[{GameInput.Label(InputActions.Upgrade)}] {Loc.T("upgrade")}");
        _hint.Text = string.Join("   ", hints);
        _hint.Visible = hud && !TooltipShowing();
    }

    public override void _Input(InputEvent @event)
    {
        // Ahead of the tools: a dead player has no tools.
        if (Survival.Own is { Dead: true } && GameInput.Pressed(@event, InputActions.Interact))
        {
            Respawn();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured || !GameInput.Pressed(@event, InputActions.Interact))
            return;
        Interact();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Asks to respawn.</summary>
    public void Respawn() => Request(SurvivalActions.Respawn, new NoArgs());

    /// <summary>Does what <see cref="Interaction"/> offers. False when there is nothing.</summary>
    public bool Interact()
    {
        if (Interaction() is not { } what)
            return false;
        what.Run();
        // Others see the player reach out and press.
        Players.LocalPlayer?.Gesture(Core.Characters.CharacterGesture.Interact);
        return true;
    }

    /// <summary>
    /// What Use (E) would do now: harvest the ripe crop or sleep in the bed under the crosshair, or else pick up
    /// the nearest pile in reach. With logic parts (chunk 10) it also stands a seated player up, sits in the
    /// seat under the crosshair, or uses a part that says how (<see cref="LogicDefinition.Use"/>). Null when
    /// there is nothing.
    /// </summary>
    public (string Label, Action Run)? Interaction()
    {
        if (Players.LocalPlayer is not { } player || Survival.Settings is not { } settings)
            return null;
        if (Logic is not null && player.Seated)
            return (Loc.T("stand up"), () => Request(LogicActions.LeaveSeat, new NoArgs()));
        if (ToolInteraction() is { } tool)
            return tool;
        if (HandTool.PartHit(HandTool.Cast(player, settings.Reach)) is { } aimed)
        {
            var creation = aimed.Node.Creation;
            var definition = creation.DefinitionOf(aimed.Part);
            if (Logic is not null && definition.Seat is not null && Logic.Seats.Occupant(creation.Id, aimed.Part) is null)
                return (Loc.T("sit in the {0}", definition.Name ?? Loc.T("seat")), () => Request(LogicActions.EnterSeat, new LogicPartArgs(creation.Id, aimed.Part)));
            if (Logic is not null && definition.Logic?.Use is { } use)
                return (Loc.T("{0} the {1}", use, definition.Name ?? Loc.T("part")), () => Request(LogicActions.ActivatePart, new LogicPartArgs(creation.Id, aimed.Part)));
            // Harvesting is the soil script's on_activate (D2); the prompt only shows once the synced crop is ripe.
            if (definition.Soil is not null && Survival.Soil.GetValueOrDefault((creation.Id, aimed.Part)) is { Ripe: true } soil)
                return (Loc.T("harvest {0}", Catalog.FindDefinition(soil.Crop!)?.Name ?? soil.Crop), () => Request(LogicActions.ActivatePart, new LogicPartArgs(creation.Id, aimed.Part)));
            if (definition.RespawnPoint == true)
                return (Loc.T("sleep here: respawn at this {0}", definition.Name ?? Loc.T("bed")), () => Request(SurvivalActions.SetSpawn, new PartUseArgs(creation.Id, aimed.Part)));
            if (Containers?.AimedInventory() is { } where)
                return (Loc.T("open the {0}", definition.Name ?? Loc.T("container")), () => Containers.Show(where));
            if (PartSettings is not null && definition.Settings is { Count: > 0 })
                return (Loc.T("set up the {0}", definition.Name ?? Loc.T("part")), () => PartSettings.Open(creation.Id, aimed.Part));
        }
        if (Quests?.Prompt() is { } quest)
            return (QuestLabel(quest), () => Request(QuestTracker.QuestInteract, new NoArgs()));
        var feet = player.GlobalPosition;
        var nearest = Survival.Drops.Values
            .Select(d => (Drop: d, Distance: d.Position.ToGodot().DistanceTo(feet)))
            .Where(d => d.Distance <= settings.PickupReach)
            .OrderBy(d => d.Distance)
            .Select(d => d.Drop)
            .FirstOrDefault();
        if (nearest is null)
            return null;
        var label = string.Join(", ", nearest.Items.Select(s => $"{Catalog.FindDefinition(s.Item)?.Name ?? s.Item} x{s.Count}"));
        return (Loc.T("pick up {0}", label), () => Request(SurvivalActions.PickUp, new PickUpArgs(nearest.Id)));
    }

    private string QuestLabel(QuestInteractResult quest) => quest switch
    {
        { Marker: "npc" } => Loc.T("talk"),
        { Ref: { } id } => Loc.T("use the {0}", Catalog.FindDefinition(id)?.Name ?? id),
        _ => Loc.T("use the {0}", quest.Marker.Replace('_', ' ')),
    };

    private void Request<T>(string action, T args) =>
        Actions.Request(action, args, result => _status.Text = result.Ok ? "" : result.Message ?? "");
}
