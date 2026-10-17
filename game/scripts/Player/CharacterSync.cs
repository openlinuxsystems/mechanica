using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Characters;
using Mechanica.Game.Net;
using Mechanica.Game.Sound;

namespace Mechanica.Game.Player;

/// <summary>
/// Which character each player plays as, and drawing them. The host keeps the choices
/// (<see cref="CharacterActions"/>: one pick per player per world, saved with the world) and tells every client;
/// each peer then draws every player as a <see cref="CharacterModel"/>. A player who has not chosen in this world
/// shows as the default character and is asked to choose: the host at once, a client when the host says so on
/// joining. New game passes the host's pick in <see cref="InitialChoice"/>, so the host is not asked again.
/// </summary>
public partial class CharacterSync : Node
{
    private readonly Dictionary<int, string?> _known = [];
    private readonly Dictionary<int, float> _health = [];
    private CharacterChoiceWindow? _window;

    /// <summary>The session. Set before adding to the tree.</summary>
    public NetSession Session { get; init; } = null!;

    /// <summary>Players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>Models, clips and props. Set before adding to the tree.</summary>
    public CharacterAssets Assets { get; init; } = null!;

    /// <summary>Where the characters' sounds play; null plays none.</summary>
    public SoundSync? Sounds { get; init; }

    /// <summary>Requests go here (choosing). Set before adding to the tree.</summary>
    public ActionGateway Actions { get; init; } = null!;

    /// <summary>The host's record of choices. Set before adding to the tree.</summary>
    public CharacterActions Choices { get; init; } = null!;

    /// <summary>Health, so a player flinches when hurt. Optional.</summary>
    public Survival.SurvivalSync? Survival { get; init; }

    /// <summary>The host's pick from New game, applied when the world starts if the host has none yet.</summary>
    public string? InitialChoice { get; set; }

    /// <summary>False in automated tests: no window asks the local player to choose.</summary>
    public bool ReadDeviceInput { get; init; } = true;

    /// <summary>The characters to choose from.</summary>
    public CharacterBook Book => Choices.Book;

    /// <summary>True while the local player has not chosen a character in this world.</summary>
    public bool LocalNeedsChoice { get; private set; }

    /// <summary>The open choice window, or null.</summary>
    public CharacterChoiceWindow? Window => _window;

    /// <summary>Raised when the local player needs to choose (or no longer does).</summary>
    public event Action<bool>? LocalNeedsChoiceChanged;

    /// <summary>Registers the choosing action. Call on every peer before the session starts.</summary>
    public void RegisterActions(ActionDispatcher dispatcher) => Choices.Register(dispatcher);

    public override void _Ready()
    {
        Choices.Changed += OnChanged;
        Players.PlayerSpawned += OnSpawned;
        Players.PlayerDespawning += OnDespawning;
        Session.StateChanged += OnStateChanged;
        if (Survival is not null)
            Survival.VitalsChanged += OnVitals;
        foreach (var player in Players.Players.Values)
            OnSpawned(player);
        if (Session.IsHost)
            CallDeferred(MethodName.StartHost);
    }

    public override void _ExitTree()
    {
        Choices.Changed -= OnChanged;
        Players.PlayerSpawned -= OnSpawned;
        Players.PlayerDespawning -= OnDespawning;
        Session.StateChanged -= OnStateChanged;
        if (Survival is not null)
            Survival.VitalsChanged -= OnVitals;
        Assets.Dispose();
    }

    // Every peer sees every player's health, so each one makes a hurt player flinch on its own.
    private void OnVitals(Core.Survival.VitalsView view)
    {
        var hurt = _health.TryGetValue(view.Peer, out var before) && view.Health < before - 0.01f && !view.Dead;
        _health[view.Peer] = view.Health;
        if (hurt && Players.Players.TryGetValue(view.Peer, out var player) && player.Character is CharacterModel model)
            model.Animator.Play(CharacterGesture.Hurt);
    }

    /// <summary>The character a player is drawn as now.</summary>
    public Character? ShownAs(int peer) => Session.IsHost ? Choices.ShownAs(peer) : Book.Find(_known.GetValueOrDefault(peer)) ?? Book.Default;

