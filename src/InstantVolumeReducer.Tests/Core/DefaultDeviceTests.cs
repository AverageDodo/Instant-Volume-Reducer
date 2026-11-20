using System.Text.Json;
using InstantVolumeReducer.Core;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.Tests.Core;

/// <summary>Following the Windows default output device in <see cref="VolumeToggleService"/>.</summary>
public class DefaultDeviceTests
{
    private const int Precision = 4;

    private static DeviceReductionConfig Config(string id, ReductionMode mode = ReductionMode.Relative, int amount = 20) =>
        new() { DeviceId = id, LastKnownName = id, Enabled = true, Mode = mode, Amount = amount };

    private static WindowsDefaultDeviceConfig Follow(ReductionMode mode = ReductionMode.Absolute, int amount = 10, bool enabled = true) =>
        new() { Enabled = enabled, Mode = mode, Amount = amount };

    private static FakeAudioDeviceService Audio() => new FakeAudioDeviceService()
        .Add("speakers", 0.8f)
        .Add("headphones", 0.6f)
        .Add("monitor", 0.5f);

    [Fact]
    public void Reduce_ReducesCurrentDefaultDevice_WithDefaultRowSettings()
    {
        var audio = Audio().SetDefault("headphones");
        var service = new VolumeToggleService(audio, [], defaultDeviceConfig: Follow());

        Assert.True(service.Reduce());

        Assert.Equal(0.1f, audio.VolumeOf("headphones"), Precision);
        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
        var device = Assert.Single(service.State.Devices);
        Assert.True(device.ViaWindowsDefault);
    }

    [Fact]
    public void Reduce_DefaultDeviceAlsoSelectedByName_UsesItsOwnSettingsOnce()
    {
        var audio = Audio().SetDefault("speakers");
        var service = new VolumeToggleService(audio, [Config("speakers", ReductionMode.Relative, 50)], defaultDeviceConfig: Follow());

        service.Reduce();

        Assert.Equal(0.4f, audio.VolumeOf("speakers"), Precision);
        Assert.Equal(1, audio.SetCalls);
        var device = Assert.Single(service.State.Devices);
        Assert.False(device.ViaWindowsDefault);
        Assert.Equal(1, service.SelectedDeviceCount);
    }

    [Fact]
    public void Reduce_CombinesNamedDevicesAndDefaultDevice()
    {
        var audio = Audio().SetDefault("headphones");
        var service = new VolumeToggleService(audio, [Config("monitor")], defaultDeviceConfig: Follow());

        service.Reduce();

        Assert.Equal(2, service.State.Devices.Count);
        Assert.Equal(0.1f, audio.VolumeOf("headphones"), Precision);
        Assert.Equal(0.1f, audio.VolumeOf("monitor"), Precision);
        Assert.Equal(2, service.SelectedDeviceCount);
    }

    [Fact]
    public void Reduce_NoDefaultDevice_ReducesOnlyNamedDevices()
    {
        var audio = Audio();
        var service = new VolumeToggleService(audio, [Config("monitor")], defaultDeviceConfig: Follow());

        service.Reduce();

        Assert.Equal("monitor", Assert.Single(service.State.Devices).DeviceId);
        Assert.Equal(2, service.SelectedDeviceCount); // The default row counts as selected but missing.
    }

    [Fact]
    public void Reduce_DefaultRowDisabled_IgnoresDefaultDevice()
    {
        var audio = Audio().SetDefault("headphones");
        var service = new VolumeToggleService(audio, [], defaultDeviceConfig: Follow(enabled: false));

        Assert.False(service.Reduce());
        Assert.Equal(0, service.SelectedDeviceCount);
    }

