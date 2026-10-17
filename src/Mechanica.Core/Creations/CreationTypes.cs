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

/// <summary>What <see cref="Creation.RemovePart"/> did.</summary>
/// <param name="Removed">The part that was taken out.</param>
/// <param name="SplitOff">New Creations made from pieces that lost their link to the rest, in order of their lowest part id.</param>
public sealed record RemovalResult(PartInstance Removed, IReadOnlyList<Creation> SplitOff);
