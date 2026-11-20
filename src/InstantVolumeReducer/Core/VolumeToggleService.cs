using InstantVolumeReducer.Audio;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.Core;

/// <summary>
/// Reduces the selected output devices on one toggle and restores their previous volume on the next.
/// Not thread-safe: call it from one thread (the UI thread).
/// </summary>
public sealed class VolumeToggleService
{
    private readonly IAudioDeviceService _audio;
    private IReadOnlyList<DeviceRule> _rules = [];

    /// <param name="audio">Access to the output devices.</param>
    /// <param name="deviceConfigs">The per-device settings; only enabled entries are used. The service keeps a copy.</param>
    /// <param name="initialState">
    /// A previously saved state, e.g. read from disk after a crash. Calling <see cref="Restore"/> then puts
    /// those devices back to their original volume. <c>null</c> means nothing is reduced.
    /// </param>
    public VolumeToggleService(
        IAudioDeviceService audio,
        IEnumerable<DeviceReductionConfig> deviceConfigs,
        ReductionState? initialState = null)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        State = initialState ?? ReductionState.Empty;
        _rules = ToRules(deviceConfigs);
    }

    /// <summary>
    /// Raised after <see cref="State"/> changed (reduced, restored, or devices dropped from the reduction).
    /// Subscribe to persist the state for crash recovery or to update the tray icon.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>The current reduction: which devices are reduced and their original volumes.</summary>
    public ReductionState State { get; private set; }

    /// <summary><c>true</c> while at least one device is reduced.</summary>
    public bool IsReduced => State.IsReduced;

    /// <summary>How many devices are enabled (selected) for reduction, connected or not.</summary>
    public int SelectedDeviceCount => _rules.Count;

    /// <summary>
    /// Reduces if nothing is reduced, otherwise restores.
    /// </summary>
    /// <returns><see cref="IsReduced"/> after the toggle.</returns>
    public bool Toggle()
    {
        if (IsReduced)
        {
            Restore();
        }
        else
        {
            Reduce();
        }

        return IsReduced;
    }

    /// <summary>
    /// Reduces every enabled device that is currently available, saving its current volume first.
    /// Unavailable devices and devices whose volume can't be read or set are skipped.
    /// Does nothing if already reduced.
    /// </summary>
    /// <returns><c>true</c> if at least one device is reduced afterwards.</returns>
    public bool Reduce()
    {
        if (IsReduced)
        {
            return true;
        }

        var reduced = new List<ReducedDevice>();
        foreach (var rule in _rules)
        {
            if (!_audio.TryGetVolume(rule.DeviceId, out var original))
            {
                continue;
            }

            var target = ReductionCalculator.ComputeReducedVolume(original, rule.Mode, rule.Amount);
            if (_audio.TrySetVolume(rule.DeviceId, target))
            {
                reduced.Add(new ReducedDevice(rule.DeviceId, original, target));
            }
        }

        if (reduced.Count > 0)
        {
            SetState(new ReductionState(reduced));
        }

        return IsReduced;
    }

    /// <summary>
    /// Sets every reduced device back to its original volume, even if the volume was changed by hand
    /// while reduced. Devices that are no longer available are skipped and forgotten. Does nothing if
    /// not reduced.
    /// </summary>
    /// <returns>How many devices were restored and how many were skipped; both 0 if nothing was reduced.</returns>
    public RestoreResult Restore()
    {
        if (!IsReduced)
        {
            return default;
        }

        var restored = 0;
        var skipped = 0;
        foreach (var device in State.Devices)
        {
            if (_audio.TrySetVolume(device.DeviceId, device.OriginalVolume))
            {
                restored++;
            }
            else
            {
                skipped++;
            }
        }

        SetState(ReductionState.Empty);
        return new RestoreResult(restored, skipped);
    }

    /// <summary>
    /// Replaces the per-device settings. Devices that are currently reduced but are no longer enabled
    /// are restored right away. Mode and amount changes, and newly enabled devices, take effect on the
    /// next reduction; devices that are already reduced keep their current level.
    /// </summary>
    public void UpdateDeviceConfigs(IEnumerable<DeviceReductionConfig> deviceConfigs)
    {
        _rules = ToRules(deviceConfigs);

        if (!IsReduced)
        {
            return;
        }

        var enabledIds = _rules.Select(r => r.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remaining = new List<ReducedDevice>();
        foreach (var device in State.Devices)
        {
            if (enabledIds.Contains(device.DeviceId))
            {
                remaining.Add(device);
            }
            else
            {
                _audio.TrySetVolume(device.DeviceId, device.OriginalVolume);
            }
        }

        if (remaining.Count != State.Devices.Count)
        {
            SetState(remaining.Count > 0 ? new ReductionState(remaining) : ReductionState.Empty);
        }
    }

    private void SetState(ReductionState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Snapshots the enabled configs, so later changes to the caller's objects don't affect the service.
    /// If a device ID appears more than once, the first enabled entry wins.
    /// </summary>
    private static IReadOnlyList<DeviceRule> ToRules(IEnumerable<DeviceReductionConfig> deviceConfigs)
    {
        ArgumentNullException.ThrowIfNull(deviceConfigs);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rules = new List<DeviceRule>();
        foreach (var config in deviceConfigs)
        {
            if (config is { Enabled: true } && !string.IsNullOrEmpty(config.DeviceId) && seen.Add(config.DeviceId))
            {
                rules.Add(new DeviceRule(config.DeviceId, config.Mode, config.Amount));
            }
        }

        return rules;
    }

    private readonly record struct DeviceRule(string DeviceId, ReductionMode Mode, int Amount);
}
