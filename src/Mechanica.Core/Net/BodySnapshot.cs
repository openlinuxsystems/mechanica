using System.Buffers.Binary;
using System.Numerics;

namespace Mechanica.Core.Net;

/// <summary>One rigid Body's simulated state as its authority sees it, in world space.</summary>
/// <param name="Position">Origin of the Body's grid frame in metres (its <see cref="Creations.Pose"/> position).</param>
/// <param name="Rotation">Unit quaternion of the Body's grid frame.</param>
/// <param name="LinearVelocity">m/s.</param>
/// <param name="AngularVelocity">rad/s (axis times rate).</param>
public readonly record struct BodyState(Vector3 Position, Quaternion Rotation, Vector3 LinearVelocity, Vector3 AngularVelocity);

/// <summary>
/// A run of one Creation's Bodies in a <see cref="BodySnapshot"/>. A large Creation may be cut into
/// several runs across packets; <see cref="FirstBody"/> says where this one starts.
/// </summary>
/// <param name="CreationId">The Creation.</param>
/// <param name="Revision">The sender's <see cref="Creations.Creation.Revision"/> (low 16 bits); a receiver
/// holding a different graph revision ignores the run.</param>
/// <param name="FirstBody">Index into <see cref="Creations.Creation.Bodies"/> of the first state.</param>
/// <param name="Bodies">States of consecutive Bodies.</param>
public sealed record CreationBodies(int CreationId, ushort Revision, int FirstBody, IReadOnlyList<BodyState> Bodies);

/// <summary>
/// Body states of some Creations at one host physics tick, sent host → client unreliably (D10). Packets
/// are kept under a size limit (<see cref="Pack"/>): ENet drops a whole unreliable packet when any of
/// its fragments is lost, so a packet that fits one datagram survives loss far better. Each packet stands
/// alone, so losing one costs only the Bodies it carried for one tick, which interpolation covers.
/// A state is <see cref="StateSize"/> bytes: the position as floats, the rotation as its three smallest
/// components (15 bits each, about 0.005° apart) and the velocities, which only carry a Body on past a
/// late packet, as half floats.
/// </summary>
/// <param name="Tick">Host physics tick the states belong to.</param>
/// <param name="Creations">Runs of Body states.</param>
public sealed record BodySnapshot(uint Tick, IReadOnlyList<CreationBodies> Creations)
{
    /// <summary>Bytes before the first run: tick and run count.</summary>
    public const int HeaderSize = 4 + 1;

    /// <summary>Bytes of a run before its states: creation id, revision, first body, state count.</summary>
    public const int RunHeaderSize = 4 + 2 + 2 + 1;

    /// <summary>Bytes per Body state: position as floats, rotation in 6 bytes, linear and angular velocity as half floats.</summary>
    public const int StateSize = 12 + RotationSize + 6 + 6;

    private const int RotationSize = 6;

    // Largest velocity component a half float carries (its largest finite value is 65504).
    private const float MaxHalf = 65000f;

    // The three smaller components of a unit quaternion lie within ±1/√2; each is stored in 15 bits over that range.
    private const float SmallestRange = 0.70710678f;
    private const int SmallestSteps = (1 << 15) - 1;

    /// <summary>Most runs one packet may hold.</summary>
    public const int MaxRuns = byte.MaxValue;

    /// <summary>Most states one run may hold.</summary>
    public const int MaxStatesPerRun = byte.MaxValue;

    /// <summary>Smallest packet limit <see cref="Pack"/> accepts: one run of one state.</summary>
    public const int MinPacketBytes = HeaderSize + RunHeaderSize + StateSize;

    /// <summary>Encoded size in bytes.</summary>
    public int Size => HeaderSize + Creations.Sum(c => RunHeaderSize + c.Bodies.Count * StateSize);

