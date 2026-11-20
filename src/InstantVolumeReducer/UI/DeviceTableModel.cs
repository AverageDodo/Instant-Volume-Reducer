using InstantVolumeReducer.Audio;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.UI;

/// <summary>One row of the settings dialog's device table.</summary>
public sealed class DeviceRow
{
    public required string DeviceId { get; init; }

    /// <summary>The friendly name: the current one if Windows lists the device, else the last known one.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Whether the device is connected and active right now. For the Windows default device row: whether
    /// there is a default device.
    /// </summary>
    public required bool IsConnected { get; set; }

    /// <summary>
    /// <c>true</c> for the pinned "Windows default device" row, which stands for whichever device is the
    /// default when the hotkey is pressed. It has no <see cref="DeviceId"/>.
    /// </summary>
    public bool IsWindowsDefault { get; init; }

    /// <summary>For the Windows default device row: the name of the current default device, if any.</summary>
    public string? CurrentDefaultName { get; set; }

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

    /// <summary>The name of the pinned row that stands for the Windows default output device.</summary>
    public const string WindowsDefaultRowName = "Windows default device";

    /// <summary>
    /// Builds the rows: the pinned Windows default device row (if <paramref name="windowsDefault"/> is
    /// given), then every connected output device (with its saved settings, or unticked with the
    /// defaults), followed by saved devices that aren't connected. Each device group is sorted by name.
    /// </summary>
    /// <param name="windowsDefault">The default-device settings; <c>null</c> leaves the pinned row out.</param>
    /// <param name="currentDefaultId">The ID of the current Windows default output device, if any.</param>
    public static List<DeviceRow> BuildRows(
        IEnumerable<AudioDeviceInfo> devices,
        IEnumerable<DeviceReductionConfig> configs,
        ReductionMode defaultMode,
        int defaultAmount,
        WindowsDefaultDeviceConfig? windowsDefault = null,
        string? currentDefaultId = null)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(configs);

        var deviceById = new Dictionary<string, AudioDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices)
        {
            deviceById.TryAdd(device.Id, device);
        }

        var deviceRows = BuildDeviceRows(deviceById, configs, defaultMode, defaultAmount);
        if (windowsDefault is null)
        {
            return deviceRows;
        }

        var defaultRow = new DeviceRow
        {
            DeviceId = string.Empty,
            Name = WindowsDefaultRowName,
            IsConnected = false,
            IsWindowsDefault = true,
            Enabled = windowsDefault.Enabled,
            Mode = windowsDefault.Mode,
            Amount = windowsDefault.Amount,
            HasConfig = true, // Its own values are always kept, so ticking it never applies the defaults.
        };
        SetCurrentDefault(defaultRow, currentDefaultId, deviceById.Values);

        return [defaultRow, .. deviceRows];
    }

    /// <summary>Points the Windows default device row at the current default device (or none).</summary>
    public static void SetCurrentDefault(DeviceRow row, string? currentDefaultId, IEnumerable<AudioDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(devices);

        var device = currentDefaultId is null
            ? null
            : devices.FirstOrDefault(d => string.Equals(d.Id, currentDefaultId, StringComparison.OrdinalIgnoreCase));
        row.IsConnected = currentDefaultId is not null;
        row.CurrentDefaultName = currentDefaultId is null ? null : FirstNonEmpty(device?.Name, UnknownDeviceName);
    }

    /// <summary>The text of a row's Status column.</summary>
    public static string StatusText(DeviceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.IsWindowsDefault)
        {
            return row.CurrentDefaultName is { } name ? $"Currently: {name}" : "No default device";
        }

        return row.IsConnected ? "Connected" : "Not connected";
    }

    /// <summary>The default-device settings from the pinned row, or <c>null</c> if there is no such row.</summary>
    public static WindowsDefaultDeviceConfig? ToWindowsDefaultConfig(IEnumerable<DeviceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows.FirstOrDefault(r => r.IsWindowsDefault) is { } row
            ? new WindowsDefaultDeviceConfig
            {
                Enabled = row.Enabled,
                Mode = row.Mode,
                Amount = Math.Clamp(row.Amount, DeviceReductionConfig.MinAmount, DeviceReductionConfig.MaxAmount),
            }
            : null;
    }

    private static List<DeviceRow> BuildDeviceRows(
        Dictionary<string, AudioDeviceInfo> deviceById,
        IEnumerable<DeviceReductionConfig> configs,
        ReductionMode defaultMode,
        int defaultAmount)
    {

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
    /// The state of the "Select all" box: checked when every row is ticked, unchecked when none is (or
    /// there are no rows), and indeterminate when only some are.
    /// </summary>
    public static CheckState GetSelectAllState(IReadOnlyCollection<DeviceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var enabled = rows.Count(r => r.Enabled);
        return enabled == 0 ? CheckState.Unchecked
            : enabled == rows.Count ? CheckState.Checked
            : CheckState.Indeterminate;
    }

    /// <summary>
    /// What clicking "Select all" does: ticks every row, unless every row is already ticked, in which
    /// case it unticks them all. Rows ticked for the first time get the defaults (see <see cref="SetEnabled"/>).
    /// </summary>
    /// <returns><c>true</c> if the rows were ticked, <c>false</c> if they were unticked.</returns>
    public static bool ToggleAll(IReadOnlyCollection<DeviceRow> rows, ReductionMode defaultMode, int defaultAmount)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var enable = GetSelectAllState(rows) != CheckState.Checked;
        foreach (var row in rows)
        {
            SetEnabled(row, enable, defaultMode, defaultAmount);
        }

        return enable;
    }

    /// <summary>
    /// Turns the rows into per-device settings. Rows that were never configured (unticked, no saved
    /// entry) are left out; everything else is kept, including disconnected devices. The Windows default
    /// device row is left out too (see <see cref="ToWindowsDefaultConfig"/>).
    /// </summary>
    public static List<DeviceReductionConfig> ToConfigs(IEnumerable<DeviceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .Where(r => !r.IsWindowsDefault && (r.HasConfig || r.Enabled))
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
