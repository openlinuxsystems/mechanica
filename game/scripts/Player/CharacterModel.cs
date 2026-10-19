using Godot;
using Mechanica.Core.Characters;
using Mechanica.Game.Building;
using Mechanica.Game.Sound;

namespace Mechanica.Game.Player;

/// <summary>
/// One player's character: its model, posed every frame by the shared <see cref="CharacterAnimator"/> from
/// what the player is doing (<see cref="PlayerController.Pose"/>), the prop in its hand, and the sounds its body
/// makes (footsteps by surface, jumps, landings, swimming). Every peer runs one for every player it shows, from
/// state it already has, so animation costs no network traffic. Poses are blended here by sampling the clips
/// directly: a whole-body layer, then an upper-body layer on <see cref="CharacterRig.UpperBodyBones"/>.
/// </summary>
public partial class CharacterModel : Node3D
{
    private sealed record Clip(Animation Animation, int[] Rotation, int[] Position);

    private readonly Dictionary<string, Clip> _clips = new(StringComparer.Ordinal);
    private Skeleton3D _skeleton = null!;
    private bool[] _upper = [];
    private Quaternion[] _restRotation = [];
    private Vector3[] _restPosition = [];
    private Vector3[] _pitchAxis = [];
    private Vector3[] _rollAxis = [];
    private Vector3[] _yawAxis = [];
    private int _spine = -1;
    private int _chest = -1;
    private int _neck = -1;
    private int _head = -1;
    private Vector3 _eyeInHead;
    private BoneAttachment3D? _hand;
    private Node3D? _held;
    private ushort _heldShown;
    private Node3D? _prop;
    private CharacterProp _propShown;
    private HeldBucket? _bucket;
    private Quaternion[] _rotations = [];
    private Vector3[] _positions = [];
    private Quaternion[] _upperRotations = [];
    private Quaternion[] _bodyRotations = [];
    private int[] _freeArm = [];
    private Quaternion[] _legRotations = [];
    private Vector3[] _legPositions = [];

    /// <summary>The player this draws, or null for a preview that stands idle (the character picker).</summary>
    public PlayerController? Player { get; init; }

    /// <summary>The character it is. Set before adding to the tree.</summary>
    public Character Character { get; init; } = null!;

    /// <summary>The model, clips and props. Set before adding to the tree.</summary>
    public CharacterAssets Assets { get; init; } = null!;

    /// <summary>The model scene from <see cref="CharacterAssets.Instantiate"/>. Set before adding to the tree.</summary>
    public Node3D Model { get; init; } = null!;

    /// <summary>
    /// True for the local player's first-person arms (<see cref="CharacterAssets.Arms"/>), a child of the camera: shown
    /// only while the player looks out of its own eyes, held where <see cref="CharacterSettings.FirstPersonOffset"/> says.
    /// </summary>
    public bool FirstPerson { get; init; }

    /// <summary>Where the body's sounds play; null plays none.</summary>
    public SoundSync? Sounds { get; init; }

    /// <summary>The state machine.</summary>
    public CharacterAnimator Animator { get; private set; } = null!;

    /// <summary>The skeleton the clips pose.</summary>
    public Skeleton3D Skeleton => _skeleton;

    /// <summary>The prop node in hand, or null.</summary>
    public Node3D? PropNode => _prop;

    /// <summary>The modded held model in the right hand (an item's <c>held</c> section), or null.</summary>
    public Node3D? HeldNode => _held;

    /// <summary>Raised for every body event, after its sound plays (tests listen).</summary>
    public event Action<CharacterEvent>? Event;

