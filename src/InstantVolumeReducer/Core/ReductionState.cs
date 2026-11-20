using System.Text.Json.Serialization;

namespace InstantVolumeReducer.Core;

/// <summary>
/// One device that is currently reduced.
/// </summary>
/// <param name="DeviceId">The Windows endpoint ID.</param>
/// <param name="OriginalVolume">The volume (0.0–1.0) before the reduction; restored on the next toggle.</param>
/// <param name="ReducedVolume">The volume (0.0–1.0) the reduction applied.</param>
public sealed record ReducedDevice(string DeviceId, float OriginalVolume, float ReducedVolume);

/// <summary>
/// The saved reduction: which devices are reduced and what to restore them to.
/// Immutable and JSON-serializable, so it can be written to disk for crash recovery.
/// </summary>
public sealed record ReductionState(IReadOnlyList<ReducedDevice> Devices)
{
    /// <summary>The state when nothing is reduced.</summary>
    public static ReductionState Empty { get; } = new([]);

    /// <summary>
    /// <c>true</c> while at least one device is reduced. Derived from <see cref="Devices"/> so the two can't disagree.
    /// </summary>
    [JsonIgnore]
    public bool IsReduced => Devices.Count > 0;
}
