using Mechanica.Core.Actions;
using Mechanica.Core.Chat;

namespace Mechanica.Core.Tests.Chat;

public class ChatLogTests
{
    private static readonly ActionCaller Ada = new(2, ActionSource.Player);
    private static readonly ActionCaller Bo = new(3, ActionSource.Player);
    private double _now;
    private readonly ActionDispatcher _dispatcher = new();
    private readonly ChatLog _chat;
    private readonly List<ChatMessage> _said = [];

    public ChatLogTests()
    {
        _chat = new ChatLog(c => c.Peer == 2 ? "Ada" : "Bo", () => _now);
        _chat.Register(_dispatcher);
        _chat.Said += _said.Add;
    }

    private ActionResult Say(ActionCaller caller, string text) => _dispatcher.Dispatch(caller, ChatLog.SendChat, new SendChatArgs(text));

    [Fact]
    public void ALineGoesToEveryoneWithTheSpeakersName()
    {
        Assert.True(Say(Ada, "  hello\u0007 there ").Ok);
        Assert.Equal(new ChatMessage(1, 2, "Ada", "hello there"), Assert.Single(_said));
        Assert.Equal(_said, _chat.Lines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyLinesAreRefused(string text) => Assert.False(Say(Ada, text).Ok);

    [Fact]
    public void LongLinesAreRefused()
    {
        Assert.True(Say(Ada, new string('a', ChatLog.MaxLength)).Ok);
        Assert.False(Say(Ada, new string('a', ChatLog.MaxLength + 1)).Ok);
    }

    [Fact]
    public void EachPlayerHasARateLimit()
    {
        for (var i = 0; i < ChatLog.Burst; i++)
            Assert.True(Say(Ada, $"line {i}").Ok);
        var refused = Say(Ada, "one more");
        Assert.False(refused.Ok);
        Assert.Contains("Slow down", refused.Message);
        Assert.True(Say(Bo, "me too").Ok);
        _now += ChatLog.WindowSeconds;
        Assert.True(Say(Ada, "later").Ok);
    }

    [Fact]
    public void OnlyTheLastLinesAreKept()
    {
        for (var i = 0; i < ChatLog.Kept + 5; i++)
        {
            _now += ChatLog.WindowSeconds;
            Say(Ada, $"line {i}");
        }
        Assert.Equal(ChatLog.Kept, _chat.Lines.Count);
        Assert.Equal("line 5", _chat.Lines[0].Text);
    }

    [Fact]
    public void AClientTakesEachLineOnce()
    {
        var client = new ChatLog(_ => "", () => 0);
        var heard = new List<ChatMessage>();
        client.Said += heard.Add;
        var line = new ChatMessage(4, 2, "Ada", "hi");
        client.Receive(line);
        client.Receive(line);
        Assert.Equal([line], heard);
    }
}
