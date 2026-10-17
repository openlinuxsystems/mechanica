using System.Text.Json;
using Mechanica.Core.Data;

namespace Mechanica.Core.Creations;

/// <summary>
/// One placed part in a <see cref="Creation"/>. The part's local cell (0, 0, 0) lands on
/// <see cref="Cell"/>, and the rest of its footprint is rotated by <see cref="Rotation"/> about that cell.
/// </summary>
/// <param name="Id">Id, unique within the Creation and kept when the Creation splits.</param>
/// <param name="Part">Qualified definition id, <c>mod_id:part_id</c>.</param>
/// <param name="Cell">Grid cell of the part's local origin cell, in the Creation's grid frame.</param>
/// <param name="Rotation">Orientation on the grid.</param>
public sealed record PartInstance(int Id, string Part, GridCell Cell, GridRotation Rotation)
{
    /// <summary>Paint Tool colour, lowercase <c>"#rrggbb"</c>; null shows the part's own colour.</summary>
    public string? Color { get; init; }

    /// <summary>
    /// Settings changed from the part's defaults (<see cref="PartDefinition.Settings"/>), keyed by setting id in
    /// ordinal order; null when every setting has its default. See <see cref="Logic.PartSettings"/>.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? Settings { get; init; }

    /// <summary>Upgrade level (UI spec §4): 0 is the part as written, up to its <see cref="Data.PartDefinition.Upgrades"/> count.</summary>
    public int Level { get; init; }

    /// <summary>Keys this build does not know, kept so a save round-trips without losing data.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>How two parts are joined. Only structural links are rigid; the rest become Jolt joints (D7).</summary>
public enum ConnectionType
{
    /// <summary>Rigid attachment; both parts belong to the same Body.</summary>
    Structural,

    /// <summary>Hinge joint (bearing).</summary>
    Rotational,

    /// <summary>Slider joint (piston).</summary>
    Linear,

    /// <summary>Spring joint (suspension).</summary>
    Spring,
}

/// <summary>
/// An edge of the Creation graph. Structural edges are stored with <see cref="A"/> &lt; <see cref="B"/>
/// and no sockets. Joint edges put the part that owns the joint socket in <see cref="A"/>, with that
/// socket in <see cref="ASocket"/>; <see cref="BSocket"/> names the other part's socket when it has one.
/// Use <see cref="Rigid"/> and <see cref="Joint"/> to build normalized edges.
/// </summary>
public sealed record Connection(ConnectionType Type, int A, int B, string? ASocket = null, string? BSocket = null)
{
    /// <summary>A rigid edge between two parts, in normalized order.</summary>
    public static Connection Rigid(int a, int b) =>
        new(ConnectionType.Structural, Math.Min(a, b), Math.Max(a, b));

    /// <summary>A joint edge from <paramref name="owner"/>'s joint socket to <paramref name="other"/>.</summary>
    public static Connection Joint(ConnectionType type, int owner, string ownerSocket, int other, string? otherSocket = null) =>
        type == ConnectionType.Structural
            ? throw new ArgumentException("Use Rigid for structural connections.", nameof(type))
            : new(type, owner, other, ownerSocket, otherSocket);

    /// <summary>True when this edge touches <paramref name="partId"/>.</summary>
    public bool Involves(int partId) => A == partId || B == partId;

    internal static readonly IComparer<Connection> Order = Comparer<Connection>.Create((x, y) =>
    {
        var c = x.A.CompareTo(y.A);
        if (c == 0) c = x.B.CompareTo(y.B);
        if (c == 0) c = x.Type.CompareTo(y.Type);
        if (c == 0) c = string.CompareOrdinal(x.ASocket, y.ASocket);
        if (c == 0) c = string.CompareOrdinal(x.BSocket, y.BSocket);
        return c;
    });
}

/// <summary>
/// A Connect Tool link of the logic and power bus (chunk 10): the output of part <see cref="From"/> feeds an
/// input of part <see cref="To"/>, in the same Creation. Links are not physical; they never join Bodies.
/// </summary>
/// <param name="From">The part whose output it carries (a seat, a switch, an engine).</param>
/// <param name="To">The part it feeds (an engine, a bearing, a piston, a logic gate).</param>
public sealed record LogicLink(int From, int To)
{
    internal static readonly IComparer<LogicLink> Order = Comparer<LogicLink>.Create((x, y) =>
        x.From != y.From ? x.From.CompareTo(y.From) : x.To.CompareTo(y.To));
}

/// <summary>
/// A set of rigidly attached parts that the game simulates as one Jolt rigid body with a compound
/// shape (D7). Derived from the graph; indices are only stable until the graph changes.
/// </summary>
/// <param name="Index">Position in <see cref="Creation.Bodies"/>; bodies are ordered by their lowest part id.</param>
/// <param name="PartIds">Member parts, ascending.</param>
/// <param name="Mass">Summed part mass in kg.</param>
/// <param name="CentreOfMass">Mass-weighted centre of the parts' footprints, in metres in the Creation's grid frame.</param>
public sealed record Body(int Index, IReadOnlyList<int> PartIds, float Mass, Vec3 CentreOfMass);

/// <summary>A joint edge resolved to the two Bodies it joins.</summary>
/// <param name="Connection">The graph edge.</param>
/// <param name="BodyA">Body of <see cref="Connection.A"/>, the joint part's own side.</param>
/// <param name="BodyB">Body of <see cref="Connection.B"/>.</param>
public sealed record JointLink(Connection Connection, int BodyA, int BodyB)
{
    /// <summary>True when both sides ended up in one Body, so the joint cannot move.</summary>
    public bool IsLocked => BodyA == BodyB;
}

/// <summary>
/// A joint edge's geometry and data in the Creation's grid frame (<see cref="Creation.TryGetJointFrame"/>).
/// Positions along the joint are measured from the pose both sides were built in on the grid: an
/// angle in rad about <see cref="Axis"/> (right-handed), or a distance in m along it.
/// </summary>
/// <param name="Type">Rotational, linear or spring.</param>
/// <param name="Anchor">The joint socket's point, in m.</param>
/// <param name="Axis">Unit grid direction the joint turns about or slides along.</param>
/// <param name="Normal">The joint socket's outward normal.</param>
/// <param name="Min">Lowest position (rad or m); null on a free-spinning hinge.</param>
/// <param name="Max">Highest position (rad or m); null on a free-spinning hinge.</param>
/// <param name="Stiffness">Spring rate in N/m, for spring joints.</param>
/// <param name="Damping">Spring damping in N·s/m, for spring joints.</param>
/// <param name="ShaftRadius">Radius of the rod drawn along a linear or spring joint, or null for none.</param>
public sealed record JointFrame(ConnectionType Type, Vec3 Anchor, Vec3 Axis, Vec3 Normal, float? Min, float? Max,
    float? Stiffness, float? Damping, float? ShaftRadius = null)
{
    /// <summary>True for a hinge with no limits.</summary>
    public bool SpinsFreely => Type == ConnectionType.Rotational && (Min is null || Max is null);

    /// <summary>Clamps a position into the joint's range (unchanged on a free hinge).</summary>
    public float Clamp(float position) => SpinsFreely ? position : Math.Clamp(position, Min ?? position, Max ?? position);
}

/// <summary>What <see cref="Creation.RemovePart"/> did.</summary>
/// <param name="Removed">The part that was taken out.</param>
/// <param name="SplitOff">New Creations made from pieces that lost their link to the rest, in order of their lowest part id.</param>
public sealed record RemovalResult(PartInstance Removed, IReadOnlyList<Creation> SplitOff);
