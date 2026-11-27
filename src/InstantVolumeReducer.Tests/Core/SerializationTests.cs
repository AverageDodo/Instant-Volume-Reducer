using System.Text.Json;
using InstantVolumeReducer.Core;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.Tests.Core;

public class SerializationTests
{
    [Fact]
    public void ReductionState_RoundTrips()
    {
        var state = new ReductionState(
        [
            new ReducedDevice("{0.0.0.00000000}.{a}", 0.8f, 0.16f),
            new ReducedDevice("{0.0.0.00000000}.{b}", 0.6f, 0.05f),
        ]);

        var json = JsonSerializer.Serialize(state);
        var restored = JsonSerializer.Deserialize<ReductionState>(json);

        Assert.NotNull(restored);
        Assert.True(restored.IsReduced);
        Assert.Equal(state.Devices, restored.Devices);
        Assert.DoesNotContain("IsReduced", json);
    }

    [Fact]
    public void EmptyReductionState_RoundTripsAsNotReduced()
    {
        var json = JsonSerializer.Serialize(ReductionState.Empty);
        var restored = JsonSerializer.Deserialize<ReductionState>(json);

        Assert.NotNull(restored);
        Assert.False(restored.IsReduced);
    }

    [Fact]
    public void AppSettings_HasExpectedDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal("Ctrl+Alt+V", settings.Hotkey);
        Assert.Equal(ReductionMode.Relative, settings.DefaultMode);
        Assert.Equal(20, settings.DefaultAmount);
        Assert.Empty(settings.Devices);
        Assert.False(settings.StartWithWindows);
        Assert.False(settings.ShowNotifications);
        Assert.True(settings.CheckForUpdates);
    }

    [Fact]
    public void AppSettings_RoundTripsWithModesAsText()
    {
        var settings = new AppSettings
        {
            Hotkey = "Ctrl+Shift+F13",
            DefaultMode = ReductionMode.Absolute,
            DefaultAmount = 15,
            StartWithWindows = true,
            ShowNotifications = true,
            Devices =
            [
                new DeviceReductionConfig { DeviceId = "a", LastKnownName = "Speakers", Enabled = true, Mode = ReductionMode.Absolute, Amount = 10 },
                new DeviceReductionConfig { DeviceId = "b", LastKnownName = "Headphones", Enabled = false, Mode = ReductionMode.Relative, Amount = 30 },
            ],
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.Contains("\"Absolute\"", json);
        Assert.NotNull(restored);
        Assert.Equal(settings.Hotkey, restored.Hotkey);
        Assert.Equal(settings.DefaultMode, restored.DefaultMode);
        Assert.Equal(settings.DefaultAmount, restored.DefaultAmount);
        Assert.Equal(settings.StartWithWindows, restored.StartWithWindows);
        Assert.Equal(settings.ShowNotifications, restored.ShowNotifications);
        Assert.Equal(2, restored.Devices.Count);
        Assert.Equal("Speakers", restored.Devices[0].LastKnownName);
        Assert.True(restored.Devices[0].Enabled);
        Assert.Equal(ReductionMode.Absolute, restored.Devices[0].Mode);
        Assert.Equal(10, restored.Devices[0].Amount);
        Assert.Equal(ReductionMode.Relative, restored.Devices[1].Mode);
    }

    [Fact]
    public void AppSettings_MissingPropertiesFallBackToDefaults()
    {
        var restored = JsonSerializer.Deserialize<AppSettings>("{}");

        Assert.NotNull(restored);
        Assert.Equal(AppSettings.DefaultHotkey, restored.Hotkey);
        Assert.Equal(20, restored.DefaultAmount);
        Assert.NotNull(restored.Devices);
    }
}
