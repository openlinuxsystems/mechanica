using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Net;

namespace Mechanica.Game.Net;

/// <summary>
/// Network front door of the Action API. Any peer calls <see cref="Request"/>; on the host it goes
/// straight to <see cref="Dispatcher"/>, on a client it travels to the host, which dispatches it as the
/// sending peer (the caller identity always comes from the transport, never from the request), and
/// sends the result back. Remote callers are rate limited.
/// </summary>
public partial class ActionGateway : Node
{
    private readonly Dictionary<int, Action<ActionResult>?> _pending = [];
    private readonly ActionRateLimiter _limiter = new();
    private int _nextRequest;

    /// <summary>The session requests travel over. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>The host's dispatcher. Systems register their actions here on every peer; only the host runs them.</summary>
    public ActionDispatcher Dispatcher { get; } = new();

    public override void _Ready()
    {
        Session.StateChanged += OnStateChanged;
        Session.PeerLeft += _limiter.Forget;
    }

    public override void _ExitTree()
    {
        Session.StateChanged -= OnStateChanged;
        Session.PeerLeft -= _limiter.Forget;
    }

    /// <summary>Requests an action with typed arguments. <paramref name="done"/> runs when the host answers.</summary>
    public void Request<TArgs>(string action, TArgs args, Action<ActionResult>? done = null) =>
        RequestJson(action, JsonSerializer.Serialize(args, ActionJson.Options), done);

    /// <summary>Requests an action with JSON arguments. <paramref name="done"/> runs when the host answers.</summary>
    public void RequestJson(string action, string argsJson, Action<ActionResult>? done = null)
    {
        if (Session.IsHost)
        {
            done?.Invoke(Dispatcher.Dispatch(new ActionCaller(Session.LocalPeer, ActionSource.Player), action, argsJson));
            return;
        }
        if (Session.State != NetSession.SessionState.Connected)
        {
            done?.Invoke(ActionResult.Failure(ActionErrors.NotConnected, "Not connected to a host."));
            return;
        }
        var id = ++_nextRequest;
        _pending[id] = done;
        RpcId(NetProtocol.HostPeer, MethodName.SubmitAction, id, action, argsJson);
    }

    /// <summary>Client → host: run an action for the sender.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitAction(int requestId, string action, string argsJson)
    {
        if (Session.State != NetSession.SessionState.Hosting)
            return;
        var peer = Multiplayer.GetRemoteSenderId();
        if (!Session.IsReady(peer))
            return;
        var result = _limiter.TryAcquire(peer, Time.GetTicksMsec() / 1000.0)
            ? Dispatcher.Dispatch(new ActionCaller(peer, ActionSource.Player), action, argsJson)
            : ActionResult.Failure(ActionErrors.RateLimited, "Too many actions; slow down.");
        if (result.Code == ActionErrors.Internal)
            GD.PushError($"[actions] {action} from peer {peer} failed: {result.Message}");
        RpcId(peer, MethodName.ActionDone, requestId, result.ToJson());
    }

    /// <summary>Host → client: the result of a request.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ActionDone(int requestId, string resultJson)
    {
        if (!_pending.Remove(requestId, out var done))
            return;
        done?.Invoke(ActionResult.FromJson(resultJson)
            ?? ActionResult.Failure(ActionErrors.Internal, "The host sent an unreadable result."));
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        if (state != NetSession.SessionState.Offline)
            return;
        var pending = _pending.Values.ToList();
        _pending.Clear();
        foreach (var done in pending)
            done?.Invoke(ActionResult.Failure(ActionErrors.NotConnected, "The connection to the host closed."));
    }
}
