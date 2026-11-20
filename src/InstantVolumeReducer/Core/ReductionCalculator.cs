using InstantVolumeReducer.Audio;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.Core;

/// <summary>
/// Computes the reduced volume for a device.
/// </summary>
public static class ReductionCalculator
{
    /// <summary>
    /// Returns the volume (0.0–1.0) to apply when reducing a device that is currently at <paramref name="currentVolume"/>.
    /// <list type="bullet">
    /// <item><see cref="ReductionMode.Relative"/>: <c>current × amount %</c>.</item>
    /// <item><see cref="ReductionMode.Absolute"/>: <c>amount %</c>, but never higher than the current volume,
    /// because a "reduce" hotkey must never make things louder.</item>
    /// </list>
    /// <paramref name="amount"/> is clamped to 0–100.
    /// </summary>
    public static float ComputeReducedVolume(float currentVolume, ReductionMode mode, int amount)
    {
        var current = VolumeLevel.Clamp(currentVolume);
        var fraction = Math.Clamp(amount, DeviceReductionConfig.MinAmount, DeviceReductionConfig.MaxAmount) / 100f;

        var reduced = mode switch
        {
            ReductionMode.Relative => current * fraction,
            ReductionMode.Absolute => Math.Min(current, fraction),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown reduction mode."),
        };

        return VolumeLevel.Clamp(reduced);
    }
}
