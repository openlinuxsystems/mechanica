using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
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
public sealed record PlaceLiftArgs(Vec3 Position, int? Creation = null);

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
public sealed record PaintArgs(int Creation, IReadOnlyList<int> Parts, string? Color = null);

/// <summary>Arguments of <c>Sledge</c>: one Sledgehammer hit.</summary>
/// <param name="Creation">The Creation hit.</param>
/// <param name="Part">The part hit.</param>
public sealed record SledgeArgs(int Creation, int Part);

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
}

/// <summary>
/// The hand tools of the Action API besides the Hammer (<see cref="BuildActions"/>), validated on the host:
/// <list type="bullet">
/// <item><c>PlaceLift</c>, <c>SetLiftLevel</c>, <c>TurnLift</c>, <c>RemoveLift</c>: each player's Lift. A
/// Creation on a Lift stands upright and still on the platform (<see cref="Seat"/>), and drops when the
/// Lift lets go. No stamina cost (D12).</item>
/// <item><c>Weld</c>: moves one Creation onto another and merges them, every touching face rigid (D7).
/// <c>WeldParts</c>: welds two touching parts of different Bodies of one Creation, merging the Bodies.</item>
/// <item><c>Paint</c>: colours parts, or takes the paint off.</item>
/// <item><c>Sledge</c>: a Sledgehammer hit, which pushes the Body and damages the part until it breaks off.</item>
/// </list>
/// Player callers must be within <see cref="BuildSettings.Reach"/> of what they act on. Each action plays its
/// tool's sound where it happened (chunk 16): <c>lift_place</c>, <c>lift_raise</c>/<c>lift_lower</c> at the
/// Lift; <c>weld_complete</c> where the weld joins; <c>paint_spray</c>/<c>paint_erase</c> on the first part;
/// <c>smash_hit</c> plus the part's impact sound, or <c>smash_break</c> plus its break sound.
/// </summary>
public sealed class ToolActions(BuildSettings settings, IToolWorld world)
{
    /// <summary>Action name for placing, moving or loading the caller's Lift.</summary>
    public const string PlaceLift = "PlaceLift";

    /// <summary>Action name for raising or lowering the caller's Lift.</summary>
    public const string SetLiftLevel = "SetLiftLevel";

    /// <summary>Action name for turning the Creation on the caller's Lift.</summary>
    public const string TurnLift = "TurnLift";

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

    // Share of the swing time the host insists on between one player's hits: swings the client spaced
    // correctly can arrive closer together after network jitter.
    private const double SwingJitter = 0.8;

    private readonly SortedDictionary<int, LiftState> _lifts = [];
    private readonly Dictionary<int, double> _lastSwing = [];

    /// <summary>Every Lift, by owner.</summary>
    public IReadOnlyCollection<LiftState> Lifts => _lifts.Values;

    /// <summary>The Lift <paramref name="peer"/> owns, or null.</summary>
    public LiftState? LiftOf(int peer) => _lifts.GetValueOrDefault(peer);

