using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Items;
using Mechanica.Core.Data;
using Mechanica.Core.Sound;
using Mechanica.Core.World;

namespace Mechanica.Core.Building;

/// <summary>
/// A player's Lift: a platform standing on the ground at <see cref="Position"/> (the middle of its base),
/// raised to <see cref="Level"/>, holding at most one Creation. Each player has at most one.
/// </summary>
/// <param name="Owner">Peer of the player who placed it.</param>
/// <param name="Position">World position of the middle of the platform's base, in metres.</param>
/// <param name="Level">How many steps the platform is raised (<see cref="LiftSettings.LevelHeight"/>).</param>
/// <param name="Creation">The Creation it holds, if any.</param>
public sealed record LiftState(int Owner, Vec3 Position, int Level = 0, int? Creation = null);

/// <summary>Arguments of <c>PlaceLift</c>.</summary>
/// <param name="Position">Where the middle of the platform's base goes, in world metres; x and z snap to the grid.</param>
/// <param name="Creation">A Creation to pick up onto the Lift. Without it, a Lift that already holds one carries it along.</param>
/// <param name="Turns">Quarter turns about the vertical, counter-clockwise seen from above, for a Creation picked up
/// onto the Lift (it is squared to the nearest quarter turn first).</param>
public sealed record PlaceLiftArgs(Vec3 Position, int? Creation = null, int Turns = 0);

/// <summary>
/// Arguments of <c>CaptureLift</c>: the caller's Lift captures a Creation, which then goes onto the Lift wherever the
/// next <c>PlaceLift</c> naming it puts the Lift down, however far it is by then. A captured Creation stays where it is
/// until then.
/// </summary>
/// <param name="Creation">The Creation to capture, or null to let the capture go.</param>
public sealed record CaptureLiftArgs(int? Creation = null);

/// <summary>Arguments of <c>SetLiftLevel</c>.</summary>
/// <param name="Level">The new level, 0 to <see cref="LiftSettings.MaxLevel"/>.</param>
public sealed record SetLiftLevelArgs(int Level);

/// <summary>Arguments of <c>TurnLift</c>.</summary>
/// <param name="Turns">Quarter turns about the vertical, counter-clockwise seen from above (negative turns the other way).</param>
public sealed record TurnLiftArgs(int Turns);

/// <summary>
/// Arguments of <c>Weld</c>: move the whole <see cref="Source"/> Creation onto <see cref="Target"/>,
/// mapping its grid by <see cref="Rotation"/> then <see cref="Offset"/> into the target's grid, and merge
/// the two with every touching face rigid.
/// </summary>
/// <param name="Source">The Creation that moves.</param>
/// <param name="Target">The Creation it is welded to.</param>
/// <param name="Offset">Where the source's grid origin lands in the target's grid.</param>
/// <param name="Rotation">How the source's grid turns into the target's.</param>
public sealed record WeldArgs(int Source, int Target, GridCell Offset, GridRotation Rotation = default);

/// <summary>Arguments of <c>WeldParts</c>: two touching parts of one Creation, in different Bodies.</summary>
/// <param name="Creation">The Creation holding both.</param>
/// <param name="A">The part selected first.</param>
/// <param name="B">The part it is welded to.</param>
public sealed record WeldPartsArgs(int Creation, int A, int B);

/// <summary>Arguments of <c>Paint</c>.</summary>
/// <param name="Creation">The Creation holding the parts.</param>
/// <param name="Parts">Part ids to paint, without repeats.</param>
/// <param name="Color"><c>"#rrggbb"</c>, or null to take the paint off.</param>
/// <param name="Stroke">
/// Which stroke of the caller's this belongs to (any number the caller picks, new for each press of the button). In
/// Survival a stroke pays <see cref="PaintSettings.AmmoPerStroke"/> once, on its first Paint; the drag's later Paint
/// requests with the same number are free while they keep coming (<see cref="PaintSettings.StrokeSeconds"/>) and stay
/// within <see cref="PaintSettings.MaxPartsPerAction"/> parts. Without it every Paint pays.
/// </param>
public sealed record PaintArgs(int Creation, IReadOnlyList<int> Parts, string? Color = null, int? Stroke = null);

/// <summary>
/// Arguments of <c>Sledge</c>: one Sledgehammer hit on a part (<see cref="Creation"/> and <see cref="Part"/>),
/// a player (<see cref="Player"/>), a world object such as a tree (<see cref="Object"/>), a bot (<see cref="Bot"/>),
/// or, with only <see cref="At"/>, anything else it lands on (the ground, a rock face): that hit only thuds.
/// </summary>
/// <param name="Creation">The Creation hit.</param>
/// <param name="Part">The part hit.</param>
/// <param name="Player">Peer of the player hit.</param>
/// <param name="Object">Id of the world object hit (<c>"3.10.-4"</c>).</param>
/// <param name="Bot">Id of the bot hit.</param>
/// <param name="At">Where the head lands, in world metres: the hit's sound and dust play there.</param>
public sealed record SledgeArgs(int? Creation = null, int? Part = null, int? Player = null, string? Object = null, int? Bot = null,
    Vec3? At = null);

/// <summary>Arguments of <c>LoadBlueprint</c>: a blueprint file's JSON (<see cref="Creations.Blueprint"/>), loaded onto the caller's empty Lift.</summary>
/// <param name="Blueprint">The blueprint.</param>
public sealed record LoadBlueprintArgs(JsonElement Blueprint);

/// <summary>Arguments of <c>Block</c>: the caller starts or stops blocking with the Sledgehammer.</summary>
/// <param name="On">True while right click is held.</param>
public sealed record BlockArgs(bool On);

/// <summary>Value of a successful Lift action: the caller's Lift as it is now (absent after <c>RemoveLift</c>).</summary>
/// <param name="Position">Middle of the platform's base.</param>
/// <param name="Level">Platform level.</param>
/// <param name="Creation">The Creation it holds.</param>
public sealed record LiftResult(Vec3 Position, int Level, int? Creation);

/// <summary>Value of a successful <c>Weld</c> or <c>WeldParts</c>.</summary>
/// <param name="Creation">The Creation that holds everything now.</param>
/// <param name="Bodies">How many rigid Bodies it has.</param>
public sealed record WeldResult(int Creation, int Bodies);

/// <summary>Value of a successful <c>Paint</c>.</summary>
/// <param name="Painted">How many parts changed colour.</param>
public sealed record PaintResult(int Painted);

/// <summary>Value of a successful <c>Sledge</c>.</summary>
/// <param name="Broken">True when the hit broke the part off.</param>
/// <param name="Health">The part's health left, or null for a part that cannot break.</param>
/// <param name="Destroyed">True when the broken part was its Creation's last.</param>
/// <param name="SplitOff">New Creations made from pieces the break disconnected.</param>
public sealed record SledgeResult(bool Broken, float? Health, bool Destroyed, IReadOnlyList<int> SplitOff);

