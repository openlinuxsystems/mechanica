using Godot;
using Mechanica.Core.Characters;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Input;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// The Paint Tool in the local player's hand (UI spec §2, §9). Left click paints the part under the crosshair in the
/// chosen colour, and held down paints every part the crosshair passes over (<see cref="PaintStroke"/>, up to
/// <see cref="PaintSettings.MaxPartsPerAction"/> a drag); right click and drag take the paint off. Q opens the colour
/// picker (<see cref="PaintPicker"/>), a ring of the palette in <c>building.json</c> round the cursor; Shift+Q steps
/// back through the palette. Each batch of parts is a <c>Paint</c> request; the host decides.
/// </summary>
public partial class PaintTool : HandTool
{
    private int _index;
    private PaintStroke? _stroke;
    private string? _strokeColor;
    private double _flushedAt;

    /// <inheritdoc/>
    public override string ToolName => "Paint Tool";

    /// <inheritdoc/>
    public override CharacterProp Prop => CharacterProp.PaintTool;

    /// <inheritdoc/>
    protected override CharacterGesture UseGesture => CharacterGesture.PaintSpray;

    /// <summary>The colour <see cref="Paint"/> uses, <c>"#rrggbb"</c>.</summary>
    public string Color => Build.Settings!.Paint.Palette[_index];

    /// <summary>The chosen colour's place in the palette.</summary>
    public int ColorIndex => _index;

    /// <summary>The colour picker.</summary>
    public PaintPicker Picker { get; private set; } = null!;

    /// <summary>The drag in progress, or null.</summary>
    public PaintStroke? Stroke => _stroke;

    public override void _Ready()
    {
        Picker = new PaintPicker { Name = "Picker", Palette = Build.Settings!.Paint.Palette, Chosen = Choose };
        AddChild(Picker);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_stroke is null)
            return;
        // A release the tool never heard (the mouse freed for a window) ends the drag.
        if (!Active || (ReadDeviceInput && Input.MouseMode != Input.MouseModeEnum.Captured))
        {
            EndStroke();
            return;
        }
        AddAimed();
        if (HammerTool.Now - _flushedAt >= Build.Settings!.Paint.DragFlushSeconds)
            Flush();
    }

    /// <summary>Picks the next (or previous) palette colour.</summary>
    public void Cycle(int step)
    {
        var count = Build.Settings!.Paint.Palette.Count;
        _index = ((_index + step) % count + count) % count;
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
    public void BeginStroke(bool erase = false)
    {
        _stroke = new PaintStroke(Build.Settings!.Paint.MaxPartsPerAction);
        _strokeColor = erase ? null : Color;
        AddAimed();
        Flush();
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
        _flushedAt = HammerTool.Now;
        if (_stroke is null)
            return;
        foreach (var (creation, parts) in _stroke.TakePending())
            Send(ToolActions.Paint, new PaintArgs(creation, parts, _strokeColor), done);
    }

    /// <summary>Ends the drag, sending what is left.</summary>
    public void EndStroke(Action<ActionResult>? done = null)
    {
        Flush(done);
        _stroke = null;
    }

    /// <inheritdoc/>
    public override string HudText() =>
        Loc.T("Paint Tool ({0})   {1} paint (drag for more)  {2} remove paint  {3} colours  {4} previous colour", Color, Key(InputActions.Primary), Key(InputActions.Secondary),
            Key(InputActions.Rotate), Key(InputActions.RotateBack)) + Status;

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
            BeginStroke();
        else if (Pressed(@event, InputActions.Secondary))
            BeginStroke(erase: true);
        else if (Released(@event, InputActions.Primary) || Released(@event, InputActions.Secondary))
        {
            if (_stroke is null)
                return false;
            EndStroke();
        }
        else if (Pressed(@event, InputActions.RotateBack))
            Cycle(-1);
        else if (Pressed(@event, InputActions.Rotate))
            Picker.Open(_index);
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
    }

    private bool Send(string? color, Action<ActionResult>? done)
    {
        if (PartHit(CastRay()) is not { } aimed)
            return false;
        Send(ToolActions.Paint, new PaintArgs(aimed.Node.Creation.Id, [aimed.Part], color), done);
        return true;
    }
}
