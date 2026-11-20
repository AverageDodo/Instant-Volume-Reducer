using InstantVolumeReducer.Audio;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.UI;

/// <summary>One row of the settings dialog's device table.</summary>
public sealed class DeviceRow
{
    public required string DeviceId { get; init; }

    /// <summary>The friendly name: the current one if Windows lists the device, else the last known one.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the device is connected and active right now.</summary>
    public required bool IsConnected { get; init; }

    public bool Enabled { get; set; }

    public ReductionMode Mode { get; set; }

    public int Amount { get; set; }

    /// <summary>
    /// Whether this device's mode and amount are worth saving: it has a saved entry, or it was ticked in
    /// this dialog. Unticked devices that were never configured aren't written to the settings file.
    /// </summary>
    public bool HasConfig { get; set; }
}

/// <summary>
/// The non-UI logic of the settings dialog's device table: merges the devices Windows reports with the
/// saved per-device settings, and turns the edited rows back into settings.
/// </summary>
public static class DeviceTableModel
{
    /// <summary>Shown for a saved device that Windows doesn't list and whose name was never saved.</summary>
    public const string UnknownDeviceName = "Unknown device";

    /// <summary>
    /// Builds the rows: every connected output device (with its saved settings, or unticked with the
    /// defaults), followed by saved devices that aren't connected. Each group is sorted by name.
    /// </summary>
    public static List<DeviceRow> BuildRows(
        IEnumerable<AudioDeviceInfo> devices,
        IEnumerable<DeviceReductionConfig> configs,
        ReductionMode defaultMode,
        int defaultAmount)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(configs);

        var deviceById = new Dictionary<string, AudioDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices)
        {
            deviceById.TryAdd(device.Id, device);
        }

        var rows = new List<DeviceRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var config in configs)
        {
            if (string.IsNullOrWhiteSpace(config.DeviceId) || !seen.Add(config.DeviceId))
            {
                continue;
            }

            deviceById.TryGetValue(config.DeviceId, out var device);
            rows.Add(new DeviceRow
            {
                DeviceId = config.DeviceId,
                Name = FirstNonEmpty(device?.Name, config.LastKnownName, UnknownDeviceName),
                IsConnected = device?.IsActive ?? false,
                Enabled = config.Enabled,
                Mode = config.Mode,
                Amount = config.Amount,
                HasConfig = true,
            });
        }

        foreach (var device in deviceById.Values)
        {
            if (device.IsActive && seen.Add(device.Id))
            {
                rows.Add(new DeviceRow
                {
                    DeviceId = device.Id,
                    Name = FirstNonEmpty(device.Name, UnknownDeviceName),
                    IsConnected = true,
                    Enabled = false,
                    Mode = defaultMode,
                    Amount = defaultAmount,
                    HasConfig = false,
                });
            }
        }

        return rows
            .OrderBy(r => r.IsConnected ? 0 : 1)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Ticks or unticks a row. A device ticked for the first time (never configured) gets the default
    /// mode and amount; a device that was configured before keeps its own values.
    /// </summary>
    public static void SetEnabled(DeviceRow row, bool enabled, ReductionMode defaultMode, int defaultAmount)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (enabled && !row.HasConfig)
        {
            row.Mode = defaultMode;
            row.Amount = Math.Clamp(defaultAmount, DeviceReductionConfig.MinAmount, DeviceReductionConfig.MaxAmount);
            row.HasConfig = true;
        }

        row.Enabled = enabled;
    }

    /// <summary>
    /// Turns the rows into per-device settings. Rows that were never configured (unticked, no saved
    /// entry) are left out; everything else is kept, including disconnected devices.
    /// </summary>
    public static List<DeviceReductionConfig> ToConfigs(IEnumerable<DeviceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .Where(r => r.HasConfig || r.Enabled)
            .Select(r => new DeviceReductionConfig
            {
                DeviceId = r.DeviceId,
                LastKnownName = r.Name == UnknownDeviceName ? string.Empty : r.Name,
                Enabled = r.Enabled,
                Mode = r.Mode,
                Amount = Math.Clamp(r.Amount, DeviceReductionConfig.MinAmount, DeviceReductionConfig.MaxAmount),
            })
            .ToList();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(v => !string.IsNullOrWhiteSpace(v))!;
}
