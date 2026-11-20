using InstantVolumeReducer.Audio;

namespace InstantVolumeReducer.Tests.Audio;

public class VolumeLevelTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.35f, 0.35f)]
    [InlineData(1f, 1f)]
    [InlineData(-0.2f, 0f)]
    [InlineData(1.5f, 1f)]
    [InlineData(float.NegativeInfinity, 0f)]
    [InlineData(float.PositiveInfinity, 1f)]
    public void Clamp_KeepsValueInRange(float input, float expected)
    {
        Assert.Equal(expected, VolumeLevel.Clamp(input));
    }

    [Fact]
    public void Clamp_TreatsNaNAsSilent()
    {
        Assert.Equal(0f, VolumeLevel.Clamp(float.NaN));
    }
}
