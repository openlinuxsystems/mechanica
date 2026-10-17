using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

namespace Mechanica.Core.Logic;

/// <summary>Arguments of <c>ConnectParts</c> and <c>DisconnectParts</c>: a logic link inside one Creation.</summary>
/// <param name="Creation">The Creation holding both parts.</param>
/// <param name="From">The part whose output the link carries.</param>
/// <param name="To">The part it feeds.</param>
public sealed record LinkPartsArgs(int Creation, int From, int To);

/// <summary>Arguments of <c>ConfigurePart</c>: settings to change on one placed part.</summary>
/// <param name="Creation">The Creation holding the part.</param>
/// <param name="Part">The part.</param>
/// <param name="Settings">New values by setting id; null puts a setting back to its default. Settings not named keep their value.</param>
public sealed record ConfigurePartArgs(int Creation, int Part, Dictionary<string, JsonElement?> Settings);

/// <summary>Arguments of <c>ActivatePart</c> and <c>EnterSeat</c>: one part of a Creation.</summary>
/// <param name="Creation">The Creation holding the part.</param>
/// <param name="Part">The part.</param>
public sealed record LogicPartArgs(int Creation, int Part);

/// <summary>Arguments of <c>InspectCreation</c>.</summary>
/// <param name="Creation">The Creation to describe.</param>
public sealed record InspectCreationArgs(int Creation);

/// <summary>Value of <c>ConnectParts</c> and <c>DisconnectParts</c>.</summary>
/// <param name="Links">How many links the Creation has now.</param>
public sealed record LinkResult(int Links);

/// <summary>Value of <c>ConfigurePart</c>: every setting of the part as it is now.</summary>
/// <param name="Settings">Values by setting id, in the definition's order.</param>
public sealed record ConfigureResult(IReadOnlyDictionary<string, JsonElement> Settings);

/// <summary>One part in the value of <c>InspectCreation</c>.</summary>
/// <param name="Id">Part id.</param>
/// <param name="Type">Qualified part type.</param>
/// <param name="Name">Display name.</param>
/// <param name="Body">Index of its Body.</param>
/// <param name="Output">Its output on the logic bus (0 when none).</param>
/// <param name="Input">The sum of its inputs on the logic bus.</param>
/// <param name="Settings">Every setting's value, when the part has settings.</param>
/// <param name="Script">Its script, when it runs one.</param>
/// <param name="Fault">Why its script is stopped, when it is.</param>
/// <param name="Occupant">The player sitting in it, when it is a taken seat.</param>
public sealed record PartInspection(int Id, string Type, string Name, int Body, float Output, float Input,
    IReadOnlyDictionary<string, JsonElement>? Settings = null, string? Script = null, string? Fault = null, int? Occupant = null);

/// <summary>One Body in the value of <c>InspectCreation</c>.</summary>
/// <param name="Index">Body index.</param>
/// <param name="Parts">How many parts it has.</param>
/// <param name="Mass">Mass in kg.</param>
public sealed record BodyInspection(int Index, int Parts, float Mass);

/// <summary>One joint in the value of <c>InspectCreation</c>.</summary>
/// <param name="Part">The part that owns the joint.</param>
/// <param name="Other">The part on its other side.</param>
/// <param name="Type">Rotational, linear or spring.</param>
/// <param name="Locked">True when both sides are in one Body.</param>
public sealed record JointInspection(int Part, int Other, ConnectionType Type, bool Locked);

/// <summary>Value of <c>InspectCreation</c>: what the Creation Inspector and the Logic Visualizer show.</summary>
/// <param name="Id">Creation id.</param>
/// <param name="AuthorityPeer">Peer that simulates it (D5).</param>
/// <param name="Revision">Its graph revision.</param>
/// <param name="Mass">Total mass in kg.</param>
/// <param name="Parts">Its parts, by id.</param>
/// <param name="Bodies">Its Bodies, by index.</param>
/// <param name="Joints">Its joints.</param>
/// <param name="Links">Its logic links, by source then target.</param>
public sealed record CreationInspection(int Id, int AuthorityPeer, int Revision, float Mass, IReadOnlyList<PartInspection> Parts,
    IReadOnlyList<BodyInspection> Bodies, IReadOnlyList<JointInspection> Joints, IReadOnlyList<LogicLink> Links);

