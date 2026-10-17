using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using Mechanica.Core.Net;
using Mechanica.Game.Creations;
using Mechanica.Game.Mods;
using Mechanica.Game.Player;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Editor;

/// <summary>
/// The Test Sandbox (chunk 5): the flat test world with a part spawner and a bot spawner, for trying parts
/// straight from the Part Editor and bots straight from the Bot Behavior Editor. It can simulate
/// multiplayer on one machine: "Add local client" starts the world hosting on localhost and joins up to <see cref="MaxLocalClients"/> extra game instances to it, each
/// with its own physics world and network peer, shown in small views. Tab switches which instance you
/// control and see full size. Spawning goes through the Action API from whichever instance you control,
/// so a spawn from a client is validated by the host and appears for everyone (D10).
/// <para>
/// Each instance is a full <see cref="Main"/> scene in its own <see cref="SubViewport"/> with its own
/// <see cref="MultiplayerApi"/>, as in the multiplayer test harness, so node paths and RPC routes match.
/// </para>
/// </summary>
public partial class SandboxScreen : Control
{
    /// <summary>Most local clients the sandbox will start.</summary>
    public const int MaxLocalClients = 3;

    private const string MainScene = "res://scenes/Main.tscn";
    private static readonly Vector2I PaneSize = new(320, 180);

    private readonly List<Instance> _instances = [];
    private Control _views = null!;
    private TextureRect _mainView = null!;
    private VBoxContainer _panes = null!;
    private ItemList _parts = null!;
    private LineEdit _filter = null!;
    private Label _status = null!;
    private Label _result = null!;
    private CheckBox _collisionOverlay = null!;
    private CheckBox _jointOverlay = null!;
    private CheckBox _forceOverlay = null!;
    private List<string> _partIds = [];
    private OptionButton _bots = null!;
    private List<string> _botIds = [];
    private int _active;
    private int _port;
    private LocalPlayerInput? _input;
    private ModLoadResult _content = null!;
    private Node _overlayRoot = null!;

    /// <summary>One game instance in the sandbox.</summary>
    /// <param name="Name">Display name (Host, Client 1, …).</param>
    /// <param name="Viewport">Its own world and multiplayer root.</param>
    /// <param name="Main">Its game scene.</param>
    public sealed record Instance(string Name, SubViewport Viewport, Main Main);

    /// <summary>The sandbox's panel: its X closes the sandbox, and so does Esc once the mouse is free.</summary>
    public ToolWindow Window { get; private set; } = null!;

    /// <summary>Raised when the user leaves the sandbox.</summary>
    public event Action? BackRequested;

    /// <summary>Loads the mods every instance runs. Defaults to the player's enabled mods.</summary>
    public Func<ModLoadResult> LoadContent { get; set; } = ModPaths.LoadContent;

    /// <summary>Running instances, host first.</summary>
    public IReadOnlyList<Instance> Instances => _instances;

    /// <summary>The instance you control.</summary>
    public Instance Active => _instances[_active];

    /// <summary>The host instance.</summary>
    public Instance Host => _instances[0];

    /// <summary>Placeable part ids the spawner lists.</summary>
    public IReadOnlyList<string> PartIds => _partIds;

    /// <summary>Bot type ids the bot spawner lists.</summary>
    public IReadOnlyList<string> BotIds => _botIds;

