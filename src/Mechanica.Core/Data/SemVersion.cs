using System.Diagnostics.CodeAnalysis;

namespace Mechanica.Core.Data;

/// <summary>
/// A semantic version (<c>major.minor.patch</c> with an optional <c>-prerelease</c> tag), used for
/// mod versions. Build metadata (<c>+...</c>) is not supported.
/// </summary>
public sealed record SemVersion(int Major, int Minor, int Patch, string? Prerelease = null) : IComparable<SemVersion>
{
    /// <summary>Parses <c>1.2.3</c> or <c>1.2.3-beta.1</c>.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out SemVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string? prerelease = null;
        var core = text;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];
            core = text[..dash];
            if (prerelease.Length == 0 || !prerelease.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
                return false;
        }
        var parts = core.Split('.');
        if (parts.Length != 3)
            return false;
        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit) || (parts[i].Length > 1 && parts[i][0] == '0')
                || !int.TryParse(parts[i], out numbers[i]))
                return false;
        }
        version = new SemVersion(numbers[0], numbers[1], numbers[2], prerelease);
        return true;
    }

    /// <summary>Parses a version or throws <see cref="FormatException"/>.</summary>
    public static SemVersion Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"'{text}' is not a valid version (expected major.minor.patch).");

    /// <inheritdoc />
    public int CompareTo(SemVersion? other)
    {
        if (other is null)
            return 1;
        var c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        // A release sorts after any of its prereleases.
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            var aNum = int.TryParse(pa[i], out var na);
            var bNum = int.TryParse(pb[i], out var nb);
            var c = (aNum, bNum) switch
            {
                (true, true) => na.CompareTo(nb),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(pa[i], pb[i]),
            };
            if (c != 0) return c;
        }
        return pa.Length.CompareTo(pb.Length);
    }

    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(SemVersion a, SemVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(SemVersion a, SemVersion b) => a.CompareTo(b) >= 0;

    /// <inheritdoc />
    public override string ToString() => Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}

/// <summary>
/// A dependency version requirement. Supported forms, combinable with spaces (all must hold):
/// <c>*</c>, <c>1.2.3</c> (exact), <c>&gt;=1.2.3</c>, <c>&gt;1.2.3</c>, <c>&lt;=1.2.3</c>, <c>&lt;2.0.0</c>,
/// <c>^1.2.3</c> (same major; same minor while major is 0) and <c>~1.2.3</c> (same minor).
/// </summary>
public sealed class VersionRange
{
    private readonly List<(string Op, SemVersion Version)> _terms;

    private VersionRange(string text, List<(string, SemVersion)> terms)
    {
        Text = text;
        _terms = terms;
    }

    /// <summary>The range exactly as written.</summary>
    public string Text { get; }

    /// <summary>A range that every version satisfies.</summary>
    public static VersionRange Any { get; } = new("*", []);

    /// <summary>Parses a range expression.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out VersionRange? range)
    {
        range = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var terms = new List<(string, SemVersion)>();
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "*")
                continue;
            var op = token.StartsWith(">=") || token.StartsWith("<=") ? token[..2]
                : token[0] is '>' or '<' or '^' or '~' or '=' ? token[..1]
                : "=";
            var versionText = op == "=" && token[0] != '=' ? token : token[op.Length..];
            if (!SemVersion.TryParse(versionText, out var version))
                return false;
            terms.Add((op, version));
        }
        range = new VersionRange(text, terms);
        return true;
    }

    /// <summary>True when <paramref name="version"/> meets every term of the range.</summary>
    public bool IsSatisfiedBy(SemVersion version) => _terms.All(t => t.Op switch
    {
        "=" => version.CompareTo(t.Version) == 0,
        ">=" => version >= t.Version,
        ">" => version > t.Version,
        "<=" => version <= t.Version,
        "<" => version < t.Version,
        "^" => version >= t.Version && (t.Version.Major > 0
            ? version.Major == t.Version.Major
            : version.Major == 0 && version.Minor == t.Version.Minor),
        "~" => version >= t.Version && version.Major == t.Version.Major && version.Minor == t.Version.Minor,
        _ => false,
    });

    /// <inheritdoc />
    public override string ToString() => Text;
}
