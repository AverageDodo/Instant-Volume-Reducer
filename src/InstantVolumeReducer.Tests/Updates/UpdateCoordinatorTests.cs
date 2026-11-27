using InstantVolumeReducer.Updates;

namespace InstantVolumeReducer.Tests.Updates;

public class UpdateCoordinatorTests
{
    [Fact]
    public async Task NotInstalled_DoesNotCheck()
    {
        var client = new FakeUpdateClient { IsInstalled = false, Available = new AvailableUpdate("9.9.9") };
        var coordinator = new UpdateCoordinator(client);

        var result = await coordinator.CheckAndDownloadAsync();

        Assert.Equal(UpdateCheckOutcome.NotInstalled, result.Outcome);
        Assert.Equal(0, client.Checks);
        Assert.False(coordinator.ApplyAfterExit());
    }

    [Fact]
    public async Task NoUpdate_IsUpToDate()
    {
        var coordinator = new UpdateCoordinator(new FakeUpdateClient());

        var result = await coordinator.CheckAndDownloadAsync();

        Assert.Equal(UpdateCheckOutcome.UpToDate, result.Outcome);
        Assert.Null(coordinator.ReadyVersion);
    }

    [Fact]
    public async Task UpdateAvailable_IsDownloadedAndReady()
    {
        var client = new FakeUpdateClient { Available = new AvailableUpdate("0.3.0") };
        var coordinator = new UpdateCoordinator(client);
        var changes = 0;
        coordinator.StateChanged += (_, _) => changes++;

        var result = await coordinator.CheckAndDownloadAsync();

        Assert.Equal(new UpdateCheckResult(UpdateCheckOutcome.Ready, "0.3.0", IsNew: true), result);
        Assert.Equal("0.3.0", coordinator.ReadyVersion);
        Assert.Equal(["0.3.0"], client.Downloads);
        Assert.False(coordinator.IsBusy);
        Assert.Equal(2, changes); // Busy, then not busy.
    }

    [Fact]
    public async Task SameUpdateFoundAgain_IsNotDownloadedTwice()
    {
        var client = new FakeUpdateClient { Available = new AvailableUpdate("0.3.0") };
        var coordinator = new UpdateCoordinator(client);
        await coordinator.CheckAndDownloadAsync();

        var result = await coordinator.CheckAndDownloadAsync();

        Assert.Equal(new UpdateCheckResult(UpdateCheckOutcome.Ready, "0.3.0"), result);
        Assert.Single(client.Downloads);
    }

    [Fact]
    public async Task NewerUpdateAfterDownloadedOne_IsDownloaded()
    {
        var client = new FakeUpdateClient { Available = new AvailableUpdate("0.3.0") };
        var coordinator = new UpdateCoordinator(client);
        await coordinator.CheckAndDownloadAsync();

        client.Available = new AvailableUpdate("0.4.0");
        var result = await coordinator.CheckAndDownloadAsync();

        Assert.Equal(new UpdateCheckResult(UpdateCheckOutcome.Ready, "0.4.0", IsNew: true), result);
        Assert.Equal(["0.3.0", "0.4.0"], client.Downloads);
    }

    [Fact]
    public async Task CheckFails_ReportsErrorAndStaysUsable()
    {
        var client = new FakeUpdateClient { CheckError = new HttpRequestException("No network") };
        var coordinator = new UpdateCoordinator(client);

        var result = await coordinator.CheckAndDownloadAsync();

        Assert.Equal(new UpdateCheckResult(UpdateCheckOutcome.Failed, Error: "No network"), result);
        Assert.False(coordinator.IsBusy);

        client.CheckError = null;
        Assert.Equal(UpdateCheckOutcome.UpToDate, (await coordinator.CheckAndDownloadAsync()).Outcome);
    }

    [Fact]
    public async Task DownloadFails_IsNotReady()
    {
        var client = new FakeUpdateClient { Available = new AvailableUpdate("0.3.0"), DownloadError = new IOException("Disk full") };
        var coordinator = new UpdateCoordinator(client);

        var result = await coordinator.CheckAndDownloadAsync();

        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
        Assert.Null(coordinator.ReadyVersion);
        Assert.False(coordinator.ApplyAfterExit());
    }

    [Fact]
    public async Task SecondCheckWhileBusy_ReportsBusy()
    {
        var gate = new TaskCompletionSource();
        var client = new FakeUpdateClient { Available = new AvailableUpdate("0.3.0"), CheckGate = gate.Task };
        var coordinator = new UpdateCoordinator(client);

        var first = coordinator.CheckAndDownloadAsync();
        Assert.True(coordinator.IsBusy);
        var second = await coordinator.CheckAndDownloadAsync();
        gate.SetResult();
        await first;

        Assert.Equal(UpdateCheckOutcome.Busy, second.Outcome);
        Assert.Equal(1, client.Checks);
    }

    [Fact]
    public async Task Cancellation_IsPassedOn()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var coordinator = new UpdateCoordinator(new FakeUpdateClient());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.CheckAndDownloadAsync(cancellation.Token));
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public void UpdateWaitingFromEarlier_IsReadyAtStart_AndCanBeApplied()
    {
        var client = new FakeUpdateClient { PendingRestartVersion = "0.3.0" };
        var coordinator = new UpdateCoordinator(client);

        Assert.Equal("0.3.0", coordinator.ReadyVersion);
        Assert.True(coordinator.ApplyAfterExit());
        Assert.Equal([null], client.Applied);
    }

    [Fact]
    public async Task ApplyAfterExit_PassesTheDownloadedUpdate()
    {
        var update = new AvailableUpdate("0.3.0", Handle: "handle");
        var client = new FakeUpdateClient { Available = update };
        var coordinator = new UpdateCoordinator(client);
        await coordinator.CheckAndDownloadAsync();

        Assert.True(coordinator.ApplyAfterExit());
        Assert.Equal([update], client.Applied);
    }

    [Fact]
    public async Task UnavailableClient_BehavesLikeNotInstalled()
    {
        var coordinator = new UpdateCoordinator(new UnavailableUpdateClient());

        Assert.False(coordinator.IsInstalled);
        Assert.Equal(UpdateCheckOutcome.NotInstalled, (await coordinator.CheckAndDownloadAsync()).Outcome);
        Assert.False(coordinator.ApplyAfterExit());
    }

    private sealed class FakeUpdateClient : IUpdateClient
    {
        public bool IsInstalled { get; set; } = true;

        public string? PendingRestartVersion { get; set; }

        public AvailableUpdate? Available { get; set; }

        public Exception? CheckError { get; set; }

        public Exception? DownloadError { get; set; }

        public Task? CheckGate { get; set; }

        public int Checks { get; private set; }

        public List<string> Downloads { get; } = [];

        public List<AvailableUpdate?> Applied { get; } = [];

        public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken)
        {
            Checks++;
            cancellationToken.ThrowIfCancellationRequested();
            if (CheckGate is not null)
            {
                await CheckGate;
            }

            if (CheckError is not null)
            {
                throw CheckError;
            }

            return Available;
        }

        public Task DownloadAsync(AvailableUpdate update, CancellationToken cancellationToken)
        {
            if (DownloadError is not null)
            {
                throw DownloadError;
            }

            Downloads.Add(update.Version);
            return Task.CompletedTask;
        }

        public void ApplyAfterExit(AvailableUpdate? update) => Applied.Add(update);
    }
}
