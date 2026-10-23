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
    // A held food's bitten versions (eat_0 whole, eat_1 after a bite, ...), and how many bites it shows (-1: eaten).
    private Node3D[] _bites = [];
    private int _bitesShown;
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

    /// <summary>The middle of the right (or left) palm in world space, or null when the skeleton has no such hand.</summary>
    public Vector3? HandOf(bool right)
    {
        var hand = _skeleton.FindBone(right ? "hand_r" : "hand_l");
        var fingers = _skeleton.FindBone(right ? "fingers_r" : "fingers_l");
        if (hand < 0 || fingers < 0)
            return null;
        var at = _skeleton.GlobalTransform;
        return ((at * _skeleton.GetBoneGlobalPose(hand)).Origin + (at * _skeleton.GetBoneGlobalPose(fingers)).Origin) * 0.5f;
    }

    /// <summary>The right hand's palm in the world (where held items sit), or null when the rig has no hand.</summary>
    public Vector3? HandPosition => _hand is null ? null : (_skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_skeleton.FindBone(CharacterRig.PropBone))).Origin;

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
            // A hand without finger bones closes its one-bone fingers as the clip's middle finger does.
            foreach (var (fingers, track) in CharacterRig.FingerFallback)
            {
                if (_skeleton.FindBone(fingers) is var bone and >= 0 && _skeleton.FindBone(track) < 0
                    && animation.FindTrack($"{animation.TrackGetPath(0).GetConcatenatedNames()}:{track}", Animation.TrackType.Rotation3D) is var t and >= 0)
                    rotation[bone] = t;
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
            // A seated body takes the seat's whole frame, whichever way its player looks: it pitches and rolls with the
            // Creation, upside down included, and stays put in the seat until the player stands up.
            var offset = Player?.ShownLocalOffset ?? Vector3.Zero;
            if (Player?.SeatBasis is { } seat)
            {
                var basis = (Player.GlobalBasis.Orthonormalized().Inverse() * seat).Orthonormalized();
                Basis = basis;
                Position = basis * new Vector3(0, Animator.Lift, 0) + offset;
            }
            else if (Player?.ClimbYaw is { } climbYaw)
            {
                // On a ladder the body faces its face, wherever its player looks.
                Rotation = new Vector3(0, climbYaw - Player.Rotation.Y, 0);
                Position = new Vector3(0, Animator.Lift, 0) + offset;
            }
            else
            {
                Rotation = new Vector3(0, Animator.BodyTurn, 0);
                Position = new Vector3(0, Animator.Lift, 0) + offset;
            }
        }
        Pose();
        ShowProp(Animator.ShowProp && Player is not null ? Animator.Prop : CharacterProp.None);
        ShowHeld(_propShown is CharacterProp.Item or CharacterProp.Food or CharacterProp.Drink && Player is not null ? Player.ShownHeld : (ushort)0);
        ShowBites();
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
        {
            _bucket?.Tint(prop == CharacterProp.ChemicalBucketFull ? HeldBucket.ChemicalColour : null);
            return;
        }
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
        _bucket?.Tint(prop == CharacterProp.ChemicalBucketFull ? HeldBucket.ChemicalColour : null);
    }

    // A held item's own model (its part data's held section) in the right hand, while the plain item pose is shown.
    private void ShowHeld(ushort item)
    {
        if (item == _heldShown || _hand is null)
            return;
        _heldShown = item;
        _held?.QueueFree();
        _held = item == 0 ? null : Assets.HeldModel?.Invoke(item);
        _bites = [];
        _bitesShown = 0;
        if (_held is null)
            return;
        _hand.AddChild(_held);
        var bites = new List<Node3D>();
        while (_held.FindChild($"eat_{bites.Count}", recursive: true, owned: false) is Node3D bitten)
            bites.Add(bitten);
        _bites = [.. bites];
        ShowBite(0);
    }

    // Food being eaten: the held model shows the bites the eat clip has reached, juice sprays and the food's eat sound
    // plays at each one, and once it is gone the hand is empty until the clip ends. A drink does the same with the drink
    // clip's gulps (its sound, no juice).
    private void ShowBites()
    {
        if (_held is null)
            return;
        var timing = _propShown is not (CharacterProp.Food or CharacterProp.Drink) ? null : Animator.Gesture switch
        {
            "eat" or "eat_fp" => Assets.Settings.Eat,
            "drink" or "drink_fp" => Assets.Settings.Drink,
            _ => null,
        };
        var bites = timing?.BitesAt(Animator.GestureTime) ?? 0;
        if (bites == _bitesShown)
            return;
        if (bites > _bitesShown && bites > 0)
            Bite();
        ShowBite(bites);
    }

    private void ShowBite(int bites)
    {
        _bitesShown = bites;
        if (_bites.Length == 0)
        {
            _held!.Visible = bites >= 0;
            return;
        }
        _held!.Visible = true;
        for (var i = 0; i < _bites.Length; i++)
            _bites[i].Visible = i == Math.Min(bites, _bites.Length - 1) && bites >= 0;
    }

    private void Bite()
    {
        if (Player is null || Assets.HeldBite?.Invoke(_heldShown) is not { } bite)
            return;
        var at = _held!.GlobalPosition;
        if (bite.Sound is { } sound)
            Sounds?.PlayLocal(sound, bite.ModId, at);
        if (bite.Drink)
            return;
        var juice = new CpuParticles3D
        {
            Name = "Juice",
            OneShot = true,
            Amount = 14,
            Lifetime = 0.45f,
            Explosiveness = 0.95f,
            Direction = Vector3.Up,
            Spread = 60f,
            InitialVelocityMin = 0.5f,
            InitialVelocityMax = 1.1f,
            Gravity = new Vector3(0, -6f, 0),
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1f,
            Mesh = new SphereMesh { Radius = 0.004f, Height = 0.008f, RadialSegments = 6, Rings = 3 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = bite.Juice, Roughness = 0.2f },
        };
        // In the world, not the hand, so the drops fall where they were bitten off.
        (GetTree().CurrentScene ?? this).AddChild(juice);
        juice.GlobalPosition = at;
        juice.Emitting = true;
        juice.Finished += juice.QueueFree;
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
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 0.6f, World.PhysicsLayers.Queries);
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
