using Mechanica.Core.Net;
using Mechanica.Core.Settings;

namespace Mechanica.Core.Tests.Net;

public class NetPasswordTests
{
    [Fact]
    public void AnOpenWorldLetsAnyoneIn()
    {
        Assert.Null(NetPassword.Check("", null));
        Assert.Null(NetPassword.Check("", "anything"));
    }

    [Fact]
    public void AClosedWorldNeedsTheRightPassword()
    {
        Assert.Equal(NetPassword.Needed, NetPassword.Check("hunter2", null));
        Assert.Equal(NetPassword.Needed, NetPassword.Check("hunter2", ""));
        Assert.Equal(NetPassword.Wrong, NetPassword.Check("hunter2", "Hunter2"));
        Assert.Null(NetPassword.Check("hunter2", "hunter2"));
    }

    [Fact]
    public void PasswordsComeFromTheCommandLineAndSettings()
    {
        Assert.True(LaunchOptions.TryParse(["--join", "10.0.0.2", "--password", "pw"], out var options, out _));
        Assert.Equal("pw", options.Password);
        Assert.False(LaunchOptions.TryParse(["--password"], out _, out _));
        var long_ = new string('x', 100);
        Assert.Equal(NetPassword.MaxLength, new UserSettings { Gameplay = new GameplaySettings { ServerPassword = long_ } }.Clamped().Gameplay.ServerPassword.Length);
    }
}
