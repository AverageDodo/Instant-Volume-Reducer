using InstantVolumeReducer.Core;

namespace InstantVolumeReducer.UI;

/// <summary>
/// The texts of the tray notifications shown on toggles and after crash recovery. Kept apart from the
/// tray code so the wording, including partial results, can be unit-tested.
/// </summary>
public static class ToggleMessages
{
    /// <summary>After a successful reduction of <paramref name="reduced"/> of <paramref name="selected"/> selected devices.</summary>
    public static string Reduced(int reduced, int selected)
    {
        var text = $"Volume reduced on {Devices(reduced)}.";
        var skipped = selected - reduced;
        return skipped > 0
            ? $"{text} {Devices(skipped)} skipped because {(skipped == 1 ? "it isn't" : "they aren't")} connected."
            : text;
    }

    /// <summary>When the hotkey was pressed but no device could be reduced.</summary>
    public static string NothingReduced(int selected) =>
        selected == 0
            ? "No output device is selected. Choose devices in Settings."
            : "None of the selected output devices is connected, so nothing was reduced.";

    /// <summary>After a restore; mentions devices whose volume couldn't be restored.</summary>
    public static string Restored(RestoreResult result)
    {
        if (result.Skipped == 0)
        {
            return "Volume restored.";
        }

        if (result.Restored == 0)
        {
            return $"The volume couldn't be restored because the {(result.Skipped == 1 ? "device isn't" : "devices aren't")} connected any more.";
        }

        return $"Volume restored on {Devices(result.Restored)}. {Devices(result.Skipped)} couldn't be restored because {(result.Skipped == 1 ? "it isn't" : "they aren't")} connected any more.";
    }

    /// <summary>
    /// The notification after startup crash recovery, or <c>null</c> if there is nothing to tell the user.
    /// </summary>
    public static string? Recovery(RecoveryResult result)
    {
        switch (result.Status)
        {
            case RecoveryStatus.Restored:
                var restore = result.Restore;
                var text = restore.Restored > 0
                    ? $"The app didn't close properly while the volume was reduced. The volume was restored on {Devices(restore.Restored)}."
                    : "The app didn't close properly while the volume was reduced.";
                if (restore.Skipped > 0)
                {
                    text += $" {Devices(restore.Skipped)} couldn't be restored because {(restore.Skipped == 1 ? "it isn't" : "they aren't")} connected.";
                }

                if (!result.StateCleared)
                {
                    text += " The saved volume levels couldn't be deleted.";
                }

                return text;

            case RecoveryStatus.Corrupt:
                return result.BackupPath is null
                    ? "The saved volume levels from last time couldn't be read, so the volume wasn't restored."
                    : $"The saved volume levels from last time couldn't be read, so the volume wasn't restored. The file was kept as {Path.GetFileName(result.BackupPath)}.";

            default:
                return null;
        }
    }

    private static string Devices(int count) => count == 1 ? "1 device" : $"{count} devices";
}
