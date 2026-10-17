using System.Numerics;

namespace Mechanica.Core.Net;

/// <summary>
/// Host-side seat allocation: each joined peer gets the lowest free slot, which decides where it
/// spawns so players don't appear inside each other. A full session refuses new peers.
/// </summary>
public sealed class PlayerSlots
{
    /// <summary>Metres between neighbouring spawn spots.</summary>
    public const float SpawnSpacing = 1.5f;

    private readonly int?[] _peers;

    /// <summary>Creates slots for <paramref name="maxPlayers"/> players, host included.</summary>
    public PlayerSlots(int maxPlayers)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPlayers, 1);
        _peers = new int?[maxPlayers];
    }

    /// <summary>Total slots.</summary>
    public int Capacity => _peers.Length;

    /// <summary>Slots in use.</summary>
    public int Count => _peers.Count(p => p is not null);

    /// <summary>Gives <paramref name="peer"/> a slot (its existing one if it has one), or null when full.</summary>
    public int? Claim(int peer)
    {
        if (SlotOf(peer) is { } existing)
            return existing;
        var free = Array.IndexOf(_peers, null);
        if (free < 0)
            return null;
        _peers[free] = peer;
        return free;
    }

    /// <summary>Frees the slot held by <paramref name="peer"/>, if any.</summary>
    public void Release(int peer)
    {
        if (SlotOf(peer) is { } slot)
            _peers[slot] = null;
    }

    /// <summary>The slot held by <paramref name="peer"/>, or null.</summary>
    public int? SlotOf(int peer)
    {
        var index = Array.IndexOf(_peers, peer);
        return index < 0 ? null : index;
    }

    /// <summary>
    /// Offset from the spawn point for a slot, in metres: slot 0 on the point, then alternating
    /// left and right along X.
    /// </summary>
    public static Vector3 SpawnOffset(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        var step = (slot + 1) / 2;
        var side = slot % 2 == 1 ? 1 : -1;
        return new Vector3(side * step * SpawnSpacing, 0f, 0f);
    }
}
