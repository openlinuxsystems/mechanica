using Godot;
using Mechanica.Core.Bots;

namespace Mechanica.Game.Bots;

/// <summary>
/// A bot's <see cref="BotEffects.Shot"/>: a copy of its model's <c>Shot</c> node flies from the muzzle to the point the
/// attack hit, tumbling a little on a shallow arc, sticks there for a moment and shrinks away. The hit is already
/// resolved on the host; this only shows it. It frees itself when it has played out.
/// </summary>
public partial class BotShot : Node3D
{
    // Flight speed (m/s), the arc's height per metre flown, how long it stays stuck and how long it takes to shrink (s).
    private const float Speed = 32f;
    private const float ArcPerMetre = 0.03f;
    private const float StickSeconds = 0.6f;
    private const float ShrinkSeconds = 0.25f;

    // Turns it makes about its flight line per second, and how far it wobbles off it (rad).
    private const float Spin = 2.5f;
    private const float Wobble = 0.35f;

    private Vector3 _from;
    private Vector3 _to;
    private float _flight;
    private float _age;
    private Node3D _piece = null!;
    private Vector3 _scale = Vector3.One;

    /// <summary>Where it ends up, in world space.</summary>
    public Vector3 Target => _to;

    /// <summary>Whether it has reached <see cref="Target"/>.</summary>
    public bool Landed => _age >= _flight;

    /// <summary>
    /// Flies a copy of <paramref name="shot"/> (the model's <c>Shot</c> node, as posed on the bot now) from where it
    /// is to <paramref name="to"/>, under <paramref name="parent"/>.
    /// </summary>
    public static BotShot Show(Node parent, Node3D shot, Vector3 to)
    {
        var piece = (Node3D)shot.Duplicate();
        piece.Transform = Transform3D.Identity;
        piece.Visible = true;
        var from = shot.GlobalPosition;
        var effect = new BotShot
        {
            Name = "BotShot",
            _from = from,
            _to = to,
            _flight = Mathf.Max(from.DistanceTo(to) / Speed, 0.05f),
            _piece = piece,
            _scale = shot.GlobalBasis.Scale,
        };
        parent.AddChild(effect);
        effect.AddChild(piece);
        effect.Place(0f);
        return effect;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= _flight + StickSeconds + ShrinkSeconds)
        {
            QueueFree();
            return;
        }
        Place(Mathf.Min(_age / _flight, 1f));
        var shrink = Mathf.Clamp((_age - _flight - StickSeconds) / ShrinkSeconds, 0f, 1f);
        _piece.Scale = _scale * Mathf.Max(1f - shrink, 0.001f);
    }

    // Puts it at fraction t of its flight, facing along its path (-Z forward, as the model's front).
    private void Place(float t)
    {
        var dir = _to - _from;
        var length = dir.Length();
        var arc = 4f * t * (1f - t) * ArcPerMetre * length;
        GlobalPosition = _from.Lerp(_to, t) + Vector3.Up * arc;
        if (length < 0.01f)
            return;
        var forward = (dir / length) + Vector3.Up * (ArcPerMetre * 4f * (1f - 2f * t));
        var up = Mathf.Abs(forward.Normalized().Dot(Vector3.Up)) > 0.98f ? Vector3.Forward : Vector3.Up;
        var basis = Basis.LookingAt(forward, up);
        var flown = Mathf.Min(_age, _flight);
        basis *= new Basis(Vector3.Forward, flown * Spin * Mathf.Tau) * new Basis(Vector3.Right, Mathf.Sin(flown * 9f) * Wobble);
        GlobalBasis = basis;
    }
}
