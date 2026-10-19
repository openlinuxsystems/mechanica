using System.Buffers.Binary;
using Mechanica.Core.Characters;
using Mechanica.Core.Player;

namespace Mechanica.Core.Net;

/// <summary>One tick of a client's input, numbered so the host can order, dedupe and acknowledge it.</summary>
/// <param name="Sequence">Client-side tick number, starting at 1 and increasing by one per physics tick.</param>
/// <param name="Input">The raw input. The host sanitizes it before simulating.</param>
public readonly record struct SequencedInput(uint Sequence, PlayerInput Input);

/// <summary>
/// Binary layout of the client → host input RPC. Each packet carries the newest few inputs so a lost
/// packet is covered by the next one. Decoding never throws: anything malformed returns null and the
/// host drops it.
/// </summary>
public static class InputPacket
{
    /// <summary>Most inputs one packet may carry.</summary>
    public const int MaxInputs = 8;

    private const int EntrySize = 4 + 4 * 4 + 1 + 4 + 2;
    private const byte JumpFlag = 1;
    private const byte SprintFlag = 2;
    private const byte CrawlFlag = 4;
    private const byte GuardFlag = 8;
    private const byte AllFlags = JumpFlag | SprintFlag | CrawlFlag | GuardFlag;

    /// <summary>Encodes 1 to <see cref="MaxInputs"/> inputs.</summary>
    public static byte[] Encode(IReadOnlyList<SequencedInput> inputs)
    {
        if (inputs.Count is 0 or > MaxInputs)
            throw new ArgumentException($"An input packet carries 1 to {MaxInputs} inputs.", nameof(inputs));
        var bytes = new byte[1 + inputs.Count * EntrySize];
        bytes[0] = (byte)inputs.Count;
        var span = bytes.AsSpan(1);
        foreach (var (sequence, input) in inputs)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span, sequence);
            BinaryPrimitives.WriteSingleLittleEndian(span[4..], input.MoveX);
            BinaryPrimitives.WriteSingleLittleEndian(span[8..], input.MoveZ);
            BinaryPrimitives.WriteSingleLittleEndian(span[12..], input.Yaw);
            BinaryPrimitives.WriteSingleLittleEndian(span[16..], input.Pitch);
            span[20] = (byte)((input.Jump ? JumpFlag : 0) | (input.Sprint ? SprintFlag : 0) | (input.Crawl ? CrawlFlag : 0) | (input.Guard ? GuardFlag : 0));
            span[21] = (byte)input.Prop;
            span[22] = (byte)input.Gesture;
            span[23] = input.GestureCount;
            span[24] = input.Tint;
            BinaryPrimitives.WriteUInt16LittleEndian(span[25..], input.Held);
            span = span[EntrySize..];
        }
        return bytes;
    }

    /// <summary>Decodes a packet, or returns null when the length, count or flags are wrong.</summary>
    public static SequencedInput[]? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 1)
            return null;
        int count = bytes[0];
        if (count is 0 or > MaxInputs || bytes.Length != 1 + count * EntrySize)
            return null;
        var result = new SequencedInput[count];
        var span = bytes[1..];
        for (var i = 0; i < count; i++)
        {
            var flags = span[20];
            if ((flags & ~AllFlags) != 0)
                return null;
            result[i] = new SequencedInput(
                BinaryPrimitives.ReadUInt32LittleEndian(span),
                new PlayerInput(
                    BinaryPrimitives.ReadSingleLittleEndian(span[4..]),
                    BinaryPrimitives.ReadSingleLittleEndian(span[8..]),
                    BinaryPrimitives.ReadSingleLittleEndian(span[12..]),
                    BinaryPrimitives.ReadSingleLittleEndian(span[16..]),
                    (flags & JumpFlag) != 0,
                    (flags & SprintFlag) != 0,
                    (flags & CrawlFlag) != 0,
                    (CharacterProp)span[21],
                    (CharacterGesture)span[22],
                    span[23],
                    (flags & GuardFlag) != 0,
                    span[24],
                    BinaryPrimitives.ReadUInt16LittleEndian(span[25..])));
            span = span[EntrySize..];
        }
        return result;
    }
}
