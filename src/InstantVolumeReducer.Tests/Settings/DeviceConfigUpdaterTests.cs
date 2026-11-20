using InstantVolumeReducer.Audio;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.Tests.Settings;

public class DeviceConfigUpdaterTests
{
    [Fact]
    public void EnableAllActive_AddsActiveDevicesWithDefaults()
    {
        var settings = new AppSettings { DefaultMode = ReductionMode.Absolute, DefaultAmount = 15 };

        var added = DeviceConfigUpdater.EnableAllActive(settings,
        [
            new AudioDeviceInfo("a", "Speakers", true),
            new AudioDeviceInfo("b", "Headphones", false),
            new AudioDeviceInfo("c", "Monitor", true),
        ]);

        Assert.True(added);
        Assert.Equal(["a", "c"], settings.Devices.Select(d => d.DeviceId));
        Assert.All(settings.Devices, d =>
        {
            Assert.True(d.Enabled);
            Assert.Equal(ReductionMode.Absolute, d.Mode);
            Assert.Equal(15, d.Amount);
        });
        Assert.Equal("Speakers", settings.Devices[0].LastKnownName);
    }

    [Fact]
    public void EnableAllActive_LeavesExistingEntriesAlone()
    {
        var settings = new AppSettings
        {
            Devices = [new DeviceReductionConfig { DeviceId = "A", Enabled = false, Amount = 80 }],
        };

        var added = DeviceConfigUpdater.EnableAllActive(settings, [new AudioDeviceInfo("a", "Speakers", true)]);

        Assert.False(added);
        var device = Assert.Single(settings.Devices);
        Assert.False(device.Enabled);
        Assert.Equal(80, device.Amount);
    }

    [Fact]
    public void RefreshNames_UpdatesKnownDevicesOnly()
    {
        var settings = new AppSettings
        {
            Devices =
            [
                new DeviceReductionConfig { DeviceId = "a", LastKnownName = "Old name" },
                new DeviceReductionConfig { DeviceId = "b", LastKnownName = "Headphones" },
            ],
        };

        var changed = DeviceConfigUpdater.RefreshNames(settings,
        [
            new AudioDeviceInfo("A", "New name", false),
            new AudioDeviceInfo("b", "Headphones", true),
            new AudioDeviceInfo("c", "Brand new", true),
        ]);

        Assert.True(changed);
        Assert.Equal(["New name", "Headphones"], settings.Devices.Select(d => d.LastKnownName));
        Assert.Equal(2, settings.Devices.Count); // "c" isn't added.
    }

    [Fact]
    public void RefreshNames_NoChange_ReturnsFalse_AndIgnoresEmptyNames()
    {
        var settings = new AppSettings
        {
            Devices = [new DeviceReductionConfig { DeviceId = "a", LastKnownName = "Speakers" }],
        };

        Assert.False(DeviceConfigUpdater.RefreshNames(settings, [new AudioDeviceInfo("a", "Speakers", true)]));
        Assert.False(DeviceConfigUpdater.RefreshNames(settings, [new AudioDeviceInfo("a", " ", true)]));
        Assert.Equal("Speakers", settings.Devices[0].LastKnownName);
    }
}