    public override void _Ready()
    {
        Name = "Character";
        // The art faces +Z (glTF's front); a player faces -Z.
        Model.Rotation = new Vector3(0, Mathf.Pi, 0);
        AddChild(Model);
        _skeleton = Model.FindChildren("*", nameof(Skeleton3D), owned: false).OfType<Skeleton3D>().First();
        var count = _skeleton.GetBoneCount();
        _upper = new bool[count];
        _restRotation = new Quaternion[count];
        _restPosition = new Vector3[count];
        _pitchAxis = new Vector3[count];
        _rollAxis = new Vector3[count];
        _yawAxis = new Vector3[count];
        _legRotations = new Quaternion[count];
        _legPositions = new Vector3[count];
        _rotations = new Quaternion[count];
        _positions = new Vector3[count];
        _upperRotations = new Quaternion[count];
        _bodyRotations = new Quaternion[count];
        for (var b = 0; b < count; b++)
        {
            var rest = _skeleton.GetBoneRest(b);
            _restRotation[b] = rest.Basis.GetRotationQuaternion();
            _restPosition[b] = rest.Origin;
            _upper[b] = IsUpper(b);
            // The look pitch turns about the model's sideways axis, expressed in the bone's own frame.
            _pitchAxis[b] = (_skeleton.GetBoneGlobalRest(b).Basis.Inverse() * Vector3.Right).Normalized();
            _rollAxis[b] = (_skeleton.GetBoneGlobalRest(b).Basis.Inverse() * Vector3.Back).Normalized();
            _yawAxis[b] = (_skeleton.GetBoneGlobalRest(b).Basis.Inverse() * Vector3.Up).Normalized();
        }
        _neck = _skeleton.FindBone("neck");
        _spine = _skeleton.FindBone("spine");
        _chest = _skeleton.FindBone("chest");
        _head = _skeleton.FindBone("head");
        _freeArm = new[] { "shoulder_l", "upper_arm_l", "forearm_l", "hand_l" }.Select(_skeleton.FindBone).Where(b => b >= 0).ToArray();
        // The eyes in the rest pose, kept in the head bone's frame.
        if (_head >= 0)
        {
            var eyes = Assets.Settings.Eyes;
            var skeletonHere = Transform3D.Identity;
            for (Node3D? node = _skeleton; node is not null && node != this; node = node.GetParentNode3D())
                skeletonHere = node.Transform * skeletonHere;
            _eyeInHead = _skeleton.GetBoneGlobalRest(_head).AffineInverse() * skeletonHere.AffineInverse() * new Vector3(eyes[0], eyes[1], eyes[2]);
        }

        foreach (var (name, animation) in Assets.Clips)
        {
            var rotation = Enumerable.Repeat(-1, count).ToArray();
            var position = Enumerable.Repeat(-1, count).ToArray();
            for (var t = 0; t < animation.GetTrackCount(); t++)
            {
                var bone = _skeleton.FindBone(animation.TrackGetPath(t).GetConcatenatedSubNames());
                if (bone < 0)
                    continue;
                if (animation.TrackGetType(t) == Animation.TrackType.Rotation3D)
                    rotation[bone] = t;
                else if (animation.TrackGetType(t) == Animation.TrackType.Position3D)
                    position[bone] = t;
            }
            _clips[name] = new Clip(animation, rotation, position);
        }
        Animator = new CharacterAnimator(Assets.Settings, Assets.ClipLengths, FirstPerson);

        var hand = _skeleton.FindBone(CharacterRig.PropBone);
        if (hand >= 0)
        {
            _hand = new BoneAttachment3D { Name = "Hand", BoneName = CharacterRig.PropBone };
            _skeleton.AddChild(_hand);
        }
    }

    public override void _Process(double delta)
    {
        Animator.Update(Player?.Pose ?? new CharacterPose(System.Numerics.Vector3.Zero, 0f, 0f, OnFloor: true), (float)delta);
        foreach (var e in Animator.Events)
        {
            PlaySound(e);
            Event?.Invoke(e);
        }
        if (FirstPerson)
        {
            Visible = Player is { BodyVisible: false, Dead: false, Seated: false };
            if (!Visible)
                return;
            var offset = Assets.Settings.FirstPersonOffset;
            Position = new Vector3(offset[0], offset[1], offset[2]);
            Rotation = new Vector3(-Mathf.DegToRad(Assets.Settings.FirstPersonPitchDegrees), 0, 0);
        }
        // Seen from inside its own head the body is hidden; it still makes its sounds.
        else if (Player is { BodyVisible: false })
        {
            return;
        }
        else
        {
            // A seated body faces the seat's front, whichever way its player looks.
            var turn = Player?.SeatYaw is { } seatYaw ? Mathf.Wrap(seatYaw - Player.Rotation.Y, -Mathf.Pi, Mathf.Pi) : Animator.BodyTurn;
            Rotation = new Vector3(0, turn, 0);
            Position = new Vector3(0, Animator.Lift, 0) + (Player?.ShownLocalOffset ?? Vector3.Zero);
        }
        Pose();
        ShowProp(Animator.ShowProp && Player is not null ? Animator.Prop : CharacterProp.None);
        ShowHeld(_propShown == CharacterProp.Item && Player is not null ? Player.ShownHeld : (ushort)0);
        _bucket?.Update(HeldBucket.Target(Assets.Settings.Bucket, _propShown, Animator), (float)delta, PlayFill);
    }

    private void PlayFill()
    {
        if (Sounds is not null && Player is not null && _prop is not null)
            Sounds.PlayLocal(Character.Sound(CharacterSounds.BucketFill), Character.ModId, _prop.GlobalPosition);
    }

    /// <summary>Where this body's eyes are now, in world space; null for a body without a head bone.</summary>
    public Vector3? Eyes
    {
        get
        {
            if (_head < 0)
                return null;
            // Unscaled, so a hidden head still has its eyes where they were.
            var head = _skeleton.GetBoneGlobalPose(_head);
            return _skeleton.GlobalTransform * new Transform3D(head.Basis.Orthonormalized(), head.Origin) * _eyeInHead;
        }
    }

