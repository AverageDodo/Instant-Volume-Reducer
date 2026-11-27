namespace InstantVolumeReducer.Updates;

/// <summary>What a check for updates ended with.</summary>
public enum UpdateCheckOutcome
{
    /// <summary>The app isn't installed (portable or development copy), so it can't update itself.</summary>
    NotInstalled,

    /// <summary>Another check or download is already running.</summary>
    Busy,

    /// <summary>No newer version exists.</summary>
    UpToDate,

    /// <summary>A newer version is downloaded and is installed on restart.</summary>
    Ready,

    /// <summary>The check or the download failed (no network, no releases, rate limit, …).</summary>
    Failed,
}

/// <param name="Outcome">What happened.</param>
/// <param name="Version">For <see cref="UpdateCheckOutcome.Ready"/>: the downloaded version.</param>
/// <param name="Error">For <see cref="UpdateCheckOutcome.Failed"/>: why.</param>
/// <param name="IsNew">For <see cref="UpdateCheckOutcome.Ready"/>: <c>true</c> if this check downloaded it (rather than an earlier one).</param>
public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, string? Version = null, string? Error = null, bool IsNew = false);

/// <summary>
/// The update flow of the tray app: check, download in the background, then install when the user restarts
/// (or automatically on the next start). Call it from the UI thread.
/// </summary>
public sealed class UpdateCoordinator
{
    private readonly IUpdateClient _client;
    private AvailableUpdate? _downloaded;

    public UpdateCoordinator(IUpdateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (_client.IsInstalled && _client.PendingRestartVersion is { } pending)
        {
            ReadyVersion = pending;
        }
    }

    /// <summary>Raised when <see cref="IsBusy"/> or <see cref="ReadyVersion"/> changes.</summary>
    public event EventHandler? StateChanged;

    /// <inheritdoc cref="IUpdateClient.IsInstalled"/>
    public bool IsInstalled => _client.IsInstalled;

    /// <summary><c>true</c> while checking or downloading.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>The downloaded version that's installed on restart, or <c>null</c>.</summary>
    public string? ReadyVersion { get; private set; }

    /// <summary>
    /// Checks for a newer version and downloads it. Never throws for update problems; they're reported
    /// as <see cref="UpdateCheckOutcome.Failed"/>.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        if (!_client.IsInstalled)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.NotInstalled);
        }

        if (IsBusy)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.Busy);
        }

        SetBusy(true);
        try
        {
            var update = await _client.CheckAsync(cancellationToken).ConfigureAwait(true);
            if (update is null)
            {
                return ReadyVersion is { } ready
                    ? new UpdateCheckResult(UpdateCheckOutcome.Ready, ready)
                    : new UpdateCheckResult(UpdateCheckOutcome.UpToDate);
            }

            if (_downloaded is not null && update.Version == ReadyVersion)
            {
                return new UpdateCheckResult(UpdateCheckOutcome.Ready, ReadyVersion);
            }

            await _client.DownloadAsync(update, cancellationToken).ConfigureAwait(true);
            _downloaded = update;
            ReadyVersion = update.Version;
            return new UpdateCheckResult(UpdateCheckOutcome.Ready, update.Version, IsNew: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.Failed, Error: ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Starts the updater for the downloaded version. Returns <c>false</c> if there's nothing to install;
    /// otherwise the caller must exit the app right away, so the updater can replace it and start it again.
    /// </summary>
    public bool ApplyAfterExit()
    {
        if (!_client.IsInstalled || ReadyVersion is null)
        {
            return false;
        }

        _client.ApplyAfterExit(_downloaded);
        return true;
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
