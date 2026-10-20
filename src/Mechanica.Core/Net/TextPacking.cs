using System.IO.Compression;
using System.Text;

namespace Mechanica.Core.Net;

/// <summary>
/// Compresses large text messages (a whole Creation's JSON in a full sync) before they go over the network: save
/// JSON packs to about a tenth of its size, so a client near a big build is not sent tens of kilobytes again on every
/// change. Brotli at its fastest level, which still costs the host well under a millisecond for a large Creation.
/// </summary>
public static class TextPacking
{
    /// <summary>Largest unpacked text accepted, in bytes; more is treated as malformed.</summary>
    public const int MaxUnpackedBytes = 64 * 1024 * 1024;

    /// <summary>The UTF-8 bytes of <paramref name="text"/>, compressed.</summary>
    public static byte[] Pack(string text)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
            brotli.Write(Encoding.UTF8.GetBytes(text));
        return output.ToArray();
    }

    /// <summary>
    /// The text <see cref="Pack"/> made, or null when <paramref name="packed"/> is not valid Brotli or unpacks to more
    /// than <paramref name="maxBytes"/>.
    /// </summary>
    public static string? Unpack(byte[] packed, int maxBytes = MaxUnpackedBytes)
    {
        try
        {
            using var input = new MemoryStream(packed, writable: false);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = brotli.Read(buffer)) > 0)
            {
                if (output.Length + read > maxBytes)
                    return null;
                output.Write(buffer, 0, read);
            }
            return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
