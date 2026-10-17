using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.Ui;

public class WorldLoadingTests
{
    private static WorldLoading New() => new(new LoadingSettings());

    [Fact]
    public void ConnectingAndWaitingLeaveTheBarEmpty()
    {
        var loading = New();
        loading.Report(LoadStage.Connecting, 0.5);
        loading.Report(LoadStage.WaitingForHost, 1);
        Assert.Equal(0, loading.Percent);
        Assert.Equal(LoadStage.WaitingForHost, loading.Stage);
    }

    [Fact]
    public void TheBarFollowsTheStages()
    {
        var loading = New();
        loading.Report(LoadStage.Terrain, 1);
        Assert.Equal(16, loading.Percent);
        loading.Report(LoadStage.Chunks, 0.5);
        Assert.Equal(25, loading.Percent);
        loading.Report(LoadStage.Finalizing);
        Assert.Equal(83, loading.Percent);
        loading.Report(LoadStage.Entering);
        Assert.Equal(100, loading.Percent);
        Assert.True(loading.Entered);
        Assert.Equal([LoadStage.Connecting, LoadStage.Terrain, LoadStage.Chunks, LoadStage.Finalizing, LoadStage.Entering], loading.Reached);
    }

    [Fact]
    public void ProgressNeverGoesBack()
    {
        var loading = New();
        loading.Report(LoadStage.Chunks, 0.8);
        Assert.False(loading.Report(LoadStage.Chunks, 0.3));
        Assert.False(loading.Report(LoadStage.Terrain, 1));
        Assert.Equal(LoadStage.Chunks, loading.Stage);
        Assert.Equal(0.8, loading.StageFraction, 6);
    }

    [Fact]
    public void ItLeavesOnlyAfterTheMinimumTime()
    {
        var loading = New();
        loading.Report(LoadStage.Entering);
        loading.Tick(1.0);
        Assert.False(loading.CanLeave);
        loading.Tick(1.0);
        Assert.True(loading.CanLeave);
    }

    [Fact]
    public void AStalledStageTimesOut()
    {
        var loading = new WorldLoading(new LoadingSettings { TimeoutSeconds = 10 });
        loading.Report(LoadStage.Chunks, 0.1);
        loading.Tick(9);
        loading.Report(LoadStage.Chunks, 0.2);
        loading.Tick(9);
        Assert.Null(loading.Error);
        loading.Tick(1.5, "late");
        Assert.Equal("late", loading.Error);
        Assert.False(loading.Report(LoadStage.Entering));
        Assert.False(loading.CanLeave);
    }

    [Fact]
    public void TheFirstFailureWinsAndAnEnteredLoadCannotFail()
    {
        var loading = New();
        loading.Fail("a");
        loading.Fail("b");
        Assert.Equal("a", loading.Error);
        var done = New();
        done.Report(LoadStage.Entering);
        done.Fail("late");
        done.Tick(100);
        Assert.Null(done.Error);
    }

    [Fact]
    public void TipsTakeTurns()
    {
        var loading = new WorldLoading(new LoadingSettings { TipSeconds = 5 });
        Assert.Equal(0, loading.TipIndex(3));
        loading.Tick(5.1);
        Assert.Equal(1, loading.TipIndex(3));
        loading.Tick(10);
        Assert.Equal(0, loading.TipIndex(3));
        Assert.Equal(-1, loading.TipIndex(0));
    }

    [Fact]
    public void EveryStageHasText()
    {
        Assert.All(Enum.GetValues<LoadStage>(), s => Assert.EndsWith("…", WorldLoading.Text(s)));
        Assert.Equal("Waiting for host…", WorldLoading.Text(LoadStage.WaitingForHost));
    }

    [Fact]
    public void SettingsReadFromJson()
    {
        var settings = LoadingSettings.Parse("""{ "min_seconds": 3, "tip_seconds": 4, /* ok */ }""");
        Assert.Equal(3, settings.MinSeconds);
        Assert.Equal(4, settings.TipSeconds);
        Assert.Equal(60, settings.TimeoutSeconds);
        Assert.Throws<FormatException>(() => LoadingSettings.Parse("""{ "tip_seconds": 0 }"""));
        Assert.Throws<FormatException>(() => LoadingSettings.Parse("{"));
    }
}
