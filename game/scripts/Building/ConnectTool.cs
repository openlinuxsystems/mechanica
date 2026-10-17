using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Input;
using Mechanica.Core.Logic;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// The Connect Tool in the local player's hand (chunk 10). Left click picks the part under the crosshair as
/// the source; left clicking another part of the same Creation then links the source's output to it, or
/// takes that link away again if it is there. The source stays picked, so one seat can feed several parts.
/// Right click lets go of the source. R opens the settings of the part under the crosshair
/// (<see cref="SettingsRequested"/>). Each change is a <c>ConnectParts</c>, <c>DisconnectParts</c> or
/// <c>ConfigurePart</c> request; the host decides. While it is in hand the Logic Visualizer shows links.
/// </summary>
public partial class ConnectTool : HandTool
{
    /// <inheritdoc/>
    public override string ToolName => "Connect Tool";

    /// <summary>The picked source part, or null.</summary>
    public (int Creation, int Part)? Source { get; private set; }

    /// <summary>Raised when the player asks to see the settings of a part (R), with its Creation and part ids.</summary>
    public event Action<int, int>? SettingsRequested;

    /// <summary>
    /// Picks the part under the crosshair as the source, or links (or unlinks) the source to it. False when no
    /// part is aimed at.
    /// </summary>
    public bool Pick(Action<ActionResult>? done = null)
    {
        if (PartHit(CastRay()) is not var (node, part))
            return false;
        var creation = node.Creation;
        if (Source is not { } source || source.Creation != creation.Id || creation.FindPart(source.Part) is null)
        {
            Source = (creation.Id, part);
            PlayFeedback("ui_click");
            return true;
        }
        if (source.Part == part)
        {
            Source = null;
            return true;
        }
        var linked = creation.Links.Contains(new Core.Creations.LogicLink(source.Part, part));
        Send(linked ? LogicActions.DisconnectParts : LogicActions.ConnectParts, new LinkPartsArgs(creation.Id, source.Part, part), done);
        return true;
    }

    /// <summary>Lets go of the source.</summary>
    public void Clear() => Source = null;

    /// <summary>Changes settings of the part under the crosshair. False when no part is aimed at.</summary>
    public bool Configure(Dictionary<string, JsonElement?> settings, Action<ActionResult>? done = null)
    {
        if (PartHit(CastRay()) is not var (node, part))
            return false;
        Send(LogicActions.ConfigurePart, new ConfigurePartArgs(node.Creation.Id, part, settings), done);
        return true;
    }

    /// <summary>Asks for the settings panel of the part under the crosshair. False when no part is aimed at.</summary>
    public bool OpenSettings()
    {
        if (PartHit(CastRay()) is not var (node, part))
            return false;
        SettingsRequested?.Invoke(node.Creation.Id, part);
        return true;
    }

    /// <inheritdoc/>
    public override string HudText()
    {
        var picked = Source is { } s ? Loc.T("part {0} picked: {1} another part to link or unlink it", s.Part, Key(InputActions.Primary)) : Loc.T("{0} pick a source part", Key(InputActions.Primary));
        return Loc.T("Connect Tool: {0}  {1} let go  {2} settings", picked, Key(InputActions.Secondary), Key(InputActions.Interact)) + Status;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (Pressed(@event, InputActions.Primary))
            Pick();
        else if (Pressed(@event, InputActions.Secondary))
            Clear();
        else
            return false;
        return true;
    }

    /// <inheritdoc/>
    protected override void OnActiveChanged()
    {
        if (!Active)
            Source = null;
    }
}