/// <summary>What the logic actions need from the part scripts. The host's script runtime implements it.</summary>
public interface ILogicHost
{
    /// <summary>The logic and power bus the scripts drive.</summary>
    SignalBus Signals { get; }

    /// <summary>Null when a player can use the part now (its script handles <c>on_activate</c>), or why not. Changes nothing.</summary>
    string? CheckActivate(Creation creation, int part);

    /// <summary>Calls the part's <c>on_activate(peer)</c>.</summary>
    void Activate(ActionCaller caller, Creation creation, int part);

    /// <summary>A player sat down in (<paramref name="entered"/>) or stood up from a seat: calls its script's <c>on_enter</c> or <c>on_leave</c>.</summary>
    void Seated(SeatAssignment seat, bool entered);

    /// <summary>The part's script and why it is stopped (null while it runs), or nulls when it runs none.</summary>
    (string? Script, string? Fault) ScriptOf(int creation, int part);
}

/// <summary>
/// The logic and power actions of the Action API (chunk 10), validated on the host like every other action:
/// <list type="bullet">
/// <item><c>ConnectParts</c> / <c>DisconnectParts</c>: the Connect Tool adds or takes away a logic link
/// (<see cref="Creation.CheckLink"/>).</item>
/// <item><c>ConfigurePart</c>: changes a placed part's settings (<see cref="PartSettings"/>).</item>
/// <item><c>ActivatePart</c>: a player uses a part (a button or switch), which calls its script's <c>on_activate</c>.</item>
/// <item><c>EnterSeat</c> / <c>LeaveSeat</c>: a player sits in a seat part or stands up (<see cref="SeatRegistry"/>).</item>
/// <item><c>InspectCreation</c>: reads a Creation's parts, Bodies, joints, links, signals and scripts (a query).</item>
/// </list>
/// Player callers must be within <see cref="BuildSettings.Reach"/> of the parts they act on. Links and settings
/// bump <see cref="Creation.Revision"/>, so state sync sends the Creation to clients again.
/// </summary>
public sealed class LogicActions(BuildSettings settings, IBuildWorld world, ILogicHost host, SeatRegistry seats)
{
    /// <summary>Action name: add a logic link.</summary>
    public const string ConnectParts = "ConnectParts";

    /// <summary>Action name: remove a logic link.</summary>
    public const string DisconnectParts = "DisconnectParts";

    /// <summary>Action name: change a part's settings.</summary>
    public const string ConfigurePart = "ConfigurePart";

    /// <summary>Action name: use a part.</summary>
    public const string ActivatePart = "ActivatePart";

    /// <summary>Action name: sit in a seat.</summary>
    public const string EnterSeat = "EnterSeat";

    /// <summary>Action name: stand up from the caller's seat.</summary>
    public const string LeaveSeat = "LeaveSeat";

    /// <summary>Action name: describe a Creation (read-only).</summary>
    public const string InspectCreation = "InspectCreation";

    /// <summary>Who sits where.</summary>
    public SeatRegistry Seats => seats;

    /// <summary>Raised after <c>EnterSeat</c> (true) or <c>LeaveSeat</c> (false) changed who sits where, so the game can move the player.</summary>
    public event Action<SeatAssignment, bool>? SeatChanged;

