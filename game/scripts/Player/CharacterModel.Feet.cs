using Godot;
using Mechanica.Core.Characters;

namespace Mechanica.Game.Player;

// Planting the feet on uneven ground: after the clips have posed the body, each foot looks for the ground under it,
// the hips drop for the lower one, each leg bends to put its foot there (LegReach) and a foot on the ground lies along it.
public partial class CharacterModel
{
    private readonly FootPlanting _planting = new();
    private int _hips = -1;
    private int[] _thigh = [], _shin = [], _foot = [];
    private int[] _parent = [];
    private float _ankleRest;
    private readonly Vector3[] _groundNormal = [Vector3.Up, Vector3.Up];
    private Transform3D[] _global = [];
    private bool[] _known = [];
    private float _plantDelta;

    // How far above and below the floor a foot looks for ground, in metres.
    private const float GroundReach = 0.5f;
    // A foot lies along the ground only this close to standing on it (its ankle's height above its rest, in metres).
    private const float ContactHeight = 0.1f;
    // Ground steeper than this (degrees) is not lain along: the foot stays as the clip has it.
    private const float MaxFootTilt = 40f;

    private void ReadyFeet()
    {
        var count = _skeleton.GetBoneCount();
        _parent = Enumerable.Range(0, count).Select(_skeleton.GetBoneParent).ToArray();
        _global = new Transform3D[count];
        _known = new bool[count];
        _hips = _skeleton.FindBone("hips");
        _thigh = [_skeleton.FindBone("thigh_l"), _skeleton.FindBone("thigh_r")];
        _shin = [_skeleton.FindBone("shin_l"), _skeleton.FindBone("shin_r")];
        _foot = [_skeleton.FindBone("foot_l"), _skeleton.FindBone("foot_r")];
        if (_foot[0] >= 0)
            _ankleRest = _skeleton.GetBoneGlobalRest(_foot[0]).Origin.Y;
    }

    private bool HasLegs => _hips >= 0 && _thigh.All(b => b >= 0) && _shin.All(b => b >= 0) && _foot.All(b => b >= 0);

    // Where bone b is in the skeleton's space under the pose being built (cached until the pose changes).
    private Transform3D Global(int b)
    {
        if (_known[b])
            return _global[b];
        var local = new Transform3D(new Basis(_rotations[b]), _positions[b]);
        _global[b] = _parent[b] >= 0 ? Global(_parent[b]) * local : local;
        _known[b] = true;
        return _global[b];
    }

    private void Forget() => Array.Clear(_known);

    // Bends the legs of the pose in _rotations and _positions to the ground under the feet.
    private void PlantFeet()
    {
        var delta = _plantDelta;
        _plantDelta = 0f;
        if (FirstPerson || Player is not { } player || !HasLegs || !IsInsideTree())
            return;
        var planted = Animator.Mode is CharacterMode.Ground or CharacterMode.Crouch && player.Pose.OnFloor
            && !player.Seated && player.ClimbYaw is null && !(Animator.LegsWeight > 0.5f) && !player.Dead;
        Forget();
        var skeleton = _skeleton.GlobalTransform;
        var toSkeleton = skeleton.AffineInverse();
        var floor = GlobalPosition.Y - Animator.Lift;
        var up = (toSkeleton.Basis * Vector3.Up).Normalized();
        var ground = new float?[2];
        var ankles = new Vector3[2];
        var space = planted ? player.GetWorld3D().DirectSpaceState : null;
        for (var i = 0; i < 2; i++)
        {
            ankles[i] = Global(_foot[i]).Origin;
            if (space is null)
                continue;
            var at = skeleton * ankles[i];
            var from = new Vector3(at.X, floor + GroundReach, at.Z);
            var query = PhysicsRayQueryParameters3D.Create(from, new Vector3(at.X, floor - GroundReach, at.Z), World.PhysicsLayers.Queries);
            query.Exclude = [player.GetRid()];
            var hit = space.IntersectRay(query);
            if (hit.Count == 0)
                continue;
            var normal = hit["normal"].AsVector3();
            if (normal.Y < 0.5f)
                continue;
            ground[i] = hit["position"].AsVector3().Y - floor;
            _groundNormal[i] = _groundNormal[i].Lerp(normal, Mathf.Clamp(delta * 20f, 0f, 1f)).Normalized();
        }
        _planting.Update(ground[0], ground[1], planted, delta);
        // On level ground (or none to plant on) the clip stands as it is.
        if (_planting.Weight < 1e-3f || MathF.Max(MathF.Abs(_planting.Hips), MathF.Max(MathF.Abs(_planting.Left), MathF.Abs(_planting.Right))) < 1e-3f)
            return;

        // The hips drop for the lower foot.
        var hipsParent = _parent[_hips] >= 0 ? Global(_parent[_hips]).Basis : Basis.Identity;
        _positions[_hips] += hipsParent.Inverse() * (toSkeleton.Basis * new Vector3(0, _planting.Hips, 0));
        Forget();

        var forward = (toSkeleton.Basis * -GlobalBasis.Z).Normalized();
        var offsets = new[] { _planting.Left, _planting.Right };
        for (var i = 0; i < 2; i++)
        {
            var (thigh, shin, foot) = (_thigh[i], _shin[i], _foot[i]);
            var hip = Global(thigh);
            var knee = Global(shin);
            var ankle = Global(foot);
            // The clip's own height for this foot, kept: the leg reaches to where the clip put the foot, moved by the ground.
            var target = ankles[i] + toSkeleton.Basis * new Vector3(0, offsets[i], 0);
            var (rootTurn, midTurn) = LegReach.Solve(hip.Origin.ToNumerics(), knee.Origin.ToNumerics(), ankle.Origin.ToNumerics(), target.ToNumerics(), forward.ToNumerics());
            var root = rootTurn.ToGodot();
            var mid = midTurn.ToGodot();
            var parent = Global(_parent[thigh]).Basis.GetRotationQuaternion();
            var hipRotation = hip.Basis.GetRotationQuaternion();
            var kneeRotation = knee.Basis.GetRotationQuaternion();
            var ankleRotation = ankle.Basis.GetRotationQuaternion();
            _rotations[thigh] = (parent.Inverse() * root * hipRotation).Normalized();
            _rotations[shin] = (hipRotation.Inverse() * mid * kneeRotation).Normalized();
            // A foot on the ground lies along it; lifted, it keeps the clip's angle.
            var height = (skeleton * ankles[i]).Y - floor - (skeleton.Basis * new Vector3(0, _ankleRest, 0)).Length();
            var contact = Mathf.Clamp(1f - height / ContactHeight, 0f, 1f) * _planting.Weight;
            var normal = (toSkeleton.Basis * _groundNormal[i]).Normalized();
            var lie = Quaternion.Identity;
            if (contact > 0f && up.AngleTo(normal) < Mathf.DegToRad(MaxFootTilt))
                lie = Quaternion.Identity.Slerp(LegReach.Between(up.ToNumerics(), normal.ToNumerics()).ToGodot(), contact);
            var newShin = root * mid * kneeRotation;
            _rotations[foot] = (newShin.Inverse() * lie * ankleRotation).Normalized();
            Forget();
        }
    }
}