    /// <summary>Registers every tool action.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<PlaceLiftArgs>(PlaceLift, ValidatePlaceLift, ApplyPlaceLift);
        dispatcher.Register<SetLiftLevelArgs>(SetLiftLevel, ValidateLevel, ApplyLevel);
        dispatcher.Register<TurnLiftArgs>(TurnLift, ValidateTurn, ApplyTurn);
        dispatcher.Register<NoArgs>(RemoveLift, ValidateRemoveLift, ApplyRemoveLift);
        dispatcher.Register<WeldArgs>(Weld, ValidateWeld, ApplyWeld);
        dispatcher.Register<WeldPartsArgs>(WeldParts, ValidateWeldParts, ApplyWeldParts);
        dispatcher.Register<PaintArgs>(Paint, ValidatePaint, ApplyPaint);
        dispatcher.Register<SledgeArgs>(Sledge, ValidateSledge, ApplySledge);
    }

    /// <summary>Forgets a peer that left (its Lift is removed through <c>RemoveLift</c> first).</summary>
    public void Forget(int peer) => _lastSwing.Remove(peer);

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
            if (_lifts.Values.Any(l => l.Owner != caller.Peer && l.Creation == id))
                return $"Creation {id} is on another player's lift.";
            if (CheckReach(caller, CreationBounds.World(carried)) is { } tooFar)
                return tooFar;
        }
        return CheckLift(lift, carried, 0);
    }

    private JsonElement? ApplyPlaceLift(ActionCaller caller, PlaceLiftArgs args)
    {
        var lift = PlannedLift(caller.Peer, args, out var carried);
        if (Held(LiftOf(caller.Peer)) is { } old && old != carried)
            Release(old);
        world.PlaySound("lift_place", Identifiers.VanillaModId, lift.Position.ToNumerics());
        return Commit(lift, carried, 0);
    }

    private string? ValidateLevel(ActionCaller caller, SetLiftLevelArgs args)
    {
        if (LiftOf(caller.Peer) is not { } lift)
            return "You have no lift.";
        if (args.Level < 0 || args.Level > settings.Lift.MaxLevel)
            return $"level must be 0 to {settings.Lift.MaxLevel} (got {args.Level}).";
        return CheckReach(caller, lift.Position.ToNumerics())
            ?? CheckLift(lift with { Level = args.Level }, Held(lift), 0);
    }

    private JsonElement? ApplyLevel(ActionCaller caller, SetLiftLevelArgs args)
    {
        var lift = LiftOf(caller.Peer)!;
        if (args.Level != lift.Level)
            world.PlaySound(args.Level > lift.Level ? "lift_raise" : "lift_lower", Identifiers.VanillaModId, PlatformTop(lift));
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

    private string? CheckLift(LiftState lift, Creation? carried, int turns)
    {
        IReadOnlyCollection<int> ignore = carried is null ? [] : [carried.Id];
        if (world.CheckLiftClear(lift, ignore) is { } blocked)
            return blocked;
        if (carried is null)
            return null;
        var poses = Seat(carried, PlatformTop(lift), turns);
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
        if (args.Source == args.Target)
            return "A creation cannot be welded to itself; weld two of its parts with WeldParts.";
        if (world.FindCreation(args.Source) is not { } source)
            return $"There is no creation {args.Source}.";
        if (world.FindCreation(args.Target) is not { } target)
            return $"There is no creation {args.Target}.";
        if (source.AuthorityPeer != target.AuthorityPeer)
            return $"Creations {source.Id} and {target.Id} are simulated by different players.";
        if (source.Parts.Count + target.Parts.Count > settings.MaxPartsPerCreation)
            return $"Together they would have more parts than allowed ({settings.MaxPartsPerCreation}).";
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
        return null;
    }

    private JsonElement? ApplyPaint(ActionCaller caller, PaintArgs args)
    {
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
        if (world.FindCreation(args.Creation) is not { } creation)
            return $"There is no creation {args.Creation}.";
        if (creation.FindPart(args.Part) is null)
            return $"Creation {args.Creation} has no part {args.Part}.";
        if (caller.Source == ActionSource.Player && _lastSwing.TryGetValue(caller.Peer, out var last)
            && world.Now - last < settings.Sledge.SwingSeconds * SwingJitter)
            return "The Sledgehammer is still swinging.";
        return CheckReach(caller, PartCentre(creation, args.Part));
    }

    private JsonElement? ApplySledge(ActionCaller caller, SledgeArgs args)
    {
        _lastSwing[caller.Peer] = world.Now;
        var creation = world.FindCreation(args.Creation)!;
        var centre = PartCentre(creation, args.Part);
        var health = settings.Sledge.HealthOf(creation.DefinitionOf(args.Part).Durability);
        if (health is { } full)
        {
            var damage = creation.PartDamage(args.Part) + settings.Sledge.Damage;
            if (damage >= full)
            {
                world.PlaySound("smash_break", Identifiers.VanillaModId, centre);
                BuildActions.PlayPartSound(world, creation, args.Part, PartSounds.Break, null);
                var removal = BuildActions.Remove(world, creation, args.Part);
                return Value(new SledgeResult(true, 0f, removal.Destroyed, removal.SplitOff));
            }
            creation.SetPartDamage(args.Part, damage);
            health = full - damage;
        }
        world.PlaySound("smash_hit", Identifiers.VanillaModId, centre);
        BuildActions.PlayPartSound(world, creation, args.Part, PartSounds.Impact, null);
        if (caller.Source == ActionSource.Player && world.EyeOf(caller.Peer) is { } eye && centre != eye)
            world.Push(creation, creation.BodyIndexOf(args.Part), centre, Vector3.Normalize(centre - eye) * settings.Sledge.Impulse);
        return Value(new SledgeResult(false, health, false, []));
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
}
