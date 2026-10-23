using Godot;
using Mechanica.Core.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The day and night cycle (<c>game/data/sky.json</c>), host plus a client. The client's sky shows the host's hour, not
/// its own: the host moves the world's clock to late evening and the client's sun goes down with it. At noon the sun
/// stands high and bright; at midnight the moon is up, the stars are out and the sky is dark but not black.
/// </summary>
public partial class DayNightTest : StagedTest
{
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;

    protected override string Tag => "daynight";

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins", _ => _client.Main.LocalPlayer is { } player && player.IsOnFloor());
        Add("the client's sky shows the host's hour", frame =>
        {
            var day = _host.Main.Scripts.Runtime.DayLength();
            if (frame == 0)
            {
                // Noon on the host: six hours after the world's 6:00 start.
                _host.Main.Scripts.Runtime.Time = day / 4;
                return false;
            }
            if (Mathf.Abs((float)(_client.Main.DayNight.Shown - 12)) > 0.2f)
                return false;
            var sun = _client.Main.GetNode<DirectionalLight3D>("Sun");
            Check(sun.GlobalBasis.Z.Y > 0.7f, $"the sun stands high (light from {sun.GlobalBasis.Z})");
            Check(sun.LightEnergy > 1f, $"and shines bright ({sun.LightEnergy:0.00})");
            return true;
        });
        Add("at midnight the moon is up and the stars are out", frame =>
        {
            var day = _host.Main.Scripts.Runtime.DayLength();
            if (frame == 0)
            {
                _host.Main.Scripts.Runtime.Time = day * 18 / 24;
                return false;
            }
            var shown = _client.Main.DayNight.Shown;
            if (shown > 0.2 && shown < 23.8)
                return false;
            var state = World.DayNight.Current.At(shown);
            Check(state.Moon && state.Stars > 0.9f, "the moon has the sky and the stars show");
            var env = _client.Main.GetNode<WorldEnvironment>("WorldEnvironment").Environment;
            var sky = (ProceduralSkyMaterial)env.Sky.SkyMaterial;
            Check(sky.SkyTopColor.Luminance < 0.15f, $"the sky is dark ({sky.SkyTopColor})");
            Check(env.AmbientLightEnergy > 0.2f, $"but not pitch black ({env.AmbientLightEnergy:0.00})");
            return true;
        });
    }
}
