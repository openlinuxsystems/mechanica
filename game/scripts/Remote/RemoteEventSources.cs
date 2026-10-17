using System.Numerics;
using Mechanica.Core.Remote;
using Mechanica.Core.Survival;
using Mechanica.Scripting;

namespace Mechanica.Game.Remote;

/// <summary>
/// Connects the host's systems to the AI endpoint's <see cref="RemoteEvents"/> topics: damage and vitals,
/// dialogue and quests, raids and bots, drops, chat, fire, seats, projectiles and script messages. Every
/// payload is built only when an agent listens to its topic.
/// </summary>
public static class RemoteEventSources
{
    /// <summary>Hooks <paramref name="main"/>'s systems up to <paramref name="events"/>. Call once, after every system registered its actions.</summary>
    public static void Connect(Main main, RemoteEvents events)
    {
        if (main.Survival.Actions is { } survival)
        {
            survival.Hurt += (peer, amount, cause) => events.Publish("hurt", () => new { peer, amount, cause });
            survival.VitalsChanged += peer => events.Publish("vitals", () => survival.View(peer));
            survival.DropChanged += drop => events.Publish("drop", () => new { kind = "changed", drop });
            survival.DropRemoved += id => events.Publish("drop", () => new { kind = "removed", drop = new { id } });
            survival.ProjectileLaunched += view => events.Publish("projectiles", () => new { kind = "launched", projectile = view });
            survival.ProjectileLanded += landing => events.Publish("projectiles", () => new
            {
                kind = "landed",
                projectile = new
                {
                    id = landing.Id,
                    owner = landing.Owner,
                    point = Array(landing.Point),
                    hit = landing.Hit is { } hit
                        ? new { player = hit.Player, creation = hit.Creation, part = hit.Part, @object = hit.Object?.ToString(), bot = hit.Bot }
                        : null,
                },
            });
        }

        main.Quests.Tracker.Said += dialogue => events.Publish("dialogue", () => dialogue);
        main.Quests.Tracker.Changed += () => events.Publish("quest", () => main.Quests.Tracker.State.ToElement());

        if (main.Bots.Raids is { } raids)
            raids.Changed += raid => events.Publish("raid", () => raid);
        if (main.Bots.Bots is { } bots)
        {
            bots.Spawned += bot => events.Publish("bots", () => new { kind = "spawned", bot = bot.View });
            bots.Removed += id => events.Publish("bots", () => new { kind = "removed", bot = new { id } });
            bots.Attacked += attack => events.Publish("bots", () => new { kind = "attacked", attack });
            bots.CropDestroyed += (creation, part, bot) => events.Publish("bots", () => new { kind = "crop_destroyed", creation, part, bot });
        }
        if (main.Bots.Runtime is { } brains)
            brains.Message += message => PublishMessage(events, "bot", message);

        main.Chat.Log.Said += message => events.Publish("chat", () => message);
        if (main.Fire.Actions is { } fire)
            fire.SiteOut += view => events.Publish("fire", () => new { kind = "out", fire = view });
        if (main.Logic.Actions is { } logic)
            logic.SeatChanged += (seat, seated) => events.Publish("seats", () => new { seat, seated });

        main.Scripts.Runtime.Message += message => PublishMessage(events, "script", message);
    }

    private static void PublishMessage(RemoteEvents events, string source, ScriptMessage message) =>
        events.Publish("messages", () => new
        {
            kind = message.Kind,
            source,
            script = message.Script,
            creation = message.Creation,
            part = message.Part,
            text = message.Text,
            tick = message.Tick,
        });

    private static float[] Array(Vector3 v) => [v.X, v.Y, v.Z];
}
