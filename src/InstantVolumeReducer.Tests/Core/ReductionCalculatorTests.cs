using InstantVolumeReducer.Core;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.Tests.Core;

public class ReductionCalculatorTests
{
    private const int Precision = 4;

    [Theory]
    [InlineData(0.8f, 20, 0.16f)]
    [InlineData(0.5f, 50, 0.25f)]
    [InlineData(1.0f, 0, 0.0f)]
    [InlineData(0.6f, 100, 0.6f)]
    [InlineData(0.0f, 20, 0.0f)]
    public void Relative_ScalesCurrentVolume(float current, int amount, float expected) =>
        Assert.Equal(expected, ReductionCalculator.ComputeReducedVolume(current, ReductionMode.Relative, amount), Precision);

    [Theory]
    [InlineData(0.8f, 20, 0.2f)]
    [InlineData(1.0f, 0, 0.0f)]
    [InlineData(1.0f, 100, 1.0f)]
    public void Absolute_SetsFixedLevel(float current, int amount, float expected) =>
        Assert.Equal(expected, ReductionCalculator.ComputeReducedVolume(current, ReductionMode.Absolute, amount), Precision);

    [Theory]
    [InlineData(0.1f, 30)]
    [InlineData(0.5f, 100)]
    public void Absolute_NeverRaisesVolume(float current, int amount) =>
        Assert.Equal(current, ReductionCalculator.ComputeReducedVolume(current, ReductionMode.Absolute, amount), Precision);

    [Theory]
    [InlineData(ReductionMode.Relative, -10, 0.0f)]
    [InlineData(ReductionMode.Relative, 150, 0.8f)]
    [InlineData(ReductionMode.Absolute, -10, 0.0f)]
    [InlineData(ReductionMode.Absolute, 150, 0.8f)]
    public void Amount_IsClampedTo0To100(ReductionMode mode, int amount, float expected) =>
        Assert.Equal(expected, ReductionCalculator.ComputeReducedVolume(0.8f, mode, amount), Precision);

    [Fact]
    public void UnknownMode_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ReductionCalculator.ComputeReducedVolume(0.5f, (ReductionMode)42, 20));
}
