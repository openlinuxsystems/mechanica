using System.Text;
using System.Text.RegularExpressions;

namespace Mechanica.Core.Docs;

/// <summary>
/// Converts the Markdown the docs are written in to Godot <c>RichTextLabel</c> BBCode, so the same
/// files ship as repo docs and as in-game help. It covers what the docs use: headings, paragraphs,
/// bullet and numbered lists, bold, italics, inline code, fenced code blocks, links and tables (shown
/// as aligned monospace text). Everything else passes through as plain text with brackets escaped. As in Markdown,
/// lines wrapped inside a paragraph or a list item join into one line, so the label wraps them to its own width.
/// </summary>
public static partial class MarkdownBbcode
{
    private static readonly int[] HeadingSizes = [26, 21, 18, 16, 15, 14];

    /// <summary>Converts a Markdown document.</summary>
    public static string Convert(string markdown)
    {
        var output = new StringBuilder();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal); i++)
                    code.Add(Escape(lines[i]));
                output.Append("[code]").Append(string.Join("\n", code)).Append("[/code]\n\n");
                continue;
            }
            if (line.TrimStart().StartsWith('|'))
            {
                var rows = new List<string>();
                for (; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
                    rows.Add(lines[i]);
                i--;
                output.Append(Table(rows)).Append('\n');
                continue;
            }
            if (Heading().Match(line) is { Success: true } heading)
            {
                var level = heading.Groups[1].Length;
                output.Append($"[font_size={HeadingSizes[level - 1]}][b]{Inline(heading.Groups[2].Value)}[/b][/font_size]\n");
                continue;
            }
            if (Bullet().Match(line) is { Success: true } bullet)
            {
                output.Append(bullet.Groups[1].Value).Append("• ").Append(Inline(Joined(bullet.Groups[2].Value, lines, ref i))).Append('\n');
                continue;
            }
            if (Numbered().Match(line) is { Success: true } numbered)
            {
                output.Append(numbered.Groups[1].Value).Append(numbered.Groups[2].Value).Append(". ")
                    .Append(Inline(Joined(numbered.Groups[3].Value, lines, ref i))).Append('\n');
                continue;
            }
            output.Append(line.Length == 0 ? "\n" : Inline(Joined(line, lines, ref i)) + "\n");
        }
        return output.ToString().TrimEnd('\n') + "\n";
    }

    // A paragraph or list item goes on for as long as the lines after it are plain text, which join with a space.
    private static string Joined(string first, string[] lines, ref int i)
    {
        var text = new StringBuilder(first.TrimEnd());
        while (i + 1 < lines.Length && Continues(lines[i + 1]))
            text.Append(' ').Append(lines[++i].Trim());
        return text.ToString();
    }

    private static bool Continues(string line) =>
        line.Trim().Length > 0 && !line.StartsWith("```", StringComparison.Ordinal) && !line.TrimStart().StartsWith('|')
        && !Heading().IsMatch(line) && !Bullet().IsMatch(line) && !Numbered().IsMatch(line);

    /// <summary>Escapes BBCode brackets in plain text.</summary>
    public static string Escape(string text) => text.Replace("[", "[lb]");

    // Code spans first so their contents are not formatted, then links, bold, italics.
    private static string Inline(string text)
    {
        var parts = text.Split('`');
        var output = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1 && i < parts.Length - 1)
            {
                output.Append("[code]").Append(Escape(parts[i])).Append("[/code]");
                continue;
            }
            var piece = i % 2 == 1 ? "`" + parts[i] : parts[i];
            var links = new List<string>();
            piece = Link().Replace(piece, m =>
            {
                links.Add($"[url={m.Groups[2].Value}]{Escape(m.Groups[1].Value)}[/url]");
                return $"\u0001{links.Count - 1}\u0001";
            });
            piece = Escape(piece);
            piece = Bold().Replace(piece, "[b]$1[/b]");
            piece = Italic().Replace(piece, "[i]$1[/i]");
            piece = Placeholder().Replace(piece, m => links[int.Parse(m.Groups[1].Value)]);
            output.Append(piece);
        }
        return output.ToString();
    }

    private static string Table(List<string> rows)
    {
        var cells = rows
            .Select(r => r.Trim().Trim('|').Split('|').Select(c => c.Trim().Replace("`", "")).ToList())
            .Where(r => !r.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':')))
            .ToList();
        var columns = cells.Max(r => r.Count);
        var widths = Enumerable.Range(0, columns).Select(c => cells.Max(r => c < r.Count ? r[c].Length : 0)).ToList();
        var lines = cells.Select((r, index) =>
        {
            var line = string.Join("  ", Enumerable.Range(0, columns).Select(c => (c < r.Count ? r[c] : "").PadRight(widths[c]))).TrimEnd();
            return index == 0 ? $"[b]{Escape(line)}[/b]" : Escape(line);
        });
        return "[code]" + string.Join("\n", lines) + "[/code]\n";
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\s*)[-*]\s+(.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^(\s*)(\d+)\.\s+(.*)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])")]
    private static partial Regex Italic();

    [GeneratedRegex("\u0001(\\d+)\u0001")]
    private static partial Regex Placeholder();
}
