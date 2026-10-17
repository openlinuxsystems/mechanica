using Godot;
using Mechanica.Core.Net;

namespace Mechanica.Game.Tests;

/// <summary>
/// Runs several game instances in one headless process for multiplayer tests (D10: every chunk is done
/// when it works host + client). Each instance is a full <see cref="Main"/> scene inside its own
/// <see cref="SubViewport"/> with its own physics world and its own <see cref="MultiplayerApi"/>, and
/// they talk over real ENet sockets on localhost. Because each instance's multiplayer root is its
/// viewport, node paths below it (and so every RPC route) are identical in all instances.
/// <para>
/// Usage: add the harness to a test scene, call <see cref="StartHost"/>, then <see cref="StartClient"/>
/// as many times as needed, and poll state from <c>_PhysicsProcess</c>. Instances never read devices;
/// tests set <c>LocalPlayer.CurrentInput</c> directly.
/// </para>
/// </summary>
public partial class NetTestHarness : Node
{
    private const string MainScene = "res://scenes/Main.tscn";

    private readonly List<Instance> _instances = [];

    /// <summary>One running game instance.</summary>
    /// <param name="Name">Instance name (also its viewport's node name).</param>
    /// <param name="Viewport">Viewport holding the instance's world.</param>
    /// <param name="Main">The instance's main scene.</param>
    public sealed record Instance(string Name, SubViewport Viewport, Main Main)
    {
        /// <summary>This instance's peer id.</summary>
        public int Peer => Main.Session.LocalPeer;

        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>World seed of the host, fixed so runs repeat.</summary>
    public long Seed { get; set; } = 1234;

    /// <summary>World generation for every instance started from now on; null loads the mods' rules, like the game.</summary>
    public Mechanica.Core.World.WorldGenData? WorldGen { get; set; }

    /// <summary>The port the host listens on, once started.</summary>
    public int Port { get; private set; }

    /// <summary>Running instances, host first.</summary>
    public IReadOnlyList<Instance> Instances => _instances;

    /// <summary>Starts a hosting instance on a free localhost port.</summary>
    public Instance StartHost(int maxPlayers = NetProtocol.DefaultMaxPlayers, string name = "Host")
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var port = (int)GD.RandRange(30000, 60000);
            var instance = Start(name, LaunchOptions.Offline with { Seed = Seed });
            if (instance.Main.Session.Host(port, maxPlayers) == Error.Ok)
            {
                Port = port;
                return instance;
            }
            Stop(instance);
        }
        throw new InvalidOperationException("No free port for the test host.");
    }

    /// <summary>Starts an instance that joins the host, optionally with its own mod list instead of the loaded mods.</summary>
    public Instance StartClient(string name, ContentManifest? content = null) =>
        Start(name, LaunchOptions.ForJoin(new NetEndpoint("127.0.0.1", Port)), content);

    /// <summary>Disconnects and frees an instance.</summary>
    public void Stop(Instance instance)
    {
        // The instance's MultiplayerApi stays registered (Godot can't unregister one); with an
        // offline peer and no nodes under its root it does nothing.
        instance.Main.Session.Leave();
        _instances.Remove(instance);
        RemoveChild(instance.Viewport);
        instance.Viewport.QueueFree();
    }

    private Instance Start(string name, LaunchOptions options, ContentManifest? content = null)
    {
        var viewport = new SubViewport
        {
            Name = name,
            OwnWorld3D = true,
            Size = new Vector2I(64, 64),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(viewport);
        GetTree().SetMultiplayer(MultiplayerApi.CreateDefaultInterface(), viewport.GetPath());

        var main = ResourceLoader.Load<PackedScene>(MainScene).Instantiate<Main>();
        main.Options = options;
        main.ReadDeviceInput = false;
        main.Content = content; // null: load the real enabled mods, like the game does
        main.WorldGen = WorldGen;
        viewport.AddChild(main);
        var instance = new Instance(name, viewport, main);
        _instances.Add(instance);
        return instance;
    }
}
