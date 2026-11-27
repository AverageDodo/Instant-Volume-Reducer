using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.Tests.Settings;

/// <summary>Every test works in its own temp folder; the real <c>%APPDATA%</c> file is never touched.</summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly SettingsStore _store;

    public SettingsStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "InstantVolumeReducer.Tests", Guid.NewGuid().ToString("N"));
        _store = new SettingsStore(Path.Combine(_directory, "sub", "settings.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void DefaultFilePath_IsInAppData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "InstantVolumeReducer",
            "settings.json");

        Assert.Equal(expected, SettingsStore.DefaultFilePath);
        Assert.Equal(expected, new SettingsStore().FilePath);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var result = _store.Load();

        Assert.Equal(SettingsLoadStatus.Missing, result.Status);
        Assert.Null(result.BackupPath);
        Assert.Equal(AppSettings.DefaultHotkey, result.Settings.Hotkey);
        Assert.Empty(result.Settings.Devices);
        Assert.False(File.Exists(_store.FilePath));
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var settings = new AppSettings
        {
            Hotkey = "Ctrl+Shift+F12",
            DefaultMode = ReductionMode.Absolute,
            DefaultAmount = 35,
            StartWithWindows = true,
            ShowNotifications = true,
            Devices =
            [
                new DeviceReductionConfig { DeviceId = "a", LastKnownName = "Speakers", Enabled = true, Mode = ReductionMode.Absolute, Amount = 10 },
                new DeviceReductionConfig { DeviceId = "b", LastKnownName = "Headphones", Enabled = false, Mode = ReductionMode.Relative, Amount = 50 },
            ],
        };

        _store.Save(settings);
        var result = _store.Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        var loaded = result.Settings;
        Assert.Equal("Ctrl+Shift+F12", loaded.Hotkey);
        Assert.Equal(ReductionMode.Absolute, loaded.DefaultMode);
        Assert.Equal(35, loaded.DefaultAmount);
        Assert.True(loaded.StartWithWindows);
        Assert.True(loaded.ShowNotifications);
        Assert.Equal(2, loaded.Devices.Count);
        Assert.Equal(("a", "Speakers", true, ReductionMode.Absolute, 10), Describe(loaded.Devices[0]));
        Assert.Equal(("b", "Headphones", false, ReductionMode.Relative, 50), Describe(loaded.Devices[1]));
    }

    [Fact]
    public void Save_WritesIndentedJson_AndLeavesNoTempFile()
    {
        _store.Save(new AppSettings());

        var json = File.ReadAllText(_store.FilePath);
        Assert.Contains(Environment.NewLine + "  \"Hotkey\"", json);
        Assert.False(File.Exists(_store.FilePath + ".tmp"));
    }

    [Fact]
    public void Save_WritesHotkeyAndNamesUnescaped()
    {
        _store.Save(new AppSettings
        {
            Hotkey = "Ctrl+Alt+V",
            Devices = [new DeviceReductionConfig { DeviceId = "a", LastKnownName = "Lautsprecher (Gerät) & Co" }],
        });

        var json = File.ReadAllText(_store.FilePath);
        Assert.Contains("\"Ctrl+Alt+V\"", json);
        Assert.Contains("\"Lautsprecher (Gerät) & Co\"", json);
    }

    [Fact]
    public void Save_ReplacesExistingFile()
    {
        _store.Save(new AppSettings { DefaultAmount = 10 });
        _store.Save(new AppSettings { DefaultAmount = 70 });

        Assert.Equal(70, _store.Load().Settings.DefaultAmount);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ \"DefaultMode\": \"Loudest\" }")]
    [InlineData("")]
    public void Load_CorruptFile_ReturnsDefaults_AndKeepsBackup(string content)
    {
        WriteRaw(content);

        var result = _store.Load();

        Assert.Equal(SettingsLoadStatus.Corrupt, result.Status);
        Assert.Equal(AppSettings.DefaultHotkey, result.Settings.Hotkey);
        Assert.Empty(result.Settings.Devices);
        Assert.Equal(_store.BackupFilePath, result.BackupPath);
        Assert.Equal(content, File.ReadAllText(_store.BackupFilePath));
    }

    [Fact]
    public void Load_CorruptFile_ThenSave_KeepsBackupOfBadFile()
    {
        WriteRaw("{ broken");

        _store.Load();
        _store.Save(new AppSettings());

        Assert.Equal("{ broken", File.ReadAllText(_store.BackupFilePath));
        Assert.Equal(SettingsLoadStatus.Loaded, _store.Load().Status);
    }

    [Fact]
    public void Load_LockedFile_ReturnsDefaultsWithoutThrowing()
    {
        WriteRaw("{}");
        using var lockStream = new FileStream(_store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = _store.Load();

        Assert.Equal(SettingsLoadStatus.Corrupt, result.Status);
        Assert.Equal(AppSettings.DefaultHotkey, result.Settings.Hotkey);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(55, 55)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    public void Load_ClampsAmounts(int stored, int expected)
    {
        WriteRaw($$"""
            {
              "DefaultAmount": {{stored}},
              "Devices": [ { "DeviceId": "a", "Enabled": true, "Amount": {{stored}} } ]
            }
            """);

        var settings = _store.Load().Settings;

        Assert.Equal(expected, settings.DefaultAmount);
        Assert.Equal(expected, settings.Devices[0].Amount);
    }

    [Theory]
    [InlineData("\"V\"")]
    [InlineData("\"Ctrl+Nope\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void Load_InvalidHotkey_FallsBackToDefault(string hotkeyJson)
    {
        WriteRaw($$"""{ "Hotkey": {{hotkeyJson}}, "DefaultAmount": 40 }""");

        var result = _store.Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(AppSettings.DefaultHotkey, result.Settings.Hotkey);
        Assert.Equal(40, result.Settings.DefaultAmount); // The rest of the file is kept.
    }

    [Fact]
    public void Load_ValidHotkey_IsWrittenInStandardForm()
    {
        WriteRaw("""{ "Hotkey": "shift + control + f12" }""");

        Assert.Equal("Ctrl+Shift+F12", _store.Load().Settings.Hotkey);
    }

    [Fact]
    public void Load_DropsDevicesWithoutIdAndDuplicates_AndFixesBadValues()
    {
        WriteRaw("""
            {
              "Devices": [
                { "DeviceId": "a", "LastKnownName": "First", "Mode": 7 },
                { "DeviceId": "" },
                null,
                { "DeviceId": "A", "LastKnownName": "Duplicate" },
                { "DeviceId": "b", "LastKnownName": null }
              ],
              "DefaultMode": 9
            }
            """);

        var settings = _store.Load().Settings;

        Assert.Equal(["a", "b"], settings.Devices.Select(d => d.DeviceId));
        Assert.Equal("First", settings.Devices[0].LastKnownName);
        Assert.Equal(AppSettings.DefaultReductionMode, settings.Devices[0].Mode);
        Assert.Equal(string.Empty, settings.Devices[1].LastKnownName);
        Assert.Equal(AppSettings.DefaultReductionMode, settings.DefaultMode);
    }

    [Fact]
    public void Load_NullDeviceList_BecomesEmpty()
    {
        WriteRaw("""{ "Devices": null }""");

        Assert.Empty(_store.Load().Settings.Devices);
    }

    [Fact]
    public void Load_FileWithoutWindowsDefaultDevice_HasItOff()
    {
        WriteRaw("""{ "Hotkey": "Ctrl+Alt+V", "Devices": [] }""");

        var config = _store.Load().Settings.WindowsDefaultDevice;

        Assert.False(config.Enabled);
        Assert.Equal((AppSettings.DefaultReductionMode, AppSettings.DefaultReductionAmount), (config.Mode, config.Amount));
    }

    [Fact]
    public void Load_WindowsDefaultDevice_NullOrBadValues_AreFixed()
    {
        WriteRaw("""{ "WindowsDefaultDevice": null }""");
        Assert.NotNull(_store.Load().Settings.WindowsDefaultDevice);

        WriteRaw("""{ "WindowsDefaultDevice": { "Enabled": true, "Mode": 7, "Amount": 250 } }""");
        var config = _store.Load().Settings.WindowsDefaultDevice;
        Assert.True(config.Enabled);
        Assert.Equal((AppSettings.DefaultReductionMode, 100), (config.Mode, config.Amount));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsWindowsDefaultDevice()
    {
        _store.Save(new AppSettings
        {
            WindowsDefaultDevice = new WindowsDefaultDeviceConfig { Enabled = true, Mode = ReductionMode.Absolute, Amount = 5 },
        });

        var config = _store.Load().Settings.WindowsDefaultDevice;
        Assert.Equal((true, ReductionMode.Absolute, 5), (config.Enabled, config.Mode, config.Amount));
    }

    [Fact]
    public void Load_FileWithoutOverlay_HasItOffWithDefaults()
    {
        WriteRaw("""{ "Hotkey": "Ctrl+Alt+V" }""");

        var overlay = _store.Load().Settings.Overlay;

        Assert.False(overlay.Enabled);
        Assert.False(overlay.KeepWindowReady);
        Assert.Equal((OverlayCorner.TopRight, OverlaySize.Medium, 85, OverlayMonitor.Primary), (overlay.Corner, overlay.Size, overlay.Opacity, overlay.Monitor));
    }

    [Fact]
    public void Load_Overlay_NullOrBadValues_AreFixed()
    {
        WriteRaw("""{ "Overlay": null }""");
        Assert.NotNull(_store.Load().Settings.Overlay);

        WriteRaw("""{ "Overlay": { "Enabled": true, "Corner": 9, "Size": -1, "Opacity": 3, "Monitor": 42 } }""");
        var overlay = _store.Load().Settings.Overlay;
        Assert.True(overlay.Enabled);
        Assert.Equal((OverlayCorner.TopRight, OverlaySize.Medium, OverlaySettings.MinOpacity, OverlayMonitor.Primary), (overlay.Corner, overlay.Size, overlay.Opacity, overlay.Monitor));

        WriteRaw("""{ "Overlay": { "Opacity": 500 } }""");
        Assert.Equal(OverlaySettings.MaxOpacity, _store.Load().Settings.Overlay.Opacity);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsOverlay_WithEnumsAsText()
    {
        _store.Save(new AppSettings
        {
            Overlay = new OverlaySettings
            {
                Enabled = true,
                Corner = OverlayCorner.BottomLeft,
                Size = OverlaySize.Large,
                Opacity = 60,
                Monitor = OverlayMonitor.ActiveWindow,
                KeepWindowReady = true,
            },
        });

        Assert.Contains("\"BottomLeft\"", File.ReadAllText(_store.FilePath));
        var overlay = _store.Load().Settings.Overlay;
        Assert.Equal(
            (true, OverlayCorner.BottomLeft, OverlaySize.Large, 60, OverlayMonitor.ActiveWindow),
            (overlay.Enabled, overlay.Corner, overlay.Size, overlay.Opacity, overlay.Monitor));
        Assert.True(overlay.KeepWindowReady);
    }

    private void WriteRaw(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        File.WriteAllText(_store.FilePath, content);
    }

    private static (string, string, bool, ReductionMode, int) Describe(DeviceReductionConfig d) =>
        (d.DeviceId, d.LastKnownName, d.Enabled, d.Mode, d.Amount);
}
