using Mechanica.Core.Player;

namespace Mechanica.Core.Tests.Player;

public class CameraZoomTests
{
    private static readonly PlayerMovementSettings Settings = new() { CameraDistance = 3.5f, CameraMinDistance = 1.5f, CameraMaxDistance = 6f, CameraZoomStep = 1f };

    [Fact]
    public void ZoomingInStepsDownToFirstPerson()
    {
        var distances = new List<float>();
        var d = Settings.CameraDistance;
        for (var i = 0; i < 5; i++)
            distances.Add(d = CameraZoom.Step(d, zoomIn: true, Settings));
        Assert.Equal([2.5f, 1.5f, 0f, 0f, 0f], distances);
    }

    [Fact]
    public void ZoomingOutLeavesFirstPersonAndStopsAtTheFurthest()
    {
        var distances = new List<float>();
        var d = 0f;
        for (var i = 0; i < 7; i++)
            distances.Add(d = CameraZoom.Step(d, zoomIn: false, Settings));
        Assert.Equal([1.5f, 2.5f, 3.5f, 4.5f, 5.5f, 6f, 6f], distances);
    }

    [Fact]
    public void TheShippedCameraStartsInFirstPersonAndPullsBackBehindThePlayer()
    {
        var settings = new PlayerMovementSettings();
        Assert.True(settings.CameraStartsFirstPerson);
        Assert.True(settings.CameraDistance > 0);
    }
}