/// <summary>
/// Players and world objects the Sledgehammer can hit besides parts (chunk 20). Survival implements it;
/// without it only parts can be hit.
/// </summary>
public interface IStrikeTargets
{
    /// <summary>Middle of a player's body, or null when the peer has no living player.</summary>
    Vector3? PlayerCentre(int peer);

    /// <summary>Null when <paramref name="caller"/> may hurt <paramref name="peer"/>, or why not.</summary>
    string? CheckStrikePlayer(ActionCaller caller, int peer);

    /// <summary>Takes <paramref name="damage"/> health from a player. Returns the health left.</summary>
    float StrikePlayer(ActionCaller caller, int peer, float damage);

    /// <summary>Middle of a world object that can break, or null when there is none with that id.</summary>
    Vector3? ObjectCentre(string id);

    /// <summary>
    /// Takes <paramref name="damage"/> health from a world object, if it takes damage of <paramref name="kind"/>
    /// (<see cref="World.DamageKinds"/>), hit at <paramref name="at"/> by a blow from <paramref name="from"/> (a felled
    /// tree falls away from it). Returns the health left (0 when it broke).
    /// </summary>
    float StrikeObject(string id, float damage, string kind, Vector3? at = null, Vector3? from = null);

    /// <summary>
    /// The world object's own sound event for <paramref name="key"/> (<see cref="World.ObjectSounds"/>: a tree's knock),
    /// played instead of the tool's, or null.
    /// </summary>
    string? ObjectSound(string id, string key) => null;

    /// <summary>Middle of a living bot, or null when there is none with that id.</summary>
    Vector3? BotCentre(int bot);

    /// <summary>Takes <paramref name="damage"/> health from a bot (a melee hit). Returns the health left (0 when it died).</summary>
    float StrikeBot(int bot, float damage);

    /// <summary>The health a Sledgehammer hit takes from a world object (its <c>sledge_damage</c>), or null for the default.</summary>
    float? SledgeDamageOf(string id) => null;

    /// <summary>Sets a living player moving at <paramref name="velocity"/> (m/s) on top of their own movement: knockback.</summary>
    void KnockPlayer(int peer, Vector3 velocity)
    {
    }

    /// <summary>Sets a living bot moving at <paramref name="velocity"/> (m/s) on top of its own walk: knockback.</summary>
    void KnockBot(int bot, Vector3 velocity)
    {
    }

    /// <summary>Null when <paramref name="caller"/> may raise the Sledgehammer's block pose, or why not (it is Creative only).</summary>
    string? CheckBlock(ActionCaller caller) => null;
}

/// <summary>What the tool actions need from the world beyond building. The host's game layer implements it.</summary>
public interface IToolWorld : IBuildWorld
{
    /// <summary>The host's clock, in seconds.</summary>
    double Now { get; }

    /// <summary>
    /// Null when the space one part would fill at <paramref name="pose"/> is clear of players, Creations,
    /// Lifts and the world, or a reason it is not. Bodies of <paramref name="ignoreCreations"/> and the Lift
    /// of <paramref name="ignoreLiftOf"/> don't count.
    /// </summary>
    string? CheckPartClear(PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose,
        IReadOnlyCollection<int> ignoreCreations, int? ignoreLiftOf);

    /// <summary>
    /// Null when a Lift's platform (its column from base to top) would be clear of players, Creations,
    /// other Lifts and the world, or a reason it is not. Its owner's current Lift and
    /// <paramref name="ignoreCreations"/> don't count.
    /// </summary>
    string? CheckLiftClear(LiftState lift, IReadOnlyCollection<int> ignoreCreations);

    /// <summary>A Creation's Body poses or <see cref="Creation.OnLift"/> changed without a graph change.</summary>
    void Moved(Creation creation);

    /// <summary>A player's Lift was placed, changed or (when <paramref name="lift"/> is null) removed.</summary>
    void LiftChanged(int owner, LiftState? lift);

    /// <summary>Gives a Body an impulse (N·s) at a world point.</summary>
    void Push(Creation creation, int bodyIndex, Vector3 point, Vector3 impulse);

    /// <summary>A part is about to break off at 0 health; what it holds should spill out.</summary>
    void Breaking(Creation creation, int partId)
    {
    }
}

/// <summary>
/// The hand tools of the Action API besides building by hand (<see cref="BuildActions"/>), validated on the host:
/// <list type="bullet">
/// <item><c>PlaceLift</c>, <c>CaptureLift</c>, <c>SetLiftLevel</c>, <c>TurnLift</c>, <c>RemoveLift</c>: each player's
/// Lift. A Creation on a Lift stands upright and still on the platform (<see cref="Seat"/>), and drops when the
/// Lift lets go. A Creation built onto the ground, one holding resources and one too big for the Lift cannot be
/// lifted (<see cref="LiftRefusal"/>), nor can one on another player's Lift. No stamina cost (D12).</item>
/// <item><c>LoadBlueprint</c>: builds a saved Creation on the caller's empty Lift (UI spec §9), paid for part by
/// part in Survival. A Creation whose storage holds items cannot be picked up (<see cref="LiftSettings.RefuseStoredItems"/>).</item>
/// <item><c>Weld</c>: moves one Creation onto another and merges them, every touching face rigid (D7).
/// <c>WeldParts</c>: welds two touching parts of different Bodies of one Creation, merging the Bodies.</item>
/// <item><c>Paint</c>: colours parts, or takes the paint off. In Survival it needs a Paint Tool, and each stroke uses
/// Paint Ammo (<see cref="PaintSettings.Ammo"/>); taking paint off is free.</item>
/// <item><c>Sledge</c>: a Sledgehammer hit, which pushes the Body and damages the part until it breaks off;
/// with <see cref="Targets"/> it can also hit players and bots (combat damage and knockback) and world objects
/// (their own gathering damage). <c>Block</c>: holding right click with it in Creative raises a block pose that only
/// shows; it changes no damage and no knockback.</item>
/// </list>
/// Player callers must be within <see cref="BuildSettings.Reach"/> of what they act on (the Sledgehammer: its own
/// <see cref="SledgeSettings.Reach"/>). Each action plays its
/// tool's sound where it happened (chunk 16): <c>lift_place</c> at the Lift (and <c>lift_lower</c> as it is taken
/// away), <c>lift_capture</c>/<c>lift_release</c> at the captured Creation; <c>weld_complete</c> where the weld joins; <c>paint_spray</c>/<c>paint_erase</c> on the first part;
/// <c>smash_hit</c> plus the part's impact sound, or <c>smash_break</c> plus its break sound.
/// </summary>
public sealed class ToolActions(BuildSettings settings, IToolWorld world, IPartCatalog? catalog = null)
{
    /// <summary>Action name for placing, moving or loading the caller's Lift.</summary>
    public const string PlaceLift = "PlaceLift";

    /// <summary>Action name for raising or lowering the caller's Lift.</summary>
    public const string SetLiftLevel = "SetLiftLevel";

    /// <summary>Action name for turning the Creation on the caller's Lift.</summary>
    public const string TurnLift = "TurnLift";

