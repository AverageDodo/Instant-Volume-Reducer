using InstantVolumeReducer.Audio;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.Core;

/// <summary>
/// Reduces the selected output devices on one toggle and restores their previous volume on the next.
/// Devices are selected by name (per-device settings) and, optionally, as "whichever device is the
/// Windows default right now"; a device selected both ways uses its own per-device settings.
/// Not thread-safe: call it from one thread (the UI thread).
/// </summary>
public sealed class VolumeToggleService
{
    private readonly IAudioDeviceService _audio;
    private IReadOnlyList<DeviceRule> _rules = [];
    private DeviceRule? _defaultDeviceRule;

    /// <param name="audio">Access to the output devices.</param>
    /// <param name="deviceConfigs">The per-device settings; only enabled entries are used. The service keeps a copy.</param>
    /// <param name="initialState">
    /// A previously saved state, e.g. read from disk after a crash. Calling <see cref="Restore"/> then puts
    /// those devices back to their original volume. <c>null</c> means nothing is reduced.
    /// </param>
    /// <param name="defaultDeviceConfig">
    /// The settings for the Windows default output device; <c>null</c> or disabled means the default device
    /// isn't followed. The service keeps a copy.
    /// </param>
    public VolumeToggleService(
        IAudioDeviceService audio,
        IEnumerable<DeviceReductionConfig> deviceConfigs,
        ReductionState? initialState = null,
        WindowsDefaultDeviceConfig? defaultDeviceConfig = null)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        State = initialState ?? ReductionState.Empty;
        _rules = ToRules(deviceConfigs);
        _defaultDeviceRule = ToDefaultDeviceRule(defaultDeviceConfig);
    }

    /// <summary>
    /// Raised after <see cref="State"/> changed (reduced, restored, devices dropped from the reduction, or
    /// the reduction moved to a new default device). Subscribe to persist the state for crash recovery or
    /// to update the tray icon.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>The current reduction: which devices are reduced and their original volumes.</summary>
    public ReductionState State { get; private set; }

    /// <summary><c>true</c> while at least one device is reduced.</summary>
    public bool IsReduced => State.IsReduced;

    /// <summary>
    /// How many devices the hotkey would reduce if they were all connected: the devices selected by name,
    /// plus the Windows default device if it's followed and isn't one of them (or there is no default).
    /// </summary>
    public int SelectedDeviceCount
    {
        get
        {
            var count = _rules.Count;
            if (_defaultDeviceRule is not null)
            {
                var defaultId = _audio.GetDefaultOutputDeviceId();
                if (defaultId is null || FindRule(defaultId) is null)
                {
                    count++;
                }
            }

            return count;
        }
    }

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
    /// Reduces every enabled device that is currently available, plus the current Windows default device
    /// if it's followed, saving each one's current volume first. Unavailable devices and devices whose
    /// volume can't be read or set are skipped. Does nothing if already reduced.
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
            if (TryReduceDevice(rule.DeviceId, rule, viaWindowsDefault: false) is { } device)
            {
                reduced.Add(device);
            }
        }

        // A default device that's also selected by name was handled above with its own settings (or
        // skipped because its volume couldn't be changed); don't try it a second time.
        if (_defaultDeviceRule is { } defaultRule
            && _audio.GetDefaultOutputDeviceId() is { } defaultId
            && FindRule(defaultId) is null
            && TryReduceDevice(defaultId, defaultRule, viaWindowsDefault: true) is { } defaultDevice)
        {
            reduced.Add(defaultDevice);
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
    /// Call when the Windows default output device changed. While the volume is reduced and the default
    /// device is followed, the reduction moves along: devices reduced only because they were the default
    /// are restored, and the new default device is reduced (with its own settings if it's also selected by
    /// name). A previous default that can't be restored right now (typically because unplugging it is what
    /// changed the default) stays in the state and is restored later. Does nothing otherwise.
    /// </summary>
    public DefaultDeviceChange HandleDefaultDeviceChanged()
    {
        if (!IsReduced || _defaultDeviceRule is not { } defaultRule)
        {
            return DefaultDeviceChange.None;
        }

        var newDefaultId = _audio.GetDefaultOutputDeviceId();
        var devices = new List<ReducedDevice>(State.Devices);
        var restoredIds = new List<string>();

        foreach (var device in State.Devices)
        {
            if (device.ViaWindowsDefault
                && !SameDevice(device.DeviceId, newDefaultId)
                && _audio.TrySetVolume(device.DeviceId, device.OriginalVolume))
            {
                devices.Remove(device);
                restoredIds.Add(device.DeviceId);
            }
        }

        string? reducedId = null;
        if (newDefaultId is not null && !devices.Any(d => SameDevice(d.DeviceId, newDefaultId)))
        {
            // A device selected by name that wasn't reduced yet (e.g. it was unplugged at the time) is
            // reduced with its own settings and stays reduced if the default moves on.
            var ownRule = FindRule(newDefaultId);
            if (TryReduceDevice(newDefaultId, ownRule ?? defaultRule, viaWindowsDefault: ownRule is null) is { } added)
            {
                devices.Add(added);
                reducedId = added.DeviceId;
            }
        }

        var change = new DefaultDeviceChange(restoredIds, reducedId);
        if (change.HasChanges)
        {
            SetState(devices.Count > 0 ? new ReductionState(devices) : ReductionState.Empty);
        }

        return change;
    }

    /// <summary>
    /// Replaces the per-device settings and the default-device settings. Devices that are currently
    /// reduced but are no longer selected (by name, or as the current Windows default) are restored right
    /// away. Mode and amount changes, and newly selected devices, take effect on the next reduction;
    /// devices that are already reduced keep their current level.
    /// </summary>
    public void UpdateDeviceConfigs(
        IEnumerable<DeviceReductionConfig> deviceConfigs,
        WindowsDefaultDeviceConfig? defaultDeviceConfig = null)
    {
        _rules = ToRules(deviceConfigs);
        _defaultDeviceRule = ToDefaultDeviceRule(defaultDeviceConfig);

        if (!IsReduced)
        {
            return;
        }

        var defaultId = _defaultDeviceRule is null ? null : _audio.GetDefaultOutputDeviceId();
        var remaining = new List<ReducedDevice>();
        foreach (var device in State.Devices)
        {
            if (FindRule(device.DeviceId) is not null)
            {
                // Selected by name now: it no longer follows the default.
                remaining.Add(device with { ViaWindowsDefault = false });
            }
            else if (_defaultDeviceRule is not null && (device.ViaWindowsDefault || SameDevice(device.DeviceId, defaultId)))
            {
                // Still (or now) covered by the default-device row.
                remaining.Add(device with { ViaWindowsDefault = true });
            }
            else
            {
                _audio.TrySetVolume(device.DeviceId, device.OriginalVolume);
            }
        }

        if (!remaining.SequenceEqual(State.Devices))
        {
            SetState(remaining.Count > 0 ? new ReductionState(remaining) : ReductionState.Empty);
        }
    }

    private ReducedDevice? TryReduceDevice(string deviceId, DeviceRule rule, bool viaWindowsDefault)
    {
        if (!_audio.TryGetVolume(deviceId, out var original))
        {
            return null;
        }

        var target = ReductionCalculator.ComputeReducedVolume(original, rule.Mode, rule.Amount);
        return _audio.TrySetVolume(deviceId, target)
            ? new ReducedDevice(deviceId, original, target, viaWindowsDefault)
            : null;
    }

    private DeviceRule? FindRule(string deviceId)
    {
        foreach (var rule in _rules)
        {
            if (SameDevice(rule.DeviceId, deviceId))
            {
                return rule;
            }
        }

        return null;
    }

    private static bool SameDevice(string a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

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

    private static DeviceRule? ToDefaultDeviceRule(WindowsDefaultDeviceConfig? config) =>
        config is { Enabled: true } ? new DeviceRule(string.Empty, config.Mode, config.Amount) : null;

    private readonly record struct DeviceRule(string DeviceId, ReductionMode Mode, int Amount);
}
