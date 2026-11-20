namespace InstantVolumeReducer.Audio;

/// <summary>
/// Describes an audio output (render) endpoint.
/// </summary>
/// <param name="Id">The stable Windows endpoint ID. Treat it as opaque; use it as the key for settings and state.</param>
/// <param name="Name">The friendly name shown to the user (e.g. "Speakers (Realtek(R) Audio)").</param>
/// <param name="IsActive">Whether the device is currently connected and enabled, so its volume can be read and set.</param>
public sealed record AudioDeviceInfo(string Id, string Name, bool IsActive);

/// <summary>
/// Access to the output devices' master volume. All methods report failures through their return
/// values instead of throwing, because devices can disappear at any moment (unplugged, disabled).
/// </summary>
public interface IAudioDeviceService
{
    /// <summary>
    /// Raised when an output device is added, removed or changes state (e.g. plugged in or unplugged).
    /// Never raised on the Windows audio notification thread, so handlers may call back into this service.
    /// </summary>
    event EventHandler? DevicesChanged;

    /// <summary>
    /// Raised when the Windows default output device changes (the one chosen in Sound settings or the
    /// taskbar volume menu). Raised once per actual change, on the same thread as <see cref="DevicesChanged"/>.
    /// </summary>
    event EventHandler? DefaultDeviceChanged;

    /// <summary>
    /// The ID of the current Windows default output device, or <c>null</c> if there is none (e.g. no
    /// output device is connected).
    /// </summary>
    string? GetDefaultOutputDeviceId();

    /// <summary>
    /// Lists output devices that are either active or unplugged (known but not currently connected).
    /// Disabled and removed devices are not listed.
    /// </summary>
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();

    /// <summary>
    /// Returns <c>true</c> if the device exists and is active, so its volume can be read and set.
    /// </summary>
    bool IsDeviceAvailable(string deviceId);

    /// <summary>
    /// Reads the device's master volume as a scalar from 0.0 to 1.0.
    /// Returns <c>false</c> if the device is missing, not active or cannot be read.
    /// </summary>
    bool TryGetVolume(string deviceId, out float volume);

    /// <summary>
    /// Sets the device's master volume as a scalar from 0.0 to 1.0. Values outside that range are clamped.
    /// Returns <c>false</c> if the device is missing, not active or cannot be written.
    /// </summary>
    bool TrySetVolume(string deviceId, float volume);
}