    /// <summary>Applies the animator's current blend to the skeleton (called every visible frame).</summary>
    public void Pose()
    {
        var count = _rotations.Length;
        Blend(Animator.Body, _rotations, _positions, withPositions: true);
        if (Animator.UpperWeight > 0f && Animator.Upper.Count > 0)
        {
            Array.Copy(_rotations, _bodyRotations, count);
            Blend(Animator.Upper, _upperRotations, null, withPositions: false);
            for (var b = 0; b < count; b++)
            {
                if (_upper[b])
                    _rotations[b] = _rotations[b].Slerp(_upperRotations[b], Animator.UpperWeight);
            }
            // Moving with something in hand: the stride's own sway of the spine goes back on over the hold, and a free left
            // arm swings as it would empty-handed.
            if (Animator.Carry > 0f && !FirstPerson)
            {
                foreach (var bone in new[] { _spine, _chest, _neck, _head })
                {
                    if (bone >= 0)
                        _rotations[bone] = (_rotations[bone] * Quaternion.Identity.Slerp(_restRotation[bone].Inverse() * _bodyRotations[bone], Animator.Carry)).Normalized();
                }
                if (Animator.FreeArm > 0f)
                {
                    foreach (var bone in _freeArm)
                        _rotations[bone] = _rotations[bone].Slerp(_bodyRotations[bone], Animator.Carry * Animator.FreeArm);
                }
            }
        }
        // A swing steps into the blow and a knockdown throws the legs about: their own hips and legs, standing.
        if (Animator.LegsWeight > 0f && Animator.Legs.Count > 0)
        {
            Blend(Animator.Legs, _legRotations, _legPositions, withPositions: true);
            var w = Animator.LegsWeight;
            for (var b = 0; b < count; b++)
            {
                if (_upper[b])
                    continue;
                _rotations[b] = _rotations[b].Slerp(_legRotations[b], w);
                _positions[b] = _positions[b].Lerp(_legPositions[b], w);
            }
        }
        if (!FirstPerson)
        {
            // The chest and head face the look while the feet point elsewhere, shared out up the spine.
            var shares = Assets.Settings.Motion.TwistShares;
            var twist = Animator.Twist;
            var bones = new[] { _spine, _chest, _neck, _head };
            for (var i = 0; i < bones.Length; i++)
            {
                if (bones[i] >= 0 && twist != 0f)
                    _rotations[bones[i]] = (_rotations[bones[i]] * new Quaternion(_yawAxis[bones[i]], twist * shares[i])).Normalized();
            }
            // Leaning into speeding up and turning, in the spine and chest; the neck takes some back to keep the head level.
            var tilt = Animator.Lean;
            if (tilt != System.Numerics.Vector2.Zero)
            {
                foreach (var (bone, share) in new[] { (_spine, 0.55f), (_chest, 0.45f), (_neck, -0.3f) })
                {
                    if (bone >= 0)
                        _rotations[bone] = (_rotations[bone] * new Quaternion(_pitchAxis[bone], tilt.X * share) * new Quaternion(_rollAxis[bone], -tilt.Y * share)).Normalized();
                }
            }
        }

        var pitch = FirstPerson ? 0f : -Animator.HeadPitch / 2f;
        foreach (var bone in new[] { _neck, _head })
        {
            if (bone >= 0)
                _rotations[bone] = (_rotations[bone] * new Quaternion(_pitchAxis[bone], pitch)).Normalized();
        }
        // A seated body sways with the ride, half in the spine and half in the chest.
        var lean = Animator.SeatLean;
        if (lean != System.Numerics.Vector2.Zero)
        {
            foreach (var bone in new[] { _spine, _chest })
            {
                if (bone >= 0)
                    _rotations[bone] = (_rotations[bone] * new Quaternion(_pitchAxis[bone], lean.X / 2f) * new Quaternion(_rollAxis[bone], lean.Y / 2f)).Normalized();
            }
        }
        for (var b = 0; b < count; b++)
        {
            _skeleton.SetBonePoseRotation(b, _rotations[b]);
            _skeleton.SetBonePosePosition(b, _positions[b]);
        }
        // Seen from its own eyes the head (and its hair) shrinks away, so the seated body can still be drawn.
        if (_head >= 0)
            _skeleton.SetBonePoseScale(_head, Player is { HeadHidden: true } ? Vector3.One * 0.001f : Vector3.One);
    }

