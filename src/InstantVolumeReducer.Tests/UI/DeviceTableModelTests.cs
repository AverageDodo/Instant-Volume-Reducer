using InstantVolumeReducer.Audio;
using InstantVolumeReducer.Settings;
using InstantVolumeReducer.UI;

namespace InstantVolumeReducer.Tests.UI;

public class DeviceTableModelTests
{
    private const ReductionMode DefaultMode = ReductionMode.Relative;
    private const int DefaultAmount = 20;

    [Fact]
    public void BuildRows_MergesConnectedDevicesAndSavedConfigs()
    {
        AudioDeviceInfo[] devices =
        [
            new("spk", "Speakers", true),
            new("new", "Monitor", true),
            new("hp", "Headphones (current name)", false),
        ];
        DeviceReductionConfig[] configs =
        [
            new() { DeviceId = "SPK", LastKnownName = "Speakers", Enabled = true, Mode = ReductionMode.Absolute, Amount = 5 },
            new() { DeviceId = "hp", LastKnownName = "Headphones", Enabled = true, Mode = ReductionMode.Relative, Amount = 40 },
            new() { DeviceId = "gone", LastKnownName = "Old USB", Enabled = false, Amount = 60 },
        ];

        var rows = DeviceTableModel.BuildRows(devices, configs, DefaultMode, DefaultAmount);

        // Connected first, then disconnected, each sorted by name.
        Assert.Equal(["Monitor", "Speakers", "Headphones (current name)", "Old USB"], rows.Select(r => r.Name));

        var monitor = rows[0];
        Assert.True(monitor.IsConnected);
        Assert.False(monitor.Enabled);
        Assert.False(monitor.HasConfig);
        Assert.Equal((DefaultMode, DefaultAmount), (monitor.Mode, monitor.Amount));

        var speakers = rows[1];
        Assert.True(speakers.IsConnected);
        Assert.True(speakers.Enabled);
        Assert.True(speakers.HasConfig);
        Assert.Equal((ReductionMode.Absolute, 5), (speakers.Mode, speakers.Amount));

        var headphones = rows[2];
        Assert.False(headphones.IsConnected); // Listed by Windows, but unplugged.
        Assert.True(headphones.Enabled);

        var gone = rows[3];
        Assert.False(gone.IsConnected);
        Assert.Equal(60, gone.Amount);
    }

    [Fact]
    public void BuildRows_UnpluggedDeviceWithoutConfig_IsNotShown()
    {
        var rows = DeviceTableModel.BuildRows([new AudioDeviceInfo("hp", "Headphones", false)], [], DefaultMode, DefaultAmount);

        Assert.Empty(rows);
    }

    [Fact]
    public void BuildRows_SavedDeviceWithoutName_GetsPlaceholderName()
    {
        var rows = DeviceTableModel.BuildRows([], [new DeviceReductionConfig { DeviceId = "x" }], DefaultMode, DefaultAmount);

        Assert.Equal(DeviceTableModel.UnknownDeviceName, Assert.Single(rows).Name);
    }

    [Fact]
    public void BuildRows_DuplicateConfigs_FirstWins()
    {
        var rows = DeviceTableModel.BuildRows(
            [],
            [
                new DeviceReductionConfig { DeviceId = "a", LastKnownName = "A", Amount = 10 },
                new DeviceReductionConfig { DeviceId = "A", LastKnownName = "A again", Amount = 90 },
            ],
            DefaultMode,
            DefaultAmount);

        Assert.Equal(10, Assert.Single(rows).Amount);
    }

    [Fact]
    public void SetEnabled_NewDevice_GetsCurrentDefaults()
    {
        var row = NewRow(hasConfig: false, mode: ReductionMode.Relative, amount: 20);

        DeviceTableModel.SetEnabled(row, true, ReductionMode.Absolute, 7);

        Assert.True(row.Enabled);
        Assert.True(row.HasConfig);
        Assert.Equal((ReductionMode.Absolute, 7), (row.Mode, row.Amount));
    }

    [Fact]
    public void SetEnabled_ConfiguredDevice_KeepsItsValues()
    {
        var row = NewRow(hasConfig: true, mode: ReductionMode.Relative, amount: 45);

        DeviceTableModel.SetEnabled(row, true, ReductionMode.Absolute, 7);

        Assert.Equal((ReductionMode.Relative, 45), (row.Mode, row.Amount));
    }

