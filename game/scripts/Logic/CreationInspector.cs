using System.Text;
using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Logic;
using Mechanica.Game.Building;
using Mechanica.Game.Net;

namespace Mechanica.Game.Logic;

/// <summary>
/// The Creation Inspector (chunk 10): F8 shows a panel describing the Creation under the crosshair (or the
/// last one inspected): its mass, Bodies, joints, logic links, and each part's signal output and input,
/// settings, script and who sits in it. It reads everything with the <c>InspectCreation</c> query twice a
/// second, so a client sees the host's state, script faults included.
/// </summary>
public partial class CreationInspector : CanvasLayer
{
    /// <summary>Seconds between reads while the panel is up.</summary>
    public const double PollSeconds = 0.5;

    private PanelContainer _panel = null!;
    private RichTextLabel _text = null!;
    private double _sincePoll;
    private bool _asking;

    /// <summary>Players, for the local camera. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Where queries go. Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>False in automated tests: F8 does nothing.</summary>
    public bool ReadDeviceInput { get; init; } = true;

    /// <summary>The Creation shown, or null.</summary>
    public int? Target { get; private set; }

    /// <summary>The last description read, or null.</summary>
    public CreationInspection? Inspection { get; private set; }

    /// <summary>True while the panel is up.</summary>
    public bool IsOpen => _panel.Visible;

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "Inspector", Visible = false, CustomMinimumSize = new Vector2(460, 0) };
        AddChild(_panel);
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight, Control.LayoutPresetMode.KeepSize, 16);
        _text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(440, 0) };
        _panel.AddChild(_text);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!ReadDeviceInput || @event is not InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F8 })
            return;
        if (IsOpen)
            Close();
        else
            Open(Aimed() ?? Target);
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (!IsOpen)
            return;
        if (Aimed() is { } aimed && aimed != Target)
            Show(aimed);
        _sincePoll += delta;
        if (_sincePoll >= PollSeconds)
            Refresh();
    }

    /// <summary>Shows the panel for a Creation (null waits for one to be aimed at).</summary>
    public void Open(int? creationId)
    {
        _panel.Visible = true;
        if (creationId is { } id)
            Show(id);
        else
            _text.Text = "Aim at a creation to inspect it.";
    }

    /// <summary>Hides the panel.</summary>
    public void Close() => _panel.Visible = false;

    /// <summary>Switches to a Creation and reads it now.</summary>
    public void Show(int creationId)
    {
        Target = creationId;
        Inspection = null;
        Refresh();
    }

    /// <summary>Reads the shown Creation again.</summary>
    public void Refresh()
    {
        _sincePoll = 0;
        if (Target is not { } id || _asking)
            return;
        _asking = true;
        Actions.Request(LogicActions.InspectCreation, new InspectCreationArgs(id), result =>
        {
            _asking = false;
            if (id != Target)
                return;
            if (result is not { Ok: true, Value: { } value })
            {
                Inspection = null;
                _text.Text = result.Message ?? "";
                return;
            }
            Inspection = value.Deserialize<CreationInspection>(ActionJson.Options);
            _text.Text = Inspection is null ? "" : Describe(Inspection);
        });
    }

    /// <summary>The panel's text for a description (BBCode).</summary>
    public static string Describe(CreationInspection c)
    {
        var text = new StringBuilder();
        text.AppendLine($"[b]Creation {c.Id}[/b]  {c.Mass:0.#} kg, {c.Parts.Count} parts, revision {c.Revision}, simulated by peer {c.AuthorityPeer}");
        text.AppendLine($"Bodies: {string.Join(", ", c.Bodies.Select(b => $"#{b.Index} {b.Parts} parts {b.Mass:0.#} kg"))}");
        if (c.Joints.Count > 0)
            text.AppendLine($"Joints: {string.Join(", ", c.Joints.Select(j => $"{j.Part}→{j.Other} {j.Type.ToString().ToLowerInvariant()}{(j.Locked ? " (locked)" : "")}"))}");
        if (c.Links.Count > 0)
            text.AppendLine($"Links: {string.Join(", ", c.Links.Select(l => $"{l.From}→{l.To}"))}");
        foreach (var p in c.Parts)
        {
            var details = new List<string> { $"body {p.Body}" };
            if (p.Output != 0 || p.Input != 0)
                details.Add($"out {p.Output:0.###} in {p.Input:0.###}");
            if (p.Settings is { Count: > 0 } settings)
                details.Add(string.Join(" ", settings.Select(s => $"{s.Key}={s.Value.GetRawText()}")));
            if (p.Occupant is { } peer)
                details.Add($"seated: peer {peer}");
            if (p.Fault is { } fault)
                details.Add($"[color=#ff6060]script stopped: {fault}[/color]");
            else if (p.Script is not null)
                details.Add("script runs");
            text.AppendLine($"  {p.Id} {p.Name}: {string.Join(", ", details)}");
        }
        return text.ToString();
    }

    private int? Aimed() =>
        Players.LocalPlayer is { } player && HandTool.Cast(player, 8f) is { Node: { } node } ? node.Creation.Id : null;
}
