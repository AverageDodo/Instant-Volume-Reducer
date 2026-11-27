namespace InstantVolumeReducer.Updates;

/// <summary>An update that was found; <see cref="Handle"/> is whatever the client needs to download and apply it.</summary>
/// <param name="Version">The new version, e.g. <c>0.3.0</c>.</param>
/// <param name="Handle">Client-specific data (Velopack's <c>UpdateInfo</c>).</param>
public sealed record AvailableUpdate(string Version, object? Handle = null);

/// <summary>
/// Checks for, downloads and applies updates. The Velopack implementation talks to GitHub Releases;
/// tests use a fake.
/// </summary>
public interface IUpdateClient
{
    /// <summary>
    /// <c>true</c> if the app runs from a Velopack installation. A portable or development copy can't
    /// update itself.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>The version of an update that was downloaded earlier and waits for a restart, if any.</summary>
    string? PendingRestartVersion { get; }

    /// <summary>Returns the newest update, or <c>null</c> if the app is up to date.</summary>
    Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken);

    Task DownloadAsync(AvailableUpdate update, CancellationToken cancellationToken);

    /// <summary>
    /// Starts the updater, which waits for this process to exit, installs the update and starts the app
    /// again. The caller must then exit the app normally. <paramref name="update"/> <c>null</c> means the
    /// update that waits for a restart.
    /// </summary>
    void ApplyAfterExit(AvailableUpdate? update);
}

/// <summary>Used when the update system can't be set up: behaves like a copy that isn't installed.</summary>
public sealed class UnavailableUpdateClient : IUpdateClient
{
    public bool IsInstalled => false;

    public string? PendingRestartVersion => null;

    public Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken) => Task.FromResult<AvailableUpdate?>(null);

    public Task DownloadAsync(AvailableUpdate update, CancellationToken cancellationToken) => Task.CompletedTask;

    public void ApplyAfterExit(AvailableUpdate? update) => throw new InvalidOperationException("Updates aren't available.");
}
