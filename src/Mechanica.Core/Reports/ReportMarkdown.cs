using System.Globalization;
using System.Text;

namespace Mechanica.Core.Reports;

/// <summary>The Markdown form of reports, for people and AIs reading the reports folder.</summary>
public static class ReportMarkdown
{
    /// <summary>The heading word for <paramref name="kind"/>.</summary>
    public static string KindName(ReportKind kind) => kind switch
    {
        ReportKind.Note => "Note",
        ReportKind.Recommendation => "Recommendation",
        _ => "Bug",
    };

    /// <summary>One report as a Markdown page.</summary>
    public static string Render(AiReport report)
    {
        var md = new StringBuilder();
        md.Append("# ").Append(KindName(report.Kind)).Append(": ").AppendLine(report.Title).AppendLine();
        Item(md, "Id", $"`{report.Id}`");
        if (report.Severity is { } severity)
            Item(md, "Severity", Lower(severity));
        if (report.Priority is { } priority)
            Item(md, "Priority", Lower(priority));
        if (report.Area is { Length: > 0 } area)
            Item(md, "Area", area);
        Item(md, "Author", report.Author);
        Item(md, "Written", string.Create(CultureInfo.InvariantCulture,
            $"{report.Created.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC, tick {report.Context.Tick} ({report.Context.WorldTime:0.0} s of world time)"));
        if (report.Tags.Count > 0)
            Item(md, "Tags", string.Join(", ", report.Tags));
        md.AppendLine();
        if (report.Text.Length > 0)
            md.AppendLine(report.Text.TrimEnd()).AppendLine();
        if (report.Steps is { Count: > 0 } steps)
        {
            md.AppendLine("## Steps to reproduce").AppendLine();
            for (var i = 0; i < steps.Count; i++)
                md.Append(i + 1).Append(". ").AppendLine(steps[i]);
            md.AppendLine();
        }
        if (report.Expected is { Length: > 0 } expected)
            md.AppendLine("## Expected").AppendLine().AppendLine(expected.TrimEnd()).AppendLine();
        if (report.Actual is { Length: > 0 } actual)
            md.AppendLine("## Actual").AppendLine().AppendLine(actual.TrimEnd()).AppendLine();
        if (report.Screenshot is { } screenshot)
            md.AppendLine("## Screenshot").AppendLine().Append("![Screenshot](").Append(screenshot).AppendLine(")").AppendLine();
        else if (report.ScreenshotError is { } why)
            md.AppendLine("## Screenshot").AppendLine().Append("Not taken: ").AppendLine(why).AppendLine();
        if (report.ReplayFile is { } replay)
        {
            md.AppendLine("## Replay").AppendLine();
            md.Append('`').Append(replay).Append("` holds ").Append(report.ReplayTruncated == true ? "the last " : "all ").Append(report.ReplayActions ?? 0)
                .Append(report.ReplayTruncated == true
                    ? " state-changing calls before this report (earlier ones were dropped, so a replay may not reach the same state)."
                    : " state-changing calls since the world started.")
                .AppendLine(" Replay them on a fresh host with the same mods:").AppendLine();
            md.AppendLine("```sh").Append("godot --path game -- ").Append(ReplayFlags(report.Context)).Append("--replay ").AppendLine(replay).AppendLine("```").AppendLine();
        }
        if (report.Messages is { Count: > 0 } messages)
        {
            md.AppendLine("## Recent messages").AppendLine().AppendLine("```text");
            foreach (var m in messages)
                md.Append("[tick ").Append(m.Tick).Append("] ").Append(m.Source).Append(' ').Append(m.Level).Append(": ").AppendLine(m.Text.Replace("```", "'''"));
            md.AppendLine("```").AppendLine();
        }
        var c = report.Context;
        md.AppendLine("## Game").AppendLine();
        md.AppendLine("| | |").AppendLine("|---|---|");
        Row(md, "Version", $"{c.GameVersion} (protocol {c.ProtocolVersion})");
        Row(md, "World", c.World.Length > 0 ? $"{c.World}, seed {c.Seed}" : $"seed {c.Seed}");
        Row(md, "Mode", c.Difficulty.Length > 0 ? $"{c.Mode}, {c.Difficulty}" : c.Mode);
        Row(md, "Mods", c.Mods.Count > 0 ? string.Join(", ", c.Mods) : "none");
        Row(md, "Content", $"`{c.ContentFingerprint}`");
        Row(md, "Players", c.Players.Count > 0
            ? string.Join(", ", c.Players.Select(p => p.Position is { } at ? $"{p.Name} (peer {p.Peer}) at {at}" : $"{p.Name} (peer {p.Peer})"))
            : "none");
        Row(md, "Display", c.Headless ? "headless" : "yes");
        return md.ToString();
    }

    /// <summary>A session's <c>index.md</c>: every report in it, oldest first.</summary>
    public static string Index(string session, IReadOnlyList<ReportSummary> reports)
    {
        var md = new StringBuilder();
        md.Append("# AI reports, session ").AppendLine(session).AppendLine();
        if (reports.Count == 0)
            return md.AppendLine("No reports.").ToString();
        md.AppendLine("| Report | Kind | Severity or priority | Title |").AppendLine("|---|---|---|---|");
        foreach (var r in reports)
        {
            var file = r.Id[(r.Id.IndexOf('/') + 1)..];
            var level = r.Severity is { } s ? Lower(s) : r.Priority is { } p ? Lower(p) : "";
            md.Append("| [").Append(file).Append("](").Append(file).Append(".md) | ").Append(KindName(r.Kind)).Append(" | ")
                .Append(level).Append(" | ").Append(r.Title.Replace("|", "\\|")).AppendLine(" |");
        }
        return md.ToString();
    }

    /// <summary>The launch flags that start a host like the one in <paramref name="context"/>, ending in a space.</summary>
    public static string ReplayFlags(ReportContext context)
    {
        var flags = new StringBuilder("--host ");
        flags.Append("--seed ").Append(context.Seed.ToString(CultureInfo.InvariantCulture)).Append(' ');
        if (context.Mode is "creative" or "survival")
            flags.Append("--").Append(context.Mode).Append(' ');
        if (context.Difficulty == "easy")
            flags.Append("--easy ");
        if (context.LoadedFrom is { } save)
            flags.Append("--load \"").Append(save).Append("\" ");
        return flags.ToString();
    }

    private static string Lower<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static void Item(StringBuilder md, string name, string value) => md.Append("- **").Append(name).Append(":** ").AppendLine(value);

    private static void Row(StringBuilder md, string name, string value) => md.Append("| ").Append(name).Append(" | ").Append(value.Replace("|", "\\|")).AppendLine(" |");
}
