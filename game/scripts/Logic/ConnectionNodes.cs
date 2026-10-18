using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Logic;

/// <summary>
/// One connection node: a part with logic as the Connect Tool shows it (UI spec §12), at its part's centre.
/// </summary>
/// <param name="Creation">The Creation's id.</param>
/// <param name="Part">The part's id.</param>
/// <param name="Position">World position, as this peer shows the part.</param>
/// <param name="Definition">The part's definition (its <see cref="PartDefinition.Logic"/> is set).</param>
/// <param name="Reversed">True when the part's reverse setting is on.</param>
/// <param name="Driven">True when something that drives it (a seat, a controller, an engine) is linked to it.</param>
/// <param name="Numbers">The order numbers seats and controllers linked to it give it (<see cref="ConnectionOrder"/>).</param>
public readonly record struct ConnectionNode(int Creation, int Part, Vector3 Position, PartDefinition Definition, bool Reversed,
    bool Driven = false, IReadOnlyList<(int Source, int Number)>? Numbers = null)
{
    /// <summary>The node's kind (its colour).</summary>
    public PortKind Kind => Definition.Logic!.KindOrDefault;

    /// <summary>
    /// True when right click reverses the part: it names a reverse setting and something drives it (a bearing linked
    /// to a seat, a controller or an engine), so it shows a turn arrow.
    /// </summary>
    public bool Reversible => Definition.Logic!.Reverse is not null && Driven;

    /// <summary>The part's display name.</summary>
    public string Name => Definition.Name ?? Definition.Id ?? "";
}

/// <summary>Finds connection nodes and part centres in the Creations this peer shows.</summary>
public static class ConnectionNodes
{
    /// <summary>The Creations with a body within <paramref name="range"/> m of <paramref name="centre"/> (all when null).</summary>
    public static IEnumerable<CreationNode> Nearby(CreationWorld creations, Vector3? centre, float range)
    {
        foreach (var creation in creations.All)
        {
            if (creations.NodeOf(creation.Id) is not { } node || node.Bodies.Count == 0)
                continue;
            if (centre is { } at && node.Bodies.All(b => b.GlobalPosition.DistanceTo(at) > range))
                continue;
            yield return node;
        }
    }

    /// <summary>Every node of a Creation: one per part that has logic, in part order.</summary>
    public static IEnumerable<ConnectionNode> Of(CreationNode node)
    {
        var creation = node.Creation;
        foreach (var part in creation.Parts.OrderBy(p => p.Id))
        {
            var definition = creation.DefinitionOf(part.Id);
            if (definition.Logic is null || Centre(node, part.Id) is not { } at)
                continue;
            yield return new ConnectionNode(creation.Id, part.Id, at, definition, Core.Logic.PartSettings.Reversed(definition, part.Settings),
                ConnectionOrder.ShowsDirection(creation, part.Id), ConnectionOrder.NumbersOf(creation, part.Id));
        }
    }

    /// <summary>World centre of a part as this peer shows it, or null when it is not shown.</summary>
    public static Vector3? Centre(CreationNode node, int partId)
    {
        var creation = node.Creation;
        if (creation.FindPart(partId) is not { } part)
            return null;
        var index = creation.BodyIndexOf(partId);
        if (index >= node.Bodies.Count)
            return null;
        return node.Bodies[index].GlobalTransform * PartGeometry.CentreMetres(creation.DefinitionOf(partId), part.Cell, part.Rotation).ToNumerics().ToGodot();
    }

    /// <summary>The colour of a node of <paramref name="kind"/>.</summary>
    public static Color Colour(PortKind kind) => kind switch
    {
        PortKind.Power => Ui.UiTokens.NodePower,
        PortKind.Bearing => Ui.UiTokens.NodeBearing,
        PortKind.Structural => Ui.UiTokens.NodeStructural,
        _ => Ui.UiTokens.NodeLogic,
    };

    /// <summary>The kind's name for a tooltip.</summary>
    public static string KindName(PortKind kind) => kind switch
    {
        PortKind.Power => Ui.Loc.T("Power"),
        PortKind.Bearing => Ui.Loc.T("Bearing"),
        PortKind.Structural => Ui.Loc.T("Structural"),
        _ => Ui.Loc.T("Logic"),
    };
}
