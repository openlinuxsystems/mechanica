using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mechanica.Core.Data;

/// <summary>Which way items go through an IO port (docs/io-ports.md): read from the arrows on the port's face.</summary>
public enum ItemPortMode
{
    /// <summary>Items come in (copper arrows pointing in).</summary>
    In,

    /// <summary>Items go out (teal arrows pointing out).</summary>
    Out,

    /// <summary>Both ways (a two-way port).</summary>
    Both,
}

/// <summary>
/// One IO port on a part (Parts map E1, docs/io-ports.md): the standard face where a Vacuum Pipe connects. Two ports join
/// when they sit on the same block face from either side: the same point, facing each other. Ports carry items between the
/// containers, stations, pumps and collectors of one Creation, through parts that are conduits (the pipes).
/// </summary>
public sealed record ItemPortDefinition
{
    /// <summary>Port id, unique within the part (lowercase snake_case).</summary>
    public string? Id { get; init; }

    /// <summary>
    /// The middle of the port's face, in metres from the part's centre in the part's frame: on the footprint boundary, at the
    /// middle of one block face.
    /// </summary>
    public Vec3? Position { get; init; }

    /// <summary>The outward direction the port faces, along one axis (<c>[1, 0, 0]</c>).</summary>
    public Vec3? Normal { get; init; }

    /// <summary>Which way items go through it. Omitted means <see cref="ItemPortMode.Both"/>.</summary>
    public ItemPortMode? Mode { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>True when items may come in through the port.</summary>
    [JsonIgnore]
    public bool TakesIn => Mode is null or ItemPortMode.In or ItemPortMode.Both;

    /// <summary>True when items may go out through the port.</summary>
    [JsonIgnore]
    public bool GivesOut => Mode is null or ItemPortMode.Out or ItemPortMode.Both;
}

/// <summary>
/// Where a part takes things in from the world and puts them out (Parts map E1): a Vacuum Pump's mouth, a Resource
/// Collector's hopper. The part's script picks up drops, loose resources, ripe crops and water within <see cref="Reach"/>
/// of it (<c>part.pickups</c>, <c>part.collect</c>) and ejects items from it (<c>part.eject</c>); the host refuses
/// anything further away.
/// </summary>
public sealed record IntakeDefinition
{
    /// <summary>Largest reach an intake may have, in m.</summary>
    public const float MaxReach = 16f;

    /// <summary>The middle of the intake, in metres from the part's centre in the part's frame.</summary>
    public Vec3? Position { get; init; }

    /// <summary>The direction the intake faces (where ejected items fly), in the part's frame.</summary>
    public Vec3? Normal { get; init; }

    /// <summary>How far from <see cref="Position"/> the part reaches, in m (more than 0, at most <see cref="MaxReach"/>).</summary>
    public float? Reach { get; init; }

    /// <summary>
    /// How far out along <see cref="Normal"/> an ejected pile lands, in m (0 to <see cref="MaxReach"/>). Omitted means half
    /// of <see cref="Reach"/>.
    /// </summary>
    public float? Throw { get; init; }

    /// <summary>Keys this build does not know.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
