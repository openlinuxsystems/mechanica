using Mechanica.Core.Data;

namespace Mechanica.Core.Characters;

/// <summary>
/// The items with a <c>held</c> model, numbered so the network can say which one a player holds in two bytes
/// (<see cref="Player.PlayerInput.Held"/>). Every peer loads the same content, so the numbers agree: the qualified ids
/// in ordinal order from 1; 0 is none.
/// </summary>
public sealed class HeldItems
{
    private readonly List<string> _ids;
    private readonly Dictionary<string, ushort> _index;

    /// <summary>The items of <paramref name="definitions"/> (qualified id, definition) that have a <c>held</c> section.</summary>
    public HeldItems(IEnumerable<KeyValuePair<string, PartDefinition>> definitions)
    {
        _ids = [.. definitions.Where(d => d.Value.Held is not null && !d.Value.IsPlaceable).Select(d => d.Key)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(ushort.MaxValue)];
        _index = _ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => (ushort)(x.i + 1), StringComparer.Ordinal);
    }

    /// <summary>No held models.</summary>
    public static HeldItems Empty { get; } = new([]);

    /// <summary>How many items have a held model.</summary>
    public int Count => _ids.Count;

    /// <summary>The number of <paramref name="id"/> (qualified), or 0 when it has no held model.</summary>
    public ushort IndexOf(string? id) => id is not null && _index.TryGetValue(id, out var index) ? index : (ushort)0;

    /// <summary>The item numbered <paramref name="index"/>, or null for 0 or a number out of range.</summary>
    public string? IdOf(ushort index) => index >= 1 && index <= _ids.Count ? _ids[index - 1] : null;
}