    /// <summary>
    /// Splits the states of many Creations into packets of at most <paramref name="maxBytes"/> each, in
    /// order. A Creation that fits in the space left goes in whole; one that does not starts a new packet,
    /// and one bigger than a packet is cut into runs.
    /// </summary>
    public static List<BodySnapshot> Pack(uint tick, IEnumerable<CreationBodies> creations, int maxBytes)
    {
        if (maxBytes < MinPacketBytes)
            throw new ArgumentOutOfRangeException(nameof(maxBytes), $"A packet needs at least {MinPacketBytes} bytes.");
        var packets = new List<BodySnapshot>();
        var runs = new List<CreationBodies>();
        var size = HeaderSize;

        void Flush()
        {
            if (runs.Count == 0)
                return;
            packets.Add(new BodySnapshot(tick, runs));
            runs = [];
            size = HeaderSize;
        }

        foreach (var creation in creations)
        {
            var whole = RunHeaderSize + creation.Bodies.Count * StateSize;
            if (size + whole > maxBytes && whole <= maxBytes - HeaderSize)
                Flush();
            var next = 0;
            while (next < creation.Bodies.Count)
            {
                if (runs.Count == MaxRuns || size + RunHeaderSize + StateSize > maxBytes)
                    Flush();
                var count = Math.Min(Math.Min((maxBytes - size - RunHeaderSize) / StateSize, MaxStatesPerRun), creation.Bodies.Count - next);
                runs.Add(creation with
                {
                    FirstBody = creation.FirstBody + next,
                    // Most Creations fit in one run whole.
                    Bodies = next == 0 && count == creation.Bodies.Count ? creation.Bodies : creation.Bodies.Skip(next).Take(count).ToList(),
                });
                size += RunHeaderSize + count * StateSize;
                next += count;
            }
        }
        Flush();
        return packets;
    }

