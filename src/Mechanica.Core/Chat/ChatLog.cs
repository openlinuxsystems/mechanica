using Mechanica.Core.Actions;

namespace Mechanica.Core.Chat;

/// <summary>Arguments of <c>SendChat</c>.</summary>
/// <param name="Text">What to say, 1 to <see cref="ChatLog.MaxLength"/> characters.</param>
public sealed record SendChatArgs(string Text);

/// <summary>A line someone said in chat, as every player is sent it.</summary>
/// <param name="Id">Order of the line in the session, from 1.</param>
/// <param name="Peer">Who said it.</param>
/// <param name="Name">Their name at the time.</param>
/// <param name="Text">What they said.</param>
public sealed record ChatMessage(int Id, int Peer, string Name, string Text);

/// <summary>
/// The text chat (UI spec §1 Chat, §11 Multiplayer UI): players say a line with the <c>SendChat</c> action
/// and the host, having checked it, hands it to every player. A line is trimmed, loses control characters
/// and must fit <see cref="MaxLength"/>; each caller may say <see cref="Burst"/> lines per
/// <see cref="WindowSeconds"/>. The host keeps the last <see cref="Kept"/> lines for players who join later.
/// </summary>
/// <param name="nameOf">The name to show for a caller.</param>
/// <param name="now">The host's clock, in seconds.</param>
public sealed class ChatLog(Func<ActionCaller, string> nameOf, Func<double> now)
{
    /// <summary>Action name: say a line.</summary>
    public const string SendChat = "SendChat";

    /// <summary>Longest line, in characters.</summary>
    public const int MaxLength = 200;

    /// <summary>Lines a caller may say within <see cref="WindowSeconds"/>.</summary>
    public const int Burst = 5;

    /// <summary>The rate limit's window.</summary>
    public const double WindowSeconds = 10;

    /// <summary>Lines kept for late joiners.</summary>
    public const int Kept = 50;

    private readonly Dictionary<int, Queue<double>> _recent = [];
    private readonly List<ChatMessage> _lines = [];
    private int _nextId = 1;

    /// <summary>The last lines said, oldest first.</summary>
    public IReadOnlyList<ChatMessage> Lines => _lines;

    /// <summary>Raised on the host when a line is said.</summary>
    public event Action<ChatMessage>? Said;

    /// <summary>Registers <c>SendChat</c>.</summary>
    public void Register(ActionDispatcher dispatcher) =>
        dispatcher.Register<SendChatArgs>(SendChat, Check, (caller, args) =>
        {
            Say(caller, Clean(args.Text));
            return null;
        });

    /// <summary><paramref name="text"/> trimmed, with control characters dropped.</summary>
    public static string Clean(string? text) => new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();

    /// <summary>Forgets a caller's rate limit (when they leave).</summary>
    public void Forget(int peer) => _recent.Remove(peer);

    /// <summary>Takes a line received from the host (client side), keeping <see cref="Lines"/> in step.</summary>
    public void Receive(ChatMessage message)
    {
        if (_lines.Any(l => l.Id == message.Id))
            return;
        Keep(message);
        Said?.Invoke(message);
    }

    private string? Check(ActionCaller caller, SendChatArgs args)
    {
        var text = Clean(args.Text);
        if (text.Length == 0)
            return "Type something to say.";
        if (text.Length > MaxLength)
            return $"A chat line is at most {MaxLength} characters.";
        if (_recent.TryGetValue(caller.Peer, out var times) && times.Count(t => now() - t < WindowSeconds) >= Burst)
            return $"Slow down: at most {Burst} lines every {WindowSeconds:0} seconds.";
        return null;
    }

    private void Say(ActionCaller caller, string text)
    {
        if (!_recent.TryGetValue(caller.Peer, out var times))
            _recent[caller.Peer] = times = new Queue<double>();
        while (times.Count > 0 && now() - times.Peek() >= WindowSeconds)
            times.Dequeue();
        times.Enqueue(now());
        var message = new ChatMessage(_nextId++, caller.Peer, nameOf(caller), text);
        Keep(message);
        Said?.Invoke(message);
    }

    private void Keep(ChatMessage message)
    {
        _lines.Add(message);
        if (_lines.Count > Kept)
            _lines.RemoveAt(0);
    }
}
