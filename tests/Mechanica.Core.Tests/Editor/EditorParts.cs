using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Editor;

/// <summary>Shared part definitions for editor tests.</summary>
internal static class EditorParts
{
    public static PartDefinition Block(string id = "test_block") => DataJson.Read<PartDefinition>(TestMods.Block(id))!;

    public static PartDefinition Bearing() => Block("bearing") with
    {
        Name = "Bearing",
        Category = PartCategory.Interactive,
        Sockets =
        [
            new SocketDefinition { Id = "base", Type = SocketType.Structural, Position = new Vec3(0, -0.125f, 0), Normal = new Vec3(0, -1, 0) },
            new SocketDefinition { Id = "rotor", Type = SocketType.Rotational, Position = new Vec3(0, 0.125f, 0), Normal = new Vec3(0, 1, 0) },
        ],
    };
}
