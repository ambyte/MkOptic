using Microsoft.Extensions.Logging.Abstractions;
using MkOptic.Services;

namespace MkOptic.Tests.Services;

public sealed class SingleInstanceServiceTests
{
    [Theory]
    [InlineData(6)]
    [InlineData(45)]
    [InlineData(1024)]
    public void PipeNameFitsMacOsSocketPathLimit(int applicationIdLength)
    {
        var name = SingleInstanceService.CreatePipeName(new string('я', applicationIdLength), "domain\\user");
        const string macOsTempDirectory = "/var/folders/h5/t85nd63d71x27_8gl5gw3l_w0000gn/T/";
        var socketPath = $"{macOsTempDirectory}CoreFxPipe_{name}";

        Assert.Equal(40, name.Length);
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(socketPath), 1, 104);
    }

    [Fact]
    public void PipeNameIsStableAndIsolatedByApplicationAndUser()
    {
        var name = SingleInstanceService.CreatePipeName("app", "user");

        Assert.Equal(name, SingleInstanceService.CreatePipeName("app", "user"));
        Assert.NotEqual(name, SingleInstanceService.CreatePipeName("other-app", "user"));
        Assert.NotEqual(name, SingleInstanceService.CreatePipeName("app", "other-user"));
    }

    [Fact]
    public async Task LongApplicationIdSupportsActivation()
    {
        var id = $"{NewApplicationId()}{new string('я', 1024)}";
        using var primary = new SingleInstanceService(id);
        using var secondary = new SingleInstanceService(id);
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartListening(paths => received.SetResult(paths), NullLogger.Instance);

        await secondary.ActivatePrimaryAsync([], TestContext.Current.CancellationToken);

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        Assert.Empty(await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

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
