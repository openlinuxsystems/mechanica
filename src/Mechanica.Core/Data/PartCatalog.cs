namespace Mechanica.Core.Data;

/// <summary>Looks up part definitions by id. <see cref="ModLoadResult"/> is the game's catalog.</summary>
public interface IPartCatalog
{
    /// <summary>Finds a definition by <c>mod_id:part_id</c>; a bare id means vanilla.</summary>
    PartDefinition? FindDefinition(string id);
}

/// <summary>An in-memory <see cref="IPartCatalog"/>, for tools and tests that build definitions in code.</summary>
public sealed class PartCatalog : IPartCatalog
{
    private readonly Dictionary<string, PartDefinition> _parts = new(StringComparer.Ordinal);

    /// <summary>Adds or replaces a definition under <paramref name="id"/> (qualified; a bare id means vanilla).</summary>
    public PartCatalog Add(string id, PartDefinition definition)
    {
        _parts[Identifiers.Qualify(id)] = definition;
        return this;
    }

    /// <inheritdoc />
    public PartDefinition? FindDefinition(string id) => _parts.GetValueOrDefault(Identifiers.Qualify(id));
}
