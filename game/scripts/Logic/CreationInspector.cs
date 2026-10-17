using System.Text;
using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Input;
using Mechanica.Core.Logic;
using Mechanica.Core.Ui;
using Mechanica.Game.Building;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Logic;

/// <summary>
/// The Creation Inspector (chunk 10): F8 shows a panel describing the Creation under the crosshair (or the
/// last one inspected): its mass, Bodies, joints, logic links, and each part's signal output and input,
/// settings, script and who sits in it. It reads everything with the <c>InspectCreation</c> query twice a
/// second, so a client sees the host's state, script faults included. Esc or its X closes it.
/// </summary>
public partial class CreationInspector : CanvasLayer
{
    /// <summary>Seconds between reads while the panel is up.</summary>
    public const double PollSeconds = 0.5;

    private ToolWindow _panel = null!;
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
        // Top right, up to most of the screen's height; a big Creation scrolls.
        _panel = new ToolWindow { Name = "Inspector", Title = "Creation Inspector", Visible = false };
        _panel.CloseRequested += Close;
        AddChild(_panel);
        (_panel.AnchorLeft, _panel.AnchorRight, _panel.AnchorTop, _panel.AnchorBottom) = (1, 1, 0, 0.7f);
        (_panel.OffsetLeft, _panel.OffsetRight, _panel.OffsetTop, _panel.OffsetBottom) = (-476, -16, 16, 0);
        _text = Loc.Verbatim(new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(440, 0) });
        _panel.Body.AddChild(_text);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!ReadDeviceInput || !GameInput.Pressed(@event, InputActions.CreationInspector))
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
        text.AppendLine($"[b]{Loc.T("Creation {0}", c.Id)}[/b]  {Loc.T("{0} kg, {1} parts, revision {2}, simulated by peer {3}", $"{c.Mass:0.#}", c.Parts.Count, c.Revision, c.AuthorityPeer)}");
        text.AppendLine(Loc.T("Bodies: {0}", string.Join(", ", c.Bodies.Select(b => Loc.T("#{0} {1} parts {2} kg", b.Index, b.Parts, $"{b.Mass:0.#}")))));
        if (c.Joints.Count > 0)
            text.AppendLine(Loc.T("Joints: {0}", string.Join(", ", c.Joints.Select(j => $"{j.Part}→{j.Other} {j.Type.ToString().ToLowerInvariant()}{(j.Locked ? " " + Loc.T("(locked)") : "")}"))));
        if (c.Links.Count > 0)
            text.AppendLine(Loc.T("Links: {0}", string.Join(", ", c.Links.Select(l => $"{l.From}→{l.To}"))));
        foreach (var p in c.Parts)
        {
            var details = new List<string> { Loc.T("body {0}", p.Body) };
            if (p.Output != 0 || p.Input != 0)
                details.Add(Loc.T("out {0} in {1}", $"{p.Output:0.###}", $"{p.Input:0.###}"));
            if (p.Settings is { Count: > 0 } settings)
                details.Add(string.Join(" ", settings.Select(s => $"{s.Key}={s.Value.GetRawText()}")));
            if (p.Occupant is { } peer)
                details.Add(Loc.T("seated: peer {0}", peer));
            if (p.Fault is { } fault)
                details.Add($"[color={UiPalette.Error}]{Loc.T("script stopped: {0}", fault)}[/color]");
            else if (p.Script is not null)
                details.Add(Loc.T("script runs"));
            text.AppendLine($"  {p.Id} {p.Name}: {string.Join(", ", details)}");
        }
        return text.ToString();
    }

    private int? Aimed() =>
        Players.LocalPlayer is { } player && HandTool.Cast(player, 8f) is { Node: { } node } ? node.Creation.Id : null;
}
