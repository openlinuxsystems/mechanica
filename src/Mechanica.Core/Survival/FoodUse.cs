using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Data;

namespace Mechanica.Core.Survival;

/// <summary>
/// How a food item is eaten (consumables batch C2), from the two keys of its <c>properties</c> that the game's eat
/// animation reads as well as its script (<c>food.lua</c>): <c>drink</c> (true for a drink: the drink clip and sound play
/// instead of the eat clip's bites, and the player may sprint meanwhile) and <c>eat_seconds</c> (how long one takes,
/// click to click; the clip's own length when unset). Values that are not a true boolean or a number from more than 0 to
/// <see cref="MaxSeconds"/> count as unset.
/// </summary>
/// <param name="Drink">True for a drink.</param>
/// <param name="Seconds">Seconds one takes, or null for the length of its clip.</param>
public sealed record FoodUse(bool Drink, float? Seconds)
{
    /// <summary>The longest <c>eat_seconds</c> may be.</summary>
    public const float MaxSeconds = 10f;

    /// <summary>Food with neither key: eaten in bites, as long as the eat clip.</summary>
    public static FoodUse Default { get; } = new(false, null);

    /// <summary>How <paramref name="definition"/> is eaten; <see cref="Default"/> for null or no properties.</summary>
    public static FoodUse Of(PartDefinition? definition)
    {
        if (definition?.Properties is not { } properties)
            return Default;
        var drink = properties["drink"] is JsonValue d && d.GetValueKind() == JsonValueKind.True;
        float? seconds = properties["eat_seconds"] is JsonValue s && s.GetValueKind() == JsonValueKind.Number
            && s.GetValue<double>() is var v && v is > 0 and <= MaxSeconds ? (float)v : null;
        return drink || seconds is not null ? new FoodUse(drink, seconds) : Default;
    }

    /// <summary>Seconds one takes: <see cref="Seconds"/>, or <paramref name="clipSeconds"/> (the eat or drink clip's length).</summary>
    public float SecondsOr(float clipSeconds) => Seconds ?? clipSeconds;
}
