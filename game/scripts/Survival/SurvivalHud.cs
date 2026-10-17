using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Core.Logic;
using Mechanica.Core.Quests;
using Mechanica.Game.Building;
using Mechanica.Game.Logic;
using Mechanica.Game.Net;
using Mechanica.Game.Quests;

namespace Mechanica.Game.Survival;

/// <summary>
/// The local player's survival HUD (chunk 20): a health bar (Survival only), the death screen with its
/// respawn countdown (R respawns), and F to interact with what is in front of the player: harvest a ripe
/// crop, sleep in a bed to respawn there, or pick up the nearest pile of items.
/// </summary>
public partial class SurvivalHud : CanvasLayer
{
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

    /// <summary>Quests, for using quest markers and talking. Optional.</summary>
    public QuestSync? Quests { get; init; }

    public override void _Ready()
    {
        _health = new ProgressBar { Name = "Health", CustomMinimumSize = new Vector2(240, 18), ShowPercentage = false, MaxValue = 100 };
        AddChild(_health);
        _health.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.KeepSize, 24);
        _healthText = new Label { Name = "HealthText", HorizontalAlignment = HorizontalAlignment.Center };
        _health.AddChild(_healthText);
        _healthText.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hint = new Label { Name = "Hint", HorizontalAlignment = HorizontalAlignment.Center };
        AddChild(_hint);
        _hint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.KeepSize);
        _hint.Position += new Vector2(0, 40);
        _status = new Label { Name = "Status", HorizontalAlignment = HorizontalAlignment.Center };
        AddChild(_status);
        _status.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.KeepSize, 52);
        _deathScreen = new ColorRect { Name = "DeathScreen", Color = new Color(0.35f, 0f, 0f, 0.55f), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_deathScreen);
        _deathScreen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _deathText = new Label { Name = "DeathText", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _deathScreen.AddChild(_deathText);
        _deathText.SetAnchorsPreset(Control.LayoutPreset.FullRect);
    }

    public override void _Process(double delta)
    {
        var own = Survival.Own;
        var survival = own?.Mode == GameMode.Survival;
        _health.Visible = survival;
        if (own is not null)
        {
            _health.MaxValue = own.MaxHealth;
            _health.Value = own.Health;
            _health.Modulate = own.Health <= own.MaxHealth * (Survival.Settings?.LowHealth ?? 0) ? new Color(1, 0.4f, 0.4f) : Colors.White;
            _healthText.Text = $"{own.Health:0} / {own.MaxHealth:0}";
        }
        _deathScreen.Visible = own?.Dead == true;
        if (own is { Dead: true })
        {
            var wait = Survival.OwnRespawnIn;
            var where = own.HasBed ? "your bed" : "the spawn";
            _deathText.Text = wait > 0 ? $"You died.\nRespawn in {Math.Ceiling(wait):0} s" : $"You died.\n[R] respawn at {where}";
        }
        _hint.Text = own is { Dead: true } ? "" : Interaction() is { } what ? $"[F] {what.Label}" : "";
    }

    public override void _Input(InputEvent @event)
    {
        // Ahead of the tools, which use R too: a dead player has no tools.
        if (Survival.Own is { Dead: true } && @event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.R })
        {
            Respawn();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured || @event is not InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F })
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
        return true;
    }

    /// <summary>
    /// What F would do now: harvest the ripe crop or sleep in the bed under the crosshair, or else pick up
    /// the nearest pile in reach. With logic parts (chunk 10) it also stands a seated player up, sits in the
    /// seat under the crosshair, or uses a part that says how (<see cref="LogicDefinition.Use"/>). Null when
    /// there is nothing.
    /// </summary>
    public (string Label, Action Run)? Interaction()
    {
        if (Players.LocalPlayer is not { } player || Survival.Settings is not { } settings)
            return null;
        if (Logic is not null && player.Seated)
            return ("stand up", () => Request(LogicActions.LeaveSeat, new NoArgs()));
        if (HandTool.PartHit(HandTool.Cast(player, settings.Reach)) is { } aimed)
        {
            var creation = aimed.Node.Creation;
            var definition = creation.DefinitionOf(aimed.Part);
            if (Logic is not null && definition.Seat is not null && Logic.Seats.Occupant(creation.Id, aimed.Part) is null)
                return ($"sit in the {definition.Name ?? "seat"}", () => Request(LogicActions.EnterSeat, new LogicPartArgs(creation.Id, aimed.Part)));
            if (Logic is not null && definition.Logic?.Use is { } use)
                return ($"{use} the {definition.Name ?? "part"}", () => Request(LogicActions.ActivatePart, new LogicPartArgs(creation.Id, aimed.Part)));
            if (definition.Soil is not null && Survival.Soil.GetValueOrDefault((creation.Id, aimed.Part)) is { Ripe: true } soil)
                return ($"harvest {Catalog.FindDefinition(soil.Crop!)?.Name ?? soil.Crop}", () => Request(SurvivalActions.Harvest, new PartUseArgs(creation.Id, aimed.Part)));
            if (definition.RespawnPoint == true)
                return ($"sleep here: respawn at this {definition.Name ?? "bed"}", () => Request(SurvivalActions.SetSpawn, new PartUseArgs(creation.Id, aimed.Part)));
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
        return ($"pick up {label}", () => Request(SurvivalActions.PickUp, new PickUpArgs(nearest.Id)));
    }

    private string QuestLabel(QuestInteractResult quest) => quest switch
    {
        { Marker: "npc" } => "talk",
        { Ref: { } id } => $"use the {Catalog.FindDefinition(id)?.Name ?? id}",
        _ => $"use the {quest.Marker.Replace('_', ' ')}",
    };

    private void Request<T>(string action, T args) =>
        Actions.Request(action, args, result => _status.Text = result.Ok ? "" : result.Message ?? "");
}
