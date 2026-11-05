using Microsoft.Extensions.Time.Testing;
using MkOptic.Core.Threading;

namespace MkOptic.Tests.Core;

public sealed class DebouncerTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(200);

    [Fact]
    public void Schedule_RunsOnlyLastActionAfterQuietPeriod()
    {
        var time = new FakeTimeProvider();
        using var debouncer = new Debouncer(Delay, time);
        var calls = new List<int>();

        debouncer.Schedule(() => calls.Add(1));
        time.Advance(TimeSpan.FromMilliseconds(150));
        debouncer.Schedule(() => calls.Add(2));
        time.Advance(TimeSpan.FromMilliseconds(150));

        Assert.Empty(calls);

        time.Advance(TimeSpan.FromMilliseconds(50));

        Assert.Equal([2], calls);
    }

    [Fact]
    public void Schedule_WithZeroDelay_RunsImmediately()
    {
        using var debouncer = new Debouncer(TimeSpan.Zero, new FakeTimeProvider());
        var called = false;

        debouncer.Schedule(() => called = true);

        Assert.True(called);
    }

    [Fact]
    public void Dispose_CancelsPendingAction()
    {
        var time = new FakeTimeProvider();
        var debouncer = new Debouncer(Delay, time);
        var called = false;

        debouncer.Schedule(() => called = true);
        debouncer.Dispose();
        time.Advance(Delay);

        Assert.False(called);
        Assert.Throws<ObjectDisposedException>(() => debouncer.Schedule(() => { }));
    }
}