    /// <summary>Action name for capturing a Creation to put on the caller's Lift, or letting the capture go.</summary>
    public const string CaptureLift = "CaptureLift";

    /// <summary>Action name for taking the caller's Lift away (its Creation drops).</summary>
    public const string RemoveLift = "RemoveLift";

    /// <summary>Action name for welding one Creation onto another.</summary>
    public const string Weld = "Weld";

    /// <summary>Action name for welding two Bodies of one Creation.</summary>
    public const string WeldParts = "WeldParts";

    /// <summary>Action name for painting parts.</summary>
    public const string Paint = "Paint";

    /// <summary>Action name for a Sledgehammer hit.</summary>
    public const string Sledge = "Sledge";

    /// <summary>Action name for loading a blueprint onto the caller's Lift.</summary>
    public const string LoadBlueprint = "LoadBlueprint";

    /// <summary>Action name for starting or stopping a block.</summary>
    public const string Block = "Block";

    /// <summary>The refusal for picking up a Creation whose storage holds items (UI spec §9).</summary>
    public const string StoredItemsRefusal = "Cannot lift with attached resources.";

    // Share of the swing time the host insists on between one player's hits: swings the client spaced
    // correctly can arrive closer together after network jitter.
    private const double SwingJitter = 0.8;

    // Metres past sledge.reach a target's centre may be (see CheckSledgeReach).
    private const float SledgeReachSlack = 0.75f;

    private readonly SortedDictionary<int, LiftState> _lifts = [];
    private readonly Dictionary<int, int> _captures = [];
    private readonly Dictionary<int, double> _lastSwing = [];
    private readonly HashSet<int> _blocking = [];
    private readonly Dictionary<int, PaidStroke> _strokes = [];

    /// <summary>Players and world objects a Sledgehammer can hit; null hits parts only.</summary>
    public IStrikeTargets? Targets { get; set; }

    /// <summary>What building costs (Survival pays for a loaded blueprint's parts). Null is free.</summary>
    public IBuildCosts? Costs { get; set; }

    /// <summary>The players blocking now.</summary>
    public IReadOnlyCollection<int> Blocking => _blocking;

    /// <summary>Every Lift, by owner.</summary>
    public IReadOnlyCollection<LiftState> Lifts => _lifts.Values;

    /// <summary>The Lift <paramref name="peer"/> owns, or null.</summary>
    public LiftState? LiftOf(int peer) => _lifts.GetValueOrDefault(peer);

    /// <summary>The Creation <paramref name="peer"/> has captured with <c>CaptureLift</c>, or null.</summary>
    public int? CaptureOf(int peer) => _captures.TryGetValue(peer, out var id) ? id : null;

