using System.Security.Cryptography;
using System.Text;

namespace Mechanica.Core.Net;

/// <summary>
/// The password a host can set for its world (UI spec §16 Gameplay, "Server password"). A joining client
/// sends it in its handshake and the host checks it before anything else of the player's is accepted.
/// It keeps strangers on the network out of a world; it is sent unencrypted, like the rest of the game's
/// traffic, so it is not for anything secret.
/// </summary>
public static class NetPassword
{
    /// <summary>Longest password accepted.</summary>
    public const int MaxLength = 64;

    /// <summary>The refusal when a password is needed and none was given.</summary>
    public const string Needed = "This world needs a password.";

    /// <summary>The refusal when the password is wrong.</summary>
    public const string Wrong = "Wrong password.";

    /// <summary>
    /// Null when <paramref name="given"/> lets a player into a world whose password is
    /// <paramref name="expected"/> (empty lets anyone in), else why not. Compared in constant time.
    /// </summary>
    public static string? Check(string expected, string? given)
    {
        if (expected.Length == 0)
            return null;
        if (string.IsNullOrEmpty(given))
            return Needed;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(given));
        return CryptographicOperations.FixedTimeEquals(a, b) ? null : Wrong;
    }

    /// <summary><paramref name="password"/> cut to <see cref="MaxLength"/>; null becomes empty.</summary>
    public static string Clamp(string? password) =>
        password is null ? "" : password.Length > MaxLength ? password[..MaxLength] : password;
}
