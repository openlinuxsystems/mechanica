using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;

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

    /// <summary>A new Creation now exists (placed on the ground, or split off).</summary>
    void Added(Creation creation);

    /// <summary>A Creation's graph changed.</summary>
    void Changed(Creation creation);

    /// <summary>A Creation lost its last part and is gone.</summary>
    void Removed(int creationId);
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
/// (Survival), placing a part uses one from the caller's inventory and removing it gives it back.
/// </summary>
public sealed class BuildActions(IPartCatalog catalog, BuildSettings settings, IBuildWorld world)
{
    /// <summary>What parts cost; null builds for free (Creative).</summary>
    public IBuildCosts? Costs { get; init; }

    /// <summary>Action name for placing.</summary>
    public const string PlacePart = "PlacePart";

    /// <summary>Action name for removing.</summary>
    public const string RemovePart = "RemovePart";

    /// <summary>Registers both actions.</summary>
    public void Register(ActionDispatcher dispatcher)
    {
        dispatcher.Register<PlacePartArgs>(PlacePart, ValidatePlace, ApplyPlace);
        dispatcher.Register<RemovePartArgs>(RemovePart, ValidateRemove, ApplyRemove);
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

    private JsonElement? ApplyPlace(ActionCaller caller, PlacePartArgs args)
    {
        PartInstance? placed;
        string? error;
        if (args.Creation is { } id)
        {
            var target = world.FindCreation(id)!;
            if (!target.TryPlacePart(args.Part, args.Cell, args.Rotation, out placed, out error))
                throw new InvalidOperationException(error);
            world.Changed(target);
            Costs?.Placed(caller, placed.Part);
            return Value(new PlacePartResult(target.Id, placed.Id));
        }

        var creation = new Creation(world.NextCreationId(), catalog);
        if (!creation.TryPlacePart(args.Part, args.Cell, args.Rotation, out placed, out error))
            throw new InvalidOperationException(error);
        creation.SetAllBodyPoses(new Pose(args.Position!.Value.ToNumerics(), Quaternion.Identity));
        world.Added(creation);
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

    private JsonElement? ApplyRemove(ActionCaller caller, RemovePartArgs args)
    {
        var creation = world.FindCreation(args.Creation)!;
        var part = creation.FindPart(args.Part)!;
        var centre = creation.BodyPose(creation.BodyIndexOf(part.Id))
            .Apply(PartGeometry.CentreMetres(creation.DefinitionOf(part.Id), part.Cell, part.Rotation).ToNumerics());
        var result = Remove(world, creation, args.Part);
        Costs?.Removed(caller, part.Part, centre);
        return Value(result);
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
