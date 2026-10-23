using Godot;
using Mechanica.Core.Characters;
using Mechanica.Game.Player;

namespace Mechanica.Game.Tools;

// Given `hands`, close-ups of the player's hands with every prop held (tools/capture-hands.sh): each prop from the thumb
// side, the back of the hand and below, the whole body for context, and first person. A third argument keeps the props
// it names (comma-separated, as the shot names spell them).
public partial class CaptureScene
{
    private static readonly (string Name, CharacterProp Prop, string? Item)[] HandProps =
    [
        ("empty", CharacterProp.None, null),
        ("sledgehammer", CharacterProp.Sledgehammer, null),
        ("connect_tool", CharacterProp.ConnectTool, null),
        ("weld_tool", CharacterProp.WeldTool, null),
        ("paint_tool", CharacterProp.PaintTool, null),
        ("refine_tool", CharacterProp.RefineTool, null),
        ("lift_remote", CharacterProp.LiftRemote, null),
        ("potato_blaster", CharacterProp.PotatoBlaster, null),
        ("water_bucket", CharacterProp.WaterBucket, null),
        ("water_bucket_full", CharacterProp.WaterBucketFull, null),
        ("soil_bag", CharacterProp.SoilBag, null),
        ("tomato", CharacterProp.Food, "vanilla:tomato"),
        ("bloom_tea", CharacterProp.Drink, "vanilla:bloom_tea"),
        ("lead", CharacterProp.Item, "vanilla:lead"),
    ];

    private void HandShots(string? only)
    {
        Shot("hands_00_wait", 60, () => { });
        foreach (var (name, prop, item) in HandProps)
        {
            if (only is { Length: > 0 } && !only.Split(',').Contains(name))
                continue;
            Shot($"hands_{name}_00_hold", 30, () =>
            {
                Input();
                Hold(prop);
                Player.HeldItem = item is null ? (ushort)0 : _main.HeldItems.IndexOf(item);
                Look(new Vector3(-1.3f, 1.4f, -2.2f), 1.1f);
            });
            Shot($"hands_{name}_1_body", 4, () => { });
            foreach (var (view, offset) in new[] { ("thumb", new Vector3(-0.32f, 0.12f, -0.38f)), ("back", new Vector3(-0.55f, 0.1f, 0.0f)), ("below", new Vector3(-0.3f, -0.2f, -0.35f)) })
                Shot($"hands_{name}_2_{view}", 4, () => HandView(offset, right: true));
            Shot($"hands_{name}_2_left", 4, () => HandView(new Vector3(0.35f, 0.1f, -0.38f), right: false));
            Shot($"hands_{name}_3_first_person", 20, () => { Player.ThirdPerson = false; Player.MakeCameraCurrent(); });
        }
    }

    // The capture camera close to a hand, at `offset` (m, in the player's frame) from it, looking at it.
    private void HandView(Vector3 offset, bool right)
    {
        Player.ThirdPerson = true;
        if (Player.Character is not CharacterModel body || body.HandOf(right) is not { } hand)
            return;
        _follow = null;
        _camera.MakeCurrent();
        _camera.GlobalPosition = hand + Player.GlobalTransform.Basis * offset;
        _camera.LookAt(hand);
    }
}
