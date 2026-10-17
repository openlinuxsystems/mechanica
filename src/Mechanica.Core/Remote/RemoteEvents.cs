using System.Buffers;
using System.Text;
using System.Text.Json;
using Mechanica.Core.Actions;

namespace Mechanica.Core.Remote;

/// <summary>
/// Notifications pushed to endpoint connections that subscribed to their topic. Host code calls
/// <see cref="Publish"/>; each subscriber gets
/// <c>{"jsonrpc":"2.0","method":"mechanica.event","params":{"topic":...,"tick":...,"data":...}}</c>
/// the next time the transport drains it. Nothing is built when no one listens to a topic.
/// </summary>
public sealed class RemoteEvents
{
    /// <summary>The notification method name.</summary>
    public const string Method = "mechanica.event";

    /// <summary>Notifications kept per subscriber before the oldest are dropped.</summary>
    public const int MaxPending = 4096;

    /// <summary>The topics that exist, with what each carries.</summary>
    public static readonly IReadOnlyDictionary<string, string> Topics = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["actions"] = "Every state-changing Action API call by players, bots and agents (not Lua scripts): peer, source, action, args, ok, code, message, value.",
        ["messages"] = "Lua prints, warnings and errors from part, item and bot scripts, and host errors from agent calls: kind, source, script, creation, part, text.",
        ["hurt"] = "A player took damage: peer, amount, cause.",
        ["vitals"] = "A player's vitals changed (health, death, respawn): the GetVitals view.",
        ["dialogue"] = "A quest character spoke: quest, speaker, text, choices.",
        ["quest"] = "Quest progress changed: the GetQuests state.",
        ["raid"] = "A raid started, advanced or ended: the GetRaid view.",
        ["bots"] = "A bot spawned, was removed or attacked: kind, bot, plus the attack or bot view.",
        ["drop"] = "An item drop appeared, changed or was picked up: kind, drop.",
        ["chat"] = "A chat line: the chat message.",
        ["fire"] = "A fire went out: the fire view.",
        ["seats"] = "Someone sat down or stood up: seat, seated.",
        ["projectiles"] = "A projectile was launched or landed.",
        ["mods"] = "Mod content was validated or reloaded.",
        ["reports"] = "An agent filed a note, recommendation or bug report.",
        ["world"] = "The world was saved, or a load, new world or reset finished: kind (saved, loaded), plus the world.info status.",
        ["events_dropped"] = "Sent automatically when a subscriber fell behind and notifications were dropped: count.",
    };

    private readonly List<Subscriber> _subscribers = [];

    /// <summary>The current physics tick, stamped on each notification. Null stamps 0.</summary>
    public Func<long>? Clock { get; set; }

    /// <summary>True when any subscriber listens to <paramref name="topic"/>.</summary>
    public bool HasSubscribers(string topic)
    {
        foreach (var subscriber in _subscribers)
            if (subscriber.Topics.Contains(topic))
                return true;
        return false;
    }

    /// <summary>Pushes <paramref name="data"/> (serialized snake_case) to every subscriber of <paramref name="topic"/>.</summary>
    public void Publish(string topic, object? data)
    {
        if (!HasSubscribers(topic))
            return;
        JsonElement payload;
        try
        {
            payload = data is JsonElement element ? element : JsonSerializer.SerializeToElement(data, ActionJson.Options);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            payload = JsonSerializer.SerializeToElement(new { error = $"Unserializable event: {e.Message}" });
        }
        var text = Notification(topic, Clock?.Invoke() ?? 0, payload);
        foreach (var subscriber in _subscribers)
            if (subscriber.Topics.Contains(topic))
                subscriber.Enqueue(text);
    }

    /// <summary>Publishes a lazily built payload; <paramref name="data"/> runs only when someone listens.</summary>
    public void Publish(string topic, Func<object?> data)
    {
        if (HasSubscribers(topic))
            Publish(topic, data());
    }

    /// <summary>
    /// Publishes every state-changing call <paramref name="dispatcher"/> handles to the <c>actions</c> topic,
    /// except Lua scripts' (they run every tick). Dispose the result to stop.
    /// </summary>
    public IDisposable WatchActions(ActionDispatcher dispatcher)
    {
        void OnDispatched(ActionRecord record)
        {
            if (record.Caller.Source == ActionSource.Script || dispatcher.IsReadOnly(record.Action) || !HasSubscribers("actions"))
                return;
            JsonElement? args = null;
            try
            {
                using var document = JsonDocument.Parse(record.ArgsJson, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    MaxDepth = ActionJson.Options.MaxDepth,
                });
                args = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Unparseable arguments are reported without them.
            }
            Publish("actions", new
            {
                peer = record.Caller.Peer,
                source = record.Caller.Source,
                action = record.Action,
                args,
                ok = record.Result.Ok,
                code = record.Result.Code,
                message = record.Result.Message,
                value = record.Result.Value,
            });
        }
        dispatcher.Dispatched += OnDispatched;
        return new Unsubscribe(() => dispatcher.Dispatched -= OnDispatched);
    }

    private sealed class Unsubscribe(Action undo) : IDisposable
    {
        private Action? _undo = undo;

        public void Dispose()
        {
            _undo?.Invoke();
            _undo = null;
        }
    }

    internal Subscriber Add()
    {
        var subscriber = new Subscriber();
        _subscribers.Add(subscriber);
        return subscriber;
    }

    internal void Remove(Subscriber subscriber) => _subscribers.Remove(subscriber);

    internal bool Has(Subscriber subscriber) => _subscribers.Contains(subscriber);

    internal string DroppedNotice(int count) =>
        Notification("events_dropped", Clock?.Invoke() ?? 0, JsonSerializer.SerializeToElement(new { count }));

    private static string Notification(string topic, long tick, JsonElement data)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WriteString("method", Method);
            w.WriteStartObject("params");
            w.WriteString("topic", topic);
            w.WriteNumber("tick", tick);
            w.WritePropertyName("data");
            data.WriteTo(w);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>One connection's topics and undelivered notifications.</summary>
    internal sealed class Subscriber
    {
        private readonly Queue<string> _pending = new();

        public HashSet<string> Topics { get; } = new(StringComparer.Ordinal);

        public int Dropped { get; private set; }

        public int Count => _pending.Count;

        public void Enqueue(string text)
        {
            if (_pending.Count >= MaxPending)
            {
                _pending.Dequeue();
                Dropped++;
            }
            _pending.Enqueue(text);
        }

        public List<string> Drain(RemoteEvents events)
        {
            var list = new List<string>(_pending.Count + 1);
            if (Dropped > 0)
            {
                list.Add(events.DroppedNotice(Dropped));
                Dropped = 0;
            }
            while (_pending.Count > 0)
                list.Add(_pending.Dequeue());
            return list;
        }
    }
}
