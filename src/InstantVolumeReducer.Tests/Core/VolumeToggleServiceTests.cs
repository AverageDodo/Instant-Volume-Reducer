using InstantVolumeReducer.Core;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.Tests.Core;

public class VolumeToggleServiceTests
{
    private const int Precision = 4;

    private static DeviceReductionConfig Config(string id, ReductionMode mode = ReductionMode.Relative, int amount = 20, bool enabled = true) =>
        new() { DeviceId = id, LastKnownName = id, Enabled = enabled, Mode = mode, Amount = amount };

    [Fact]
    public void Toggle_ReducesThenRestores()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var service = new VolumeToggleService(audio, [Config("speakers")]);

        Assert.True(service.Toggle());
        Assert.True(service.IsReduced);
        Assert.Equal(0.16f, audio.VolumeOf("speakers"), Precision);

        Assert.False(service.Toggle());
        Assert.False(service.IsReduced);
        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
        Assert.Same(ReductionState.Empty, service.State);
    }

    [Fact]
    public void Reduce_RecordsOriginalAndReducedVolume()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.5f);
        var service = new VolumeToggleService(audio, [Config("speakers", ReductionMode.Relative, 50)]);

        service.Reduce();

        var device = Assert.Single(service.State.Devices);
        Assert.Equal("speakers", device.DeviceId);
        Assert.Equal(0.5f, device.OriginalVolume, Precision);
        Assert.Equal(0.25f, device.ReducedVolume, Precision);
    }

    [Fact]
    public void Reduce_AbsoluteMode_SetsFixedLevel()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var service = new VolumeToggleService(audio, [Config("speakers", ReductionMode.Absolute, 10)]);

        service.Reduce();

        Assert.Equal(0.1f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void Reduce_DevicesWithDifferentModes_EachUseTheirOwnSettings()
    {
        var audio = new FakeAudioDeviceService()
            .Add("speakers", 0.8f)
            .Add("headphones", 0.6f);
        var service = new VolumeToggleService(audio,
        [
            Config("speakers", ReductionMode.Relative, 25),
            Config("headphones", ReductionMode.Absolute, 5),
        ]);

        service.Toggle();

        Assert.Equal(0.2f, audio.VolumeOf("speakers"), Precision);
        Assert.Equal(0.05f, audio.VolumeOf("headphones"), Precision);
        Assert.Equal(2, service.State.Devices.Count);

        service.Toggle();

        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
        Assert.Equal(0.6f, audio.VolumeOf("headphones"), Precision);
    }

    [Fact]
    public void Reduce_SkipsDisabledDevices()
    {
        var audio = new FakeAudioDeviceService()
            .Add("speakers", 0.8f)
            .Add("headphones", 0.6f);
        var service = new VolumeToggleService(audio, [Config("speakers"), Config("headphones", enabled: false)]);

        service.Reduce();

        Assert.Equal(0.6f, audio.VolumeOf("headphones"), Precision);
        Assert.Equal("speakers", Assert.Single(service.State.Devices).DeviceId);
    }

    [Fact]
    public void Reduce_SkipsDeviceUnpluggedBeforehand()
    {
        var audio = new FakeAudioDeviceService()
            .Add("speakers", 0.8f)
            .Add("headphones", 0.6f, active: false);
        var service = new VolumeToggleService(audio, [Config("speakers"), Config("headphones")]);

        service.Reduce();

        Assert.Equal(0.6f, audio.VolumeOf("headphones"), Precision);
        Assert.Equal("speakers", Assert.Single(service.State.Devices).DeviceId);
    }

    [Fact]
    public void Reduce_SkipsUnknownDevice()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var service = new VolumeToggleService(audio, [Config("speakers"), Config("does-not-exist")]);

        Assert.True(service.Reduce());
        Assert.Single(service.State.Devices);
    }

    [Fact]
    public void Reduce_NothingEnabled_StaysNotReduced()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var service = new VolumeToggleService(audio, [Config("speakers", enabled: false)]);
        var stateChanges = 0;
        service.StateChanged += (_, _) => stateChanges++;

        Assert.False(service.Toggle());
        Assert.False(service.IsReduced);
        Assert.Equal(0, stateChanges);
        Assert.Equal(0, audio.SetCalls);
    }

    [Fact]
    public void Reduce_NoConfigs_StaysNotReduced()
    {
        var service = new VolumeToggleService(new FakeAudioDeviceService(), []);

        Assert.False(service.Toggle());
    }

    [Fact]
    public void Reduce_AllSelectedDevicesUnplugged_StaysNotReduced()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f, active: false);
        var service = new VolumeToggleService(audio, [Config("speakers")]);

        Assert.False(service.Reduce());
        Assert.False(service.IsReduced);
    }

    [Fact]
    public void Reduce_WhenAlreadyReduced_DoesNothing()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var service = new VolumeToggleService(audio, [Config("speakers")]);
        service.Reduce();
        var state = service.State;

        Assert.True(service.Reduce());

        Assert.Same(state, service.State);
        Assert.Equal(0.16f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void Restore_WhenNotReduced_DoesNothing()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var service = new VolumeToggleService(audio, [Config("speakers")]);
        var stateChanges = 0;
        service.StateChanged += (_, _) => stateChanges++;

        service.Restore();

        Assert.Equal(0, stateChanges);
        Assert.Equal(0, audio.SetCalls);
    }

    [Fact]
    public void Amount0_Relative_SilencesAndRestores()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.7f);
        var service = new VolumeToggleService(audio, [Config("speakers", ReductionMode.Relative, 0)]);

        service.Toggle();
        Assert.Equal(0f, audio.VolumeOf("speakers"), Precision);

        service.Toggle();
        Assert.Equal(0.7f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void Amount0_Absolute_SilencesAndRestores()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.7f);
        var service = new VolumeToggleService(audio, [Config("speakers", ReductionMode.Absolute, 0)]);

        service.Toggle();
        Assert.Equal(0f, audio.VolumeOf("speakers"), Precision);

        service.Toggle();
        Assert.Equal(0.7f, audio.VolumeOf("speakers"), Precision);
    }

    [Theory]
    [InlineData(ReductionMode.Relative)]
    [InlineData(ReductionMode.Absolute)]
    public void Amount100_LeavesVolumeUnchangedButCountsAsReduced(ReductionMode mode)
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.7f);
        var service = new VolumeToggleService(audio, [Config("speakers", mode, 100)]);

        Assert.True(service.Toggle());
        Assert.Equal(0.7f, audio.VolumeOf("speakers"), Precision);

        Assert.False(service.Toggle());
        Assert.Equal(0.7f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void Restore_DeviceUnpluggedWhileReduced_IsSkippedAndForgotten()
    {
        var audio = new FakeAudioDeviceService()
            .Add("speakers", 0.8f)
            .Add("headphones", 0.6f);
        var service = new VolumeToggleService(audio, [Config("speakers"), Config("headphones")]);
        service.Reduce();
        audio.Unplug("headphones");

        service.Restore();

        Assert.False(service.IsReduced);
        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
        Assert.Equal(0.12f, audio.VolumeOf("headphones"), Precision);

        // Plugging it back in doesn't bring the entry back: the next toggle is a fresh reduction.
        audio.PlugIn("headphones");
        Assert.Empty(service.State.Devices);
    }

    [Fact]
    public void Restore_IgnoresManualVolumeChangesWhileReduced()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var service = new VolumeToggleService(audio, [Config("speakers")]);
        service.Reduce();
        audio.SetVolumeByHand("speakers", 0.4f);

        service.Restore();

        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void UpdateDeviceConfigs_DeselectedWhileReduced_RestoresThatDeviceImmediately()
    {
        var audio = new FakeAudioDeviceService()
            .Add("speakers", 0.8f)
            .Add("headphones", 0.6f);
        var service = new VolumeToggleService(audio, [Config("speakers"), Config("headphones")]);
        service.Reduce();
        var stateChanges = 0;
        service.StateChanged += (_, _) => stateChanges++;

        service.UpdateDeviceConfigs([Config("speakers"), Config("headphones", enabled: false)]);

        Assert.Equal(1, stateChanges);
        Assert.True(service.IsReduced);
        Assert.Equal("speakers", Assert.Single(service.State.Devices).DeviceId);
        Assert.Equal(0.6f, audio.VolumeOf("headphones"), Precision);
        Assert.Equal(0.16f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void UpdateDeviceConfigs_RemovedFromListWhileReduced_RestoresThatDeviceImmediately()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f).Add("headphones", 0.6f);
        var service = new VolumeToggleService(audio, [Config("speakers"), Config("headphones")]);
        service.Reduce();

        service.UpdateDeviceConfigs([Config("speakers")]);

        Assert.Equal(0.6f, audio.VolumeOf("headphones"), Precision);
        Assert.Single(service.State.Devices);
    }

    [Fact]
    public void UpdateDeviceConfigs_LastReducedDeviceDeselected_EndsReduction()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var service = new VolumeToggleService(audio, [Config("speakers")]);
        service.Reduce();

        service.UpdateDeviceConfigs([Config("speakers", enabled: false)]);

        Assert.False(service.IsReduced);
        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void UpdateDeviceConfigs_ModeOrAmountChangedWhileReduced_TakesEffectOnNextReduction()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var service = new VolumeToggleService(audio, [Config("speakers", ReductionMode.Relative, 20)]);
        service.Reduce();
        var stateChanges = 0;
        service.StateChanged += (_, _) => stateChanges++;

        service.UpdateDeviceConfigs([Config("speakers", ReductionMode.Absolute, 50)]);

        Assert.Equal(0, stateChanges);
        Assert.Equal(0.16f, audio.VolumeOf("speakers"), Precision);

        service.Toggle(); // restore
        service.Toggle(); // reduce with the new settings
        Assert.Equal(0.5f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void UpdateDeviceConfigs_NewlyEnabledWhileReduced_IsNotReducedUntilNextToggle()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f).Add("headphones", 0.6f);
        var service = new VolumeToggleService(audio, [Config("speakers")]);
        service.Reduce();

        service.UpdateDeviceConfigs([Config("speakers"), Config("headphones")]);

        Assert.Equal(0.6f, audio.VolumeOf("headphones"), Precision);
        Assert.Single(service.State.Devices);
    }

    [Fact]
    public void Service_KeepsCopyOfConfigs()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var config = Config("speakers", ReductionMode.Relative, 50);
        var service = new VolumeToggleService(audio, [config]);

        config.Amount = 0;
        config.Enabled = false;
        service.Reduce();

        Assert.Equal(0.4f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void DuplicateDeviceIds_FirstEnabledEntryWins()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var service = new VolumeToggleService(audio,
        [
            Config("speakers", ReductionMode.Relative, 50, enabled: false),
            Config("speakers", ReductionMode.Relative, 25),
            Config("SPEAKERS", ReductionMode.Absolute, 0),
        ]);

        service.Reduce();

        Assert.Equal(0.2f, audio.VolumeOf("speakers"), Precision);
        Assert.Single(service.State.Devices);
    }

    [Fact]
    public void StateChanged_RaisedOnReduceAndRestore()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var service = new VolumeToggleService(audio, [Config("speakers")]);
        var observed = new List<bool>();
        service.StateChanged += (_, _) => observed.Add(service.IsReduced);

        service.Toggle();
        service.Toggle();

        Assert.Equal([true, false], observed);
    }

    [Fact]
    public void ConstructedFromSavedState_RestoreReturnsToOriginalVolumes()
    {
        // Simulates a crash while reduced: the device is still at its reduced level after restart.
        var audio = new FakeAudioDeviceService().Add("speakers", 0.16f).Add("headphones", 0.05f);
        var saved = new ReductionState(
        [
            new ReducedDevice("speakers", 0.8f, 0.16f),
            new ReducedDevice("headphones", 0.6f, 0.05f),
        ]);
        var service = new VolumeToggleService(audio, [Config("speakers")], saved);

        Assert.True(service.IsReduced);

        Assert.False(service.Toggle());
        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
        Assert.Equal(0.6f, audio.VolumeOf("headphones"), Precision);
    }

    [Fact]
    public void ConstructedFromEmptyState_IsNotReduced()
    {
        var service = new VolumeToggleService(new FakeAudioDeviceService(), [], ReductionState.Empty);

        Assert.False(service.IsReduced);
    }
}
