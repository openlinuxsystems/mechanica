using System.Numerics;
using Mechanica.Core.Survival;
using Mechanica.Core.World;

namespace Mechanica.Core.Bots;

/// <summary>A point in a POI where a guard bot stands: a <c>bot</c> marker of its prefab.</summary>
/// <param name="Poi">The POI's qualified id (<c>mod_id:poi_id</c>).</param>
/// <param name="Bot">The guard's bot type, qualified.</param>
/// <param name="Feet">Where it stands, in world metres.</param>
/// <param name="Yaw">Which way it faces, in degrees about the vertical (0 faces -Z).</param>
public sealed record GuardPost(string Poi, string Bot, Vector3 Feet, float Yaw);

/// <summary>
/// POI guards, run on the host. Every <c>bot</c> marker of a placed POI is a post: in Survival, when a living
/// player comes within <see cref="BotSettings.GuardWakeDistance"/>, the post's bot appears there (and wanders
/// round it, going for players its behaviour finds). When every player is past
/// <see cref="BotSettings.GuardSleepDistance"/> the guard leaves until someone comes back. A guard that dies
/// leaves its post empty for <see cref="BotSettings.GuardRespawn"/> seconds. A post in a <see cref="BotActions.BotFree"/>
/// zone never fields one.
/// </summary>
public sealed class PoiGuards
{
    /// <summary>The marker type that places a guard.</summary>
    public const string MarkerType = "bot";

    private readonly BotActions _bots;
    private readonly SurvivalActions _survival;
    private readonly Post[] _posts;

    /// <summary>Guards for <paramref name="posts"/>; a post whose bot type does not exist never fields one.</summary>
    public PoiGuards(IEnumerable<GuardPost> posts, BotActions bots, SurvivalActions survival)
    {
        _bots = bots;
        _survival = survival;
        _posts = [.. posts.Select(p => new Post(p))];
    }

    /// <summary>The posts, in world generation order.</summary>
    public IEnumerable<GuardPost> Posts => _posts.Select(p => p.Def);

    /// <summary>The bot on duty at each post (null when none), in <see cref="Posts"/> order.</summary>
    public IEnumerable<int?> OnDuty => _posts.Select(p => p.Bot);

    /// <summary>The guard posts of every POI <paramref name="world"/> placed.</summary>
    public static IReadOnlyList<GuardPost> PostsOf(WorldGenerator world) =>
    [
        .. world.MarkersOfType(MarkerType)
            .Where(m => !string.IsNullOrEmpty(m.Marker.Ref))
            // A bare ref names a bot of the POI's own mod.
            .Select(m => new GuardPost(m.Poi.Poi.QualifiedId, PoiMarker.Qualify(m.Marker.Ref!, m.Poi.Poi.QualifiedId), m.Position,
                m.Yaw * 180f / MathF.PI)),
    ];

    /// <summary>Puts guards on and takes them off duty. The host calls it every physics tick.</summary>
    public void Tick()
    {
        var now = _bots.Now;
        var survival = _survival.Mode == GameMode.Survival;
        var players = survival ? _bots.LivingPlayers().Select(p => p.Centre).ToList() : [];
        foreach (var post in _posts)
        {
            if (post.Bot is { } id)
            {
                var bot = _bots.Find(id);
                if (bot is null || bot.Dead)
                {
                    // Killed (or taken away): the post waits before it fields another.
                    post.Bot = null;
                    post.ReadyAt = (bot?.DiedAt ?? now) + _bots.Settings.GuardRespawn;
                }
                else if (!Near(players, post.Def.Feet, _bots.Settings.GuardSleepDistance))
                {
                    post.Bot = null;
                    _bots.Remove(id);
                }
                continue;
            }
            if (now < post.ReadyAt || !Near(players, post.Def.Feet, _bots.Settings.GuardWakeDistance)
                || _bots.Bots.Count >= _bots.Settings.MaxBots || _bots.Types.FindBot(post.Def.Bot) is not { } type
                || _bots.BotFree.Contains(post.Def.Feet.X, post.Def.Feet.Z, !type.Passive))
                continue;
            post.Bot = _bots.Spawn(type, post.Def.Feet + new Vector3(0, 0.05f, 0), post.Def.Yaw).Id;
        }
    }

    // Horizontally, so a guard on a warehouse's upper floor wakes for a player below.
    private static bool Near(List<Vector3> players, Vector3 point, float distance) =>
        players.Any(p => new Vector2(p.X - point.X, p.Z - point.Z).LengthSquared() <= distance * distance);

    private sealed class Post(GuardPost def)
    {
        public GuardPost Def { get; } = def;

        public int? Bot { get; set; }

        public double ReadyAt { get; set; } = double.NegativeInfinity;
    }
}