    [Fact]
    public void DefaultChangesWhileReduced_MovesReduction()
    {
        var audio = Audio().SetDefault("speakers");
        var service = new VolumeToggleService(audio, [], defaultDeviceConfig: Follow());
        service.Reduce();
        var stateChanges = 0;
        service.StateChanged += (_, _) => stateChanges++;

        audio.SetDefault("headphones");
        var change = service.HandleDefaultDeviceChanged();

        Assert.Equal(["speakers"], change.RestoredDeviceIds);
        Assert.Equal("headphones", change.ReducedDeviceId);
        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
        Assert.Equal(0.1f, audio.VolumeOf("headphones"), Precision);
        var device = Assert.Single(service.State.Devices);
        Assert.Equal(("headphones", 0.6f, true), (device.DeviceId, device.OriginalVolume, device.ViaWindowsDefault));
        Assert.Equal(1, stateChanges);

        service.Restore();
        Assert.Equal(0.6f, audio.VolumeOf("headphones"), Precision);
        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void DefaultChangesWhileNotReduced_DoesNothing()
    {
        var audio = Audio().SetDefault("speakers");
        var service = new VolumeToggleService(audio, [], defaultDeviceConfig: Follow());

        audio.SetDefault("headphones");
        var change = service.HandleDefaultDeviceChanged();

        Assert.False(change.HasChanges);
        Assert.False(service.IsReduced);
        Assert.Equal(0, audio.SetCalls);
    }

    [Fact]
    public void DefaultChangesWhileReduced_DefaultRowDisabled_DoesNothing()
    {
        var audio = Audio().SetDefault("speakers");
        var service = new VolumeToggleService(audio, [Config("speakers")]);
        service.Reduce();

        audio.SetDefault("headphones");
        Assert.False(service.HandleDefaultDeviceChanged().HasChanges);
        Assert.Equal(0.6f, audio.VolumeOf("headphones"), Precision);
    }

    [Fact]
    public void DefaultMovesAwayFromNamedDevice_NamedDeviceStaysReduced()
    {
        var audio = Audio().SetDefault("speakers");
        var service = new VolumeToggleService(audio, [Config("speakers", ReductionMode.Relative, 50)], defaultDeviceConfig: Follow());
        service.Reduce();

        audio.SetDefault("headphones");
        var change = service.HandleDefaultDeviceChanged();

        Assert.Empty(change.RestoredDeviceIds);
        Assert.Equal("headphones", change.ReducedDeviceId);
        Assert.Equal(0.4f, audio.VolumeOf("speakers"), Precision);
        Assert.Equal(0.1f, audio.VolumeOf("headphones"), Precision);
        Assert.Equal(2, service.State.Devices.Count);
    }

    [Fact]
    public void DefaultMovesToReducedNamedDevice_OnlyRestoresPreviousDefault()
    {
        var audio = Audio().SetDefault("headphones");
        var service = new VolumeToggleService(audio, [Config("speakers", ReductionMode.Relative, 50)], defaultDeviceConfig: Follow());
        service.Reduce();

        audio.SetDefault("speakers");
        var change = service.HandleDefaultDeviceChanged();

        Assert.Equal(["headphones"], change.RestoredDeviceIds);
        Assert.Null(change.ReducedDeviceId);
        Assert.Equal(0.6f, audio.VolumeOf("headphones"), Precision);
        Assert.Equal(0.4f, audio.VolumeOf("speakers"), Precision); // Not reduced a second time.
        Assert.Equal("speakers", Assert.Single(service.State.Devices).DeviceId);
    }

    [Fact]
    public void DefaultMovesToNamedDeviceThatWasNotReduced_ReducesItWithItsOwnSettings()
    {
        var audio = Audio().SetDefault("headphones");
        audio.Unplug("speakers");
        var service = new VolumeToggleService(audio, [Config("speakers", ReductionMode.Relative, 50)], defaultDeviceConfig: Follow());
        service.Reduce();

        audio.PlugIn("speakers");
        audio.SetDefault("speakers");
        var change = service.HandleDefaultDeviceChanged();

        Assert.Equal("speakers", change.ReducedDeviceId);
        Assert.Equal(0.4f, audio.VolumeOf("speakers"), Precision);
        Assert.False(service.State.Devices.Single(d => d.DeviceId == "speakers").ViaWindowsDefault);
    }

    [Fact]
    public void PreviousDefaultUnplugged_StaysInStateAndIsRestoredLater()
    {
        var audio = Audio().SetDefault("headphones");
        var service = new VolumeToggleService(audio, [], defaultDeviceConfig: Follow());
        service.Reduce();

        // Unplugging the headphones is what makes Windows switch the default to the speakers.
        audio.Unplug("headphones");
        audio.SetDefault("speakers");
        var change = service.HandleDefaultDeviceChanged();

        Assert.Empty(change.RestoredDeviceIds);
        Assert.Equal("speakers", change.ReducedDeviceId);
        Assert.Equal(["headphones", "speakers"], service.State.Devices.Select(d => d.DeviceId));
        Assert.Equal(0.1f, audio.VolumeOf("headphones"), Precision);

        // Plugged back in and the default again: it's still reduced, so it isn't reduced a second time,
        // and the speakers go back to their original volume.
        audio.PlugIn("headphones");
        audio.SetDefault("headphones");
        change = service.HandleDefaultDeviceChanged();

        Assert.Equal(["speakers"], change.RestoredDeviceIds);
        Assert.Null(change.ReducedDeviceId);
        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);

        service.Restore();
        Assert.Equal(0.6f, audio.VolumeOf("headphones"), Precision);
    }

