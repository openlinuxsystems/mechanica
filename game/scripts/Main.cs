using Godot;
using Mechanica.Core;

namespace Mechanica.Game;

/// <summary>Placeholder main scene until the menus and player controller exist.</summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        GD.Print($"{MechanicaInfo.GameName} started (physics: {ProjectSettings.GetSetting("physics/3d/physics_engine")})");
    }
}
