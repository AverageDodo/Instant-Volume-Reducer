using InstantVolumeReducer.Audio;

namespace InstantVolumeReducer.Core;

/// <summary>What <see cref="CrashRecovery.Run"/> found and did.</summary>
public enum RecoveryStatus
{
    /// <summary>The app exited normally last time; nothing was restored.</summary>
    NothingToRecover,

    /// <summary>The volume was still reduced from last time and has been restored (possibly only partly).</summary>
    Restored,

    /// <summary>A state file existed but couldn't be read, so nothing could be restored.</summary>
    Corrupt,
}

/// <summary>The result of <see cref="CrashRecovery.Run"/>.</summary>
/// <param name="Status">What was found.</param>
/// <param name="Restore">How many devices were restored and skipped; both 0 unless <see cref="RecoveryStatus.Restored"/>.</param>
/// <param name="BackupPath">For <see cref="RecoveryStatus.Corrupt"/>: where the bad file was moved to, if it could be.</param>
/// <param name="StateCleared">
/// <c>false</c> if the state file couldn't be deleted after restoring, so the next start would restore the
/// same volumes again.
/// </param>
public sealed record RecoveryResult(
    RecoveryStatus Status,
    RestoreResult Restore = default,
    string? BackupPath = null,
    bool StateCleared = true)
{
    public static RecoveryResult Nothing { get; } = new(RecoveryStatus.NothingToRecover);
}

/// <summary>
/// Runs once at startup: if the app didn't exit normally while the volume was reduced (crash, killed
/// process, power loss), the saved state file still exists. The original volumes are then restored
/// automatically and the file is deleted.
/// </summary>
public static class CrashRecovery
{
    /// <summary>
    /// Restores the volumes saved in <paramref name="store"/>, if any, and clears the file. Never throws for
    /// file problems. Devices from the saved state that aren't connected now are skipped and forgotten,
    /// the same as a normal restore.
    /// </summary>
    public static RecoveryResult Run(ReductionStateStore store, IAudioDeviceService audio)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(audio);

        var loaded = store.Load();
        switch (loaded.Status)
        {
            case StateLoadStatus.Missing:
                return RecoveryResult.Nothing;

            case StateLoadStatus.Corrupt:
                return new RecoveryResult(RecoveryStatus.Corrupt, BackupPath: loaded.BackupPath);
        }

        if (!loaded.State.IsReduced)
        {
            // A leftover file with nothing in it: just tidy up.
            store.TryClear();
            return RecoveryResult.Nothing;
        }

        // A toggle service with no device rules restores exactly the saved devices, nothing else.
        var toggle = new VolumeToggleService(audio, [], loaded.State);
        var restore = toggle.Restore();
        var cleared = store.TryClear();
        return new RecoveryResult(RecoveryStatus.Restored, restore, StateCleared: cleared);
    }
}
