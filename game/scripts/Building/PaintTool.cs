using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Input;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// The Paint Tool in the local player's hand (UI spec §2, §9; Chase's paint tool spec). Left click paints the part under
/// the crosshair in the chosen colour, and held down paints every part the crosshair passes over (<see cref="PaintStroke"/>,
/// up to <see cref="PaintSettings.MaxPartsPerAction"/> a drag); right click and drag take the paint off. Q opens the
/// colour picker (<see cref="PaintPicker"/>: the palette's hues round a ring, each in its shades); Shift+Q steps back
/// through the palette. R twists the tank off and back on (<see cref="PaintToolSettings.ReloadSeconds"/>, no spraying
/// meanwhile). Each batch of parts is a <c>Paint</c> request carrying the drag's <see cref="PaintArgs.Stroke"/>; the host
/// decides, and in Survival takes <see cref="PaintSettings.AmmoPerStroke"/> Paint Ammo once a drag. While a drag is held
/// the tool is raised and spraying (the guard byte), and its colour (<see cref="PlayerController.HeldTint"/>) tints the
/// tubes, the tank and the mist on every peer (<see cref="PaintToolLook"/>).
/// </summary>
public partial class PaintTool : HandTool
{
    private int _index = -1;
    private PaintStroke? _stroke;
    private string? _strokeColor;
    private double _flushedAt;
    private int _strokeId;
    private double _reloadUntil = double.NegativeInfinity;

    /// <summary>Character settings (the reload's length); null uses the defaults. Set before adding to the tree.</summary>
    public CharacterSettings? Characters { get; init; }

    /// <summary>How much Paint Ammo the player carries, or null when painting costs none (Creative). Set by the tool belt.</summary>
    public Func<int?>? AmmoCount { get; set; }

    /// <summary>Holds the tool up and spraying (taking paint off with <see cref="KeepErasing"/>) with no drag, for capture renders.</summary>
    public bool KeepSpraying { get; set; }

    /// <summary>With <see cref="KeepSpraying"/>: the mist is the pale one of taking paint off.</summary>
    public bool KeepErasing { get; set; }

    /// <summary>True while the tank is being twisted off and on (R): the tool does not spray.</summary>
    public bool Reloading => BuildTool.Now < _reloadUntil;

    /// <summary>Seconds a reload takes.</summary>
    public float ReloadSeconds => (Characters?.PaintTool ?? new PaintToolSettings()).ReloadSeconds;

    /// <inheritdoc/>
    public override string ToolName => "Paint Tool";

    /// <inheritdoc/>
    public override CharacterProp Prop => Owns ? CharacterProp.PaintTool : CharacterProp.None;

    /// <inheritdoc/>
    protected override CharacterGesture UseGesture => CharacterGesture.PaintSpray;

    /// <summary>The colour <see cref="Paint"/> uses, <c>"#rrggbb"</c>.</summary>
    public string Color => Build.Settings!.Paint.Palette[ColorIndex];

    /// <summary>The chosen colour's place in the palette (the settings' default colour until one is picked).</summary>
    public int ColorIndex => _index >= 0 ? _index : Build.Settings!.Paint.DefaultColor;

    /// <summary>
    /// What others see of the tool: <see cref="ColorIndex"/>, with <see cref="CharacterRig.TintErase"/> while a drag takes
    /// paint off.
    /// </summary>
    public byte Tint => (byte)(ColorIndex | ((_stroke is not null && _strokeColor is null) || (KeepSpraying && KeepErasing) ? CharacterRig.TintErase : 0));

    /// <summary>The colour picker.</summary>
    public PaintPicker Picker { get; private set; } = null!;

    /// <summary>The drag in progress, or null.</summary>
    public PaintStroke? Stroke => _stroke;

