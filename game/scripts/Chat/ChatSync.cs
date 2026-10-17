using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Chat;
using Mechanica.Core.Saves;
using Mechanica.Game.Net;

namespace Mechanica.Game.Chat;

/// <summary>
/// Carries chat between peers. Every peer registers <c>SendChat</c>; the host runs it (<see cref="ChatLog"/>)
/// and sends each line to every ready peer, and the recent lines to a player who joins.
/// </summary>
public partial class ChatSync : Node
{
    /// <summary>The session. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>The lines said, on every peer.</summary>
    public ChatLog Log { get; private set; } = null!;

    /// <summary>Raised on every peer when a line is said.</summary>
    public event Action<ChatMessage>? Said;

    /// <summary>Registers the chat action (every peer registers; only the host runs it).</summary>
    public void RegisterActions(ActionDispatcher dispatcher)
    {
        Log = new ChatLog(caller => PlayerProfile.CleanName(Session.ProfileOf(caller.Peer)?.Name ?? $"Player {caller.Peer}"),
            () => Time.GetTicksMsec() / 1000.0);
        Log.Register(dispatcher);
        Log.Said += OnSaid;
    }

    public override void _Ready()
    {
        Session.PeerReady += SendRecentTo;
        Session.PeerLeft += Log.Forget;
    }

    public override void _ExitTree()
    {
        Session.PeerReady -= SendRecentTo;
        Session.PeerLeft -= Log.Forget;
    }

    private void OnSaid(ChatMessage message)
    {
        Said?.Invoke(message);
        if (Session.State != NetSession.SessionState.Hosting)
            return;
        var json = JsonSerializer.Serialize(message, ActionJson.Options);
        foreach (var peer in Session.ReadyPeers)
            RpcId(peer, MethodName.ClientSaid, json);
    }

    private void SendRecentTo(int peer)
    {
        foreach (var message in Log.Lines)
            RpcId(peer, MethodName.ClientSaid, JsonSerializer.Serialize(message, ActionJson.Options));
    }

    /// <summary>Host → client: a line said.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientSaid(string json)
    {
        if (Session.IsHost || json.Length > 4096)
            return;
        try
        {
            if (JsonSerializer.Deserialize<ChatMessage>(json, ActionJson.Options) is { Text: not null, Name: not null } message)
                Log.Receive(message with { Name = PlayerProfile.CleanName(message.Name), Text = ChatLog.Clean(message.Text) });
        }
        catch (JsonException)
        {
            GD.PushWarning("[chat] bad chat line from the host");
        }
    }
}