    [Fact]
    public void SetEnabled_TickUntickTick_KeepsValuesFromFirstTick()
    {
        var row = NewRow(hasConfig: false, mode: ReductionMode.Relative, amount: 20);

        DeviceTableModel.SetEnabled(row, true, ReductionMode.Relative, 30);
        row.Amount = 55;
        DeviceTableModel.SetEnabled(row, false, ReductionMode.Absolute, 1);
        DeviceTableModel.SetEnabled(row, true, ReductionMode.Absolute, 1);

        Assert.Equal((ReductionMode.Relative, 55), (row.Mode, row.Amount));
    }

    [Fact]
    public void ToConfigs_SkipsNeverConfiguredRows_AndKeepsDisconnectedOnes()
    {
        DeviceRow[] rows =
        [
            NewRow(id: "untouched", hasConfig: false),
            NewRow(id: "ticked", hasConfig: true, enabled: true, mode: ReductionMode.Absolute, amount: 12),
            NewRow(id: "unticked-saved", hasConfig: true, enabled: false, amount: 33),
            NewRow(id: "offline", hasConfig: true, enabled: true, connected: false, amount: 150),
        ];

        var configs = DeviceTableModel.ToConfigs(rows);

        Assert.Equal(["ticked", "unticked-saved", "offline"], configs.Select(c => c.DeviceId));
        Assert.Equal((true, ReductionMode.Absolute, 12), (configs[0].Enabled, configs[0].Mode, configs[0].Amount));
        Assert.False(configs[1].Enabled);
        Assert.Equal(100, configs[2].Amount); // Clamped.
        Assert.Equal("Name of offline", configs[2].LastKnownName);
    }

    [Fact]
    public void ToConfigs_PlaceholderName_IsNotSaved()
    {
        var row = NewRow(id: "x", hasConfig: true, name: DeviceTableModel.UnknownDeviceName);

        Assert.Equal(string.Empty, Assert.Single(DeviceTableModel.ToConfigs([row])).LastKnownName);
    }

    [Fact]
    public void BuildRows_ThenToConfigs_RoundTripsSavedConfigs()
    {
        DeviceReductionConfig[] configs =
        [
            new() { DeviceId = "a", LastKnownName = "Speakers", Enabled = true, Mode = ReductionMode.Absolute, Amount = 5 },
            new() { DeviceId = "b", LastKnownName = "Headphones", Enabled = false, Mode = ReductionMode.Relative, Amount = 40 },
        ];

        var rows = DeviceTableModel.BuildRows([new AudioDeviceInfo("a", "Speakers", true)], configs, DefaultMode, DefaultAmount);
        var result = DeviceTableModel.ToConfigs(rows);

        Assert.Equal(
            configs.Select(c => (c.DeviceId, c.LastKnownName, c.Enabled, c.Mode, c.Amount)),
            result.Select(c => (c.DeviceId, c.LastKnownName, c.Enabled, c.Mode, c.Amount)));
    }

    [Theory]
    [InlineData(new bool[0], CheckState.Unchecked)]
    [InlineData(new[] { false, false }, CheckState.Unchecked)]
    [InlineData(new[] { true, false }, CheckState.Indeterminate)]
    [InlineData(new[] { true, true }, CheckState.Checked)]
    public void GetSelectAllState_ReflectsTickedRows(bool[] enabled, CheckState expected)
    {
        var rows = enabled.Select((e, i) => NewRow(id: $"d{i}", enabled: e, hasConfig: e)).ToList();

        Assert.Equal(expected, DeviceTableModel.GetSelectAllState(rows));
    }

    [Theory]
    [InlineData(new[] { false, false }, true)]
    [InlineData(new[] { true, false }, true)]
    [InlineData(new[] { true, true }, false)]
    public void ToggleAll_SelectsAllUnlessAllAreSelected(bool[] enabled, bool expectedEnabled)
    {
        var rows = enabled.Select((e, i) => NewRow(id: $"d{i}", enabled: e, hasConfig: e)).ToList();

        var result = DeviceTableModel.ToggleAll(rows, DefaultMode, DefaultAmount);

        Assert.Equal(expectedEnabled, result);
        Assert.All(rows, r => Assert.Equal(expectedEnabled, r.Enabled));
    }

    [Fact]
    public void ToggleAll_NewRowsGetDefaults_ConfiguredRowsKeepTheirValues()
    {
        var configured = NewRow(id: "a", hasConfig: true);
        configured.Mode = ReductionMode.Absolute;
        configured.Amount = 7;
        var fresh = NewRow(id: "b", connected: false);
        fresh.Mode = ReductionMode.Absolute;
        fresh.Amount = 99;

        DeviceTableModel.ToggleAll([configured, fresh], ReductionMode.Relative, 35);

        Assert.Equal((true, ReductionMode.Absolute, 7), (configured.Enabled, configured.Mode, configured.Amount));
        Assert.Equal((true, ReductionMode.Relative, 35, true), (fresh.Enabled, fresh.Mode, fresh.Amount, fresh.HasConfig));
    }

