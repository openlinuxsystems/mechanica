using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Input;
using Mechanica.Core.Logic;
using Mechanica.Game.Creations;
using Mechanica.Game.Logic;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// The Connect Tool in the local player's hand (chunk 10, UI spec §2, §8, §9, §10, §12). With it out every part with
/// logic shows a coloured node (<see cref="LogicOverlay"/> draws them). Pressing the left button on a node and
/// dragging pulls a line from it that snaps to the nearest node it may end on within
/// <see cref="ConnectSettings.SnapMetres"/> of the aim; nodes it may not end on fade. Letting go on a node links
/// the two (or takes the link away if they were linked), and every bearing the drag passed over is linked too. Two
/// parts link whichever one the drag starts from: a Gas Container dragged to a Gas Engine, or the engine to the
/// container, both make container → engine (<see cref="Creation.DragLink"/>), so the link sent is the same either way.
/// Pressing on a line instead grabs its far end: letting go away from any node disconnects it, and on another node
/// moves it there. Right click on a node that can be reversed (a bearing) flips which way it turns, and right click
/// during a drag cancels it. Each change is a <c>ConnectParts</c>, <c>DisconnectParts</c> or <c>ConfigurePart</c>
/// request; the line shows at once and fades out red if the host refuses it. Others see the tool raised and aimed
/// while it is over a node or dragging (<see cref="Raised"/>, sent as the guard flag), a click of the wrist and a
/// pulse of its lights as a link is made or taken away, and a twist with the lights pulsing the other way as a
/// bearing is reversed (<see cref="ConnectToolLights"/>).
/// </summary>
public partial class ConnectTool : HandTool
{
    private readonly List<ConnectionNode> _nodes = [];
    private readonly List<ConnectionNode> _swept = [];
    private readonly List<PendingLink> _pending = [];
    private readonly Dictionary<(int Creation, int Part), bool> _reversing = [];

    /// <summary>A link the player made or took away that the host has not settled yet.</summary>
    /// <param name="Creation">The Creation's id.</param>
    /// <param name="Link">The link.</param>
    /// <param name="Adding">True for a new link, false for one being taken away.</param>
    public sealed record PendingLink(int Creation, LogicLink Link, bool Adding)
    {
        /// <summary>Seconds since it was sent.</summary>
        public double Age { get; set; }

        /// <summary>Seconds since the host refused it, or null while it waits or was taken.</summary>
        public double? Refused { get; set; }

        /// <summary>True once the host took it.</summary>
        public bool Taken { get; set; }
    }

    /// <inheritdoc/>
    public override string ToolName => "Connect Tool";

    /// <inheritdoc/>
    public override CharacterProp Prop => Owns ? CharacterProp.ConnectTool : CharacterProp.None;

    /// <inheritdoc/>
    /// <remarks>The tool's gestures go out from <see cref="ReleaseDrag"/> and <see cref="Reverse"/> when they change something.</remarks>
    protected override CharacterGesture GestureFor(InputEvent @event) => CharacterGesture.None;

    /// <inheritdoc/>
    protected override CharacterGesture UseGesture => CharacterGesture.ConnectClick;

    /// <inheritdoc/>
    protected override string RefusalSound => "connect_fail";

    private CreationWorld Creations => Build.Creations;

    /// <summary>The output this peer last read for a part, for tooltips; 0 until set.</summary>
    public Func<int, int, float> SignalOf { get; set; } = (_, _) => 0f;

    /// <summary>Raised when the player asks to see the settings of a part, with its Creation and part ids.</summary>
    public event Action<int, int>? SettingsRequested;

    /// <summary>The nodes in reach, refreshed every frame while the tool is out.</summary>
    public IReadOnlyList<ConnectionNode> Nodes => _nodes;

    /// <summary>The node under the crosshair (while dragging, only one the drag may end on), or null.</summary>
    public ConnectionNode? HoveredNode { get; private set; }

    /// <summary>The line under the crosshair when no node is, or null.</summary>
    public (int Creation, LogicLink Link)? HoveredLink { get; private set; }

    /// <summary>The node a drag started from, or null when not dragging.</summary>
    public ConnectionNode? DragFrom { get; private set; }

    /// <summary>The existing link whose far end is being dragged, or null.</summary>
    public (int Creation, LogicLink Link)? Grabbed { get; private set; }

    /// <summary>True while a line is being dragged.</summary>
    public bool Dragging => DragFrom is not null;

    /// <summary>True while the tool is held up and aimed: over a node, or dragging a line (or <see cref="KeepRaised"/>).</summary>
    public bool Raised => Dragging || HoveredNode is not null || KeepRaised;