    [Fact]
    public void DefaultDisappearsWhileReduced_RestoresPreviousDefault()
    {
        var audio = Audio().SetDefault("speakers");
        var service = new VolumeToggleService(audio, [], defaultDeviceConfig: Follow());
        service.Reduce();

        audio.SetDefault(null);
        var change = service.HandleDefaultDeviceChanged();

        Assert.Equal(["speakers"], change.RestoredDeviceIds);
        Assert.False(service.IsReduced);
        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void UpdateDeviceConfigs_DefaultRowUnticked_RestoresDefaultDevice()
    {
        var audio = Audio().SetDefault("headphones");
        var service = new VolumeToggleService(audio, [Config("monitor")], defaultDeviceConfig: Follow());
        service.Reduce();

        service.UpdateDeviceConfigs([Config("monitor")], Follow(enabled: false));

        Assert.Equal(0.6f, audio.VolumeOf("headphones"), Precision);
        Assert.Equal("monitor", Assert.Single(service.State.Devices).DeviceId);
    }

    [Fact]
    public void UpdateDeviceConfigs_NamedDeviceUntickedButStillDefault_StaysReducedAndFollowsDefault()
    {
        var audio = Audio().SetDefault("speakers");
        var service = new VolumeToggleService(audio, [Config("speakers")], defaultDeviceConfig: Follow());
        service.Reduce();

        service.UpdateDeviceConfigs([], Follow());

        var device = Assert.Single(service.State.Devices);
        Assert.True(device.ViaWindowsDefault);
        Assert.Equal(0.16f, audio.VolumeOf("speakers"), Precision);

        // Now it's restored when the default moves on.
        audio.SetDefault("headphones");
        service.HandleDefaultDeviceChanged();
        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void UpdateDeviceConfigs_DefaultDeviceTickedByName_NoLongerFollowsDefault()
    {
        var audio = Audio().SetDefault("speakers");
        var service = new VolumeToggleService(audio, [], defaultDeviceConfig: Follow());
        service.Reduce();

        service.UpdateDeviceConfigs([Config("speakers")], Follow());
        Assert.False(Assert.Single(service.State.Devices).ViaWindowsDefault);

        audio.SetDefault("headphones");
        service.HandleDefaultDeviceChanged();
        Assert.Equal(0.1f, audio.VolumeOf("speakers"), Precision); // Still reduced.
    }

    [Fact]
    public void UpdateDeviceConfigs_NothingChanged_DoesNotRaiseStateChanged()
    {
        var audio = Audio().SetDefault("speakers");
        var service = new VolumeToggleService(audio, [Config("monitor")], defaultDeviceConfig: Follow());
        service.Reduce();
        var stateChanges = 0;
        service.StateChanged += (_, _) => stateChanges++;

        service.UpdateDeviceConfigs([Config("monitor")], Follow());

        Assert.Equal(0, stateChanges);
    }

    [Fact]
    public void ReducedDevice_ViaWindowsDefault_RoundTripsAndDefaultsToFalse()
    {
        var state = new ReductionState([new ReducedDevice("a", 0.5f, 0.1f, ViaWindowsDefault: true)]);

        var restored = JsonSerializer.Deserialize<ReductionState>(JsonSerializer.Serialize(state));
        Assert.True(Assert.Single(restored!.Devices).ViaWindowsDefault);

        // A state file written before this option existed.
        var old = JsonSerializer.Deserialize<ReductionState>("""{"Devices":[{"DeviceId":"a","OriginalVolume":0.5,"ReducedVolume":0.1}]}""");
        Assert.False(Assert.Single(old!.Devices).ViaWindowsDefault);
    }
}
