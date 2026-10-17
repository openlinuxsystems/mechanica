using Mechanica.Core.Actions;

namespace Mechanica.Core.Characters;

/// <summary>Arguments of <see cref="CharacterActions.ChooseCharacter"/>.</summary>
/// <param name="Character">Character id (a bare id means vanilla).</param>
public sealed record ChooseCharacterArgs(string Character);

/// <summary>
/// The host's record of which character each player in the world chose. A player picks once, the first time
/// they are in a world (on New Game, or when first joining); the choice is saved with the world under their
/// profile and comes back when they return. Until then they show as <see cref="CharacterBook.Default"/>.
/// </summary>
public sealed class CharacterActions(CharacterBook book)
{
    /// <summary>Picks the caller's character for this world. Only once per player per world.</summary>
    public const string ChooseCharacter = "ChooseCharacter";

    private readonly SortedDictionary<int, string> _chosen = [];

    /// <summary>The characters to choose from.</summary>
    public CharacterBook Book => book;

    /// <summary>Who chose what: qualified character id by peer.</summary>
    public IReadOnlyDictionary<int, string> Chosen => _chosen;

    /// <summary>A player's character changed: peer, and the qualified id (null when they have not chosen).</summary>
    public event Action<int, string?>? Changed;

    /// <summary>Registers <see cref="ChooseCharacter"/>.</summary>
    public void Register(ActionDispatcher dispatcher) =>
        dispatcher.Register<ChooseCharacterArgs>(ChooseCharacter, Validate, (caller, args) =>
        {
            Set(caller.Peer, args.Character);
            return null;
        });

    /// <summary>The character a player chose, or null.</summary>
    public string? ChosenBy(int peer) => _chosen.GetValueOrDefault(peer);

    /// <summary>The character a player shows as: their choice, or the default.</summary>
    public Character? ShownAs(int peer) => book.Find(ChosenBy(peer)) ?? book.Default;

    /// <summary>
    /// Host: gives a player back the character saved for them (null or a character no longer installed: none, so
    /// they choose again). Raises <see cref="Changed"/>.
    /// </summary>
    public void Restore(int peer, string? character)
    {
        if (book.Find(character) is { } found)
            _chosen[peer] = found.QualifiedId;
        else
            _chosen.Remove(peer);
        Changed?.Invoke(peer, ChosenBy(peer));
    }

    /// <summary>Host: forgets a player who left.</summary>
    public void Forget(int peer) => _chosen.Remove(peer);

    private string? Validate(ActionCaller caller, ChooseCharacterArgs args)
    {
        if (caller.Source != ActionSource.Player)
            return "Only players choose a character.";
        if (book.Find(args.Character) is null)
            return $"There is no character '{args.Character}'.";
        if (_chosen.TryGetValue(caller.Peer, out var already))
            return $"You already play as {book.Find(already)?.Name ?? already} in this world.";
        return null;
    }

    private void Set(int peer, string character)
    {
        _chosen[peer] = book.Find(character)!.QualifiedId;
        Changed?.Invoke(peer, _chosen[peer]);
    }
}
