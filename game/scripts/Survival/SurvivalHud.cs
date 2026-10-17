using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Logic;
using Mechanica.Core.Quests;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Building;
using Mechanica.Game.Items;
using Mechanica.Game.Logic;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Quests;
using Mechanica.Game.Ui;
using Mechanica.Game.World;

namespace Mechanica.Game.Survival;

/// <summary>
/// The local player's survival HUD (chunk 20): a blue health bar at the top left inside the safe area (Survival
/// only, UI spec §6), the death screen with its respawn countdown, and Use to interact with what is in front of the
/// player: harvest a ripe
/// crop (the soil's <c>on_activate</c>, through <c>ActivatePart</c>), sleep in a bed to respawn there, or pick up
/// the nearest pile of items.
/// <para>
/// Also the survival interactions of UI spec §9 and §10: right click on a pile of items picks it up at once, with any
/// tool. A resource node (a tree, a rock node) under the crosshair gets a blue outline; holding Use on it fills the
/// ring round the crosshair over <see cref="SurvivalSettings.RefineSeconds"/> and refines it into the inventory.
/// Letting go, moving more than <see cref="SurvivalSettings.RefineCancelDistance"/>, opening a window or the game
/// losing focus cancels it. The host checks the hold (<c>StartRefine</c>, <c>FinishRefine</c>).
/// </para>
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
    private Node3D? _outline;
    private WorldObjectId? _outlined;
    // The refine being held: the node, when Use went down (this peer's clock) and where the player stood.
    private (WorldObjectId Id, double Started, Vector3 Feet)? _refine;

    // Blue, unshaded and drawn from the inside, slightly grown, so it reads as an outline round the node.
    private static readonly StandardMaterial3D OutlineMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Front,
        Grow = true,
        GrowAmount = 0.06f,
        AlbedoColor = UiTokens.Blue,
    };

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

    /// <summary>True while Use (E) is held; tests set it.</summary>
    public Func<bool> UseHeld { get; init; } = () => Input.IsActionPressed(InputActions.Interact);

    /// <summary>True while the player is in the world rather than a window (the mouse is captured); tests set it.</summary>
    public Func<bool> Playing { get; init; } = () => Input.MouseMode == Input.MouseModeEnum.Captured;

    /// <summary>The health bar.</summary>
    public ProgressBar HealthBar => _health;

    /// <summary>The node being refined, or null.</summary>
    public WorldObjectId? Refining => _refine?.Id;

    /// <summary>How far the refine being held has got, 0 to 1 (the ring round the crosshair), or null when none is.</summary>
    public float? RefineProgress => _refine is { } r && Survival.Settings is { } settings
        ? Math.Clamp((float)((Survival.Now - r.Started) / settings.RefineSeconds), 0f, 1f)
        : null;

    /// <summary>The resource node shown with the blue outline, or null.</summary>
    public WorldObjectId? Outlined => _outlined;

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
        _deathScreen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _deathText = new Label { Name = "DeathText", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _deathScreen.AddChild(_deathText);
        _deathText.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
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
        UpdateRefine();
        UpdateOutline(own is not { Dead: true } && Playing() ? AimedNode() : null);
        var hints = new List<string>();
        if (_refine is { } refining)
            hints.Add(Loc.T("Refining the {0}…", NodeName(Survival.FindObject(refining.Id))));
        else if (own is not { Dead: true } && Interaction() is { } what)
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
        // Right click on a loose pile picks it up whatever is in hand (UI spec §9), ahead of the tool's own right click.
        else if (Survival.Own is not { Dead: true } && Playing() && GameInput.Pressed(@event, InputActions.Secondary) && PickUpAimed())
            GetViewport().SetInputAsHandled();
    }

    public override void _Notification(int what)
    {
        // A hold never outlives the window losing focus (UI spec §11).
        if (what == NotificationApplicationFocusOut)
            CancelRefine();
    }

    /// <summary>The pile of items under the crosshair within pickup reach, or null.</summary>
    public Core.Survival.ItemDrop? AimedDrop()
    {
        if (Players.LocalPlayer is not { } player || Survival.Settings is not { } settings)
            return null;
        var from = player.AimOrigin;
        var direction = -player.Camera.GlobalBasis.Z;
        // A pile behind a wall or a part is not aimed at.
        var blocked = HandTool.Cast(player, settings.PickupReach) is { } hit ? hit.Point.DistanceTo(from) : settings.PickupReach;
        return DropAim.Aimed(Survival.Drops.Values, from.ToNumerics(), direction.ToNumerics(), Math.Min(blocked + DropAim.Radius, settings.PickupReach));
    }

    /// <summary>Picks up the pile under the crosshair. False when there is none.</summary>
    public bool PickUpAimed()
    {
        if (AimedDrop() is not { } drop)
            return false;
        Request(SurvivalActions.PickUp, new PickUpArgs(drop.Id));
        Players.LocalPlayer?.Gesture(Core.Characters.CharacterGesture.Interact);
        return true;
    }

    /// <summary>The resource node (<see cref="WorldObjectDefinition.Refine"/>) under the crosshair within reach, or null.</summary>
    public WorldObjectPlacement? AimedNode()
    {
        if (Players.LocalPlayer is not { } player || Survival.Settings is not { } settings)
            return null;
        if (HandTool.Cast(player, settings.Reach) is not { Collider: StaticBody3D objects } hit || objects.GetParent() is not TerrainChunk chunk
            || objects != chunk.Objects || chunk.ObjectAt(hit.Shape) is not { } id)
            return null;
        return Survival.FindObject(id) is { Definition.Refine: true } node ? node : null;
    }

    /// <summary>Starts holding Use on <paramref name="node"/> to refine it.</summary>
    public void BeginRefine(WorldObjectPlacement node)
    {
        if (Players.LocalPlayer is not { } player)
            return;
        _refine = (node.Id, Survival.Now, player.GlobalPosition);
        Request(SurvivalActions.StartRefine, new RefineArgs(node.Id.ToString()));
        player.Gesture(Core.Characters.CharacterGesture.Interact);
    }

    /// <summary>Stops a refine being held before it finished.</summary>
    public void CancelRefine()
    {
        if (_refine is null)
            return;
        _refine = null;
        Request(SurvivalActions.CancelRefine, new NoArgs());
    }

    // Finishes the refine once Use has been held long enough, or cancels it on letting go, moving off or a window.
    private void UpdateRefine()
    {
        if (_refine is not { } r || Survival.Settings is not { } settings)
            return;
        var player = Players.LocalPlayer;
        if (player is null || Survival.Own is { Dead: true } || !UseHeld() || !Playing()
            || player.GlobalPosition.DistanceTo(r.Feet) > settings.RefineCancelDistance)
        {
            CancelRefine();
            return;
        }
        if (Survival.Now - r.Started < settings.RefineSeconds)
            return;
        _refine = null;
        Request(SurvivalActions.FinishRefine, new RefineArgs(r.Id.ToString()));
    }

    // The blue outline round the resource node under the crosshair (UI spec §10).
    private void UpdateOutline(WorldObjectPlacement? node)
    {
        if (node?.Id == _outlined)
            return;
        _outline?.QueueFree();
        _outline = null;
        _outlined = node?.Id;
        if (node is null)
            return;
        _outline = TerrainChunk.Shell(node, OutlineMaterial);
        Survival.AddChild(_outline);
    }

    private static string NodeName(WorldObjectPlacement? node) => (node?.Definition.Id ?? "node").Replace('_', ' ');

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
        if (AimedNode() is { } node)
            return (Loc.T("hold to refine the {0}", NodeName(node)), () => BeginRefine(node));
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
