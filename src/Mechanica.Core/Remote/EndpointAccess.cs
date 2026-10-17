using System.Security.Cryptography;
using System.Text;
using Mechanica.Core.Actions;

namespace Mechanica.Core.Remote;

/// <summary>
/// Who may use the endpoint and with which <see cref="AgentScope"/>s. With no tokens every connection
/// gets every scope (the endpoint is on loopback). Otherwise a connection starts with none and gains a
/// token's scopes by calling <c>mechanica.auth</c> with it.
/// </summary>
public sealed class EndpointAccess
{
    /// <summary>Every scope.</summary>
    public static readonly IReadOnlySet<AgentScope> All = new HashSet<AgentScope>(Enum.GetValues<AgentScope>());

    private readonly List<(byte[] Token, IReadOnlySet<AgentScope> Scopes)> _tokens = [];

    /// <summary>True when connections must authenticate.</summary>
    public bool RequiresToken => _tokens.Count > 0;

    /// <summary>Adds a token that grants <paramref name="scopes"/>. Empty tokens are ignored.</summary>
    public EndpointAccess AddToken(string? token, params AgentScope[] scopes)
    {
        if (string.IsNullOrEmpty(token))
            return this;
        if (scopes.Length == 0)
            throw new ArgumentException("A token must grant at least one scope.", nameof(scopes));
        _tokens.Add((Encoding.UTF8.GetBytes(token), new HashSet<AgentScope>(scopes)));
        return this;
    }

    /// <summary>The scopes <paramref name="token"/> grants, or null when it matches none.</summary>
    public IReadOnlySet<AgentScope>? ScopesFor(string token)
    {
        var given = Encoding.UTF8.GetBytes(token);
        IReadOnlySet<AgentScope>? found = null;
        // Compare against every token so the time taken doesn't say which one matched.
        foreach (var (expected, scopes) in _tokens)
            if (CryptographicOperations.FixedTimeEquals(given, expected))
                found ??= scopes;
        return found;
    }

    /// <summary>Parses a scope list like <c>"observe,play"</c>. Returns null on an unknown name.</summary>
    public static AgentScope[]? ParseScopes(string text)
    {
        var scopes = new List<AgentScope>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<AgentScope>(part, ignoreCase: true, out var scope) || !Enum.IsDefined(scope))
                return null;
            scopes.Add(scope);
        }
        return scopes.Count == 0 ? null : [.. scopes.Distinct()];
    }
}

/// <summary>Which <see cref="AgentScope"/> an external agent needs for each Action API action.</summary>
public static class AgentPermissions
{
    /// <summary>
    /// Actions only an Admin connection may call: debug spawning and wiping, driving bots, starting raids
    /// and giving items. Everything else that changes state needs Play, and queries need Observe.
    /// </summary>
    public static readonly IReadOnlySet<string> AdminActions = new HashSet<string>(StringComparer.Ordinal)
    {
        "SpawnPart", "ClearCreations", "SpawnBot", "DespawnBot", "MoveBot", "BotAttack", "StartRaid", "GiveItems",
    };

    /// <summary>The scope <paramref name="action"/> needs.</summary>
    public static AgentScope ScopeOf(ActionDispatcher dispatcher, string action) =>
        AdminActions.Contains(action) ? AgentScope.Admin
        : dispatcher.IsReadOnly(action) ? AgentScope.Observe
        : AgentScope.Play;
}