    /// <summary>Holds the tool up and aimed whatever it is over, for capture renders.</summary>
    public bool KeepRaised { get; set; }

    /// <summary>Bearings the drag passed over, linked too when it ends.</summary>
    public IReadOnlyList<ConnectionNode> Swept => _swept;

    /// <summary>Links sent and not settled, and refused ones still fading out.</summary>
    public IReadOnlyList<PendingLink> Pending => _pending;

    /// <summary>Where the dragged line ends now: the snapped node, else the aim point in reach.</summary>
    public Vector3 DragEnd => HoveredNode?.Position ?? Eye - Player.Camera.GlobalBasis.Z * MathF.Min(Build.Settings!.Reach, 2.5f);

    private ConnectSettings Look => Build.Settings!.Connect;

    /// <summary>
    /// True when a drag from the current source may end on <paramref name="node"/>: another part of the same
    /// Creation that the link checks allow either way round, or one already linked either way (letting go there
    /// takes the link away). A grabbed link's own target counts too.
    /// </summary>
    public bool CanEnd(ConnectionNode node)
    {
        if (DragFrom is not { } from || node.Creation != from.Creation || node.Part == from.Part || Creations.Find(from.Creation) is not { } creation)
            return false;
        if (Grabbed is { } grabbed)
            return node.Part == grabbed.Link.To || creation.LinkBetween(from.Part, node.Part) is not null;
        return creation.LinkBetween(from.Part, node.Part) is not null || creation.LinkedBetween(from.Part, node.Part) is not null;
    }

    /// <summary>The reverse state a node shows: the one just asked for until the host answers, else its own.</summary>
    public bool ShownReversed(ConnectionNode node) => _reversing.TryGetValue((node.Creation, node.Part), out var asked) ? asked : node.Reversed;

    public override void _Ready() => HudRoot.FocusLost += CancelDrag;

    public override void _ExitTree() => HudRoot.FocusLost -= CancelDrag;

    public override void _Process(double delta)
    {
        foreach (var pending in _pending)
        {
            pending.Age += delta;
            if (pending.Refused is { } refused)
                pending.Refused = refused + delta;
        }
        // A taken link goes once the Creation shows it; a refused one once it has faded; any after the timeout.
        _pending.RemoveAll(p => p.Refused > Build.Settings!.Placement.RollbackSeconds || p.Age > PendingGhost.TimeoutSeconds
            || (p.Taken && Creations.Find(p.Creation) is var c && (c is null || c.Links.Contains(p.Link) == p.Adding)));
        if (!Active || Build.Settings is null)
            return;
        if (!Owns)
        {
            CancelDrag();
            _nodes.Clear();
            HoveredNode = null;
            HoveredLink = null;
            Player.HeldGuard = false;
            return;
        }
        if (ReadDeviceInput && Input.MouseMode != Input.MouseModeEnum.Captured && Dragging)
            CancelDrag();
        UpdateHover();
        Player.HeldGuard = Raised;
    }

    /// <summary>Re-reads the nodes in reach and what the crosshair is over. Runs every frame while the tool is out.</summary>
    public void UpdateHover()
    {
        _nodes.Clear();
        var reach = Build.Settings!.Reach;
        foreach (var node in ConnectionNodes.Nearby(Creations, Eye, Look.Range))
            _nodes.AddRange(ConnectionNodes.Of(node));
        var direction = -Player.Camera.GlobalBasis.Z;
        var candidates = Dragging ? _nodes.Where(CanEnd).ToList() : _nodes;
        ConnectionNode? hovered = null;
        // The node of the part the crosshair is on wins. Otherwise a drag snaps to the one nearest the aim within the
        // snap radius (§10), and a hover takes a node only when the crosshair is on its dot.
        if (PartHit(CastRay()) is var (aimed, part) && candidates.FirstOrDefault(n => n.Creation == aimed.Creation.Id && n.Part == part) is { Definition: not null } own)
            hovered = own;
        else if (Dragging && ConnectionLayout.Snap(Eye.ToNumerics(), direction.ToNumerics(), [.. candidates.Select(n => n.Position.ToNumerics())], Look.SnapMetres, reach) is var i and >= 0)
            hovered = candidates[i];
        else if (!Dragging)
            hovered = NodeUnderCrosshair(candidates, reach);
        HoveredNode = hovered;
        foreach (var node in _nodes)
        {
            if (_reversing.TryGetValue((node.Creation, node.Part), out var asked) && asked == node.Reversed)
                _reversing.Remove((node.Creation, node.Part));
        }
        HoveredLink = hovered is null && !Dragging ? LinkUnderCrosshair() : null;
        // Bearings the drag passes over join it (§2: dragging over several bearings connects them all).
        if (Dragging && Grabbed is null && hovered is { Kind: PortKind.Bearing } bearing && !_swept.Any(n => n.Creation == bearing.Creation && n.Part == bearing.Part)
            && Creations.Find(bearing.Creation) is { } owner && owner.LinkBetween(DragFrom!.Value.Part, bearing.Part) is not null)
            _swept.Add(bearing);
    }

