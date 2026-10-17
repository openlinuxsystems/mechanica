using System.Buffers.Binary;
using System.Numerics;
using Mechanica.Core.Characters;

namespace Mechanica.Core.Net;

/// <summary>One player's simulated state as the host sees it.</summary>
/// <param name="Peer">Owning peer id.</param>
/// <param name="AckSequence">Last input sequence from that peer the state includes (0 for the host's own player).</param>
/// <param name="Position">Feet position in metres.</param>
/// <param name="Velocity">Velocity in m/s.</param>
/// <param name="Yaw">Body yaw in radians.</param>
/// <param name="Pitch">Look pitch in radians.</param>
/// <param name="Body">How the player's character stands, for its animation.</param>
/// <param name="Prop">What the player holds.</param>
/// <param name="Gesture">The player's newest gesture.</param>
/// <param name="GestureCount">Counts the player's gestures.</param>
public readonly record struct PlayerState(int Peer, uint AckSequence, Vector3 Position, Vector3 Velocity, float Yaw, float Pitch,
    BodyFlags Body = BodyFlags.None, CharacterProp Prop = CharacterProp.None, CharacterGesture Gesture = CharacterGesture.None, byte GestureCount = 0);

/// <summary>How a player's body is, as the host sees it, so clients animate other players the same way.</summary>
[Flags]
public enum BodyFlags : byte
{
    /// <summary>In the air, on dry land, standing.</summary>
    None = 0,

    /// <summary>Standing on something.</summary>
    OnFloor = 1,

    /// <summary>In deep water.</summary>
    Swimming = 2,

    /// <summary>Crouching.</summary>
    Crawl = 4,

    /// <summary>In a seat.</summary>
    Seated = 8,

    /// <summary>Every flag.</summary>
    All = OnFloor | Swimming | Crawl | Seated,
}

/// <summary>All players at one host physics tick.</summary>
/// <param name="Tick">Host physics tick the states belong to.</param>
/// <param name="Players">One entry per spawned player.</param>
public sealed record WorldSnapshot(uint Tick, IReadOnlyList<PlayerState> Players)
{
    /// <summary>Most players a snapshot may carry.</summary>
    public const int MaxPlayers = 64;

    private const int EntrySize = 4 + 4 + 12 + 12 + 4 + 4 + 4;

    /// <summary>The binary form sent host → clients.</summary>
    public byte[] Encode()
    {
        if (Players.Count > MaxPlayers)
            throw new InvalidOperationException($"A snapshot carries at most {MaxPlayers} players.");
        var bytes = new byte[5 + Players.Count * EntrySize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Tick);
        bytes[4] = (byte)Players.Count;
        var span = bytes.AsSpan(5);
        foreach (var p in Players)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span, p.Peer);
            BinaryPrimitives.WriteUInt32LittleEndian(span[4..], p.AckSequence);
            WriteVector(span[8..], p.Position);
            WriteVector(span[20..], p.Velocity);
            BinaryPrimitives.WriteSingleLittleEndian(span[32..], p.Yaw);
            BinaryPrimitives.WriteSingleLittleEndian(span[36..], p.Pitch);
            span[40] = (byte)p.Body;
            span[41] = (byte)p.Prop;
            span[42] = (byte)p.Gesture;
            span[43] = p.GestureCount;
            span = span[EntrySize..];
        }
        return bytes;
    }

    /// <summary>Decodes a snapshot, or returns null when it is malformed or holds non-finite numbers.</summary>
    public static WorldSnapshot? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 5)
            return null;
        var tick = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        int count = bytes[4];
        if (count > MaxPlayers || bytes.Length != 5 + count * EntrySize)
            return null;
        var players = new PlayerState[count];
        var span = bytes[5..];
        for (var i = 0; i < count; i++)
        {
            var state = new PlayerState(
                BinaryPrimitives.ReadInt32LittleEndian(span),
                BinaryPrimitives.ReadUInt32LittleEndian(span[4..]),
                ReadVector(span[8..]),
                ReadVector(span[20..]),
                BinaryPrimitives.ReadSingleLittleEndian(span[32..]),
                BinaryPrimitives.ReadSingleLittleEndian(span[36..]),
                (BodyFlags)span[40],
                CharacterRig.Sanitize((CharacterProp)span[41]),
                CharacterRig.Sanitize((CharacterGesture)span[42]),
                span[43]);
            if (((byte)state.Body & ~(byte)BodyFlags.All) != 0)
                return null;
            if (state.Peer < 1 || !IsFinite(state.Position) || !IsFinite(state.Velocity)
                || !float.IsFinite(state.Yaw) || !float.IsFinite(state.Pitch))
                return null;
            players[i] = state;
            span = span[EntrySize..];
        }
        return new WorldSnapshot(tick, players);
    }

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