    /// <summary>The last action result shown, for tests.</summary>
    public string LastResult => _result.Text;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _views = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _views.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_views);
        _overlayRoot = new Node { Name = "Instances" };
        AddChild(_overlayRoot);
        _mainView = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
        _mainView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _views.AddChild(_mainView);
        _panes = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _panes.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        _panes.Position = new Vector2(-PaneSize.X - 12, 12);
        _panes.GrowHorizontal = GrowDirection.Begin;
        _views.AddChild(_panes);
        BuildPanel();

        _content = LoadContent();
        StartInstance("Host", LaunchOptions.Offline with { GameMode = Core.Survival.GameMode.Creative });
        Activate(0);
        RefreshParts();
    }

    public override void _Process(double delta)
    {
        // The first Esc frees the mouse (the player's own), the next one closes the sandbox.
        Window.CloseOnEscape = Input.MouseMode != Input.MouseModeEnum.Captured;
        var size = (Vector2I)GetViewportRect().Size;
        for (var i = 0; i < _instances.Count; i++)
        {
            var want = i == _active ? new Vector2I(Math.Max(size.X, 64), Math.Max(size.Y, 64)) : PaneSize;
            if (_instances[i].Viewport.Size != want)
                _instances[i].Viewport.Size = want;
        }
        if (_collisionOverlay.ButtonPressed)
            CollisionOverlay.Apply(Active.Main.Creations);
        for (var i = 0; i < _instances.Count; i++)
        {
            _instances[i].Main.JointOverlay.Visible = _jointOverlay.ButtonPressed && i == _active;
            _instances[i].Main.ForceOverlay.Enabled = _forceOverlay.ButtonPressed && i == _active;
        }
        _status.Text = string.Join("\n", _instances.Select((inst, i) =>
            (i == _active ? "▶ " : "  ") + Loc.T("{0}: {1}, peer {2}, {3} players, {4} creations",
                inst.Name, inst.Main.Session.State, inst.Main.Session.LocalPeer, inst.Main.PlayerSync.Players.Count, inst.Main.Creations.All.Count())));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsVisibleInTree())
            return;
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab })
        {
            Activate((_active + 1) % _instances.Count);
            GetViewport().SetInputAsHandled();
            return;
        }
        // The world views are textures, so forward input to the instance being controlled.
        Active.Viewport.PushInput(@event, inLocalCoords: true);
    }

    public override void _ExitTree()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        foreach (var instance in _instances.ToList())
            instance.Main.Session.Leave();
    }

    /// <summary>Leaves the sandbox, as the Back button does.</summary>
    public void EmitBack() => BackRequested?.Invoke();

    /// <summary>Reloads mods in every instance, so a part just exported from the editor can be spawned.</summary>
    public void ReloadContent()
    {
        _content = LoadContent();
        foreach (var instance in _instances)
            instance.Main.ReloadMods(_content);
        RefreshParts();
        var errors = _content.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        _result.Text = errors == 0 ? Loc.T("Mods reloaded: {0} parts.", _content.Parts.Count) : Loc.T("Mods reloaded with {0} error(s); see the log.", errors);
        foreach (var d in _content.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            GD.PushWarning($"[sandbox] {d}");
    }

    /// <summary>
    /// Starts hosting on localhost if needed and joins one more local client. Returns the new instance,
    /// or null when the limit is reached or hosting failed.
    /// </summary>
    public Instance? AddLocalClient()
    {
        if (_instances.Count > MaxLocalClients)
            return null;
        if (Host.Main.Session.State == Net.NetSession.SessionState.Offline && !TryHost())
            return null;
        var client = StartInstance($"Client {_instances.Count}", LaunchOptions.ForJoin(new NetEndpoint("127.0.0.1", _port)));
        return client;
    }

    /// <summary>Disconnects and removes the newest local client.</summary>
    public void RemoveLocalClient()
    {
        if (_instances.Count <= 1)
            return;
        var last = _instances[^1];
        if (_active == _instances.Count - 1)
            Activate(0);
        last.Main.Session.Leave();
        _instances.Remove(last);
        _overlayRoot.RemoveChild(last.Viewport);
        last.Viewport.QueueFree();
        RebuildPanes();
    }

    /// <summary>Controls and shows <paramref name="index"/> full size.</summary>
    public void Activate(int index)
    {
        _active = Math.Clamp(index, 0, _instances.Count - 1);
        _input?.QueueFree();
        _input = null;
        if (Active.Main.LocalPlayer is { } player)
        {
            _input = new LocalPlayerInput { Name = "SandboxInput", Target = player };
            Active.Main.AddChild(_input);
        }
        RebuildPanes();
    }

    /// <summary>
    /// Asks the controlled instance's host to spawn <paramref name="partId"/> a little in front of the
    /// controlled player. <paramref name="done"/> gets the result.
    /// </summary>
    public void Spawn(string partId, Action<ActionResult>? done = null)
    {
        var player = Active.Main.LocalPlayer;
        var origin = player?.GlobalPosition ?? Vector3.Zero;
        var forward = player is null ? Vector3.Forward : -player.GlobalBasis.Z with { Y = 0 };
        forward = forward.LengthSquared() > 0 ? forward.Normalized() : Vector3.Forward;
        var size = (_content.FindDefinition(partId)?.Size ?? GridSize.One).Metres;
        var at = origin + forward * (2f + size.Length / 2) + Vector3.Up * (size.Y / 2 + 0.5f);
        var yaw = Mathf.RadToDeg(player?.Rotation.Y ?? 0);
        Active.Main.Actions.Request(CreationActions.SpawnPart, new SpawnPartArgs(partId, [at.X, at.Y, at.Z], yaw), result =>
        {
            _result.Text = result.Ok ? Loc.T("Spawned {0} (creation {1}).", partId, result.Value) : Loc.T("Spawn refused: {0}", result.Message);
            done?.Invoke(result);
        });
    }

    /// <summary>
    /// Asks the controlled instance's host to spawn a bot of type <paramref name="botId"/> a few metres in
    /// front of the controlled player, facing them. <paramref name="done"/> gets the result.
    /// </summary>
    public void SpawnBot(string botId, Action<ActionResult>? done = null)
    {
        var player = Active.Main.LocalPlayer;
        var origin = player?.GlobalPosition ?? Vector3.Zero;
        var forward = player is null ? Vector3.Forward : -player.GlobalBasis.Z with { Y = 0 };
        forward = forward.LengthSquared() > 0 ? forward.Normalized() : Vector3.Forward;
        var at = origin + forward * 6f;
        var yaw = Mathf.RadToDeg(player?.Rotation.Y ?? 0) + 180f;
        Active.Main.Actions.Request(BotActions.SpawnBot, new SpawnBotArgs(botId, [at.X, at.Y, at.Z], yaw), result =>
        {
            _result.Text = result.Ok ? Loc.T("Spawned bot {0}.", botId) : Loc.T("Bot spawn refused: {0}", result.Message);
            done?.Invoke(result);
        });
    }

    /// <summary>Asks the host to remove every Creation.</summary>
    public void Clear(Action<ActionResult>? done = null) =>
        Active.Main.Actions.Request(CreationActions.ClearCreations, new NoArgs(), result =>
        {
            _result.Text = result.Ok ? Loc.T("Cleared.") : Loc.T("Clear refused: {0}", result.Message);
            done?.Invoke(result);
        });

    private bool TryHost()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var port = (int)GD.RandRange(30000, 60000);
            if (Host.Main.Session.Host(port, MaxLocalClients + 1) == Error.Ok)
            {
                _port = port;
                return true;
            }
        }
        _result.Text = Loc.T("Could not host: {0}", Host.Main.Session.LastMessage);
        return false;
    }

    private Instance StartInstance(string name, LaunchOptions options)
    {
        var viewport = new SubViewport
        {
            Name = name.Replace(" ", ""),
            OwnWorld3D = true,
            Size = PaneSize,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = true,
        };
        _overlayRoot.AddChild(viewport);
        GetTree().SetMultiplayer(MultiplayerApi.CreateDefaultInterface(), viewport.GetPath());
        var main = ResourceLoader.Load<PackedScene>(MainScene).Instantiate<Main>();
        main.Options = options;
        main.ReadDeviceInput = false;
        main.Mods = _content;
        // Local clients are separate players, so each needs its own profile.
        main.Profile = Core.Saves.PlayerProfile.Fixed($"Sandbox {name}");
        viewport.AddChild(main);
        var instance = new Instance(name, viewport, main);
        _instances.Add(instance);
        main.PlayerSync.PlayerSpawned += player =>
        {
            if (player == main.LocalPlayer && _instances.IndexOf(instance) == _active)
                Activate(_active);
        };
        RebuildPanes();
        return instance;
    }

    private void RebuildPanes()
    {
        _mainView.Texture = Active.Viewport.GetTexture();
        foreach (var child in _panes.GetChildren())
        {
            _panes.RemoveChild(child);
            child.QueueFree();
        }
        for (var i = 0; i < _instances.Count; i++)
        {
            if (i == _active)
                continue;
            var pane = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            pane.AddChild(new Label { Text = Loc.T("{0} (Tab to switch)", _instances[i].Name) });
            pane.AddChild(new TextureRect
            {
                Texture = _instances[i].Viewport.GetTexture(),
                CustomMinimumSize = PaneSize,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            });
            _panes.AddChild(pane);
        }
    }

    private void RefreshParts()
    {
        var filter = _filter.Text.Trim();
        _partIds = [.. _content.Parts.Values
            .Where(p => p.Definition.IsPlaceable)
            .Select(p => p.QualifiedId)
            .Where(id => filter.Length == 0 || id.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)];
        _parts.Clear();
        foreach (var id in _partIds)
            _parts.AddItem($"{_content.FindDefinition(id)?.Name}  ({id})");
        var bots = Host.Main.BotTypes?.All ?? [];
        _botIds = [.. bots.Select(b => b.QualifiedId).Order(StringComparer.Ordinal)];
        _bots.Clear();
        foreach (var id in _botIds)
            _bots.AddItem($"{bots.First(b => b.QualifiedId == id).Name}  ({id})");
    }

    private void BuildPanel()
    {
        // A strip down the left, 2% in from the edges; it scrolls when it is taller than the screen.
        Window = new ToolWindow { Name = "Window", Title = "Test Sandbox" };
        Window.CloseRequested += EmitBack;
        AddChild(Window);
        (Window.AnchorLeft, Window.AnchorTop, Window.AnchorRight, Window.AnchorBottom) = (0.02f, 0.02f, 0.02f, 0.98f);
        (Window.OffsetLeft, Window.OffsetTop, Window.OffsetRight, Window.OffsetBottom) = (0, 0, 360, 0);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Window.Body.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(column);
        column.AddChild(new Label
        {
            Text = "Click the world to look around. Esc frees the mouse, and Esc again closes the sandbox. WASD moves, Space jumps. Tab switches instance.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _filter = new LineEdit { PlaceholderText = "Filter parts" };
        _filter.TextChanged += _ => RefreshParts();
        column.AddChild(_filter);
        _parts = Loc.Verbatim(new ItemList { CustomMinimumSize = new Vector2(0, 200) });
        _parts.ItemActivated += i => Spawn(_partIds[(int)i]);
        column.AddChild(_parts);
        var row = new HFlowContainer();
        column.AddChild(row);
        AddButton(row, "Spawn", () =>
        {
            if (_parts.GetSelectedItems() is [var i, ..])
                Spawn(_partIds[i]);
        }, "Put the selected part in front of you");
        AddButton(row, "Clear all", () => AskClear(), "Remove every Creation and bot from the sandbox");
        AddButton(row, "Reload mods", ReloadContent, "Read every mod again, so edits made since show up");
        var botRow = new HBoxContainer();
        column.AddChild(botRow);
        _bots = Loc.Verbatim(new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false });
        botRow.AddChild(_bots);
        AddButton(botRow, "Spawn bot", () =>
        {
            if (_bots.Selected >= 0 && _bots.Selected < _botIds.Count)
                SpawnBot(_botIds[_bots.Selected]);
        }, "Put the selected bot in front of you");
        var net = new HFlowContainer();
        column.AddChild(net);
        AddButton(net, "Add local client", () =>
        {
            if (AddLocalClient() is null && _instances.Count > MaxLocalClients)
                _result.Text = Loc.T("At most {0} local clients.", MaxLocalClients);
        }, "Join a second player to this sandbox to test multiplayer");
        AddButton(net, "Remove client", () => AskRemoveClient(), "Disconnect the last local client");
        _collisionOverlay = new CheckBox { Text = "Show collision and centre of mass", TooltipText = "Draw each body's collision shapes and centre of mass" };
        _collisionOverlay.Toggled += on =>
        {
            if (!on)
                CollisionOverlay.Remove(Active.Main.Creations);
        };
        column.AddChild(_collisionOverlay);
        // Drawn on the instance being watched, so a client's view shows the joints as it received them.
        _jointOverlay = new CheckBox { Text = "Show joints", TooltipText = "Draw each joint's axis, range and state" };
        column.AddChild(_jointOverlay);
        _forceOverlay = new CheckBox { Text = "Show forces", TooltipText = "Draw the forces and torques parts put on their bodies" };
        column.AddChild(_forceOverlay);
        _result = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_result);
        _status = Loc.Verbatim(new Label { ThemeTypeVariation = "SecondaryLabel" });
        column.AddChild(_status);
    }

    /// <summary>Asks, then removes every Creation and bot from the sandbox.</summary>
    public ConfirmDialog AskClear() =>
        ConfirmDialog.Ask(this, "Clear the sandbox", "Remove every Creation and bot from the sandbox?", "Clear", () => Clear());

    /// <summary>Asks, then disconnects the last local client. Null when there is none.</summary>
    public ConfirmDialog? AskRemoveClient() => _instances.Count < 2 ? null
        : ConfirmDialog.Ask(this, "Remove client", "Disconnect the last local client?", "Remove", RemoveLocalClient);

    private static void AddButton(Container parent, string text, Action pressed, string tooltip)
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        button.Pressed += pressed;
        parent.AddChild(button);
    }
}

