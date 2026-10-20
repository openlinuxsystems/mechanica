using Godot;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Building;

/// <summary>
/// A see-through colour laid over every mesh of one Creation (<see cref="GeometryInstance3D.MaterialOverlay"/>), as the
/// Lift marks what it would capture (blue) and what it has captured (red). It follows the Creation as it rebuilds its
/// meshes, and <see cref="Set"/> to null takes it off.
/// </summary>
public sealed class CreationTint
{
    private readonly List<MeshInstance3D> _meshes = [];
    private CreationNode? _node;
    private Material? _material;

    /// <summary>The Creation tinted now, or null.</summary>
    public int? Creation => _node is not null && GodotObject.IsInstanceValid(_node) ? _node.Creation.Id : null;

    /// <summary>The colour on it now, or null.</summary>
    public Material? Material => Creation is null ? null : _material;

    /// <summary>Tints <paramref name="node"/> with <paramref name="material"/>, taking the tint off whatever had it.</summary>
    public void Set(CreationNode? node, Material? material)
    {
        if (node is null || material is null)
        {
            Clear();
            return;
        }
        if (node == _node && material == _material && Current())
            return;
        Clear();
        _node = node;
        _material = material;
        foreach (var mesh in node.FindChildren("*", nameof(MeshInstance3D), owned: false).OfType<MeshInstance3D>())
        {
            if (mesh.MaterialOverlay is not null)
                continue;
            mesh.MaterialOverlay = material;
            _meshes.Add(mesh);
        }
    }

    /// <summary>Takes the tint off.</summary>
    public void Clear()
    {
        foreach (var mesh in _meshes.Where(GodotObject.IsInstanceValid))
            mesh.MaterialOverlay = null;
        _meshes.Clear();
        _node = null;
        _material = null;
    }

    // False once the Creation rebuilt its meshes (a part placed or broken, a Lift moving it).
    private bool Current() => _meshes.Count > 0 && _meshes.All(m => GodotObject.IsInstanceValid(m) && m.IsInsideTree());

    /// <summary>A tint's look: one colour, unshaded and see-through, drawn over the Creation's own surfaces.</summary>
    public static StandardMaterial3D Look(Color colour, float alpha) => new()
    {
        AlbedoColor = colour with { A = alpha },
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Back,
    };
}