    /// <summary>The binary form.</summary>
    public byte[] Encode()
    {
        if (Creations.Count > MaxRuns)
            throw new InvalidOperationException($"A body snapshot carries at most {MaxRuns} runs.");
        var bytes = new byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Tick);
        bytes[4] = (byte)Creations.Count;
        var span = bytes.AsSpan(HeaderSize);
        foreach (var run in Creations)
        {
            if (run.Bodies.Count > MaxStatesPerRun || run.FirstBody is < 0 or > ushort.MaxValue)
                throw new InvalidOperationException($"Run for creation {run.CreationId} is out of range.");
            BinaryPrimitives.WriteInt32LittleEndian(span, run.CreationId);
            BinaryPrimitives.WriteUInt16LittleEndian(span[4..], run.Revision);
            BinaryPrimitives.WriteUInt16LittleEndian(span[6..], (ushort)run.FirstBody);
            span[8] = (byte)run.Bodies.Count;
            span = span[RunHeaderSize..];
            foreach (var state in run.Bodies)
            {
                WriteVector(span, state.Position);
                WriteRotation(span[12..], state.Rotation);
                WriteHalves(span[18..], state.LinearVelocity);
                WriteHalves(span[24..], state.AngularVelocity);
                span = span[StateSize..];
            }
        }
        return bytes;
    }

    /// <summary>
    /// Decodes a packet, or returns null when it is malformed: wrong length, a non-positive creation id,
    /// non-finite numbers, or a rotation whose stored components cannot be the smallest three of a unit quaternion.
    /// Positions are kept bit for bit; rotations and velocities to their stored precision.
    /// </summary>
    public static BodySnapshot? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
            return null;
        var tick = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        int count = bytes[4];
        var span = bytes[HeaderSize..];
        var runs = new CreationBodies[count];
        for (var i = 0; i < count; i++)
        {
            if (span.Length < RunHeaderSize)
                return null;
            var id = BinaryPrimitives.ReadInt32LittleEndian(span);
            var revision = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
            int first = BinaryPrimitives.ReadUInt16LittleEndian(span[6..]);
            int states = span[8];
            span = span[RunHeaderSize..];
            if (id < 1 || span.Length < states * StateSize)
                return null;
            var bodies = new BodyState[states];
            for (var j = 0; j < states; j++)
            {
                if (ReadRotation(span[12..]) is not { } rotation)
                    return null;
                var state = new BodyState(ReadVector(span), rotation, ReadHalves(span[18..]), ReadHalves(span[24..]));
                if (!IsFinite(state.Position) || !IsFinite(state.LinearVelocity) || !IsFinite(state.AngularVelocity))
                    return null;
                bodies[j] = state;
                span = span[StateSize..];
            }
            runs[i] = new CreationBodies(id, revision, first, bodies);
        }
        return span.IsEmpty ? new BodySnapshot(tick, runs) : null;
    }

    // Smallest three: which component is largest (2 bits), then the other three, 15 bits each, with the largest made
    // positive (q and -q are the same rotation) so it can be worked out from them.
    private static void WriteRotation(Span<byte> span, Quaternion q)
    {
        q = Quaternion.Normalize(q);
        Span<float> c = [q.X, q.Y, q.Z, q.W];
        var largest = 0;
        for (var i = 1; i < 4; i++)
        {
            if (MathF.Abs(c[i]) > MathF.Abs(c[largest]))
                largest = i;
        }
        var sign = c[largest] < 0 ? -1f : 1f;
        ulong bits = (ulong)largest;
        for (var i = 0; i < 4; i++)
        {
            if (i == largest)
                continue;
            var unit = Math.Clamp(sign * c[i] / SmallestRange * 0.5f + 0.5f, 0f, 1f);
            bits = (bits << 15) | (ulong)MathF.Round(unit * SmallestSteps);
        }
        for (var i = 0; i < RotationSize; i++)
            span[i] = (byte)(bits >> (8 * i));
    }

    private static Quaternion? ReadRotation(ReadOnlySpan<byte> span)
    {
        ulong bits = 0;
        for (var i = 0; i < RotationSize; i++)
            bits |= (ulong)span[i] << (8 * i);
        var largest = (int)(bits >> 45) & 3;
        Span<float> c = stackalloc float[4];
        var sum = 0f;
        for (var i = 3; i >= 0; i--)
        {
            if (i == largest)
                continue;
            c[i] = ((bits & (ulong)SmallestSteps) / (float)SmallestSteps - 0.5f) * 2f * SmallestRange;
            sum += c[i] * c[i];
            bits >>= 15;
        }
        // The largest of four squares adding to one is at least a quarter, so the rest add to at most three quarters.
        if (sum > 0.751f)
            return null;
        c[largest] = MathF.Sqrt(1f - sum);
        return Quaternion.Normalize(new Quaternion(c[0], c[1], c[2], c[3]));
    }

    private static void WriteHalves(Span<byte> span, Vector3 v)
    {
        BinaryPrimitives.WriteHalfLittleEndian(span, (Half)Math.Clamp(v.X, -MaxHalf, MaxHalf));
        BinaryPrimitives.WriteHalfLittleEndian(span[2..], (Half)Math.Clamp(v.Y, -MaxHalf, MaxHalf));
        BinaryPrimitives.WriteHalfLittleEndian(span[4..], (Half)Math.Clamp(v.Z, -MaxHalf, MaxHalf));
    }

    private static Vector3 ReadHalves(ReadOnlySpan<byte> span) => new(
        (float)BinaryPrimitives.ReadHalfLittleEndian(span),
        (float)BinaryPrimitives.ReadHalfLittleEndian(span[2..]),
        (float)BinaryPrimitives.ReadHalfLittleEndian(span[4..]));

    private static void WriteVector(Span<byte> span, Vector3 v)
    {
        BinaryPrimitives.WriteSingleLittleEndian(span, v.X);
        BinaryPrimitives.WriteSingleLittleEndian(span[4..], v.Y);
        BinaryPrimitives.WriteSingleLittleEndian(span[8..], v.Z);
    }

    private static Vector3 ReadVector(ReadOnlySpan<byte> span) => new(
        BinaryPrimitives.ReadSingleLittleEndian(span),
        BinaryPrimitives.ReadSingleLittleEndian(span[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(span[8..]));

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
