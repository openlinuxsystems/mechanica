using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Sound;

namespace Mechanica.Core.Building;

/// <summary>
/// Arguments of <c>PlacePart</c>. Give <see cref="Creation"/> to attach to an existing Creation, with
/// <see cref="Cell"/> in its grid frame; or give <see cref="Position"/> to start a new Creation whose
/// grid frame sits at that world position, aligned with the world axes.
/// </summary>
/// <param name="Part">Part id, <c>mod_id:part_id</c> (a bare id means vanilla).</param>
/// <param name="Cell">Grid cell of the part's local origin cell.</param>
/// <param name="Rotation">Orientation on the grid, like <c>"+x+y"</c>.</param>
/// <param name="Creation">Creation to attach to.</param>
/// <param name="Position">World position in metres of a new Creation's grid origin.</param>
public sealed record PlacePartArgs(string Part, GridCell Cell, GridRotation Rotation = default, int? Creation = null, Vec3? Position = null);

/// <summary>Arguments of <c>RemovePart</c>.</summary>
/// <param name="Creation">Creation holding the part.</param>
/// <param name="Part">Part id within that Creation.</param>
public sealed record RemovePartArgs(int Creation, int Part);

/// <summary>
/// Arguments of <c>PlaceParts</c>: one part at each of <see cref="Cells"/>, all in one grid frame (a Hammer drag,
/// UI spec §2). With <see cref="Position"/> the first cell starts a new Creation there and the rest attach to it.
/// </summary>
/// <param name="Part">Part id, <c>mod_id:part_id</c>.</param>
/// <param name="Cells">Origin cells, each touching one before it.</param>
/// <param name="Rotation">Orientation of every part.</param>
/// <param name="Creation">Creation to attach to.</param>
/// <param name="Position">World position in metres of a new Creation's grid origin.</param>
public sealed record PlacePartsArgs(string Part, IReadOnlyList<GridCell> Cells, GridRotation Rotation = default, int? Creation = null, Vec3? Position = null);

/// <summary>Arguments of <c>RemoveParts</c>: parts of one Body of a Creation (a Hammer drag, UI spec §8).</summary>
/// <param name="Creation">Creation holding the parts.</param>
/// <param name="Parts">Part ids, all on the same Body.</param>
public sealed record RemovePartsArgs(int Creation, IReadOnlyList<int> Parts);

/// <summary>Value of a successful <c>PlaceParts</c> or <c>RemoveParts</c>.</summary>
/// <param name="Creation">Creation the parts went into or came from.</param>
/// <param name="Done">How many were placed or removed.</param>
/// <param name="Skipped">How many could not be (blocked, out of reach, nothing left to pay with).</param>
/// <param name="Reason">Why the first skipped one could not be, or null.</param>
public sealed record BatchResult(int Creation, int Done, int Skipped, string? Reason = null);

/// <summary>Value of a successful <c>PlacePart</c>.</summary>
/// <param name="Creation">Creation the part is now in.</param>
/// <param name="Part">The new part's id.</param>
public sealed record PlacePartResult(int Creation, int Part);

/// <summary>Value of a successful <c>RemovePart</c>.</summary>
/// <param name="Creation">Creation the part was in.</param>
/// <param name="Destroyed">True when that was its last part and the Creation is gone.</param>
/// <param name="SplitOff">New Creations made from pieces that lost their link.</param>
public sealed record RemovePartResult(int Creation, bool Destroyed, IReadOnlyList<int> SplitOff);

/// <summary>What the building actions need from the world they change. The host's game layer implements it.</summary>
public interface IBuildWorld
{
    /// <summary>The Creation with this id, with its Body poses current, or null.</summary>
    Creation? FindCreation(int id);

    /// <summary>World position of a peer's player's eyes, or null when it has no player.</summary>
    Vector3? EyeOf(int peer);

    /// <summary>A fresh, never used Creation id.</summary>
    int NextCreationId();