    // Weighted blend of clips per bone; bones a clip does not move count as at rest.
    private void Blend(IReadOnlyList<ClipWeight> layer, Quaternion[] rotations, Vector3[]? positions, bool withPositions)
    {
        var count = rotations.Length;
        var total = 0f;
        for (var b = 0; b < count; b++)
        {
            rotations[b] = new Quaternion(0, 0, 0, 0);
            if (positions is not null)
                positions[b] = Vector3.Zero;
        }
        foreach (var sample in layer)
        {
            if (!_clips.TryGetValue(sample.Clip, out var clip))
                continue;
            total += sample.Weight;
            for (var b = 0; b < count; b++)
            {
                var q = clip.Rotation[b] >= 0 ? clip.Animation.RotationTrackInterpolate(clip.Rotation[b], sample.Time) : _restRotation[b];
                // Quaternions q and -q are the same turn; add them on the same side so they don't cancel.
                var sum = rotations[b];
                rotations[b] = sum.Dot(q) < 0 ? sum - q * sample.Weight : sum + q * sample.Weight;
                if (withPositions && positions is not null)
                    positions[b] += (clip.Position[b] >= 0 ? clip.Animation.PositionTrackInterpolate(clip.Position[b], sample.Time) : _restPosition[b]) * sample.Weight;
            }
        }
        for (var b = 0; b < count; b++)
        {
            rotations[b] = total > 0f && rotations[b].LengthSquared() > 1e-8f ? rotations[b].Normalized() : _restRotation[b];
            if (positions is not null)
                positions[b] = total > 0f ? positions[b] / total : _restPosition[b];
        }
    }

    private void ShowProp(CharacterProp prop)
    {
        if (prop == _propShown || _hand is null)
            return;
        // The empty and the full bucket are one model: filling or emptying it keeps the bucket in hand.
        var same = _prop is not null && CharacterRig.PropNode(prop) is { } node && node == CharacterRig.PropNode(_propShown);
        _propShown = prop;
        if (same)
            return;
        _bucket = null;
        _prop?.QueueFree();
        _prop = null;
        if (Assets.Prop(prop) is not { } made)
            return;
        // The prop file places each prop in the rest-pose right hand; keep that grip relative to the hand bone.
        var hand = _skeleton.GetBoneGlobalRest(_skeleton.FindBone(CharacterRig.PropBone));
        var skeletonInModel = Model.GlobalTransform.AffineInverse() * _skeleton.GlobalTransform;
        made.Node.Transform = hand.AffineInverse() * skeletonInModel.AffineInverse() * made.Grip;
        _prop = made.Node;
        _hand.AddChild(_prop);
        _bucket = HeldBucket.Find(_prop, Assets.Settings.Bucket);
    }

    // A held item's own model (its part data's held section) in the right hand, while the plain item pose is shown.
    private void ShowHeld(ushort item)
    {
        if (item == _heldShown || _hand is null)
            return;
        _heldShown = item;
        _held?.QueueFree();
        _held = item == 0 ? null : Assets.HeldModel?.Invoke(item);
        if (_held is not null)
            _hand.AddChild(_held);
    }

    private void PlaySound(CharacterEvent e)
    {
        if (Sounds is null || Player is null)
            return;
        var name = e switch
        {
            CharacterEvent.StepLeft or CharacterEvent.StepRight => FootstepSound(),
            CharacterEvent.Jump => CharacterSounds.Jump,
            CharacterEvent.Land => CharacterSounds.Land,
            CharacterEvent.LandHard => CharacterSounds.LandHard,
            CharacterEvent.SwimStroke => CharacterSounds.SwimStroke,
            CharacterEvent.WaterEnter => CharacterSounds.WaterEnter,
            CharacterEvent.WaterExit => CharacterSounds.WaterExit,
            CharacterEvent.Equip => CharacterSounds.ToolEquip,
            _ => null,
        };
        if (name is not null)
            Sounds.PlayLocal(Character.Sound(name), Character.ModId, Player.GlobalPosition);
    }

    // What the feet are on: water, a part's material, or the ground.
    private string FootstepSound()
    {
        var player = Player!;
        if (player.WaterDepth > 0.05f)
            return CharacterSounds.FootstepWater;
        var from = player.GlobalPosition + Vector3.Up * 0.2f;
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 0.6f);
        query.Exclude = [player.GetRid()];
        var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return CharacterSounds.Footstep;
        var ray = new HandTool.RayHit(hit["position"].AsVector3(), hit["normal"].AsVector3(), hit["collider"].AsGodotObject(), hit["shape"].AsInt32());
        return HandTool.PartHit(ray) is { } part
            ? CharacterSounds.FootstepFor(part.Node.Creation.DefinitionOf(part.Part).SoundMaterial)
            : CharacterSounds.Footstep;
    }

    private bool IsUpper(int bone)
    {
        for (var b = bone; b >= 0; b = _skeleton.GetBoneParent(b))
        {
            var name = _skeleton.GetBoneName(b);
            if (CharacterRig.UpperBodyBones.Contains(name))
                return true;
            if (CharacterRig.Bones.Contains(name))
                return false;
        }
        return false;
    }
}
