using System.Text.Json.Serialization;

namespace InstantVolumeReducer.Core;

/// <summary>
/// One device that is currently reduced.
/// </summary>
/// <param name="DeviceId">The Windows endpoint ID.</param>
/// <param name="OriginalVolume">The volume (0.0–1.0) before the reduction; restored on the next toggle.</param>
/// <param name="ReducedVolume">The volume (0.0–1.0) the reduction applied.</param>
/// <param name="ViaWindowsDefault">
/// <c>true</c> if the device is reduced only because it was the Windows default output device (not
/// because it's selected by name), so it's restored when the default moves to another device. Missing in
/// state files from before this option, which means <c>false</c>.
/// </param>
public sealed record ReducedDevice(string DeviceId, float OriginalVolume, float ReducedVolume, bool ViaWindowsDefault = false);

/// <summary>
/// The outcome of <see cref="VolumeToggleService.Restore"/>.
/// </summary>
/// <param name="Restored">Devices set back to their original volume.</param>
/// <param name="Skipped">Devices that couldn't be restored (not connected any more, or the volume couldn't be set).</param>
public readonly record struct RestoreResult(int Restored, int Skipped);

/// <summary>
/// The outcome of <see cref="VolumeToggleService.HandleDefaultDeviceChanged"/>.
/// </summary>
/// <param name="RestoredDeviceIds">Devices restored because they stopped being the default.</param>
/// <param name="ReducedDeviceId">The new default device, if it was reduced.</param>
public sealed record DefaultDeviceChange(IReadOnlyList<string> RestoredDeviceIds, string? ReducedDeviceId)
{
    /// <summary>Nothing changed.</summary>
    public static DefaultDeviceChange None { get; } = new([], null);

    /// <summary><c>true</c> if any device was restored or reduced.</summary>
    public bool HasChanges => RestoredDeviceIds.Count > 0 || ReducedDeviceId is not null;
}

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