    /// <summary>
    /// Null when the world space a part would fill is clear of players, other Creations and the world,
    /// or a reason it is not. <paramref name="target"/> is the Creation it joins (null for a new one),
    /// whose own bodies don't count; <paramref name="pose"/> maps its grid frame to the world.
    /// </summary>
    string? CheckClear(Creation? target, PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose);

    /// <summary>
    /// True when a part placed at <paramref name="pose"/> rests on the world itself (the ground, a rock, a building),
    /// not only on players, Creations, Lifts or loose props. <paramref name="target"/>'s own bodies don't count.
    /// Such a part is anchored (<see cref="Creation.SetAnchored"/>) when it starts a Creation or joins a grounded one.
    /// Worlds without terrain anchor nothing.
    /// </summary>
    bool RestsOnWorld(Creation? target, PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose) => false;

    /// <summary>A new Creation now exists (placed on the ground, or split off).</summary>
    void Added(Creation creation);

    /// <summary>A Creation's graph changed.</summary>
    void Changed(Creation creation);

    /// <summary>A Creation lost its last part and is gone.</summary>
    void Removed(int creationId);

    /// <summary>
    /// Plays a sound event at a world point for everyone in earshot (chunk 16). <paramref name="sound"/> is an
    /// id as data writes it; a bare one means <paramref name="ownerModId"/>'s event, else vanilla's
    /// (<see cref="SoundRegistry.Find"/>). Worlds without sound ignore it.
    /// </summary>
    void PlaySound(string sound, string ownerModId, Vector3 position)
    {
    }
}

/// <summary>
/// What building costs, in Survival: a player pays for each part placed with one of that item from
/// their inventory, and gets it back when they take the part off again. Null costs (Creative) make
/// building free.
/// </summary>
public interface IBuildCosts
{
    /// <summary>Null when <paramref name="caller"/> can pay for one <paramref name="part"/> (a qualified id), or why not.</summary>
    string? CheckPlace(ActionCaller caller, string part);

    /// <summary>Takes payment for one <paramref name="part"/> that was just placed.</summary>
    void Placed(ActionCaller caller, string part);

    /// <summary>
    /// Null when <paramref name="caller"/> can pay for all of <paramref name="parts"/> at once (qualified id to count),
    /// as loading a blueprint does, or why not.
    /// </summary>
    string? CheckPlaceAll(ActionCaller caller, IReadOnlyDictionary<string, int> parts) =>
        parts.Keys.Select(p => CheckPlace(caller, p)).FirstOrDefault(r => r is not null);

    /// <summary>Gives back one <paramref name="part"/> that was just removed at world point <paramref name="at"/> (dropped there when it does not fit).</summary>
    void Removed(ActionCaller caller, string part, Vector3 at);
}

/// <summary>
/// The building actions of the Action API, validated on the host like every other action:
/// <list type="bullet">
/// <item><c>PlacePart</c>: places a part against an existing Creation, attaching by the D7 face rules,
/// or starts a new Creation with it.</item>
/// <item><c>RemovePart</c>: removes a part; pieces that lose their link split into new Creations, and a
/// Creation with no parts left is destroyed.</item>
/// </list>
/// Player callers must be within <see cref="BuildSettings.Reach"/> of the part. With <see cref="Costs"/>
/// (Survival), placing a part uses one from the caller's inventory and removing it gives it back. Each
/// change plays the part's sound where it happened (<see cref="PartSounds"/>): its place sound, or
/// <c>part_attach</c> for a part without one; its remove sound, or <c>remove_block</c> (blocks) or
/// <c>part_detach</c> (other parts).
/// </summary>
public sealed class BuildActions(IPartCatalog catalog, BuildSettings settings, IBuildWorld world)
{
    /// <summary>What parts cost; null builds for free (Creative).</summary>
    public IBuildCosts? Costs { get; init; }

    /// <summary>Action name for placing.</summary>
    public const string PlacePart = "PlacePart";

