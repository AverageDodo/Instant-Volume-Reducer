using InstantVolumeReducer.Core;
using InstantVolumeReducer.UI;

namespace InstantVolumeReducer.Tests.UI;

public class ToggleMessagesTests
{
    [Theory]
    [InlineData(1, 1, "Volume reduced on 1 device.")]
    [InlineData(2, 2, "Volume reduced on 2 devices.")]
    [InlineData(1, 2, "Volume reduced on 1 device. 1 device skipped because it isn't connected.")]
    [InlineData(1, 3, "Volume reduced on 1 device. 2 devices skipped because they aren't connected.")]
    public void Reduced(int reduced, int selected, string expected)
    {
        Assert.Equal(expected, ToggleMessages.Reduced(reduced, selected));
    }

    [Fact]
    public void NothingReduced_DistinguishesNoSelectionFromNotConnected()
    {
        Assert.Contains("No output device is selected", ToggleMessages.NothingReduced(0));
        Assert.Contains("None of the selected output devices is connected", ToggleMessages.NothingReduced(2));
    }

    [Theory]
    [InlineData(2, 0, "Volume restored.")]
    [InlineData(1, 1, "Volume restored on 1 device. 1 device couldn't be restored because it isn't connected any more.")]
    [InlineData(1, 2, "Volume restored on 1 device. 2 devices couldn't be restored because they aren't connected any more.")]
    [InlineData(0, 1, "The volume couldn't be restored because the device isn't connected any more.")]
    [InlineData(0, 2, "The volume couldn't be restored because the devices aren't connected any more.")]
    public void Restored(int restored, int skipped, string expected)
    {
        Assert.Equal(expected, ToggleMessages.Restored(new RestoreResult(restored, skipped)));
    }

    [Fact]
    public void Recovery_NothingToRecover_IsNull()
    {
        Assert.Null(ToggleMessages.Recovery(RecoveryResult.Nothing));
    }

    [Fact]
    public void Recovery_Restored_MentionsCount()
    {
        var text = ToggleMessages.Recovery(new RecoveryResult(RecoveryStatus.Restored, new RestoreResult(2, 0)));

        Assert.Equal("The app didn't close properly while the volume was reduced. The volume was restored on 2 devices.", text);
    }

    [Fact]
    public void Recovery_PartlyRestored_MentionsSkippedDevices()
    {
        var text = ToggleMessages.Recovery(new RecoveryResult(RecoveryStatus.Restored, new RestoreResult(1, 1)));

        Assert.EndsWith("restored on 1 device. 1 device couldn't be restored because it isn't connected.", text);
    }

    [Fact]
    public void Recovery_NothingRestored_DoesNotClaimARestore()
    {
        var text = ToggleMessages.Recovery(new RecoveryResult(RecoveryStatus.Restored, new RestoreResult(0, 2)));

        Assert.NotNull(text);
        Assert.DoesNotContain("was restored", text);
        Assert.Contains("2 devices couldn't be restored", text);
    }

    [Fact]
    public void Recovery_StateNotCleared_IsMentioned()
    {
        var text = ToggleMessages.Recovery(new RecoveryResult(RecoveryStatus.Restored, new RestoreResult(1, 0), StateCleared: false));

        Assert.EndsWith("The saved volume levels couldn't be deleted.", text);
    }

    [Fact]
    public void Recovery_Corrupt_MentionsBackupFileName()
    {
        var text = ToggleMessages.Recovery(new RecoveryResult(RecoveryStatus.Corrupt, BackupPath: @"C:\x\state.json.bak"));

        Assert.NotNull(text);
        Assert.Contains("couldn't be read", text);
        Assert.EndsWith("The file was kept as state.json.bak.", text);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(0, 10)]
    public void AllMessages_FitInABalloon(int restored, int skipped)
    {
        // NotifyIcon balloon text is limited to 255 characters.
        Assert.True(ToggleMessages.Restored(new RestoreResult(restored, skipped)).Length <= 255);
        Assert.True(ToggleMessages.Reduced(restored, restored + skipped).Length <= 255);
        var recovery = ToggleMessages.Recovery(new RecoveryResult(RecoveryStatus.Restored, new RestoreResult(restored, skipped), StateCleared: false));
        Assert.True(recovery!.Length <= 255);
    }
}