    /// <summary>Starts a drag from the hovered node, or grabs the hovered line's far end. False when neither is hovered.</summary>
    public bool PressDrag()
    {
        UpdateHover();
        if (HoveredNode is { } node)
        {
            DragFrom = node;
            Grabbed = null;
        }
        else if (HoveredLink is var (creationId, link) && Creations.NodeOf(creationId) is { } creationNode
            && ConnectionNodes.Of(creationNode).FirstOrDefault(n => n.Part == link.From) is { Definition: not null } from)
        {
            DragFrom = from;
            Grabbed = (creationId, link);
        }
        else
        {
            return false;
        }
        _swept.Clear();
        PlayFeedback("ui_click");
        UpdateHover();
        return true;
    }

    /// <summary>
    /// Ends the drag: links the source to the node under the crosshair and every bearing passed over, takes the link
    /// away when that node was already linked, or moves or disconnects a grabbed link. False when nothing changes.
    /// </summary>
    public bool ReleaseDrag(Action<ActionResult>? done = null)
    {
        if (DragFrom is not { } from)
            return false;
        UpdateHover();
        var target = HoveredNode;
        var grabbed = Grabbed;
        var swept = _swept.ToList();
        CancelDrag();
        if (Creations.Find(from.Creation) is not { } creation)
            return false;
        var sent = false;
        void Change(LogicLink link, bool adding)
        {
            _pending.Add(new PendingLink(from.Creation, link, adding));
            var pending = _pending[^1];
            Send(adding ? LogicActions.ConnectParts : LogicActions.DisconnectParts, new LinkPartsArgs(from.Creation, link.From, link.To), result =>
            {
                if (result.Ok)
                    pending.Taken = true;
                else
                    pending.Refused = 0;
                done?.Invoke(result);
            });
            sent = true;
        }
        if (grabbed is var (_, old))
        {
            // Let go off any node: disconnect; on another node: move the link there.
            if (target is { } moved && moved.Part == old.To)
                return false;
            Change(old, adding: false);
            if (target is { } to)
                Change(creation.LinkBetween(from.Part, to.Part) ?? new LogicLink(from.Part, to.Part), adding: true);
            Player.Gesture(CharacterGesture.ConnectClick);
            return true;
        }
        var targets = swept.Select(n => n.Part).ToList();
        if (target is { } end && !targets.Contains(end.Part))
            targets.Add(end.Part);
        foreach (var part in targets)
        {
            var (link, remove) = creation.DragLink(from.Part, part);
            // Letting go on a part that is already linked takes that link away; a swept bearing is only ever added.
            if (remove && (swept.Count > 0 || part != target?.Part))
                continue;
            Change(link, adding: !remove);
        }
        if (sent)
            Player.Gesture(CharacterGesture.ConnectClick);
        return sent;
    }

    /// <summary>Drops the drag without changing anything.</summary>
    public void CancelDrag()
    {
        DragFrom = null;
        Grabbed = null;
        _swept.Clear();
    }

    /// <summary>
    /// Flips which way the hovered node's part turns (a bearing something drives). False when the hovered node has no
    /// direction to flip.
    /// </summary>
    public bool Reverse(Action<ActionResult>? done = null)
    {
        UpdateHover();
        if (HoveredNode is not { Reversible: true } node)
            return false;
        var key = (node.Creation, node.Part);
        var flipped = !ShownReversed(node);
        _reversing[key] = flipped;
        PlayFeedback("logic_click", node.Position);
        Player.Gesture(CharacterGesture.ConnectReverse);
        var settings = new Dictionary<string, JsonElement?> { [node.Definition.Logic!.Reverse!] = JsonSerializer.SerializeToElement(flipped) };
        Send(LogicActions.ConfigurePart, new ConfigurePartArgs(node.Creation, node.Part, settings), result =>
        {
            // A refusal shows the old way again; a change stays shown until the part itself shows it.
            if (!result.Ok && _reversing.TryGetValue(key, out var asked) && asked == flipped)
                _reversing.Remove(key);
            done?.Invoke(result);
        });
        return true;
    }

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