    [Fact]
    public void BuildRows_WithWindowsDefault_PinsDefaultRowFirst()
    {
        AudioDeviceInfo[] devices = [new("spk", "Speakers", true), new("hp", "Headphones", true)];
        var windowsDefault = new WindowsDefaultDeviceConfig { Enabled = true, Mode = ReductionMode.Absolute, Amount = 5 };

        var rows = DeviceTableModel.BuildRows(devices, [], DefaultMode, DefaultAmount, windowsDefault, "SPK");

        Assert.Equal([DeviceTableModel.WindowsDefaultRowName, "Headphones", "Speakers"], rows.Select(r => r.Name));
        var row = rows[0];
        Assert.True(row.IsWindowsDefault);
        Assert.True(row.IsConnected);
        Assert.Equal((true, ReductionMode.Absolute, 5), (row.Enabled, row.Mode, row.Amount));
        Assert.Equal("Currently: Speakers", DeviceTableModel.StatusText(row));
    }

    [Fact]
    public void BuildRows_WithoutWindowsDefaultConfig_HasNoDefaultRow()
    {
        var rows = DeviceTableModel.BuildRows([new AudioDeviceInfo("spk", "Speakers", true)], [], DefaultMode, DefaultAmount);

        Assert.DoesNotContain(rows, r => r.IsWindowsDefault);
    }

    [Fact]
    public void DefaultRow_NoDefaultDevice_IsGreyedWithStatus()
    {
        var rows = DeviceTableModel.BuildRows([], [], DefaultMode, DefaultAmount, new WindowsDefaultDeviceConfig(), currentDefaultId: null);

        var row = Assert.Single(rows);
        Assert.False(row.IsConnected);
        Assert.Equal("No default device", DeviceTableModel.StatusText(row));
    }

    [Fact]
    public void SetCurrentDefault_UpdatesNameAndStatus()
    {
        AudioDeviceInfo[] devices = [new("spk", "Speakers", true), new("hp", "Headphones", true)];
        var row = DeviceTableModel.BuildRows(devices, [], DefaultMode, DefaultAmount, new WindowsDefaultDeviceConfig(), "spk")[0];

        DeviceTableModel.SetCurrentDefault(row, "hp", devices);
        Assert.Equal("Currently: Headphones", DeviceTableModel.StatusText(row));

        DeviceTableModel.SetCurrentDefault(row, "unknown-id", devices);
        Assert.Equal($"Currently: {DeviceTableModel.UnknownDeviceName}", DeviceTableModel.StatusText(row));
    }

    [Fact]
    public void ToConfigs_LeavesOutDefaultRow_ToWindowsDefaultConfigReadsIt()
    {
        var windowsDefault = new WindowsDefaultDeviceConfig { Enabled = false, Mode = ReductionMode.Relative, Amount = 30 };
        var rows = DeviceTableModel.BuildRows([new AudioDeviceInfo("spk", "Speakers", true)], [], DefaultMode, DefaultAmount, windowsDefault, "spk");

        DeviceTableModel.ToggleAll(rows, ReductionMode.Absolute, 15);

        var configs = DeviceTableModel.ToConfigs(rows);
        Assert.Equal("spk", Assert.Single(configs).DeviceId);

        // Select all ticks the default row too, but it keeps its own mode and amount.
        var config = DeviceTableModel.ToWindowsDefaultConfig(rows);
        Assert.NotNull(config);
        Assert.Equal((true, ReductionMode.Relative, 30), (config.Enabled, config.Mode, config.Amount));
    }

    [Fact]
    public void ToWindowsDefaultConfig_WithoutDefaultRow_IsNull()
    {
        Assert.Null(DeviceTableModel.ToWindowsDefaultConfig([NewRow()]));
    }

    private static DeviceRow NewRow(
        string id = "dev",
        bool hasConfig = false,
        bool enabled = false,
        bool connected = true,
        ReductionMode mode = DefaultMode,
        int amount = DefaultAmount,
        string? name = null) =>
        new()
        {
            DeviceId = id,
            Name = name ?? $"Name of {id}",
            IsConnected = connected,
            Enabled = enabled,
            Mode = mode,
            Amount = amount,
            HasConfig = hasConfig,
        };
}
