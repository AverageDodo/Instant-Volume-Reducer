using InstantVolumeReducer.Core;

namespace InstantVolumeReducer.Tests.Core;

/// <summary>Every test works in its own temp folder; the real <c>%APPDATA%</c> file is never touched.</summary>
public sealed class ReductionStateStoreTests : IDisposable
{
    private const int Precision = 4;

    private readonly string _directory;
    private readonly ReductionStateStore _store;

    public ReductionStateStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "InstantVolumeReducer.Tests", Guid.NewGuid().ToString("N"));
        _store = new ReductionStateStore(Path.Combine(_directory, "sub", "state.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static ReductionState TwoDevices() => new(
    [
        new ReducedDevice("speakers", 0.8f, 0.16f),
        new ReducedDevice("headphones", 0.5f, 0.1f),
    ]);

    private void WriteFile(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        File.WriteAllText(_store.FilePath, content);
    }

    [Fact]
    public void DefaultFilePath_IsNextToSettingsInAppData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "InstantVolumeReducer",
            "state.json");

        Assert.Equal(expected, ReductionStateStore.DefaultFilePath);
        Assert.Equal(expected, new ReductionStateStore().FilePath);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var result = _store.Load();

        Assert.Equal(StateLoadStatus.Missing, result.Status);
        Assert.False(result.State.IsReduced);
        Assert.Null(result.BackupPath);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        _store.Save(TwoDevices());

        var result = _store.Load();

        Assert.Equal(StateLoadStatus.Loaded, result.Status);
        Assert.Equal(TwoDevices().Devices, result.State.Devices);
    }

    [Fact]
    public void Save_CreatesFolderAndLeavesNoTempFile()
    {
        _store.Save(TwoDevices());

        Assert.True(File.Exists(_store.FilePath));
        Assert.False(File.Exists(_store.FilePath + ".tmp"));
    }

    [Fact]
    public void Save_EmptyState_DeletesFile()
    {
        _store.Save(TwoDevices());

        _store.Save(ReductionState.Empty);

        Assert.False(File.Exists(_store.FilePath));
    }

    [Fact]
    public void Save_EmptyState_WithoutFileOrFolder_DoesNothing()
    {
        _store.Save(ReductionState.Empty);

        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void Save_ReplacesPreviousState()
    {
        _store.Save(TwoDevices());
        var single = new ReductionState([new ReducedDevice("speakers", 0.3f, 0.06f)]);

        _store.Save(single);

        Assert.Equal(single.Devices, _store.Load().State.Devices);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{ \"Devices\": [ { \"DeviceId\": \"a\", ")]
    [InlineData("null")]
    [InlineData("[1, 2, 3]")]
    public void Load_CorruptFile_ReturnsEmptyAndMovesFileToBackup(string content)
    {
        WriteFile(content);

        var result = _store.Load();

        Assert.Equal(StateLoadStatus.Corrupt, result.Status);
        Assert.False(result.State.IsReduced);
        Assert.Equal(_store.BackupFilePath, result.BackupPath);
        Assert.Equal(content, File.ReadAllText(_store.BackupFilePath));
        Assert.False(File.Exists(_store.FilePath));
    }

    [Fact]
    public void Load_CorruptFile_IsOnlyReportedOnce()
    {
        WriteFile("not json");

        _store.Load();
        var second = _store.Load();

        Assert.Equal(StateLoadStatus.Missing, second.Status);
    }

    [Fact]
    public void Load_EmptyObject_IsLoadedAsNotReduced()
    {
        WriteFile("{}");

        var result = _store.Load();

        Assert.Equal(StateLoadStatus.Loaded, result.Status);
        Assert.False(result.State.IsReduced);
    }

    [Fact]
    public void Load_DropsInvalidEntriesAndClampsVolumes()
    {
        WriteFile("""
            {
              "Devices": [
                { "DeviceId": "speakers", "OriginalVolume": 1.5, "ReducedVolume": -0.2 },
                { "DeviceId": "", "OriginalVolume": 0.5, "ReducedVolume": 0.1 },
                null,
                { "DeviceId": "SPEAKERS", "OriginalVolume": 0.4, "ReducedVolume": 0.1 },
                { "DeviceId": "headphones", "OriginalVolume": 0.6, "ReducedVolume": 0.12 }
              ]
            }
            """);

        var devices = _store.Load().State.Devices;

        Assert.Equal(2, devices.Count);
        Assert.Equal("speakers", devices[0].DeviceId);
        Assert.Equal(1f, devices[0].OriginalVolume, Precision);
        Assert.Equal(0f, devices[0].ReducedVolume, Precision);
        Assert.Equal("headphones", devices[1].DeviceId);
    }

    [Fact]
    public void TryClear_DeletesFile_AndSucceedsWhenMissing()
    {
        _store.Save(TwoDevices());

        Assert.True(_store.TryClear());
        Assert.False(File.Exists(_store.FilePath));
        Assert.True(_store.TryClear());
    }
}
