namespace InstantVolumeReducer.UI;

/// <summary>The tray notification texts about updates. NotifyIcon balloons hold at most 255 characters.</summary>
public static class UpdateMessages
{
    private const int MaxLength = 255;

    public static string Ready(string version) =>
        $"Version {version} is ready to install. Click here to restart the app and install it now, or it's installed the next time the app starts.";

    public static string UpToDate(string currentVersion) =>
        $"You have the latest version ({currentVersion}).";

    public const string Busy = "Already checking for updates.";

    public const string NotInstalled =
        "This copy can't update itself because it isn't installed. Use the installer from the Releases page to get automatic updates.";

    public static string Failed(string? error)
    {
        const string Prefix = "Couldn't check for updates";
        var text = string.IsNullOrWhiteSpace(error) ? $"{Prefix}." : $"{Prefix}: {error.Trim()}";
        return text.Length <= MaxLength ? text : string.Concat(text.AsSpan(0, MaxLength - 1), "…");
    }

    public static string ApplyFailed(string error)
    {
        var text = $"The update couldn't be started: {error}";
        return text.Length <= MaxLength ? text : string.Concat(text.AsSpan(0, MaxLength - 1), "…");
    }
}