    /// <summary>Asks the host to make <paramref name="character"/> the local player's character in this world.</summary>
    public void Choose(string character, Action<ActionResult>? done = null) =>
        Actions.Request(CharacterActions.ChooseCharacter, new ChooseCharacterArgs(character), result =>
        {
            if (!result.Ok)
                GD.PushWarning($"[characters] {result.Message}");
            done?.Invoke(result);
        });

    // The host's own player: the pick from New game, else a saved one (restored by the save), else ask.
    private void StartHost()
    {
        if (Choices.ChosenBy(Session.LocalPeer) is null && InitialChoice is { } pick && Book.Find(pick) is not null)
            Choose(pick);
        InitialChoice = null;
        SetLocalNeedsChoice(Choices.ChosenBy(Session.LocalPeer) is null);
    }

    private void OnChanged(int peer, string? character)
    {
        Show(peer);
        if (peer == Session.LocalPeer)
            SetLocalNeedsChoice(character is null);
        if (Session.State != NetSession.SessionState.Hosting)
            return;
        foreach (var other in Session.ReadyPeers)
            RpcId(other, MethodName.ClientCharacter, peer, character ?? "");
    }

    private void OnSpawned(PlayerController player)
    {
        Show(player.PeerId);
        if (!Session.IsHost || player.PeerId == Session.LocalPeer)
            return;
        // After every other arrival handler, so a saved character is restored before the newcomer hears.
        Callable.From(() => Welcome(player.PeerId)).CallDeferred();
    }

    // Host → newcomer: every player's character, its own last (empty when it must choose).
    private void Welcome(int peer)
    {
        if (Session.State != NetSession.SessionState.Hosting || !Players.Players.ContainsKey(peer))
            return;
        foreach (var other in Players.Players.Keys.Where(p => p != peer))
            RpcId(peer, MethodName.ClientCharacter, other, Choices.ChosenBy(other) ?? "");
        RpcId(peer, MethodName.ClientCharacter, peer, Choices.ChosenBy(peer) ?? "");
        foreach (var other in Session.ReadyPeers.Where(p => p != peer))
            RpcId(other, MethodName.ClientCharacter, peer, Choices.ChosenBy(peer) ?? "");
    }

    private void OnDespawning(PlayerController player)
    {
        _known.Remove(player.PeerId);
        _health.Remove(player.PeerId);
        if (Session.IsHost)
            Choices.Forget(player.PeerId);
    }

    private void OnStateChanged(NetSession.SessionState state)
    {
        if (state == NetSession.SessionState.Connecting)
        {
            _known.Clear();
            SetLocalNeedsChoice(false);
        }
    }

    /// <summary>Host → client: <paramref name="peer"/> plays as <paramref name="character"/> (empty: has not chosen).</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientCharacter(int peer, string character)
    {
        if (Session.IsHost)
            return;
        _known[peer] = character.Length == 0 ? null : character;
        Show(peer);
        if (peer == Session.LocalPeer)
            SetLocalNeedsChoice(character.Length == 0);
    }

    // Draws a player as the character it plays as now.
    private void Show(int peer)
    {
        if (!Players.Players.TryGetValue(peer, out var player))
            return;
        var character = ShownAs(peer);
        if (player.Character is CharacterModel current && current.Character == character)
            return;
        if (character is null || Assets.Instantiate(character) is not { } model)
        {
            player.SetCharacter(null);
            player.SetArms(null);
            return;
        }
        player.SetCharacter(new CharacterModel { Player = player, Character = character, Assets = Assets, Model = model, Sounds = Sounds });
        // Only the local player looks out of its own eyes, so only it has first-person arms.
        player.SetArms(peer == Session.LocalPeer && Assets.Arms(character) is { } arms
            ? new CharacterModel { Player = player, Character = character, Assets = Assets, Model = arms, FirstPerson = true }
            : null);
    }

    private void SetLocalNeedsChoice(bool needs)
    {
        if (needs && Book.All.Count == 0)
            needs = false;
        if (LocalNeedsChoice == needs && (_window is not null) == (needs && ReadDeviceInput))
            return;
        LocalNeedsChoice = needs;
        if (needs && ReadDeviceInput && _window is null)
        {
            _window = new CharacterChoiceWindow { Name = "CharacterChoice", Book = Book, Assets = Assets };
            _window.Chosen += id => Choose(id);
            AddChild(_window);
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (!needs && _window is not null)
        {
            _window.QueueFree();
            _window = null;
        }
        LocalNeedsChoiceChanged?.Invoke(needs);
    }
}
