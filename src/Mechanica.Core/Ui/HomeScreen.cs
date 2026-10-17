using System.Text.Json;
using Mechanica.Core.Data;

namespace Mechanica.Core.Ui;

/// <summary>One link at the bottom right of the home screen.</summary>
/// <param name="Label">A short word for the button.</param>
/// <param name="Url">An https address.</param>
public sealed record HomeLink(string Label, string Url);

/// <summary>
/// What the home screen (UI spec §14) shows besides its buttons: the community links from
/// <c>game/data/links.json</c>, and how long ago a world was last played.
/// </summary>
public static class HomeScreen
{
    private sealed record LinksFile
    {
        public List<HomeLink>? Links { get; init; }
    }

    /// <summary>The links in <paramref name="json"/>; entries without a label or an https address are dropped.</summary>
    public static IReadOnlyList<HomeLink> ParseLinks(string json)
    {
        try
        {
            return [.. (DataJson.Read<LinksFile>(json)?.Links ?? [])
                .Where(l => !string.IsNullOrWhiteSpace(l?.Label)
                    && Uri.TryCreate(l.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// "just now", "5 minutes ago", "3 hours ago", "yesterday", "4 days ago", or the date. <paramref name="text"/>
    /// fills a template with its values in the language in use; English when left out.
    /// </summary>
    public static string Ago(DateTimeOffset then, DateTimeOffset now, Func<string, object?[], string>? text = null)
    {
        text ??= Language.Format;
        var span = now - then;
        return span.TotalMinutes switch
        {
            < 1 => text("just now", []),
            < 2 => text("a minute ago", []),
            < 60 => text("{0} minutes ago", [(int)span.TotalMinutes]),
            < 120 => text("an hour ago", []),
            < 24 * 60 => text("{0} hours ago", [(int)span.TotalHours]),
            < 48 * 60 => text("yesterday", []),
            < 7 * 24 * 60 => text("{0} days ago", [(int)span.TotalDays]),
            _ => then.ToLocalTime().ToString("yyyy-MM-dd"),
        };
    }
}
