namespace InstantVolumeReducer.Tests;

public class AppInfoTests
{
    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.1.0+3f2a9c1e", "0.1.0")]
    [InlineData("1.2.3-beta.1+abc", "1.2.3-beta.1")]
    [InlineData(" 2.0.0 ", "2.0.0")]
    public void StripBuildMetadata_RemovesEverythingAfterPlus(string input, string expected)
    {
        Assert.Equal(expected, AppInfo.StripBuildMetadata(input));
    }

    [Fact]
    public void Version_IsTheProjectVersionWithoutBuildMetadata()
    {
        Assert.DoesNotContain('+', AppInfo.Version);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", AppInfo.Version);
    }
}
