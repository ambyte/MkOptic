using Microsoft.Extensions.Logging.Abstractions;
using MkOptic.Services;

namespace MkOptic.Tests.Services;

public sealed class SingleInstanceServiceTests
{
    [Fact]
    public void OnlyFirstInstanceIsPrimary()
    {
        var id = NewApplicationId();
        using var primary = new SingleInstanceService(id);
        using var secondary = new SingleInstanceService(id);

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
    }

    [Fact]
    public void InstanceCanRestartAfterDisposal()
    {
        var id = NewApplicationId();
        using (var primary = new SingleInstanceService(id))
        {
            Assert.True(primary.IsPrimary);
        }

        using var restarted = new SingleInstanceService(id);
        Assert.True(restarted.IsPrimary);
    }

    [Fact]
    public async Task LaunchWithoutFilesRequestsActivation()
    {
        var id = NewApplicationId();
        using var primary = new SingleInstanceService(id);
        using var secondary = new SingleInstanceService(id);
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartListening(paths => received.SetResult(paths), NullLogger.Instance);

        await secondary.ActivatePrimaryAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LaunchForwardsAbsolutePathsIncludingUnicodeAndSpaces()
    {
        var id = NewApplicationId();
        using var primary = new SingleInstanceService(id);
        using var secondary = new SingleInstanceService(id);
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartListening(paths => received.SetResult(paths), NullLogger.Instance);
        string[] files = ["notes.md", Path.Combine("папка с пробелами", "заметки.md")];

        await secondary.ActivatePrimaryAsync(files, TestContext.Current.CancellationToken);

        Assert.Equal(files.Select(Path.GetFullPath),
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LaunchDuringStartupWaitsForListener()
    {
        var id = NewApplicationId();
        using var primary = new SingleInstanceService(id);
        using var secondary = new SingleInstanceService(id);
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        var activation = secondary.ActivatePrimaryAsync([], TestContext.Current.CancellationToken);
        Assert.False(activation.IsCompleted);
        primary.StartListening(paths => received.SetResult(paths), NullLogger.Instance);
        await activation;

        Assert.Empty(await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListenerAcceptsMultipleLaunches()
    {
        var id = NewApplicationId();
        using var primary = new SingleInstanceService(id);
        var requests = 0;
        primary.StartListening(_ => Interlocked.Increment(ref requests), NullLogger.Instance);

        for (var index = 0; index < 3; index++)
        {
            using var secondary = new SingleInstanceService(id);
            await secondary.ActivatePrimaryAsync([], TestContext.Current.CancellationToken);
        }

        Assert.Equal(3, Volatile.Read(ref requests));
    }

    [Fact]
    public async Task ActivationCanBeCancelledWhilePrimaryIsStarting()
    {
        var id = NewApplicationId();
        using var primary = new SingleInstanceService(id);
        using var secondary = new SingleInstanceService(id);
        using var cancellation = new CancellationTokenSource();

        var activation = secondary.ActivatePrimaryAsync([], cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => activation);
    }

    private static string NewApplicationId() => $"MkOptic.Tests.{Guid.NewGuid():N}";
}
