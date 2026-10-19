using Godot;

namespace Mechanica.Game.Survival;

/// <summary>Turns and squashes thrown water's lumps (its parent's mesh children) so it sloshes in the air.</summary>
public partial class WaterWobble : Node
{
    private double _time;

    public override void _Process(double delta)
    {
        _time += delta;
        if (GetParent() is not Node3D water)
            return;
        var t = (float)_time;
        water.Rotation = new Vector3(t * 3.1f, t * 2.3f, t * 1.7f);
        var i = 0;
        foreach (var child in water.GetChildren())
        {
            if (child is not MeshInstance3D lump)
                continue;
            var squash = 1f + 0.25f * Mathf.Sin(t * 11f + i * 2.1f);
            lump.Scale = new Vector3(squash, 1f / squash, squash);
            i++;
        }
    }
}
