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
/// Reduction settings for "whichever device is the Windows default output device right now". When enabled,
/// the hotkey also reduces the current default device, and the reduction moves along when the default
/// changes while the volume is reduced.
/// </summary>
public sealed class WindowsDefaultDeviceConfig
{
    /// <summary>Whether the hotkey reduces the current Windows default output device.</summary>
    public bool Enabled { get; set; }

    public ReductionMode Mode { get; set; } = AppSettings.DefaultReductionMode;

    /// <summary>The reduction amount in percent (0–100); its meaning depends on <see cref="Mode"/>.</summary>
    public int Amount { get; set; } = AppSettings.DefaultReductionAmount;
}

/// <summary>The screen corner the overlay icon is shown in.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OverlayCorner>))]
public enum OverlayCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>The overlay icon's size; the pixel size also depends on the monitor's scaling.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OverlaySize>))]
public enum OverlaySize
{
    Small,
    Medium,
    Large,
}

/// <summary>Which monitor the overlay icon is shown on.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OverlayMonitor>))]
public enum OverlayMonitor
{
    /// <summary>The main (primary) monitor.</summary>
    Primary,

    /// <summary>The monitor that shows the active (foreground) window; follows it as it moves.</summary>
    ActiveWindow,
}

/// <summary>
/// The on-screen indicator: a small icon shown in a corner of the screen while the volume is reduced.
/// </summary>
public sealed class OverlaySettings
{
    public const int MinOpacity = 20;
    public const int MaxOpacity = 100;

    /// <summary>Whether the icon is shown while the volume is reduced. Off by default.</summary>
    public bool Enabled { get; set; }

    public OverlayCorner Corner { get; set; } = OverlayCorner.TopRight;

    public OverlaySize Size { get; set; } = OverlaySize.Medium;

    /// <summary>The icon's opacity in percent (<see cref="MinOpacity"/>–<see cref="MaxOpacity"/>).</summary>
    public int Opacity { get; set; } = 85;

    public OverlayMonitor Monitor { get; set; } = OverlayMonitor.Primary;

    /// <summary>
    /// Experimental: keep the indicator window on screen (practically invisible) whenever the overlay is
    /// enabled, and only make it visible while the volume is reduced. A window appearing over a fullscreen
    /// game makes Windows switch from sending the game's frames straight to the monitor to composing the
    /// desktop, which causes a short black screen on some HDR setups; a window that is always there avoids
    /// that switch on toggle. Off by default.
    /// </summary>
    public bool KeepWindowReady { get; set; }
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

    /// <summary>
    /// The "Windows default device" row. A device that is also in <see cref="Devices"/> (enabled) uses its
    /// own settings there instead. Missing in settings files from before this option, which means off.
    /// </summary>
    public WindowsDefaultDeviceConfig WindowsDefaultDevice { get; set; } = new();

    /// <summary>The on-screen indicator. Missing in settings files from before this option, which means off.</summary>
    public OverlaySettings Overlay { get; set; } = new();

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
