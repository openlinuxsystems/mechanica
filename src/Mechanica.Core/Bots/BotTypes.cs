using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;

namespace Mechanica.Core.Bots;

/// <summary>Arguments of <c>SpawnBot</c>: put a bot in the world.</summary>
/// <param name="Bot">Bot type id (<c>mod_id:bot_id</c>; a bare id means vanilla).</param>
/// <param name="Position">Where its feet go, <c>[x, y, z]</c> metres.</param>
/// <param name="Yaw">Which way it faces, in degrees about the vertical.</param>
public sealed record SpawnBotArgs(string Bot, float[] Position, float Yaw = 0);

/// <summary>Arguments of the actions that name one bot (<c>DespawnBot</c>, <c>GetBot</c>).</summary>
/// <param name="Bot">The bot's id.</param>
public sealed record BotArgs(int Bot);

/// <summary>Arguments of <c>MoveBot</c>: which way a bot walks, until told otherwise.</summary>
/// <param name="Bot">The bot.</param>
/// <param name="Direction">Direction to walk in; only its horizontal part counts. Null stops it.</param>
/// <param name="Speed">Speed in m/s, at most its top speed. Null means top speed.</param>
public sealed record MoveBotArgs(int Bot, Vec3? Direction = null, float? Speed = null);

/// <summary>
/// Arguments of <c>BotAttack</c>. Melee and ranged attacks name exactly one target: a player, a part (a
/// crop's soil counts) or another bot. Area attacks need none.
/// </summary>
/// <param name="Bot">The attacking bot.</param>
/// <param name="Attack">Id of one of its attacks.</param>
/// <param name="Player">Peer of the player to hit.</param>
/// <param name="Creation">Creation holding the part to hit.</param>
/// <param name="Part">The part to hit.</param>
/// <param name="Target">The bot to hit.</param>
public sealed record BotAttackArgs(int Bot, string Attack, int? Player = null, int? Creation = null, int? Part = null, int? Target = null);

/// <summary>Arguments of <c>StartRaid</c>.</summary>
/// <param name="Level">Raid level to send; null picks it from the crops, as a raid check would.</param>
public sealed record StartRaidArgs(int? Level = null);

/// <summary>A bot as <c>GetBots</c> returns it and clients are sent it.</summary>
/// <param name="Id">The bot's id.</param>
/// <param name="Type">Qualified bot type id.</param>
/// <param name="Position">Its feet, in world metres.</param>
/// <param name="Yaw">Which way it faces, in degrees.</param>
/// <param name="Health">Health now.</param>
/// <param name="MaxHealth">Full health.</param>
/// <param name="Dead">True from its death until its body is gone.</param>
/// <param name="Raid">The raid it belongs to, or null.</param>
public sealed record BotView(int Id, string Type, Vec3 Position, float Yaw, float Health, float MaxHealth, bool Dead, int? Raid = null);

/// <summary>Value of a successful <c>SpawnBot</c>.</summary>
/// <param name="Bot">The new bot's id.</param>
public sealed record SpawnBotResult(int Bot);

/// <summary>Value of a successful <c>BotAttack</c>: what it hit (kind <c>player</c>, <c>part</c>, <c>crop</c> or <c>bot</c>).</summary>
/// <param name="Hits">Everything hit.</param>
public sealed record BotAttackResult(IReadOnlyList<ShotHitView> Hits);

/// <summary>Something an attack used, for animation and sound on every peer.</summary>
/// <param name="Bot">The attacker.</param>
/// <param name="Attack">The attack id.</param>
/// <param name="Sound">The sound event it plays.</param>
/// <param name="Hits">What it hit.</param>
public sealed record BotAttackEvent(int Bot, string Attack, string Sound, IReadOnlyList<ShotHitView> Hits);

/// <summary>A planted crop a bot can go for.</summary>
/// <param name="Creation">Creation holding the soil.</param>
/// <param name="Part">The soil part.</param>
/// <param name="Position">Middle of the soil part, in world metres.</param>
/// <param name="Value">What the crop is worth to raids.</param>
/// <param name="Ripe">Whether it can be harvested.</param>
public sealed record CropTarget(int Creation, int Part, Vector3 Position, float Value, bool Ripe);

/// <summary>Where a raid is and how far it has got, as <c>GetRaid</c> returns it and clients are sent it.</summary>
/// <param name="Id">The raid's id (0 when there is none).</param>
/// <param name="Level">Raid level.</param>
/// <param name="Budget">Bot budget after player scaling.</param>
/// <param name="CropValue">The crop value that set it off.</param>
/// <param name="Players">Players it was scaled for.</param>
/// <param name="Phase"><c>none</c>, <c>warning</c> (the horn has sounded) or <c>attacking</c>.</param>
/// <param name="Target">The farm it goes for.</param>
/// <param name="Bots">Raiders planned (warning) or still standing (attacking).</param>
/// <param name="Composition">Bot type ids sent, one entry per bot.</param>
public sealed record RaidView(int Id, int Level, int Budget, float CropValue, int Players, string Phase, Vec3 Target, int Bots, IReadOnlyList<string> Composition)
{
    /// <summary>No raid.</summary>
    public static RaidView None { get; } = new(0, 0, 0, 0, 0, RaidPhases.None, new Vec3(0, 0, 0), 0, []);
}

/// <summary>Values of <see cref="RaidView.Phase"/>.</summary>
public static class RaidPhases
{
    /// <summary>No raid is on.</summary>
    public const string None = "none";

    /// <summary>The horn sounded; the raiders arrive when the warning ends.</summary>
    public const string Warning = "warning";

    /// <summary>The raiders are out.</summary>
    public const string Attacking = "attacking";
}

/// <summary>
/// The bots' share of the damage API: weapons, splash and the Sledgehammer reach bots through it
/// (<see cref="SurvivalActions.Bots"/>). <see cref="BotActions"/> implements it.
/// </summary>
public interface IBotTargets
{
    /// <summary>Middle of a living bot's body, or null.</summary>
    Vector3? BotCentre(int bot);

    /// <summary>Living bots whose middle is within <paramref name="radius"/> of <paramref name="point"/>, with their middles.</summary>
    IEnumerable<(int Bot, Vector3 Centre)> BotsNear(Vector3 point, float radius);

    /// <summary>
    /// Takes health from a bot. <paramref name="melee"/> hits do nothing to melee-immune bots. Returns the
    /// health left (0 when it died).
    /// </summary>
    float DamageBot(int bot, float damage, string cause, bool melee);
}

/// <summary>What the bot actions need from the host's world beyond survival's. The game implements it.</summary>
public interface IBotWorld
{
    /// <summary>The first thing a ray from <paramref name="from"/> to <paramref name="to"/> hits, skipping bot <paramref name="ignoreBot"/>; null when it is clear.</summary>
    RayHit? Cast(Vector3 from, Vector3 to, int ignoreBot);
}

/// <summary>JSON for bot values on the wire (the Action API's options).</summary>
internal static class BotValues
{
    public static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, Actions.ActionJson.Options);
}
