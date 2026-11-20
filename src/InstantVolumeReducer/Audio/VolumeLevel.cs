namespace InstantVolumeReducer.Audio;

/// <summary>
/// Helpers for volume scalars (0.0 = silent, 1.0 = full volume).
/// </summary>
public static class VolumeLevel
{
    public const float Min = 0f;
    public const float Max = 1f;

    /// <summary>
    /// Clamps a volume scalar to the 0.0–1.0 range. <see cref="float.NaN"/> becomes 0.0.
    /// </summary>
    public static float Clamp(float volume) =>
        float.IsNaN(volume) ? Min : Math.Clamp(volume, Min, Max);
}