    /// <summary>Over a node, or dragging, the crosshair pulses cyan (§8); over a line it is blue.</summary>
    public override Core.Ui.CrosshairState Crosshair =>
        HoveredNode is not null || Dragging ? Core.Ui.CrosshairState.Connection
        : HoveredLink is not null ? Core.Ui.CrosshairState.Valid
        : Core.Ui.CrosshairState.Default;

    /// <summary>
    /// The tooltip for the line under the crosshair (§9, §12): "Source → Target" and "Signal: …". A node shows its
    /// part's own tooltip, which names its connection kind.
    /// </summary>
    public override (object Key, string Title, string Body)? HoverTip
    {
        get
        {
            if (HoveredLink is var (creationId, link) && Creations.Find(creationId) is { } creation)
            {
                string Name(int part) => creation.DefinitionOf(part).Name ?? creation.DefinitionOf(part).Id ?? "";
                var signal = SignalOf(creationId, link.From);
                var value = signal == 0 ? Loc.T("Off") : signal == 1 ? Loc.T("On") : $"{signal:0.##}";
                return (HoveredLink, $"{Name(link.From)} → {Name(link.To)}", Loc.T("Signal: {0}", value));
            }
            return null;
        }
    }

    /// <summary>Right click reverses a bearing or cancels a drag, once the player has the tool.</summary>
    public override bool OwnsRightClick => Owns;

    /// <inheritdoc/>
    public override string HudText()
    {
        if (!Owns)
            return Loc.T("Connect Tool: craft one at a Fabricator to wire parts");
        if (Grabbed is not null)
            return Loc.T("Connect Tool: let go on a node to move the link, away from one to disconnect  {0} cancel", Key(InputActions.Secondary)) + Status;
        if (Dragging)
            return Loc.T("Connect Tool: let go on a node to link it  {0} cancel", Key(InputActions.Secondary)) + Status;
        return Loc.T("Connect Tool: {0} drag from a node to link  {1} reverse a bearing  {2} settings",
            Key(InputActions.Primary), Key(InputActions.Secondary), Key(InputActions.Interact)) + Status;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(InputEvent @event)
    {
        if (!Owns)
            return false;
        if (Pressed(@event, InputActions.Primary))
            PressDrag();
        else if (Game.Player.GameInput.Released(@event, InputActions.Primary))
            return ReleaseDrag();
        else if (Pressed(@event, InputActions.Secondary))
        {
            if (Dragging)
                CancelDrag();
            else
                Reverse();
        }
        else
            return false;
        return true;
    }

    /// <inheritdoc/>
    protected override void OnActiveChanged()
    {
        if (Active)
            return;
        CancelDrag();
        HoveredNode = null;
        HoveredLink = null;
        _nodes.Clear();
        Player.HeldGuard = false;
    }

    // The node in reach whose dot the screen centre is on, nearest first.
    private ConnectionNode? NodeUnderCrosshair(IReadOnlyList<ConnectionNode> nodes, float reach)
    {
        var camera = Player.Camera;
        var viewport = Player.GetViewport();
        if (viewport is null)
            return null;
        var centre = viewport.GetVisibleRect().Size / 2;
        var radius = (Look.NodePixels / 2 * Look.HoverScale + Look.HitPixels) * LogicOverlay.PixelScale(viewport);
        return nodes.Where(n => !camera.IsPositionBehind(n.Position) && n.Position.DistanceTo(Eye) <= reach + Look.SnapMetres
                && camera.UnprojectPosition(n.Position).DistanceTo(centre) <= radius)
            .OrderBy(n => n.Position.DistanceTo(Eye)).Cast<ConnectionNode?>().FirstOrDefault();
    }

    // The line nearest the screen centre, within a few pixels of it.
    private (int Creation, LogicLink Link)? LinkUnderCrosshair()
    {
        var viewport = Player.GetViewport();
        if (viewport is null)
            return null;
        var centre = viewport.GetVisibleRect().Size / 2;
        var scale = LogicOverlay.PixelScale(viewport);
        var best = ((int, LogicLink)?)null;
        var bestDistance = (Look.LineWidth / 2 + Look.HitPixels) * scale;
        foreach (var segment in LogicOverlay.Segments(Player.Camera, Creations, Eye, Look, scale))
        {
            var distance = ConnectionLayout.DistanceToSegment(centre.ToNumerics(), segment.From.ToNumerics(), segment.To.ToNumerics());
            if (distance <= bestDistance)
                (best, bestDistance) = ((segment.Creation, segment.Link), distance);
        }
        return best;
    }
}
