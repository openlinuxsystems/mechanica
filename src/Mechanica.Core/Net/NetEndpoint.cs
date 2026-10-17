using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Mechanica.Core.Net;

/// <summary>A host name or IP address plus a UDP port, as typed into "Join" (<c>host:port</c>).</summary>
/// <param name="Address">Host name or IP address, without IPv6 brackets.</param>
/// <param name="Port">UDP port, 1 to 65535.</param>
public readonly record struct NetEndpoint(string Address, int Port)
{
    /// <summary>
    /// Parses <c>host</c>, <c>host:port</c>, <c>[ipv6]</c>, <c>[ipv6]:port</c> or a bare IPv6 address.
    /// A missing port becomes <paramref name="defaultPort"/>.
    /// </summary>
    public static bool TryParse(string? text, int defaultPort, [NotNullWhen(true)] out NetEndpoint? endpoint, out string error)
    {
        endpoint = null;
        error = "";
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            error = "Enter an address, for example 192.168.1.20:27460.";
            return false;
        }

        string address;
        string? portText = null;
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close < 0)
            {
                error = $"'{text}' is missing a closing ']'.";
                return false;
            }
            address = text[1..close];
            var rest = text[(close + 1)..];
            if (rest.Length > 0)
            {
                if (rest[0] != ':')
                {
                    error = $"Unexpected '{rest}' after the address.";
                    return false;
                }
                portText = rest[1..];
            }
        }
        else
        {
            var colon = text.IndexOf(':');
            if (colon >= 0 && colon == text.LastIndexOf(':'))
            {
                address = text[..colon];
                portText = text[(colon + 1)..];
            }
            else
            {
                address = text; // no colon, or a bare IPv6 address
            }
        }

        if (address.Length == 0 || address.Length > 253 || address.Any(char.IsWhiteSpace))
        {
            error = $"'{address}' is not a valid address.";
            return false;
        }

        var port = defaultPort;
        if (portText is not null && !TryParsePort(portText, out port))
        {
            error = $"'{portText}' is not a valid port (1-65535).";
            return false;
        }

        endpoint = new NetEndpoint(address, port);
        return true;
    }

    /// <summary>Parses a port number in 1 to 65535.</summary>
    public static bool TryParsePort(string? text, out int port) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;

    /// <summary>The <c>host:port</c> form, with brackets around IPv6 addresses.</summary>
    public override string ToString() =>
        Address.Contains(':') ? $"[{Address}]:{Port}" : $"{Address}:{Port}";
}