    public override void _Ready()
    {
        Picker = new PaintPicker { Name = "Picker", Palette = Build.Settings!.Paint.Palette, Shades = Build.Settings.Paint.Shades, Chosen = Choose };
        AddChild(Picker);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Active)
        {
            Player.HeldTint = Tint;
            Player.HeldGuard = _stroke is not null || KeepSpraying;
        }
        if (_stroke is null)
            return;
        // A release the tool never heard (the mouse freed for a window) ends the drag.
        if (!Active || (ReadDeviceInput && Input.MouseMode != Input.MouseModeEnum.Captured))
        {
            EndStroke();
            return;
        }
        AddAimed();
        if (BuildTool.Now - _flushedAt >= Build.Settings!.Paint.DragFlushSeconds)
            Flush();
    }

    /// <summary>Picks the next (or previous) palette colour.</summary>
    public void Cycle(int step)
    {
        var count = Build.Settings!.Paint.Palette.Count;
        _index = ((ColorIndex + step) % count + count) % count;
    }

    /// <summary>Picks palette colour <paramref name="index"/>; out of range is ignored.</summary>
    public void Choose(int index)
    {
        if (index >= 0 && index < Build.Settings!.Paint.Palette.Count)
            _index = index;
    }

    /// <summary>Paints the part under the crosshair. False when no part is aimed at.</summary>
    public bool Paint(Action<ActionResult>? done = null) => Send(Color, done);

    /// <summary>Takes the paint off the part under the crosshair. False when no part is aimed at.</summary>
    public bool Erase(Action<ActionResult>? done = null) => Send(null, done);

    /// <summary>
    /// Starts a drag that paints in the chosen colour (or, with <paramref name="erase"/>, takes paint off) the part under
    /// the crosshair now and every one it passes over until <see cref="EndStroke"/>.
    /// </summary>
    /// <returns>False while reloading: nothing starts.</returns>
    public bool BeginStroke(bool erase = false)
    {
        if (Reloading || !Owns)
            return false;
        _stroke = new PaintStroke(Build.Settings!.Paint.MaxPartsPerAction);
        _strokeColor = erase ? null : Color;
        _strokeId++;
        Player.HeldGuard = true;
        Player.HeldTint = Tint;
        AddAimed();
        Flush();
        return true;
    }

    /// <summary>
    /// Twists the tank off and back on (R): ends any drag, plays the reload for everyone and stops spraying for
    /// <see cref="ReloadSeconds"/>. False while a reload is already under way.
    /// </summary>
    public bool Reload()
    {
        if (Reloading || !Owns)
            return false;
        EndStroke();
        _reloadUntil = BuildTool.Now + ReloadSeconds;
        Player.Gesture(CharacterGesture.PaintReload);
        return true;
    }

    /// <summary>Adds the part under the crosshair to the drag (the tool does it every physics frame while held).</summary>
    public void AddAimed()
    {
        if (_stroke is not null && PartHit(CastRay()) is { } aimed)
            _stroke.Add(aimed.Node.Creation.Id, aimed.Part);
    }

    /// <summary>Sends what the drag has passed over since the last send.</summary>
    public void Flush(Action<ActionResult>? done = null)
    {
        _flushedAt = BuildTool.Now;
        if (_stroke is null)
            return;
        foreach (var (creation, parts) in _stroke.TakePending())
            Send(ToolActions.Paint, new PaintArgs(creation, parts, _strokeColor, _strokeId), done);
    }

    /// <summary>Ends the drag, sending what is left.</summary>
    public void EndStroke(Action<ActionResult>? done = null)
    {
        Flush(done);
        _stroke = null;
        Player.HeldGuard = false;
    }

    /// <summary>
    /// The HUD's ammo counter while the tool is in hand: the Paint Ammo carried and "Reloading…" during a reload, or null
    /// when painting is free.
    /// </summary>
    public string? AmmoText()
    {
        if (AmmoCount?.Invoke() is not { } count || Build.Settings!.Paint.Ammo is not { } ammo)
            return Reloading ? Loc.T("Reloading…") : null;
        var name = Build.Catalog.FindDefinition(Core.Data.Identifiers.Qualify(ammo))?.Name ?? ammo;
        var line = $"{name} {count}";
        return Reloading ? $"{line}\n{Loc.T("Reloading…")}" : count == 0 ? $"{line}\n{Loc.T("No paint: craft Paint Ammo at a Fabricator")}" : line;
    }

    /// <summary>Right click takes paint off.</summary>
    public override bool OwnsRightClick => true;

    /// <inheritdoc/>
    public override string HudText() => !Owns ? Loc.T("Paint Tool: craft one at a Fabricator to paint parts") :
        Loc.T("Paint Tool ({0})   {1} paint (drag for more)  {2} remove paint  {3} colours  {4} previous colour  {5} reload", Color, Key(InputActions.Primary), Key(InputActions.Secondary),
            Key(InputActions.Rotate), Key(InputActions.RotateBack), Key(InputActions.Reload)) + Status;

    /// <inheritdoc/>
    protected override CharacterGesture GestureFor(InputEvent @event) =>
        (Pressed(@event, InputActions.Primary) || Pressed(@event, InputActions.Secondary)) && _stroke is not null ? UseGesture : CharacterGesture.None;

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
            BeginStroke();
        else if (Pressed(@event, InputActions.Secondary))
            BeginStroke(erase: true);
        else if (Pressed(@event, InputActions.Reload))
            Reload();
        else if (Released(@event, InputActions.Primary) || Released(@event, InputActions.Secondary))
        {
            if (_stroke is null)
                return false;
            EndStroke();
        }
        else if (Pressed(@event, InputActions.RotateBack))
            Cycle(-1);
        else if (Pressed(@event, InputActions.Rotate))
            Picker.Open(ColorIndex);
        else
            return false;
        return true;
    }

    /// <inheritdoc/>
    protected override void OnActiveChanged()
    {
        if (Active)
            return;
        EndStroke();
        Picker.Close();
        Player.HeldGuard = false;
    }

    private bool Send(string? color, Action<ActionResult>? done)
    {
        if (PartHit(CastRay()) is not { } aimed)
            return false;
        Send(ToolActions.Paint, new PaintArgs(aimed.Node.Creation.Id, [aimed.Part], color, ++_strokeId), done);
        return true;
    }
}
