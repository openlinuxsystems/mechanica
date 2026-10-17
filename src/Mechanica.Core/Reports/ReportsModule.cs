using System.Text.Json;
using Mechanica.Core.Remote;

namespace Mechanica.Core.Reports;

/// <summary>
/// The endpoint's <c>reports.*</c> methods (AUDIT §5 PR 4): notes, recommendations and bug reports through
/// the <see cref="Reporter"/>, listing and reading them back, and a screenshot of the host's screen. Writing
/// and reading need the Observe scope; deleting a report needs Admin. New reports are pushed on the
/// <c>reports</c> event topic.
/// </summary>
public sealed class ReportsModule : IRpcModule
{
    private readonly Reporter _reporter;

    /// <summary>Wraps <paramref name="reporter"/>; new reports are published on <paramref name="events"/> when given.</summary>
    public ReportsModule(Reporter reporter, RemoteEvents? events = null)
    {
        _reporter = reporter;
        if (events is not null)
            reporter.Added += report => events.Publish("reports", () => ReportSummary.Of(report));
    }

    /// <summary>Who a report from <paramref name="context"/> is by when it names no author.</summary>
    public static string AuthorOf(RpcContext context) => $"agent {context.ConnectionId}";

    /// <inheritdoc/>
    public IEnumerable<RpcMethodInfo> Methods =>
    [
        RpcMethod.Create<NoteParams>("reports.note", AgentScope.Observe,
            "Save a note: something noticed or worth remembering. Returns the saved report.",
            (c, p) => Run(() => ReportStore.ToElement(_reporter.Note(AuthorOf(c), p)))),
        RpcMethod.Create<RecommendParams>("reports.recommend", AgentScope.Observe,
            "Save a recommendation: a change to the game or a mod, and why. Returns the saved report.",
            (c, p) => Run(() => ReportStore.ToElement(_reporter.Recommend(AuthorOf(c), p)))),
        RpcMethod.Create<BugParams>("reports.bug", AgentScope.Observe,
            "Save a bug report with steps, expected and actual. The host adds the game's context, the calls since the world started as a replay, recent log lines and a screenshot.",
            (c, p) => Run(() => ReportStore.ToElement(_reporter.Bug(AuthorOf(c), p)))),
        RpcMethod.Create<ListReportsParams>("reports.list", AgentScope.Observe,
            "List saved reports, newest first, filtered by kind, session and tag.",
            (_, p) => Run(() => ReportStore.ToElement(_reporter.List(p)))),
        RpcMethod.Create<GetReportParams>("reports.get", AgentScope.Observe,
            "Read one report, optionally with its replay recording and screenshot (base64 PNG).",
            (_, p) => Run(() => _reporter.Get(p))),
        RpcMethod.Create<DeleteReportParams>("reports.delete", AgentScope.Admin,
            "Delete a report and its files.",
            (_, p) => Run(() =>
            {
                _reporter.Delete(p);
                return RpcMethod.Value(true);
            })),
        RpcMethod.Create<Actions.NoArgs>("reports.screenshot", AgentScope.Observe,
            "What the host's screen shows now, as base64 PNG with its width and height.",
            (_, _) =>
            {
                var shot = _reporter.Capture();
                if (shot.Png is not { } png)
                    throw new RpcException(RpcErrors.Busy, shot.Error ?? "No picture.");
                var (width, height) = shot.Size;
                return RpcMethod.Value(new { png = Convert.ToBase64String(png), width, height });
            }),
    ];

    private static JsonElement? Run(Func<JsonElement> call)
    {
        try
        {
            return call();
        }
        catch (ReportException e)
        {
            throw new RpcException(RpcErrors.BadParams, e.Message);
        }
    }
}
