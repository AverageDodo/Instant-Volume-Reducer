using InstantVolumeReducer.UI;

namespace InstantVolumeReducer.Tests.UI;

public class UpdateMessagesTests
{
    [Fact]
    public void Failed_WithoutError_IsShortSentence()
    {
        Assert.Equal("Couldn't check for updates.", UpdateMessages.Failed(null));
        Assert.Equal("Couldn't check for updates.", UpdateMessages.Failed("  "));
        Assert.Equal("Couldn't check for updates: Timeout", UpdateMessages.Failed(" Timeout "));
    }

    [Fact]
    public void AllMessages_FitInABalloon()
    {
        var longError = new string('x', 1000);

        Assert.True(UpdateMessages.Failed(longError).Length <= 255);
        Assert.EndsWith("…", UpdateMessages.Failed(longError));
        Assert.True(UpdateMessages.ApplyFailed(longError).Length <= 255);
        Assert.True(UpdateMessages.Ready("10.20.30-beta.40").Length <= 255);
        Assert.True(UpdateMessages.UpToDate("10.20.30-beta.40").Length <= 255);
        Assert.True(UpdateMessages.NotInstalled.Length <= 255);
    }
}
