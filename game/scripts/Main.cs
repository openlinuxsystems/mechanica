using Godot;
using Mechanica.Core;
using Mechanica.Game.Player;

namespace Mechanica.Game;

/// <summary>
/// Flat test world: spawns the local player at <c>SpawnPoint</c> and drives it from this machine's
/// keyboard and mouse. Chunk 2b moves spawning to the host so every peer gets a player.
/// </summary>
public partial class Main : Node3D
{
    /// <summary>The local player, once spawned.</summary>
    public PlayerController? LocalPlayer { get; private set; }

    public override void _Ready()
    {
        GD.Print($"{MechanicaInfo.GameName} started (physics: {ProjectSettings.GetSetting("physics/3d/physics_engine")})");
        LocalPlayer = SpawnPlayer("LocalPlayer");
        AddChild(new LocalPlayerInput { Name = "LocalPlayerInput", Target = LocalPlayer });
    }

    /// <summary>Adds a player at the spawn point and returns it.</summary>
    public PlayerController SpawnPlayer(string name)
    {
        var spawn = GetNodeOrNull<Node3D>("SpawnPoint")?.GlobalTransform ?? Transform3D.Identity;
        var player = new PlayerController { Name = name };
        AddChild(player);
        player.GlobalTransform = spawn;
        return player;
    }
}
