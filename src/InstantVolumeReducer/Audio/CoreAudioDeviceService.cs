using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace InstantVolumeReducer.Audio;

/// <summary>
/// <see cref="IAudioDeviceService"/> implementation backed by the Windows Core Audio API via NAudio.
/// </summary>
public sealed class CoreAudioDeviceService : IAudioDeviceService, IDisposable
{
    private const DeviceState ListedStates = DeviceState.Active | DeviceState.Unplugged;

    private readonly MMDeviceEnumerator _enumerator;
    private readonly MMDeviceNotificationClient _notificationClient;
    private readonly SynchronizationContext? _synchronizationContext;
    private bool _disposed;

    /// <summary>
    /// Creates the service. Create it on the UI thread so <see cref="DevicesChanged"/> is raised there;
    /// without a synchronization context it is raised on a thread-pool thread.
    /// </summary>
    public CoreAudioDeviceService()
    {
        _synchronizationContext = SynchronizationContext.Current;
        _enumerator = new MMDeviceEnumerator();

        // Raise NAudio's events synchronously on the audio worker thread and re-dispatch them ourselves,
        // so handlers never run on that thread (it holds an internal lock and must not call back into
        // the audio stack) regardless of whether a synchronization context exists.
        _notificationClient = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
        _notificationClient.DeviceAdded += (_, _) => OnDevicesChanged();
        _notificationClient.DeviceRemoved += (_, _) => OnDevicesChanged();
        _notificationClient.DeviceStateChanged += (_, _) => OnDevicesChanged();
    }

    /// <inheritdoc />
    public event EventHandler? DevicesChanged;

    /// <inheritdoc />
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var devices = new List<AudioDeviceInfo>();
        try
        {
            using var collection = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, ListedStates);
            for (var i = 0; i < collection.Count; i++)
            {
                using var device = collection[i];
                if (TryDescribe(device) is { } info)
                {
                    devices.Add(info);
                }
            }
        }
        catch (Exception ex) when (IsDeviceError(ex))
        {
            // Return what was collected so far; the device list may have changed mid-enumeration.
        }

        return devices;
    }

    /// <inheritdoc />
    public bool IsDeviceAvailable(string deviceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var device = TryGetActiveDevice(deviceId);
        return device is not null;
    }

    /// <inheritdoc />
    public bool TryGetVolume(string deviceId, out float volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        volume = 0f;
        using var device = TryGetActiveDevice(deviceId);
        if (device is null)
        {
            return false;
        }

        try
        {
            volume = VolumeLevel.Clamp(device.AudioEndpointVolume.MasterVolumeLevelScalar);
            return true;
        }
        catch (Exception ex) when (IsDeviceError(ex))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool TrySetVolume(string deviceId, float volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var device = TryGetActiveDevice(deviceId);
        if (device is null)
        {
            return false;
        }

        try
        {
            device.AudioEndpointVolume.MasterVolumeLevelScalar = VolumeLevel.Clamp(volume);
            return true;
        }
        catch (Exception ex) when (IsDeviceError(ex))
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DevicesChanged = null;
        _notificationClient.Dispose();
        _enumerator.Dispose();
    }

    /// <summary>
    /// Looks up an active render device by ID. Returns <c>null</c> (and disposes any wrapper it got)
    /// if the device doesn't exist, isn't an output device or isn't active.
    /// </summary>
    private MMDevice? TryGetActiveDevice(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return null;
        }

        MMDevice? device = null;
        try
        {
            device = _enumerator.GetDevice(deviceId);
            if (device.DataFlow == DataFlow.Render && device.State == DeviceState.Active)
            {
                return device;
            }
        }
        catch (Exception ex) when (IsDeviceError(ex))
        {
            // Unknown ID (E_NOTFOUND) or the device vanished while we were looking at it.
        }

        device?.Dispose();
        return null;
    }

    private static AudioDeviceInfo? TryDescribe(MMDevice device)
    {
        try
        {
            return new AudioDeviceInfo(device.ID, device.FriendlyName, device.State == DeviceState.Active);
        }
        catch (Exception ex) when (IsDeviceError(ex))
        {
            // The device disappeared while we were reading its properties; skip it.
            return null;
        }
    }

    /// <summary>
    /// Errors that mean a device is missing, gone or temporarily unusable, as opposed to programming errors.
    /// </summary>
    private static bool IsDeviceError(Exception ex) =>
        ex is COMException or InvalidOperationException or ArgumentException or UnauthorizedAccessException;

    private void OnDevicesChanged()
    {
        if (_disposed)
        {
            return;
        }

        if (_synchronizationContext is not null)
        {
            _synchronizationContext.Post(_ => RaiseDevicesChanged(), null);
        }
        else
        {
            ThreadPool.QueueUserWorkItem(_ => RaiseDevicesChanged());
        }
    }

    private void RaiseDevicesChanged()
    {
        if (!_disposed)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
