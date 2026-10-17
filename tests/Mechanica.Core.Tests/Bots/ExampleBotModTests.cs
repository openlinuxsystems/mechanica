using Mechanica.Core.Bots;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Bots;

/// <summary>
/// The Bot Tutorial's example mod (<c>examples/mods/example_bots</c>): it loads beside vanilla without
/// problems, and its bot file is exactly what its editor batch makes.
/// </summary>
public class ExampleBotModTests
{
    private const string Batch = "editor/batches/bots.json";

    private static readonly string Example = Path.Combine(TestMods.RepoRoot, "examples", "mods", "example_bots");
    private static readonly string Vanilla = Path.Combine(TestMods.RepoRoot, "mods", "vanilla");

    [Fact]
    public void TheExampleModLoadsBesideVanilla()
    {
        var content = ModLoader.Load([Vanilla, Example]);
        Assert.DoesNotContain(content.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var book = BotBook.Load(content);
        Assert.Empty(book.Diagnostics);
        var drone = book.FindBot("example_bots:sentry_drone");
        Assert.NotNull(drone);
        Assert.Null(drone.RaidCost);
        Assert.Equal("example_bots:scripts/bots/sentry_drone.lua", drone.Script);
        Assert.Contains(drone.Drops, d => d.Item == "vanilla:circuit_board");
    }

    [Fact]
    public void TheBatchReproducesTheCheckedInBot()
    {
        var root = Path.Combine(Path.GetTempPath(), "mechanica-tests", Guid.NewGuid().ToString("N"));
        var scratch = Path.Combine(root, "examples", "example_bots");
        try
        {
            foreach (var file in Directory.GetFiles(Example, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(Example, file).Replace('\\', '/');
                if (relative.StartsWith("bots/", StringComparison.Ordinal))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(scratch, relative))!);
                File.Copy(file, Path.Combine(scratch, relative));
            }
            var diagnostics = new List<DataDiagnostic>();
            var batch = BotBatch.Read(File.ReadAllText(Path.Combine(scratch, Batch)), Batch, diagnostics)!;
            // Outside the game's mods folder, so vanilla comes in as one of the other mods.
            var results = BotBatch.Run(batch, scratch, VanillaBotsTests.Template, Batch, diagnostics, [Vanilla]);

            Assert.Empty(diagnostics);
            Assert.Single(results, r => r.Succeeded);
            Assert.Equal(["sentry_drone.json"], Directory.GetFiles(Path.Combine(Example, "bots")).Select(Path.GetFileName));
            Assert.True(File.ReadAllText(Path.Combine(Example, "bots", "sentry_drone.json")) == File.ReadAllText(Path.Combine(scratch, "bots", "sentry_drone.json")),
                $"bots/sentry_drone.json differs from the batch's output; edit {Batch} and re-run tools/author-vanilla.sh instead of the file.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
