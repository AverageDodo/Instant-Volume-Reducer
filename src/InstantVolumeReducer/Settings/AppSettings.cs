using System.Text.Json.Serialization;

namespace InstantVolumeReducer.Settings;

/// <summary>
/// How a device's volume is reduced.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReductionMode>))]
public enum ReductionMode
{
    /// <summary>Reduce to <c>Amount</c> % of the device's current volume.</summary>
    Relative,

    /// <summary>Set the device to a fixed <c>Amount</c> % volume (never raises it, see <c>ReductionCalculator</c>).</summary>
    Absolute,
}

/// <summary>
/// Reduction settings for one output device.
/// </summary>
public sealed class DeviceReductionConfig
{
    public const int MinAmount = 0;
    public const int MaxAmount = 100;

    /// <summary>The stable Windows endpoint ID; the key for this entry.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>The device's friendly name when it was last seen, for showing disconnected devices.</summary>
    public string LastKnownName { get; set; } = string.Empty;

    /// <summary>Whether the hotkey reduces this device.</summary>
    public bool Enabled { get; set; }

    public ReductionMode Mode { get; set; } = AppSettings.DefaultReductionMode;

    /// <summary>The reduction amount in percent (0–100); its meaning depends on <see cref="Mode"/>.</summary>
    public int Amount { get; set; } = AppSettings.DefaultReductionAmount;
}

/// <summary>
/// All user settings. Serialized as JSON to <c>%APPDATA%\InstantVolumeReducer\settings.json</c>.
/// </summary>
public sealed class AppSettings
{
    public const ReductionMode DefaultReductionMode = ReductionMode.Relative;
    public const int DefaultReductionAmount = 20;
    public const string DefaultHotkey = "Ctrl+Alt+V";

    /// <summary>The global hotkey as text, e.g. <c>Ctrl+Alt+V</c>. Parsed by the hotkey code (step 4).</summary>
    public string Hotkey { get; set; } = DefaultHotkey;

    /// <summary>Per-device settings, including devices that are currently disconnected.</summary>
    public List<DeviceReductionConfig> Devices { get; set; } = [];

    /// <summary>The mode a device gets when it is selected for the first time.</summary>
    public ReductionMode DefaultMode { get; set; } = DefaultReductionMode;

    /// <summary>The amount a device gets when it is selected for the first time.</summary>
    public int DefaultAmount { get; set; } = DefaultReductionAmount;

    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Whether every toggle shows a notification. Off by default. Warnings (e.g. a device that couldn't
    /// be restored, nothing to reduce, crash recovery) are shown regardless.
    /// </summary>
    public bool ShowNotifications { get; set; }
}