    /// <summary>Registers every tool action.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<PlaceLiftArgs>(PlaceLift, ValidatePlaceLift, ApplyPlaceLift);
        dispatcher.Register<SetLiftLevelArgs>(SetLiftLevel, ValidateLevel, ApplyLevel);
        dispatcher.Register<TurnLiftArgs>(TurnLift, ValidateTurn, ApplyTurn);
        dispatcher.Register<CaptureLiftArgs>(CaptureLift, ValidateCapture, ApplyCapture);
        dispatcher.Register<NoArgs>(RemoveLift, ValidateRemoveLift, ApplyRemoveLift);
        dispatcher.Register<WeldArgs>(Weld, ValidateWeld, ApplyWeld);
        dispatcher.Register<WeldPartsArgs>(WeldParts, ValidateWeldParts, ApplyWeldParts);
        dispatcher.Register<PaintArgs>(Paint, ValidatePaint, ApplyPaint);
        dispatcher.Register<SledgeArgs>(Sledge, ValidateSledge, ApplySledge);
        dispatcher.Register<LoadBlueprintArgs>(LoadBlueprint, ValidateLoadBlueprint, ApplyLoadBlueprint, maxArgsLength: settings.Lift.BlueprintMaxBytes);
        dispatcher.Register<BlockArgs>(Block, ValidateBlock, ApplyBlock);
    }

    /// <summary>Forgets a peer that left (its Lift is removed through <c>RemoveLift</c> first).</summary>
    public void Forget(int peer)
    {
        _lastSwing.Remove(peer);
        _captures.Remove(peer);
        _blocking.Remove(peer);
        _strokes.Remove(peer);
    }

    /// <summary>
    /// Where a Creation's Bodies go on a platform whose top middle is <paramref name="platformTop"/>: the
    /// whole Creation turns as one so its first Body stands upright, squared to the nearest quarter turn
    /// about the vertical plus <paramref name="turns"/>; then it moves so the bottom of its box rests on
    /// the platform, centred over it. Bodies keep their places relative to each other.
    /// </summary>
    public static IReadOnlyList<Pose> Seat(Creation creation, Vector3 platformTop, int turns = 0)
    {
        var poses = Enumerable.Range(0, creation.Bodies.Count).Select(creation.BodyPose).ToList();
        var first = poses[0];
        var yaw = Enumerable.Range(0, 4).MaxBy(k =>
        {
            var q = Yaw(k);
            return Vector3.Dot(Vector3.Transform(Vector3.UnitX, q), Vector3.Transform(Vector3.UnitX, first.Rotation))
                + Vector3.Dot(Vector3.Transform(Vector3.UnitZ, q), Vector3.Transform(Vector3.UnitZ, first.Rotation));
        });
        var correction = Quaternion.Normalize(Yaw(yaw + turns) * Quaternion.Inverse(first.Rotation));
        var turned = poses
            .Select(p => new Pose(Vector3.Transform(p.Position - first.Position, correction) + first.Position,
                Quaternion.Normalize(correction * p.Rotation)))
            .ToList();
        var box = CreationBounds.World(CreationBounds.LocalBodyBounds(creation), turned);
        var centre = (box.Min + box.Max) / 2;
        var shift = new Vector3(platformTop.X - centre.X, platformTop.Y - box.Min.Y, platformTop.Z - centre.Z);
        return [.. turned.Select(p => p with { Position = p.Position + shift })];
    }

    /// <summary>World position of the middle of a Lift platform's top.</summary>
    public Vector3 PlatformTop(LiftState lift) =>
        lift.Position.ToNumerics() + new Vector3(0, settings.Lift.TopAt(lift.Level), 0);

    private static Quaternion Yaw(int quarterTurns) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, (((quarterTurns % 4) + 4) % 4) * MathF.PI / 2);

    // ---- Lift ----

    private string? ValidatePlaceLift(ActionCaller caller, PlaceLiftArgs args)
    {
        var p = args.Position;
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
            return "position must be finite.";
        var lift = PlannedLift(caller.Peer, args, out var carried);
        if (CheckReach(caller, lift.Position.ToNumerics()) is { } far)
            return far;
        if (args.Creation is { } id)
        {
            if (carried is null)
                return $"There is no creation {id}.";
            if (LiftOf(caller.Peer)?.Creation != id && LiftRefusal(carried, settings.Lift, _lifts.Values, caller.Peer) is { } refused)
                return refused;
            // A captured Creation goes onto the Lift wherever it is; any other must be within reach.
            if (CaptureOf(caller.Peer) != id && CheckReach(caller, CreationBounds.World(carried)) is { } tooFar)
                return tooFar;
        }
        return CheckLift(lift, carried, args.Creation is null ? 0 : args.Turns);
    }

    private JsonElement? ApplyPlaceLift(ActionCaller caller, PlaceLiftArgs args)
    {
        var lift = PlannedLift(caller.Peer, args, out var carried);
        if (Held(LiftOf(caller.Peer)) is { } old && old != carried)
            Release(old);
        if (args.Creation is not null)
            _captures.Remove(caller.Peer);
        world.PlaySound("lift_place", Identifiers.VanillaModId, lift.Position.ToNumerics());
        return Commit(lift, carried, args.Creation is null ? 0 : args.Turns);
    }

    private string? ValidateCapture(ActionCaller caller, CaptureLiftArgs args)
    {
        if (args.Creation is not { } id)
            return null;
        if (world.FindCreation(id) is not { } creation)
            return $"There is no creation {id}.";
        if (LiftOf(caller.Peer)?.Creation != id && LiftRefusal(creation, settings.Lift, _lifts.Values, caller.Peer) is { } refused)
            return refused;
        return CheckReach(caller, CreationBounds.World(creation));
    }

    private JsonElement? ApplyCapture(ActionCaller caller, CaptureLiftArgs args)
    {
        var before = CaptureOf(caller.Peer);
        if (args.Creation is { } id)
        {
            _captures[caller.Peer] = id;
            if (before != id)
                world.PlaySound("lift_capture", Identifiers.VanillaModId, Centre(world.FindCreation(id)!));
        }
        else if (_captures.Remove(caller.Peer) && before is { } let && world.FindCreation(let) is { } released)
            world.PlaySound("lift_release", Identifiers.VanillaModId, Centre(released));
        return null;
    }

    private static Vector3 Centre(Creation creation)
    {
        var box = CreationBounds.World(creation);
        return (box.Min + box.Max) / 2;
    }

    /// <summary>
    /// Why <paramref name="peer"/>'s Lift cannot pick up <paramref name="creation"/>, or null when it can: it is built
    /// onto the ground (<see cref="LiftSettings.RefuseGrounded"/>), it holds a part the Lift refuses
    /// (<see cref="LiftSettings.RefuseCategories"/>, <see cref="LiftSettings.RefuseParts"/>) or stored items
    /// (<see cref="LiftSettings.RefuseStoredItems"/>), it is bigger than <see cref="LiftSettings.MaxExtent"/>, or it is
    /// on another player's Lift (one of <paramref name="lifts"/>). Every peer can ask, to show what the Lift may capture.
    /// </summary>
    public static string? LiftRefusal(Creation creation, LiftSettings settings, IEnumerable<LiftState> lifts, int peer)
    {
        if (lifts.Any(l => l.Owner != peer && l.Creation == creation.Id))
            return "It is on another player's lift.";
        if (settings.RefuseGrounded && creation.IsGrounded)
            return GroundedRefusal;
        foreach (var part in creation.Parts)
        {
            var definition = creation.DefinitionOf(part.Id);
            var category = definition.Category?.ToString();
            if (category is not null && settings.RefuseCategories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase)))
                return StoredItemsRefusal;
            if (settings.RefuseParts.Any(r => Identifiers.Qualify(r) == Identifiers.Qualify(part.Part)))
                return StoredItemsRefusal;
            if (settings.RefuseStoredItems && creation.HoldsItems(part.Id) && !creation.CarriesContents(part.Id))
                return StoredItemsRefusal;
        }
        var box = CreationBounds.World(creation);
        var size = box.Max - box.Min;
        var extent = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        return extent > settings.MaxExtent ? $"It is too big for the lift ({extent:F1} m across; at most {settings.MaxExtent:F1} m)." : null;
    }

    /// <summary>The refusal for picking up a Creation built onto the ground.</summary>
    public const string GroundedRefusal = "It is built onto the ground. Remove the parts that touch the ground, or build on the Lift.";

    private string? ValidateLevel(ActionCaller caller, SetLiftLevelArgs args)
    {
        if (LiftOf(caller.Peer) is not { } lift)
            return "You have no lift.";
        if (args.Level < 0 || args.Level > settings.Lift.MaxLevel)
            return $"level must be 0 to {settings.Lift.MaxLevel} (got {args.Level}).";
        // Anywhere along the column counts, so a player riding the deck can drive it all the way up.
        return CheckReach(caller, new WorldBounds(lift.Position.ToNumerics(), PlatformTop(lift)))
            ?? CheckLift(lift with { Level = args.Level }, Held(lift), 0);
    }

    private JsonElement? ApplyLevel(ActionCaller caller, SetLiftLevelArgs args)
    {
        // Every peer eases the column to the new level and plays its motor as it goes (LiftBody), so nothing is sent.
        var lift = LiftOf(caller.Peer)!;
        return Commit(lift with { Level = args.Level }, Held(lift), 0);
    }

    private string? ValidateTurn(ActionCaller caller, TurnLiftArgs args)
    {
        if (LiftOf(caller.Peer) is not { } lift)
            return "You have no lift.";
        if (Held(lift) is not { } carried)
            return "Your lift holds no creation.";
        return CheckReach(caller, lift.Position.ToNumerics()) ?? CheckLift(lift, carried, args.Turns);
    }

    private JsonElement? ApplyTurn(ActionCaller caller, TurnLiftArgs args)
    {
        var lift = LiftOf(caller.Peer)!;
        world.PlaySound("lift_place", Identifiers.VanillaModId, PlatformTop(lift));
        return Commit(lift, Held(lift), args.Turns);
    }

    private string? ValidateRemoveLift(ActionCaller caller, NoArgs args) =>
        LiftOf(caller.Peer) is null ? "You have no lift." : null;

    private JsonElement? ApplyRemoveLift(ActionCaller caller, NoArgs args)
    {
        if (Held(LiftOf(caller.Peer)) is { } carried)
            Release(carried);
        world.PlaySound("lift_lower", Identifiers.VanillaModId, LiftOf(caller.Peer)!.Position.ToNumerics());
        _lifts.Remove(caller.Peer);
        world.LiftChanged(caller.Peer, null);
        return null;
    }

    private string? ValidateLoadBlueprint(ActionCaller caller, LoadBlueprintArgs args)
    {
        if (LiftOf(caller.Peer) is not { } lift)
            return "You have no lift to load a blueprint onto.";
        if (Held(lift) is not null)
            return "Your lift already holds a creation.";
        if (ReadBlueprint(args, 0, out var error) is not { } creation)
            return error;
        if (creation.Parts.Count > settings.MaxPartsPerCreation)
            return $"The blueprint has more parts than a creation may ({creation.Parts.Count}; at most {settings.MaxPartsPerCreation}).";
        if (CheckReach(caller, lift.Position.ToNumerics()) is { } far)
            return far;
        if (Costs?.CheckPlaceAll(caller, PartCounts(creation)) is { } unpaid)
            return unpaid;
        return CheckLift(lift with { Creation = creation.Id }, creation, 0);
    }

    private JsonElement? ApplyLoadBlueprint(ActionCaller caller, LoadBlueprintArgs args)
    {
        var creation = ReadBlueprint(args, world.NextCreationId(), out var error) ?? throw new InvalidOperationException(error);
        world.Added(creation);
        foreach (var part in creation.Parts)
            Costs?.Placed(caller, part.Part);
        var lift = LiftOf(caller.Peer)! with { Creation = creation.Id };
        world.PlaySound("lift_place", Identifiers.VanillaModId, PlatformTop(lift));
        return Commit(lift, creation, 0);
    }

    private Creation? ReadBlueprint(LoadBlueprintArgs args, int id, out string? error)
    {
        error = null;
        if (catalog is null)
        {
            error = "Blueprints cannot be loaded here.";
            return null;
        }
        var diagnostics = new List<DataDiagnostic>();
        if (args.Blueprint.ValueKind == JsonValueKind.Object && Blueprint.Read(args.Blueprint.GetRawText(), catalog, id, "blueprint", diagnostics) is { } creation)
            return creation;
        var first = diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        error = first is null ? "That is not a blueprint." : $"The blueprint does not load: {first.Message}";
        return null;
    }

    private static Dictionary<string, int> PartCounts(Creation creation) =>
        creation.Parts.GroupBy(p => p.Part).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    // The caller's Lift as PlaceLift would leave it, and the Creation it would hold.
    private LiftState PlannedLift(int peer, PlaceLiftArgs args, out Creation? carried)
    {
        var existing = LiftOf(peer);
        carried = args.Creation is { } id ? world.FindCreation(id) : Held(existing);
        var p = args.Position;
        var snapped = new Vec3(MechanicaInfo.ToGridCell(p.X) * MechanicaInfo.GridSize, p.Y, MechanicaInfo.ToGridCell(p.Z) * MechanicaInfo.GridSize);
        return new LiftState(peer, snapped, existing?.Level ?? 0, carried?.Id);
    }

    private Creation? Held(LiftState? lift) => lift?.Creation is { } id ? world.FindCreation(id) : null;

    private string? CheckLift(LiftState lift, Creation? carried, int turns) => CheckLiftPlacement(world, settings.Lift, lift, carried, turns);

    /// <summary>
    /// Null when <paramref name="lift"/> would stand clear, with <paramref name="carried"/> (if any) seated on it turned by
    /// <paramref name="turns"/>, or why not. Every peer can ask, to colour the Lift's placement ghost.
    /// </summary>
    public static string? CheckLiftPlacement(IToolWorld world, LiftSettings settings, LiftState lift, Creation? carried, int turns)
    {
        IReadOnlyCollection<int> ignore = carried is null ? [] : [carried.Id];
        if (world.CheckLiftClear(lift, ignore) is { } blocked)
            return blocked;
        if (carried is null)
            return null;
        var poses = Seat(carried, lift.Position.ToNumerics() + new Vector3(0, settings.TopAt(lift.Level), 0), turns);
        foreach (var part in carried.Parts)
        {
            if (world.CheckPartClear(carried.DefinitionOf(part.Id), part.Cell, part.Rotation, poses[carried.BodyIndexOf(part.Id)], ignore, lift.Owner) is { } reason)
                return reason;
        }
        return null;
    }

    private JsonElement? Commit(LiftState lift, Creation? carried, int turns)
    {
        _lifts[lift.Owner] = lift;
        if (carried is not null)
        {
            var poses = Seat(carried, PlatformTop(lift), turns);
            for (var i = 0; i < poses.Count; i++)
            {
                carried.SetBodyPose(i, poses[i]);
                carried.SetBodyVelocity(i, BodyVelocity.Zero);
            }
            carried.OnLift = true;
            // Off the ground now, so it is no longer anchored: it drops and simulates when the Lift lets go.
            carried.ClearAnchors();
            world.Moved(carried);
        }
        world.LiftChanged(lift.Owner, lift);
        return Value(new LiftResult(lift.Position, lift.Level, lift.Creation));
    }

    private void Release(Creation creation)
    {
        creation.OnLift = false;
        world.Moved(creation);
    }

    // ---- Weld ----

    private string? ValidateWeld(ActionCaller caller, WeldArgs args)
    {
        if (Costs?.CheckTool(caller, HotbarTools.Weld) is { } tool)
            return tool;
        if (args.Source == args.Target)
            return "A creation cannot be welded to itself; weld two of its parts with WeldParts.";
        if (world.FindCreation(args.Source) is not { } source)
            return $"There is no creation {args.Source}.";
        if (world.FindCreation(args.Target) is not { } target)
            return $"There is no creation {args.Target}.";
        if (source.AuthorityPeer != target.AuthorityPeer)
            return $"Creations {source.Id} and {target.Id} are simulated by different players.";
        // What is built on the ground stays put: select the loose creation first and weld it to this one.
        if (source.IsGrounded)
            return $"Creation {source.Id} is built on the ground and cannot move; select the loose creation first and weld it to this one.";
        if (source.Parts.Count + target.Parts.Count > settings.MaxPartsPerCreation)
            return $"Together they would have more parts than allowed ({settings.MaxPartsPerCreation}).";
        // Ground-only parts (soil) stay flat on the ground, so they never move onto another creation or take one on.
        if (new[] { source, target }.SelectMany(c => c.Parts.Select(p => c.DefinitionOf(p.Id))).FirstOrDefault(d => d.GroundOnly == true) is { } grounded)
            return $"{grounded.Name ?? "This part"} stays on the ground and cannot be welded.";
        if (new[] { source, target }.SelectMany(c => c.Parts.Select(p => c.DefinitionOf(p.Id))).FirstOrDefault(d => d.Loose == true) is { } loose)
            return $"A {loose.Name ?? "loose part"} cannot be welded to anything.";
        if (!target.CanMergeFrom(source, args.Rotation, args.Offset, weld: true, out var attachedTo, out var error))
            return Sentence(error);
        var pose = target.BodyPose(target.BodyIndexOf(attachedTo[0]));
        if (CheckReach(caller, PartCentre(target, attachedTo[0])) is { } far)
            return far;
        foreach (var part in source.Parts)
        {
            var cell = args.Rotation.Apply(part.Cell) + args.Offset;
            var rotation = GridRotation.Combine(part.Rotation, args.Rotation);
            if (world.CheckPartClear(source.DefinitionOf(part.Id), cell, rotation, pose, [source.Id, target.Id], null) is { } blocked)
                return blocked;
        }
        return null;
    }

    private JsonElement? ApplyWeld(ActionCaller caller, WeldArgs args)
    {
        var source = world.FindCreation(args.Source)!;
        var target = world.FindCreation(args.Target)!;
        if (!target.CanMergeFrom(source, args.Rotation, args.Offset, weld: true, out var attachedTo, out var error)
            || !target.TryMergeFrom(source, args.Rotation, args.Offset, weld: true, out var map, out error))
            throw new InvalidOperationException(error);
        // The source lands in its built shape against the Body it was welded to, joints and all.
        var pose = target.BodyPose(target.BodyIndexOf(attachedTo[0]));
        foreach (var id in map.Values)
            target.SetPartPose(id, pose);
        world.Removed(source.Id);
        world.Changed(target);
        world.PlaySound("weld_complete", Identifiers.VanillaModId, PartCentre(target, attachedTo[0]));
        return Value(new WeldResult(target.Id, target.Bodies.Count));
    }

    private string? ValidateWeldParts(ActionCaller caller, WeldPartsArgs args)
    {
        if (Costs?.CheckTool(caller, HotbarTools.Weld) is { } tool)
            return tool;
        if (world.FindCreation(args.Creation) is not { } creation)
            return $"There is no creation {args.Creation}.";
        if (!creation.CanWeldParts(args.A, args.B, out var error))
            return Sentence(error);
        return CheckReach(caller, PartCentre(creation, args.B));
    }

    private JsonElement? ApplyWeldParts(ActionCaller caller, WeldPartsArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        if (!creation.TryWeldParts(args.A, args.B, out var error))
            throw new InvalidOperationException(error);
        world.Changed(creation);
        world.PlaySound("weld_complete", Identifiers.VanillaModId, PartCentre(creation, args.B));
        return Value(new WeldResult(creation.Id, creation.Bodies.Count));
    }

    // ---- Paint ----

    private string? ValidatePaint(ActionCaller caller, PaintArgs args)
    {
        if (world.FindCreation(args.Creation) is not { } creation)
            return $"There is no creation {args.Creation}.";
        if (args.Parts.Count == 0 || args.Parts.Count > settings.Paint.MaxPartsPerAction)
            return $"Paint 1 to {settings.Paint.MaxPartsPerAction} parts at a time (got {args.Parts.Count}).";
        if (args.Parts.Distinct().Count() != args.Parts.Count)
            return "parts lists a part twice.";
        if (args.Color is not null && !HexColor.TryParse(args.Color, out _))
            return $"color must be \"#rrggbb\" (got \"{args.Color}\").";
        foreach (var id in args.Parts)
        {
            if (creation.FindPart(id) is null)
                return $"Creation {creation.Id} has no part {id}.";
            var definition = creation.DefinitionOf(id);
            if (definition.Visual?.Paintable == false)
                return $"{definition.Name ?? creation.FindPart(id)!.Part} cannot be painted.";
            if (CheckReach(caller, PartCentre(creation, id)) is { } far)
                return far;
        }
        if (Costs?.CheckTool(caller, Items.HotbarTools.Paint) is { } tool)
            return tool;
        if (PaintCost(caller, args) is { } cost && Costs?.CheckSpend(caller, cost.Item, cost.Count) is { } unpaid)
            return unpaid;
        return null;
    }

    // What a Paint pays: nothing for taking paint off, for a caller that is not a player, or for a later part of a stroke
    // already paid for; otherwise the stroke's ammo.
    private (string Item, int Count)? PaintCost(ActionCaller caller, PaintArgs args)
    {
        var paint = settings.Paint;
        if (args.Color is null || caller.Source != ActionSource.Player || paint.Ammo is not { } ammo || paint.AmmoPerStroke <= 0)
            return null;
        if (args.Stroke is { } stroke && _strokes.TryGetValue(caller.Peer, out var paid) && paid.Stroke == stroke
            && world.Now - paid.LastAt <= paint.StrokeSeconds && paid.Parts + args.Parts.Count <= paint.MaxPartsPerAction)
            return null;
        return (Identifiers.Qualify(ammo), paint.AmmoPerStroke);
    }

    private JsonElement? ApplyPaint(ActionCaller caller, PaintArgs args)
    {
        if (PaintCost(caller, args) is { } cost)
        {
            Costs?.Spend(caller, cost.Item, cost.Count);
            _strokes[caller.Peer] = new PaidStroke(args.Stroke, world.Now, args.Parts.Count);
        }
        else if (args.Color is not null && args.Stroke is not null && _strokes.TryGetValue(caller.Peer, out var paid))
            _strokes[caller.Peer] = paid with { LastAt = world.Now, Parts = paid.Parts + args.Parts.Count };
        var creation = world.FindCreation(args.Creation)!;
        var painted = args.Parts.Count(id => creation.SetPartColor(id, args.Color));
        if (painted > 0)
            world.Changed(creation);
        world.PlaySound(args.Color is null ? "paint_erase" : "paint_spray", Identifiers.VanillaModId, PartCentre(creation, args.Parts[0]));
        return Value(new PaintResult(painted));
    }

    // ---- Sledgehammer ----

    private string? ValidateSledge(ActionCaller caller, SledgeArgs args)
    {
        if (CheckSledgeTarget(caller, args) is { } bad)
            return bad;
        if (caller.Source == ActionSource.Player && _lastSwing.TryGetValue(caller.Peer, out var last)
            && world.Now - last < settings.Sledge.SwingSeconds * SwingJitter)
            return "The Sledgehammer is still swinging.";
        return null;
    }

    private string? CheckSledgeTarget(ActionCaller caller, SledgeArgs args)
    {
        var targets = (args.Creation is not null || args.Part is not null ? 1 : 0) + (args.Player is not null ? 1 : 0) + (args.Object is not null ? 1 : 0)
            + (args.Bot is not null ? 1 : 0);
        if (targets > 1)
            return "Hit one thing: a creation and part, a player, an object or a bot.";
        if (args.At is { } at && !(float.IsFinite(at.X) && float.IsFinite(at.Y) && float.IsFinite(at.Z)))
            return "Give a real place for the hit.";
        if (targets == 0)
            return args.At is { } ground ? CheckSledgeReach(caller, ground.ToNumerics()) : "Hit one thing: a creation and part, a player, an object or a bot.";
        if (args.Bot is { } bot)
            return Targets?.BotCentre(bot) is { } botCentre ? CheckSledgeReach(caller, botCentre) : $"There is no bot {bot} to hit.";
        if (args.Player is { } peer)
        {
            if (Targets?.PlayerCentre(peer) is not { } centre)
                return $"There is no player {peer} to hit.";
            if (peer == caller.Peer)
                return "You cannot hit yourself.";
            return Targets.CheckStrikePlayer(caller, peer) ?? CheckSledgeReach(caller, centre);
        }
        if (args.Object is { } id)
            return Targets?.ObjectCentre(id) is { } centre ? CheckSledgeReach(caller, centre) : $"There is no object {id} that can break.";
        if (args.Creation is not { } creationId || args.Part is not { } partId)
            return "Give both creation and part.";
        if (world.FindCreation(creationId) is not { } creation)
            return $"There is no creation {creationId}.";
        if (creation.FindPart(partId) is null)
            return $"Creation {creationId} has no part {partId}.";
        return CheckSledgeReach(caller, PartCentre(creation, partId));
    }

    // The swing reaches sledge.reach from the eye to the crosshair; the target's centre may sit up to half a block
    // further in (a big part, a bot's middle), so that much slack is allowed.
    private string? CheckSledgeReach(ActionCaller caller, Vector3 centre, float slack = SledgeReachSlack)
    {
        if (caller.Source != ActionSource.Player)
            return null;
        if (world.EyeOf(caller.Peer) is not { } eye)
            return "You have no player to use tools with.";
        var distance = Vector3.Distance(eye, centre);
        var reach = settings.Sledge.Reach + slack;
        return distance <= reach ? null : $"That is {distance:F1} m away; the Sledgehammer reaches {settings.Sledge.Reach:F1} m.";
    }

    // Where a hit's sound and dust go: where the head landed, when that is on the thing hit, else its centre.
    private static Vector3 HitPoint(SledgeArgs args, Vector3 centre) =>
        args.At is { } at && Vector3.Distance(at.ToNumerics(), centre) <= HitPointSlack ? at.ToNumerics() : centre;

    // How far from a target's centre the head may land and still count as on it (half a big part, a bot's reach).
    private const float HitPointSlack = 1.5f;

    private JsonElement? ApplySledge(ActionCaller caller, SledgeArgs args)
    {
        _lastSwing[caller.Peer] = world.Now;
        if (args is { Creation: null, Part: null, Player: null, Object: null, Bot: null, At: { } ground })
        {
            world.PlaySound("smash_hit", Identifiers.VanillaModId, ground.ToNumerics());
            return Value(new SledgeResult(false, null, false, []));
        }
        if (args.Player is { } peer)
        {
            var target = Targets!.PlayerCentre(peer)!.Value;
            world.PlaySound("smash_hit", Identifiers.VanillaModId, HitPoint(args, target));
            var left = Targets.StrikePlayer(caller, peer, settings.Sledge.CombatDamage);
            if (left > 0 && Knockback(caller, target) is { } push)
                Targets.KnockPlayer(peer, push);
            return Value(new SledgeResult(false, left, false, []));
        }
        if (args.Bot is { } bot)
        {
            var target = Targets!.BotCentre(bot)!.Value;
            world.PlaySound("smash_hit", Identifiers.VanillaModId, HitPoint(args, target));
            var health = Targets.StrikeBot(bot, settings.Sledge.CombatDamage);
            if (health > 0 && Knockback(caller, target) is { } push)
                Targets.KnockBot(bot, push);
            return Value(new SledgeResult(health <= 0, health, false, []));
        }
        if (args.Object is { } id)
        {
            var at = HitPoint(args, Targets!.ObjectCentre(id)!.Value);
            var own = Targets.ObjectSound(id, World.ObjectSounds.Hit) is not null;
            var left = Targets.StrikeObject(id, Targets.SledgeDamageOf(id) ?? settings.Sledge.ObjectDamage, World.DamageKinds.Sledge, at, world.EyeOf(caller.Peer) ?? at);
            // An object with its own sounds (a tree's knock and fall) makes them itself.
            if (!own)
                world.PlaySound(left <= 0 ? "smash_break" : "smash_hit", Identifiers.VanillaModId, at);
            return Value(new SledgeResult(left <= 0, left, left <= 0, []));
        }
        var creation = world.FindCreation(args.Creation!.Value)!;
        var centre = HitPoint(args, PartCentre(creation, args.Part!.Value));
        var body = creation.BodyIndexOf(args.Part.Value);
        var result = DamagePart(creation, args.Part.Value, creation.DefinitionOf(args.Part.Value).SledgeDamage ?? settings.Sledge.Damage);
        world.PlaySound(result.Broken ? "smash_break" : "smash_hit", Identifiers.VanillaModId, centre);
        if (!result.Broken && caller.Source == ActionSource.Player && world.EyeOf(caller.Peer) is { } eye && centre != eye)
        {
            var impulse = settings.Sledge.ImpulseFor(creation.Bodies[body].Mass);
            if (impulse > 0f)
                world.Push(creation, body, centre, Vector3.Normalize(centre - eye) * impulse);
        }
        return Value(result);
    }

    /// <summary>
    /// Takes <paramref name="damage"/> health off a part (its full health is durability ×
    /// <see cref="SledgeSettings.HealthPerDurability"/>). At 0 it breaks off: what it holds spills out
    /// (<see cref="IToolWorld.Breaking"/>) and the pieces it held together split. Weapons and impacts use
    /// it as well as the Sledgehammer. A part without a durability takes no damage. The part plays its
    /// <c>impact</c> sound, or its <c>break</c> sound as it breaks (<see cref="PartSounds"/>).
    /// </summary>
    public SledgeResult DamagePart(Creation creation, int partId, float damage)
    {
        if (settings.Sledge.HealthOf(creation.DefinitionOf(partId).Durability) is not { } full)
        {
            BuildActions.PlayPartSound(world, creation, partId, PartSounds.Impact, null);
            return new SledgeResult(false, null, false, []);
        }
        var total = creation.PartDamage(partId) + Math.Max(0, damage);
        if (total >= full)
        {
            BuildActions.PlayPartSound(world, creation, partId, PartSounds.Break, null);
            world.Breaking(creation, partId);
            var breaks = creation.DefinitionOf(partId).BreaksInto;
            var centre = PartCentre(creation, partId);
            var size = creation.DefinitionOf(partId).Size is { } cells ? Math.Max(cells.X, Math.Max(cells.Y, cells.Z)) * MechanicaInfo.GridSize : MechanicaInfo.GridSize;
            var seed = World.Noise.Hash((uint)creation.Id, partId, (int)(world.Now * 1000) & 0xFFFF);
            var removal = BuildActions.Remove(world, creation, partId);
            // A rock chunk falls apart into rods where it was.
            if (breaks is not null)
                Shatter(breaks, centre, size * 0.5f, seed);
            return new SledgeResult(true, 0f, removal.Destroyed, removal.SplitOff);
        }
        BuildActions.PlayPartSound(world, creation, partId, PartSounds.Impact, null);
        creation.SetPartDamage(partId, total);
        return new SledgeResult(false, full - total, false, []);
    }

    /// <summary>Takes a part out of the world for every peer, as picking it up does, but gives it to no one (a refined rod).</summary>
    public RemovePartResult RemovePart(Creation creation, int partId) => BuildActions.Remove(world, creation, partId);

    /// <summary>
    /// Something <paramref name="radius"/> m across at <paramref name="centre"/> fell apart: spawns its
    /// <paramref name="breaks"/> pieces (loose parts, <see cref="PartDefinition.Loose"/>), each its own Creation, spread
    /// round the middle and thrown out and up (<see cref="BreakSettings"/>). <paramref name="seed"/> picks what each
    /// piece is and how it lies. Parts that are unknown or not loose are skipped. Returns the new Creations.
    /// </summary>
    public IReadOnlyList<Creation> Shatter(World.BreakDefinition breaks, Vector3 centre, float radius, uint seed)
    {
        var made = new List<Creation>();
        if (catalog is null)
            return made;
        var count = breaks.CountFor(World.Noise.Unit(World.Noise.Hash(seed, 2, 2)));
        var turn = World.Noise.Unit(World.Noise.Hash(seed, 0, 0)) * MathF.Tau;
        for (var i = 0; i < count; i++)
        {
            var part = breaks.Pick(World.Noise.Unit(World.Noise.Hash(seed, i, 1)));
            if (part is null || catalog.FindDefinition(part) is not { Loose: true } definition)
                continue;
            // Evenly round the middle, a third of the way out, each lying at its own angle.
            var angle = turn + MathF.Tau * i / count;
            var outward = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            var at = centre + outward * (count > 1 ? radius * 0.35f : 0f) + new Vector3(0, (i % 2) * 0.1f, 0);
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle + MathF.PI / 2f);
            var creation = new Creation(world.NextCreationId(), catalog);
            if (!creation.TryPlacePart(part, new GridCell(0, 0, 0), GridRotation.Identity, out var placed, out _))
                continue;
            var offset = PartGeometry.CentreMetres(definition, placed.Cell, placed.Rotation).ToNumerics();
            creation.SetAllBodyPoses(new Pose(at - Vector3.Transform(offset, rotation), rotation));
            world.Added(creation);
            var velocity = outward * settings.Breaking.SpreadSpeed + new Vector3(0, settings.Breaking.LiftSpeed, 0);
            var mass = creation.Bodies[0].Mass;
            if (mass > 0 && velocity != Vector3.Zero)
                world.Push(creation, 0, at, velocity * mass);
            made.Add(creation);
        }
        return made;
    }

    /// <summary>
    /// A tree was felled: spawns its <paramref name="breaks"/> pieces (loose log segments), each its own Creation, stood
    /// on end one above the other from <paramref name="foot"/> up a trunk <paramref name="height"/> m tall, and sets them
    /// all tipping over along <paramref name="direction"/> (level) as one trunk turning at <paramref name="spin"/> rad/s
    /// about its foot: each piece moves at that rate times its height and turns with it. <paramref name="seed"/> picks how
    /// many pieces there are and what each is. Returns the new Creations, lowest first.
    /// </summary>
    public IReadOnlyList<Creation> Fell(World.BreakDefinition breaks, Vector3 foot, float height, Vector3 direction, float spin, uint seed)
    {
        var made = new List<Creation>();
        if (catalog is null)
            return made;
        var count = breaks.CountFor(World.Noise.Unit(World.Noise.Hash(seed, 2, 2)));
        var fall = new Vector3(direction.X, 0, direction.Z);
        fall = fall.LengthSquared() > 1e-6f ? Vector3.Normalize(fall) : Vector3.UnitX;
        // Pieces go up the trunk in even steps, never closer than end to end.
        var cursor = foot.Y + settings.Breaking.FellGap;
        var step = height / count;
        for (var i = 0; i < count; i++)
        {
            var part = breaks.Pick(World.Noise.Unit(World.Noise.Hash(seed, i, 1)));
            if (part is null || catalog.FindDefinition(part) is not { Loose: true } definition)
                continue;
            var cells = definition.Size ?? GridSize.One;
            var dims = new Vector3(cells.X, cells.Y, cells.Z) * MechanicaInfo.GridSize;
            // Stand the piece's longest side up, turned a little about it.
            var upright = dims.Z >= dims.X && dims.Z >= dims.Y ? Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f)
                : dims.X > dims.Y ? Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f)
                : Quaternion.Identity;
            var length = MathF.Max(dims.X, MathF.Max(dims.Y, dims.Z));
            var thickness = MathF.Min(dims.X, MathF.Min(dims.Y, dims.Z));
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, World.Noise.Unit(World.Noise.Hash(seed, i, 3)) * MathF.Tau) * upright;
            var at = new Vector3(foot.X, cursor + length / 2f, foot.Z);
            cursor += MathF.Max(length + settings.Breaking.FellGap, step);
            var creation = new Creation(world.NextCreationId(), catalog);
            if (!creation.TryPlacePart(part, new GridCell(0, 0, 0), GridRotation.Identity, out var placed, out _))
                continue;
            var offset = PartGeometry.CentreMetres(definition, placed.Cell, placed.Rotation).ToNumerics();
            creation.SetAllBodyPoses(new Pose(at - Vector3.Transform(offset, rotation), rotation));
            world.Added(creation);
            var mass = creation.Bodies[0].Mass;
            var rise = at.Y - foot.Y;
            if (mass > 0 && spin > 0 && rise > 0)
            {
                // Moving at spin × rise, pushed far enough above its middle to turn at spin too (a rod's inertia).
                var lever = MathF.Min(length / 2f, (3f * thickness * thickness / 4f + length * length) / (12f * rise));
                world.Push(creation, 0, at + Vector3.UnitY * lever, fall * (spin * rise * mass));
            }
            made.Add(creation);
        }
        return made;
    }

    private string? ValidateBlock(ActionCaller caller, BlockArgs args) =>
        !args.On ? null
        : world.EyeOf(caller.Peer) is null ? "You have no player to block with."
        : Targets?.CheckBlock(caller);

    // The speed a player's hit sets a character moving at (m/s), along the swing from their eye; null when there is none.
    private Vector3? Knockback(ActionCaller caller, Vector3 target)
    {
        if (caller.Source != ActionSource.Player || world.EyeOf(caller.Peer) is not { } eye)
            return null;
        var push = settings.Sledge.KnockbackFor(target - eye);
        return push == Vector3.Zero ? null : push;
    }

    private JsonElement? ApplyBlock(ActionCaller caller, BlockArgs args)
    {
        if (args.On)
            _blocking.Add(caller.Peer);
        else
            _blocking.Remove(caller.Peer);
        return null;
    }

    // ---- Shared ----

    private static Vector3 PartCentre(Creation creation, int partId) => BuildActions.PartCentre(creation, partId);

    private string? CheckReach(ActionCaller caller, Vector3 point) => CheckReach(caller, new WorldBounds(point, point));

    private string? CheckReach(ActionCaller caller, WorldBounds box)
    {
        if (caller.Source != ActionSource.Player)
            return null;
        if (world.EyeOf(caller.Peer) is not { } eye)
            return "You have no player to use tools with.";
        var distance = Vector3.Distance(eye, Vector3.Clamp(eye, box.Min, box.Max));
        return distance <= settings.Reach ? null : $"That is {distance:F1} m away; you can reach {settings.Reach:F1} m.";
    }

    private static string Sentence(string error) => char.ToUpperInvariant(error[0]) + error[1..];

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);

    // The stroke a player last paid for: its number, when its last Paint came, and the parts it has covered.
    private sealed record PaidStroke(int? Stroke, double LastAt, int Parts);
}