/// <summary>Debug overlay for Creations: each body's collision shapes as wireframes and its centre of mass.</summary>
public static class CollisionOverlay
{
    private const string NodeName = "DebugOverlay";
    private static readonly StandardMaterial3D ShapeMaterial = new()
    {
        AlbedoColor = UiTokens.Collision,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = true,
    };
    private static readonly StandardMaterial3D MassMaterial = new()
    {
        AlbedoColor = UiTokens.CentreOfMass,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = true,
    };

    /// <summary>Adds the overlay to every live body that lacks one.</summary>
    public static void Apply(CreationWorld world)
    {
        foreach (var body in world.All.Select(c => world.NodeOf(c.Id)).OfType<CreationNode>().SelectMany(n => n.Bodies))
        {
            if (body.HasNode(NodeName))
                continue;
            var overlay = new Node3D { Name = NodeName };
            foreach (var shape in body.GetChildren().OfType<CollisionShape3D>())
                overlay.AddChild(new MeshInstance3D { Mesh = shape.Shape.GetDebugMesh(), Transform = shape.Transform, MaterialOverride = ShapeMaterial });
            overlay.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.03f, Height = 0.06f },
                Position = body.CenterOfMass,
                MaterialOverride = MassMaterial,
            });
            body.AddChild(overlay);
        }
    }

    /// <summary>Removes the overlay from every body.</summary>
    public static void Remove(CreationWorld world)
    {
        foreach (var body in world.All.Select(c => world.NodeOf(c.Id)).OfType<CreationNode>().SelectMany(n => n.Bodies))
            body.GetNodeOrNull(NodeName)?.QueueFree();
    }
}
