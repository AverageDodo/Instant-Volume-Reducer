using InstantVolumeReducer.Audio;

namespace InstantVolumeReducer.Tests.Core;

/// <summary>
/// In-memory <see cref="IAudioDeviceService"/> for tests.
/// </summary>
internal sealed class FakeAudioDeviceService : IAudioDeviceService
{
    private readonly Dictionary<string, FakeDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private string? _defaultId;

    public event EventHandler? DevicesChanged;

    public event EventHandler? DefaultDeviceChanged;

    /// <summary>Simulates the user picking another default output device (or none) in Windows.</summary>
    public FakeAudioDeviceService SetDefault(string? id)
    {
        _defaultId = id;
        DefaultDeviceChanged?.Invoke(this, EventArgs.Empty);
        return this;
    }

    public string? GetDefaultOutputDeviceId() => _defaultId;

    /// <summary>Number of successful <see cref="TrySetVolume"/> calls.</summary>
    public int SetCalls { get; private set; }

    public FakeAudioDeviceService Add(string id, float volume, bool active = true)
    {
        _devices[id] = new FakeDevice(id, volume, active);
        return this;
    }

    public void Unplug(string id)
    {
        _devices[id].IsActive = false;
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlugIn(string id)
    {
        _devices[id].IsActive = true;
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Simulates the user moving the Windows volume slider.</summary>
    public void SetVolumeByHand(string id, float volume) => _devices[id].Volume = volume;

    /// <summary>Reads the volume regardless of whether the device is active.</summary>
    public float VolumeOf(string id) => _devices[id].Volume;

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
        _devices.Values.Select(d => new AudioDeviceInfo(d.Id, d.Id, d.IsActive)).ToList();

    public bool IsDeviceAvailable(string deviceId) =>
        _devices.TryGetValue(deviceId, out var device) && device.IsActive;

    public bool TryGetVolume(string deviceId, out float volume)
    {
        volume = 0f;
        if (!IsDeviceAvailable(deviceId))
        {
            return false;
        }

        volume = _devices[deviceId].Volume;
        return true;
    }

    public bool TrySetVolume(string deviceId, float volume)
    {
        if (!IsDeviceAvailable(deviceId))
        {
            return false;
        }

        _devices[deviceId].Volume = VolumeLevel.Clamp(volume);
        SetCalls++;
        return true;
    }

    private sealed class FakeDevice(string id, float volume, bool isActive)
    {
        public string Id { get; } = id;
        public float Volume { get; set; } = volume;
        public bool IsActive { get; set; } = isActive;
    }
}
