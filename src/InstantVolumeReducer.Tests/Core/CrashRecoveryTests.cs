using InstantVolumeReducer.Core;

namespace InstantVolumeReducer.Tests.Core;

/// <summary>Startup recovery against a temp state file and the fake audio service.</summary>
public sealed class CrashRecoveryTests : IDisposable
{
    private const int Precision = 4;

    private readonly string _directory;
    private readonly ReductionStateStore _store;

    public CrashRecoveryTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "InstantVolumeReducer.Tests", Guid.NewGuid().ToString("N"));
        _store = new ReductionStateStore(Path.Combine(_directory, "state.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void NoStateFile_DoesNothing()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.16f);

        var result = CrashRecovery.Run(_store, audio);

        Assert.Equal(RecoveryStatus.NothingToRecover, result.Status);
        Assert.Equal(0, audio.SetCalls);
        Assert.Equal(0.16f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void SavedReduction_IsRestoredAndFileDeleted()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.16f).Add("headphones", 0.1f);
        _store.Save(new ReductionState(
        [
            new ReducedDevice("speakers", 0.8f, 0.16f),
            new ReducedDevice("headphones", 0.5f, 0.1f),
        ]));

        var result = CrashRecovery.Run(_store, audio);

        Assert.Equal(RecoveryStatus.Restored, result.Status);
        Assert.Equal(new RestoreResult(2, 0), result.Restore);
        Assert.True(result.StateCleared);
        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
        Assert.Equal(0.5f, audio.VolumeOf("headphones"), Precision);
        Assert.False(File.Exists(_store.FilePath));
    }

    [Fact]
    public void SavedReduction_RestoresEvenAfterManualVolumeChange()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.16f);
        _store.Save(new ReductionState([new ReducedDevice("speakers", 0.8f, 0.16f)]));
        audio.SetVolumeByHand("speakers", 0.3f);

        CrashRecovery.Run(_store, audio);

        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void UnpluggedDevice_IsSkippedAndCounted()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.16f).Add("headphones", 0.1f, active: false);
        _store.Save(new ReductionState(
        [
            new ReducedDevice("speakers", 0.8f, 0.16f),
            new ReducedDevice("headphones", 0.5f, 0.1f),
        ]));

        var result = CrashRecovery.Run(_store, audio);

        Assert.Equal(RecoveryStatus.Restored, result.Status);
        Assert.Equal(new RestoreResult(1, 1), result.Restore);
        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
        Assert.Equal(0.1f, audio.VolumeOf("headphones"), Precision);
        Assert.False(File.Exists(_store.FilePath));
    }

    [Fact]
    public void DeviceThatNoLongerExists_IsSkipped()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.16f);
        _store.Save(new ReductionState([new ReducedDevice("gone", 0.8f, 0.16f)]));

        var result = CrashRecovery.Run(_store, audio);

        Assert.Equal(new RestoreResult(0, 1), result.Restore);
        Assert.Equal(0.16f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void CorruptStateFile_DoesNotThrowOrChangeVolume()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.16f);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_store.FilePath, "{ broken");

        var result = CrashRecovery.Run(_store, audio);

        Assert.Equal(RecoveryStatus.Corrupt, result.Status);
        Assert.Equal(_store.BackupFilePath, result.BackupPath);
        Assert.Equal(0, audio.SetCalls);
        Assert.False(File.Exists(_store.FilePath));
    }

    [Fact]
    public void EmptyStateFile_IsDeletedWithoutRestoring()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.16f);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_store.FilePath, """{ "Devices": [] }""");

        var result = CrashRecovery.Run(_store, audio);

        Assert.Equal(RecoveryStatus.NothingToRecover, result.Status);
        Assert.Equal(0, audio.SetCalls);
        Assert.False(File.Exists(_store.FilePath));
    }

    [Fact]
    public void StateFileFollowsToggleService_ThenRecoversAfterSimulatedCrash()
    {
        // The tray persists the state on every StateChanged; simulate that, then "crash" while reduced.
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var config = new InstantVolumeReducer.Settings.DeviceReductionConfig
        {
            DeviceId = "speakers",
            Enabled = true,
            Mode = InstantVolumeReducer.Settings.ReductionMode.Relative,
            Amount = 20,
        };
        var toggle = new VolumeToggleService(audio, [config]);
        toggle.StateChanged += (_, _) => _store.Save(toggle.State);

        toggle.Reduce();
        Assert.True(File.Exists(_store.FilePath));
        Assert.Equal(0.16f, audio.VolumeOf("speakers"), Precision);

        var result = CrashRecovery.Run(_store, audio);

        Assert.Equal(RecoveryStatus.Restored, result.Status);
        Assert.Equal(0.8f, audio.VolumeOf("speakers"), Precision);
    }

    [Fact]
    public void StateFileIsDeletedAfterNormalRestore()
    {
        var audio = new FakeAudioDeviceService().Add("speakers", 0.8f);
        var config = new InstantVolumeReducer.Settings.DeviceReductionConfig { DeviceId = "speakers", Enabled = true };
        var toggle = new VolumeToggleService(audio, [config]);
        toggle.StateChanged += (_, _) => _store.Save(toggle.State);

        toggle.Reduce();
        toggle.Restore();

        Assert.False(File.Exists(_store.FilePath));
        Assert.Equal(RecoveryStatus.NothingToRecover, CrashRecovery.Run(_store, audio).Status);
    }
}
