using Velopack;
using Velopack.Sources;

namespace InstantVolumeReducer.Updates;

/// <summary>
/// <see cref="IUpdateClient"/> backed by Velopack. Updates come from the repository's GitHub Releases,
/// where the release workflow uploads the Velopack packages and the <c>releases.win.json</c> feed.
/// </summary>
internal sealed class VelopackUpdateClient : IUpdateClient
{
    /// <summary>
    /// For testing updates without GitHub: a local folder (or HTTP URL) with packages made by <c>vpk pack</c>.
    /// </summary>
    public const string SourceOverrideVariable = "INSTANTVOLUMEREDUCER_UPDATE_SOURCE";

    private readonly UpdateManager _manager;

    private VelopackUpdateClient(UpdateManager manager)
    {
        _manager = manager;
    }

    /// <summary>
    /// Creates the client. Pre-releases are only offered to installations that run a pre-release
    /// themselves, so stable users stay on stable versions.
    /// </summary>
    public static VelopackUpdateClient Create()
    {
        var sourceOverride = Environment.GetEnvironmentVariable(SourceOverrideVariable);
        var manager = string.IsNullOrWhiteSpace(sourceOverride)
            ? new UpdateManager(new GithubSource(AppInfo.RepositoryUrl, accessToken: null, prerelease: AppInfo.IsPrerelease))
            : new UpdateManager(sourceOverride);
        return new VelopackUpdateClient(manager);
    }

    public bool IsInstalled => _manager.IsInstalled;

    public string? PendingRestartVersion => _manager.UpdatePendingRestart?.Version?.ToString();

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(true);
        return info is null ? null : new AvailableUpdate(info.TargetFullRelease.Version.ToString(), info);
    }

    public Task DownloadAsync(AvailableUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.Handle is not UpdateInfo info)
        {
            throw new ArgumentException("The update wasn't found by this client.", nameof(update));
        }

        return _manager.DownloadUpdatesAsync(info, progress: null, cancellationToken);
    }

    public void ApplyAfterExit(AvailableUpdate? update)
    {
        var asset = (update?.Handle as UpdateInfo)?.TargetFullRelease ?? _manager.UpdatePendingRestart;
        _manager.WaitExitThenApplyUpdates(asset, silent: false, restart: true);
    }
}