    /// <summary>Action name for removing.</summary>
    public const string RemovePart = "RemovePart";

    /// <summary>Action name for placing a drag's worth of parts.</summary>
    public const string PlaceParts = "PlaceParts";

    /// <summary>Action name for removing a drag's worth of parts.</summary>
    public const string RemoveParts = "RemoveParts";

    /// <summary>Registers the actions.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<PlacePartArgs>(PlacePart, ValidatePlace, (caller, args) => ApplyPlace(caller, args, sound: true));
        dispatcher.Register<RemovePartArgs>(RemovePart, ValidateRemove, (caller, args) => ApplyRemove(caller, args, sound: true));
        dispatcher.Register<PlacePartsArgs>(PlaceParts, ValidatePlaceMany, ApplyPlaceMany);
        dispatcher.Register<RemovePartsArgs>(RemoveParts, ValidateRemoveMany, ApplyRemoveMany);
    }

    // The batch is refused only when its first part is; later ones that no longer fit are skipped.
    private string? ValidatePlaceMany(ActionCaller caller, PlacePartsArgs args)
    {
        if (args.Cells is not { Count: > 0 } cells)
            return "Give at least one cell.";
        if (cells.Count > settings.Hammer.MaxPartsPerAction)
            return $"At most {settings.Hammer.MaxPartsPerAction} parts go down at once.";
        if (cells.Distinct().Count() != cells.Count)
            return "A cell is given twice.";
        return ValidatePlace(caller, new PlacePartArgs(args.Part, cells[0], args.Rotation, args.Creation, args.Position));
    }

    private JsonElement? ApplyPlaceMany(ActionCaller caller, PlacePartsArgs args)
    {
        var first = ApplyPlace(caller, new PlacePartArgs(args.Part, args.Cells[0], args.Rotation, args.Creation, args.Position), sound: true)!.Value
            .Deserialize<PlacePartResult>(ActionJson.Options)!;
        var (done, skipped, reason) = (1, 0, (string?)null);
        foreach (var cell in args.Cells.Skip(1))
        {
            var one = new PlacePartArgs(args.Part, cell, args.Rotation, first.Creation);
            if (ValidatePlace(caller, one) is { } error)
            {
                skipped++;
                reason ??= error;
                continue;
            }
            ApplyPlace(caller, one, sound: false);
            done++;
        }
        return Value(new BatchResult(first.Creation, done, skipped, reason));
    }

    private string? ValidateRemoveMany(ActionCaller caller, RemovePartsArgs args)
    {
        if (args.Parts is not { Count: > 0 } parts)
            return "Give at least one part.";
        if (parts.Count > settings.Hammer.MaxPartsPerAction)
            return $"At most {settings.Hammer.MaxPartsPerAction} parts come off at once.";
        if (ValidateRemove(caller, new RemovePartArgs(args.Creation, parts[0])) is { } error)
            return error;
        var creation = world.FindCreation(args.Creation)!;
        var body = creation.BodyIndexOf(parts[0]);
        // Removing across a joint takes the joint's own part (UI spec §8).
        if (parts.Any(p => creation.FindPart(p) is null || creation.BodyIndexOf(p) != body))
            return "Every part must be on the same body; take the bearing or piston off on its own.";
        return null;
    }

    private JsonElement? ApplyRemoveMany(ActionCaller caller, RemovePartsArgs args)
    {
        // Part ids stay the same when pieces split off, so each part is found in whichever piece now holds it.
        var pieces = new List<int> { args.Creation };
        var (done, skipped, reason) = (0, 0, (string?)null);
        foreach (var part in args.Parts)
        {
            var holder = pieces.Select(world.FindCreation).FirstOrDefault(c => c?.FindPart(part) is not null);
            var one = new RemovePartArgs(holder?.Id ?? args.Creation, part);
            if ((holder is null ? $"Part {part} is already gone." : ValidateRemove(caller, one)) is { } error)
            {
                skipped++;
                reason ??= error;
                continue;
            }
            var result = ApplyRemove(caller, one, sound: done == 0)!.Value.Deserialize<RemovePartResult>(ActionJson.Options)!;
            pieces.AddRange(result.SplitOff);
            done++;
        }
        return Value(new BatchResult(args.Creation, done, skipped, reason));
    }

    private string? ValidatePlace(ActionCaller caller, PlacePartArgs args)
    {
        if ((args.Creation is null) == (args.Position is null))
            return "Give either creation (to attach) or position (to start a new creation), not both.";
        var definition = catalog.FindDefinition(args.Part);
        if (definition is null)
            return $"Unknown part '{args.Part}'.";
        if (!definition.IsPlaceable)
            return $"'{Identifiers.Qualify(args.Part)}' cannot be placed.";

        Creation? target = null;
        Pose pose;
        if (args.Creation is { } id)
        {
            target = world.FindCreation(id);
            if (target is null)
                return $"There is no creation {id}.";
            if (target.Parts.Count >= settings.MaxPartsPerCreation)
                return $"Creation {id} already has the most parts allowed ({settings.MaxPartsPerCreation}).";
            if (!target.CanPlacePart(args.Part, args.Cell, args.Rotation, out var attachedTo, out var error))
                return Sentence(error);
            pose = target.BodyPose(target.BodyIndexOf(attachedTo[0]));
        }
        else
        {
            var position = args.Position!.Value;
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                return "position must be finite.";
            pose = new Pose(position.ToNumerics(), Quaternion.Identity);
        }

        var centre = pose.Apply(PartGeometry.CentreMetres(definition, args.Cell, args.Rotation).ToNumerics());
        return CheckReach(caller, centre)
            ?? world.CheckClear(target, definition, args.Cell, args.Rotation, pose)
            ?? Costs?.CheckPlace(caller, Identifiers.Qualify(args.Part));
    }

    private JsonElement? ApplyPlace(ActionCaller caller, PlacePartArgs args, bool sound)
    {
        PartInstance? placed;
        string? error;
        if (args.Creation is { } id)
        {
            var target = world.FindCreation(id)!;
            if (!target.TryPlacePart(args.Part, args.Cell, args.Rotation, out placed, out error))
                throw new InvalidOperationException(error);
            // A part set down on the ground holds a grounded Creation there too, so it stays put when others come off.
            if (target.IsGrounded && world.RestsOnWorld(target, target.DefinitionOf(placed.Id), args.Cell, args.Rotation,
                    target.BodyPose(target.BodyIndexOf(placed.Id))))
                target.SetAnchored(placed.Id, true);
            world.Changed(target);
            if (sound)
                PlayPlaced(world, target, placed.Id);
            Costs?.Placed(caller, placed.Part);
            return Value(new PlacePartResult(target.Id, placed.Id));
        }

        var creation = new Creation(world.NextCreationId(), catalog);
        if (!creation.TryPlacePart(args.Part, args.Cell, args.Rotation, out placed, out error))
            throw new InvalidOperationException(error);
        var start = new Pose(args.Position!.Value.ToNumerics(), Quaternion.Identity);
        creation.SetAllBodyPoses(start);
        // Built on the ground, it is anchored there as in Scrap Mechanic: players can't kick it about.
        if (world.RestsOnWorld(null, creation.DefinitionOf(placed.Id), args.Cell, args.Rotation, start))
            creation.SetAnchored(placed.Id, true);
        world.Added(creation);
        PlayPlaced(world, creation, placed.Id);
        Costs?.Placed(caller, placed.Part);
        return Value(new PlacePartResult(creation.Id, placed.Id));
    }

    private string? ValidateRemove(ActionCaller caller, RemovePartArgs args)
    {
        if (world.FindCreation(args.Creation) is not { } creation)
            return $"There is no creation {args.Creation}.";
        if (creation.FindPart(args.Part) is not { } part)
            return $"Creation {args.Creation} has no part {args.Part}.";
        if (creation.HoldsItems(part.Id))
            return $"Empty the {creation.DefinitionOf(part.Id).Name ?? part.Part} before picking it up.";
        var pose = creation.BodyPose(creation.BodyIndexOf(part.Id));
        var centre = pose.Apply(PartGeometry.CentreMetres(creation.DefinitionOf(part.Id), part.Cell, part.Rotation).ToNumerics());
        return CheckReach(caller, centre);
    }

    private JsonElement? ApplyRemove(ActionCaller caller, RemovePartArgs args, bool sound)
    {
        var creation = world.FindCreation(args.Creation)!;
        var definition = creation.DefinitionOf(args.Part);
        var fallback = definition.Category == PartCategory.Block ? "remove_block" : "part_detach";
        if (sound)
            PlayPartSound(world, creation, args.Part, PartSounds.Remove, fallback);
        var part = creation.FindPart(args.Part)!;
        var centre = PartCentre(creation, part.Id);
        var result = Remove(world, creation, args.Part);
        Costs?.Removed(caller, part.Part, centre);
        return Value(result);
    }

    /// <summary>
    /// Plays the sound part <paramref name="partId"/> binds to <paramref name="key"/>, or its material's
    /// event, at its centre; both are looked up in the part's own mod first, so a mod can bring a new
    /// material. Without either, <paramref name="fallback"/> (a vanilla event) plays. Call it while the part
    /// is still in the Creation.
    /// </summary>
    internal static void PlayPartSound(IBuildWorld world, Creation creation, int partId, string key, string? fallback)
    {
        var part = creation.FindPart(partId)!;
        var definition = creation.DefinitionOf(partId);
        var bound = PartSounds.For(definition, key);
        var sound = bound ?? fallback;
        if (sound is null)
            return;
        var owner = bound is null ? Identifiers.VanillaModId : OwnerOf(part.Part);
        world.PlaySound(sound, owner, PartCentre(creation, partId));
    }

    /// <summary>World position of a part's centre.</summary>
    internal static Vector3 PartCentre(Creation creation, int partId)
    {
        var part = creation.FindPart(partId)!;
        var pose = creation.BodyPose(creation.BodyIndexOf(partId));
        return pose.Apply(PartGeometry.CentreMetres(creation.DefinitionOf(partId), part.Cell, part.Rotation).ToNumerics());
    }

    private static void PlayPlaced(IBuildWorld world, Creation creation, int partId) =>
        PlayPartSound(world, creation, partId, PartSounds.Place, "part_attach");

    // Mod a part id belongs to: its prefix, vanilla for a bare id.
    internal static string OwnerOf(string partId)
    {
        var qualified = Identifiers.Qualify(partId);
        return qualified[..qualified.IndexOf(':')];
    }

    /// <summary>
    /// Takes a part out of a Creation and tells <paramref name="world"/>: the Creation is removed when
    /// that was its last part, otherwise changed, and every piece that split off is added.
    /// </summary>
    internal static RemovePartResult Remove(IBuildWorld world, Creation creation, int partId)
    {
        var result = creation.RemovePart(partId, world.NextCreationId);
        if (creation.IsEmpty)
            world.Removed(creation.Id);
        else
            world.Changed(creation);
        foreach (var piece in result.SplitOff)
            world.Added(piece);
        return new RemovePartResult(creation.Id, creation.IsEmpty, [.. result.SplitOff.Select(c => c.Id)]);
    }

    private string? CheckReach(ActionCaller caller, Vector3 point)
    {
        if (caller.Source != ActionSource.Player)
            return null;
        if (world.EyeOf(caller.Peer) is not { } eye)
            return "You have no player to build with.";
        var distance = Vector3.Distance(eye, point);
        return distance <= settings.Reach ? null : $"That is {distance:F1} m away; you can reach {settings.Reach:F1} m.";
    }

    private static string Sentence(string error) => char.ToUpperInvariant(error[0]) + error[1..];

    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, ActionJson.Options);
}