    /// <summary>Registers the actions.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<LinkPartsArgs>(ConnectParts, ValidateConnect, ApplyConnect);
        dispatcher.Register<LinkPartsArgs>(DisconnectParts, ValidateDisconnect, ApplyDisconnect);
        dispatcher.Register<ConfigurePartArgs>(ConfigurePart, ValidateConfigure, ApplyConfigure);
        dispatcher.Register<LogicPartArgs>(ActivatePart, ValidateActivate, ApplyActivate);
        dispatcher.Register<LogicPartArgs>(EnterSeat, ValidateEnter, ApplyEnter);
        dispatcher.Register<NoArgs>(LeaveSeat, ValidateLeave, ApplyLeave);
        dispatcher.Register<InspectCreationArgs>(InspectCreation, (_, args) => world.FindCreation(args.Creation) is null ? $"There is no creation {args.Creation}." : null,
            (_, args) => Value(Inspect(world.FindCreation(args.Creation)!)), readOnly: true);
    }

    /// <summary>
    /// Stands up every player whose seat is gone (its part removed or split into another Creation) or who
    /// may no longer sit (<paramref name="canSit"/> false: they left or died). Returns who was stood up.
    /// </summary>
    public IReadOnlyList<SeatAssignment> Vacate(Func<int, bool> canSit)
    {
        var gone = new List<SeatAssignment>();
        foreach (var seat in seats.All)
        {
            var creation = world.FindCreation(seat.Creation);
            if (creation?.FindPart(seat.Part) is not null && creation.DefinitionOf(seat.Part).Seat is not null && canSit(seat.Peer))
                continue;
            seats.Leave(seat.Peer);
            if (creation?.FindPart(seat.Part) is not null)
                host.Seated(seat, entered: false);
            gone.Add(seat);
        }
        return gone;
    }

    /// <summary>Describes a Creation as <c>InspectCreation</c> does.</summary>
    public CreationInspection Inspect(Creation creation)
    {
        var parts = creation.Parts.Select(p =>
        {
            var definition = creation.DefinitionOf(p.Id);
            var (script, fault) = host.ScriptOf(creation.Id, p.Id);
            return new PartInspection(p.Id, p.Part, definition.Name ?? p.Part, creation.BodyIndexOf(p.Id),
                host.Signals.Output(creation.Id, p.Id), host.Signals.Inputs(creation, p.Id).Sum(i => i.Value),
                definition.Settings is { Count: > 0 } ? PartSettings.All(definition, p.Settings).ToDictionary(s => s.Id, s => s.Value) : null,
                script, fault, seats.Occupant(creation.Id, p.Id));
        }).ToList();
        return new CreationInspection(creation.Id, creation.AuthorityPeer, creation.Revision, creation.Bodies.Sum(b => b.Mass), parts,
            [.. creation.Bodies.Select(b => new BodyInspection(b.Index, b.PartIds.Count, b.Mass))],
            [.. creation.Joints.Select(j => new JointInspection(j.Connection.A, j.Connection.B, j.Connection.Type, j.IsLocked))],
            creation.Links);
    }

    private string? ValidateConnect(ActionCaller caller, LinkPartsArgs args)
    {
        if (world.FindCreation(args.Creation) is not { } creation)
            return $"There is no creation {args.Creation}.";
        if (creation.CheckLink(args.From, args.To) is { } error)
            return Sentence(error);
        return CheckReach(caller, creation, args.From) ?? CheckReach(caller, creation, args.To);
    }

    private JsonElement? ApplyConnect(ActionCaller caller, LinkPartsArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        if (!creation.TryLink(args.From, args.To, out var error))
            throw new InvalidOperationException(error);
        world.PlaySound("connect_success", Identifiers.VanillaModId, BuildActions.PartCentre(creation, args.To));
        return Value(new LinkResult(creation.Links.Count));
    }

    private string? ValidateDisconnect(ActionCaller caller, LinkPartsArgs args)
    {
        if (world.FindCreation(args.Creation) is not { } creation)
            return $"There is no creation {args.Creation}.";
        if (!creation.Links.Contains(new LogicLink(args.From, args.To)))
            return $"Part {args.From} does not feed part {args.To}.";
        return CheckReach(caller, creation, args.To);
    }

    private JsonElement? ApplyDisconnect(ActionCaller caller, LinkPartsArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        creation.Unlink(args.From, args.To);
        world.PlaySound("disconnect", Identifiers.VanillaModId, BuildActions.PartCentre(creation, args.To));
        return Value(new LinkResult(creation.Links.Count));
    }

    private string? ValidateConfigure(ActionCaller caller, ConfigurePartArgs args)
    {
        if (world.FindCreation(args.Creation) is not { } creation)
            return $"There is no creation {args.Creation}.";
        if (creation.FindPart(args.Part) is null)
            return $"Creation {args.Creation} has no part {args.Part}.";
        var definition = creation.DefinitionOf(args.Part);
        if (definition.Settings is not { Count: > 0 })
            return $"{definition.Name ?? definition.Id} has no settings.";
        if (args.Settings.Count == 0)
            return "Name at least one setting.";
        foreach (var (id, value) in args.Settings)
        {
            if (PartSettings.Find(definition, id) is not { } setting)
                return $"{definition.Name ?? definition.Id} has no setting '{id}'.";
            if (value is { } v && PartSettings.Check(setting, v) is { } error)
                return error;
        }
        return CheckReach(caller, creation, args.Part);
    }

    private JsonElement? ApplyConfigure(ActionCaller caller, ConfigurePartArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        var definition = creation.DefinitionOf(args.Part);
        var stored = PartSettings.Apply(definition, creation.FindPart(args.Part)!.Settings, args.Settings);
        if (creation.SetPartSettings(args.Part, stored))
            world.PlaySound("logic_click", Identifiers.VanillaModId, BuildActions.PartCentre(creation, args.Part));
        return Value(new ConfigureResult(PartSettings.All(definition, stored).ToDictionary(s => s.Id, s => s.Value)));
    }

    private string? ValidateActivate(ActionCaller caller, LogicPartArgs args)
    {
        if (world.FindCreation(args.Creation) is not { } creation)
            return $"There is no creation {args.Creation}.";
        if (creation.FindPart(args.Part) is null)
            return $"Creation {args.Creation} has no part {args.Part}.";
        return host.CheckActivate(creation, args.Part) ?? CheckReach(caller, creation, args.Part);
    }

    private JsonElement? ApplyActivate(ActionCaller caller, LogicPartArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        PlayPartSound(creation, args.Part, "use", "logic_activate");
        host.Activate(caller, creation, args.Part);
        return null;
    }

    private string? ValidateEnter(ActionCaller caller, LogicPartArgs args)
    {
        if (world.FindCreation(args.Creation) is not { } creation)
            return $"There is no creation {args.Creation}.";
        if (creation.FindPart(args.Part) is null)
            return $"Creation {args.Creation} has no part {args.Part}.";
        var definition = creation.DefinitionOf(args.Part);
        if (definition.Seat is null)
            return $"{definition.Name ?? definition.Id} is not a seat.";
        if (world.EyeOf(caller.Peer) is null)
            return "You have no player to sit down.";
        if (seats.Occupant(args.Creation, args.Part) is { } other && other != caller.Peer)
            return "Someone is already sitting there.";
        return CheckReach(caller, creation, args.Part);
    }

    private JsonElement? ApplyEnter(ActionCaller caller, LogicPartArgs args)
    {
        if (seats.SeatOf(caller.Peer) is { } old && old != new SeatAssignment(caller.Peer, args.Creation, args.Part))
            ApplyLeave(caller, new NoArgs());
        var creation = world.FindCreation(args.Creation)!;
        if (seats.Occupant(args.Creation, args.Part) == caller.Peer)
            return null;
        seats.Sit(caller.Peer, args.Creation, args.Part);
        PlayPartSound(creation, args.Part, "enter", "seat_enter");
        var seat = new SeatAssignment(caller.Peer, args.Creation, args.Part);
        host.Seated(seat, entered: true);
        SeatChanged?.Invoke(seat, true);
        return null;
    }

    private string? ValidateLeave(ActionCaller caller, NoArgs args) =>
        seats.SeatOf(caller.Peer) is null ? "You are not sitting anywhere." : null;

    private JsonElement? ApplyLeave(ActionCaller caller, NoArgs args)
    {
        var seat = seats.Leave(caller.Peer)!.Value;
        if (world.FindCreation(seat.Creation) is { } creation && creation.FindPart(seat.Part) is not null)
        {
            PlayPartSound(creation, seat.Part, "exit", "seat_exit");
            host.Seated(seat, entered: false);
        }
        SeatChanged?.Invoke(seat, false);
        return null;
    }

    private void PlayPartSound(Creation creation, int part, string key, string fallback) =>
        BuildActions.PlayPartSound(world, creation, part, key, fallback);

    private string? CheckReach(ActionCaller caller, Creation creation, int part)
    {
        if (caller.Source != ActionSource.Player)
            return null;
        if (world.EyeOf(caller.Peer) is not { } eye)
            return "You have no player to use tools with.";
        var distance = Vector3.Distance(eye, BuildActions.PartCentre(creation, part));
        return distance <= settings.Reach ? null : $"That is {distance:F1} m away; you can reach {settings.Reach:F1} m.";
    }

    private static string Sentence(string error) => char.ToUpperInvariant(error[0]) + error[1..];

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);
}
