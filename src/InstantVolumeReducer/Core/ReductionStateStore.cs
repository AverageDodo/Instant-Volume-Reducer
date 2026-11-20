using System.Text.Json;
using InstantVolumeReducer.Audio;
using InstantVolumeReducer.Settings;

namespace InstantVolumeReducer.Core;

/// <summary>How <see cref="ReductionStateStore.Load"/> found the state file.</summary>
public enum StateLoadStatus
{
    /// <summary>There is no state file: the app exited normally (or never reduced anything).</summary>
    Missing,

    /// <summary>The file was read successfully. The state may still be empty.</summary>
    Loaded,

    /// <summary>
    /// The file couldn't be read or isn't valid state JSON. An empty state is returned and, if possible,
    /// the bad file was moved to <see cref="StateLoadResult.BackupPath"/>.
    /// </summary>
    Corrupt,
}

/// <summary>The result of <see cref="ReductionStateStore.Load"/>.</summary>
/// <param name="State">The loaded (and normalized) state, or <see cref="ReductionState.Empty"/>.</param>
/// <param name="Status">Whether the file was loaded, missing or corrupt.</param>
/// <param name="BackupPath">Where a corrupt file was moved to; <c>null</c> if it couldn't be moved.</param>
public sealed record StateLoadResult(ReductionState State, StateLoadStatus Status, string? BackupPath = null);

/// <summary>
/// Keeps the current <see cref="ReductionState"/> on disk while the volume is reduced, by default at
/// <c>%APPDATA%\InstantVolumeReducer\state.json</c>, so the original volumes can be restored after a crash.
/// The file only exists while something is reduced.
/// </summary>
public sealed class ReductionStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <param name="filePath">The state file. <c>null</c> uses <see cref="DefaultFilePath"/>; tests pass a temp path.</param>
    public ReductionStateStore(string? filePath = null)
    {
        FilePath = filePath ?? DefaultFilePath;
    }

    /// <summary><c>%APPDATA%\InstantVolumeReducer\state.json</c>, next to the settings file.</summary>
    public static string DefaultFilePath { get; } = Path.Combine(
        Path.GetDirectoryName(SettingsStore.DefaultFilePath)!,
        "state.json");

    /// <summary>The state file this store reads and writes.</summary>
    public string FilePath { get; }

    /// <summary>Where a corrupt state file is moved to, so it isn't reported again on the next start.</summary>
    public string BackupFilePath => FilePath + ".bak";

    /// <summary>
    /// Reads the state file. Never throws for a missing, unreadable or invalid file: it returns an empty
    /// state and says why in <see cref="StateLoadResult.Status"/>. Loaded values are normalized
    /// (see <see cref="Normalize"/>).
    /// </summary>
    public StateLoadResult Load()
    {
        if (!File.Exists(FilePath))
        {
            return new StateLoadResult(ReductionState.Empty, StateLoadStatus.Missing);
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var state = JsonSerializer.Deserialize<ReductionState>(json, JsonOptions)
                ?? throw new JsonException("The state file contains null.");
            return new StateLoadResult(Normalize(state), StateLoadStatus.Loaded);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new StateLoadResult(ReductionState.Empty, StateLoadStatus.Corrupt, TryMoveBadFileToBackup());
        }
    }

    /// <summary>
    /// Writes a reduced state atomically, or deletes the file when <paramref name="state"/> isn't reduced.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be written or deleted.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or file isn't writable.</exception>
    public void Save(ReductionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.IsReduced)
        {
            AtomicFile.WriteJson(FilePath, state, JsonOptions);
        }
        else if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }

    /// <summary>Deletes the state file; returns <c>false</c> only if it exists and couldn't be deleted.</summary>
    public bool TryClear() => AtomicFile.TryDelete(FilePath);

    /// <summary>
    /// Makes a loaded state safe to use, since the file may have been edited by hand: entries without a
    /// device ID, or repeating an earlier entry's ID, are dropped, and volumes are clamped to 0.0–1.0.
    /// </summary>
    public static ReductionState Normalize(ReductionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var devices = new List<ReducedDevice>();
        foreach (var device in state.Devices ?? [])
        {
            if (device is null || string.IsNullOrWhiteSpace(device.DeviceId) || !seen.Add(device.DeviceId))
            {
                continue;
            }

            devices.Add(device with
            {
                OriginalVolume = VolumeLevel.Clamp(device.OriginalVolume),
                ReducedVolume = VolumeLevel.Clamp(device.ReducedVolume),
            });
        }

        return devices.Count > 0 ? new ReductionState(devices) : ReductionState.Empty;
    }

    /// <summary>Moves the bad file to <see cref="BackupFilePath"/>; returns that path, or <c>null</c> if the move failed.</summary>
    private string? TryMoveBadFileToBackup()
    {
        try
        {
            File.Move(FilePath, BackupFilePath, overwrite: true);
            return BackupFilePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Can't keep a copy; at least don't report the same bad file on every start.
            AtomicFile.TryDelete(FilePath);
            return null;
        }
    }
}
