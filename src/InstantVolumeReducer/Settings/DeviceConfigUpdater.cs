using InstantVolumeReducer.Audio;

namespace InstantVolumeReducer.Settings;

/// <summary>
/// Keeps the per-device settings in step with the output devices Windows reports.
/// </summary>
public static class DeviceConfigUpdater
{
    /// <summary>
    /// First run: selects every active output device with the default mode and amount, so the hotkey
    /// works right away. Devices that already have an entry are left as they are.
    /// </summary>
    /// <returns><c>true</c> if an entry was added.</returns>
    public static bool EnableAllActive(AppSettings settings, IEnumerable<AudioDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(devices);

        var added = false;
        foreach (var device in devices)
        {
            if (!device.IsActive || Find(settings, device.Id) is not null)
            {
                continue;
            }

            settings.Devices.Add(new DeviceReductionConfig
            {
                DeviceId = device.Id,
                LastKnownName = device.Name,
                Enabled = true,
                Mode = settings.DefaultMode,
                Amount = settings.DefaultAmount,
            });
            added = true;
        }

        return added;
    }

    /// <summary>
    /// Updates <see cref="DeviceReductionConfig.LastKnownName"/> of saved devices that Windows lists, so a
    /// disconnected device is still shown with its current name. New devices are not added: they are
    /// only selected in the settings dialog.
    /// </summary>
    /// <returns><c>true</c> if a name changed.</returns>
    public static bool RefreshNames(AppSettings settings, IEnumerable<AudioDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(devices);

        var changed = false;
        foreach (var device in devices)
        {
            if (Find(settings, device.Id) is { } config
                && !string.IsNullOrWhiteSpace(device.Name)
                && !string.Equals(config.LastKnownName, device.Name, StringComparison.Ordinal))
            {
                config.LastKnownName = device.Name;
                changed = true;
            }
        }

        return changed;
    }

    private static DeviceReductionConfig? Find(AppSettings settings, string deviceId) =>
        settings.Devices.Find(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
}
