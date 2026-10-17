namespace Mechanica.Core.Net;

/// <summary>
/// Constants shared by host and clients. Bump <see cref="Version"/> whenever an RPC signature or a
/// packet layout changes; the host refuses clients with a different version.
/// </summary>
public static class NetProtocol
{
    /// <summary>Wire protocol version, checked in the join handshake.</summary>
    public const int Version = 1;

    /// <summary>UDP port used when none is given.</summary>
    public const int DefaultPort = 27460;

    /// <summary>Players per session, host included (the spec's 4-player target).</summary>
    public const int DefaultMaxPlayers = 4;

    /// <summary>Largest allowed <c>--max-players</c>.</summary>
    public const int MaxPlayersLimit = 16;

    /// <summary>Godot peer id of the host.</summary>
    public const int HostPeer = 1;

    /// <summary>Godot's default channel, used for reliable control messages (handshake, spawns, actions).</summary>
    public const int ControlChannel = 0;

    /// <summary>Channel for client input packets (reliable, kept apart so control traffic never waits on it).</summary>
    public const int InputChannel = 1;

    /// <summary>Channel for host snapshots (unreliable, newest wins).</summary>
    public const int SnapshotChannel = 2;

    /// <summary>Custom channels the transport opens on top of Godot's own (<see cref="InputChannel"/> and <see cref="SnapshotChannel"/>).</summary>
    public const int ChannelCount = 2;

    /// <summary>The host sends a snapshot every this many physics ticks (30 Hz at 60 Hz physics).</summary>
    public const int SnapshotInterval = 2;

    /// <summary>Seconds a connected peer has to complete the handshake before it is dropped.</summary>
    public const double HandshakeTimeoutSeconds = 5;
}
