using Mechanica.Core.Player;

namespace Mechanica.Core.Tests.Player;

public class PlayerInputTests
{
    private static readonly PlayerMovementSettings Settings = new();

    [Fact]
    public void MoveVectorIsClampedToUnitLength()
    {
        var input = new PlayerInput(3, 4, 0, 0, false, false).Sanitized(Settings);
        Assert.Equal(0.6f, input.MoveX, 5);
        Assert.Equal(0.8f, input.MoveZ, 5);
    }

    [Fact]
    public void SmallMoveVectorIsKept()
    {
        var input = new PlayerInput(0.25f, -0.5f, 0, 0, false, false).Sanitized(Settings);
        Assert.Equal(0.25f, input.MoveX);
        Assert.Equal(-0.5f, input.MoveZ);
    }

    [Fact]
    public void PitchIsClampedToTheLimit()
    {
        var limit = Settings.MaxPitchDegrees * MathF.PI / 180f;
        Assert.Equal(limit, new PlayerInput(0, 0, 0, 10f, false, false).Sanitized(Settings).Pitch, 5);
        Assert.Equal(-limit, new PlayerInput(0, 0, 0, -10f, false, false).Sanitized(Settings).Pitch, 5);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(3f * MathF.PI, MathF.PI)]
    [InlineData(-MathF.PI, MathF.PI)]
    [InlineData(2.5f * MathF.PI, 0.5f * MathF.PI)]
    public void YawIsWrapped(float yaw, float expected) =>
        Assert.Equal(expected, new PlayerInput(0, 0, yaw, 0, false, false).Sanitized(Settings).Yaw, 4);

    [Fact]
    public void NonFiniteValuesBecomeZero()
    {
        var input = new PlayerInput(float.NaN, float.NegativeInfinity, float.NaN, float.PositiveInfinity, true, true)
            .Sanitized(Settings);
        Assert.Equal(new PlayerInput(0, 0, 0, 0, true, true), input);
    }
}
