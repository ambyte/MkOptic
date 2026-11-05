namespace MkOptic.Core.Threading;

/// <summary>
/// Runs the most recently scheduled action once no new action was scheduled for <c>delay</c>.
/// The action is posted to the <see cref="SynchronizationContext"/> captured when it was scheduled,
/// so UI callers receive the callback on the UI thread.
/// </summary>
public sealed class Debouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly ITimer? _timer;
    private readonly Lock _lock = new();
    private Action? _pending;
    private SynchronizationContext? _context;
    private bool _disposed;

    public Debouncer(TimeSpan delay, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _delay = delay;
        if (delay > TimeSpan.Zero)
        {
            _timer = timeProvider.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public void Schedule(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_timer is null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            action();
            return;
        }

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pending = action;
            _context = SynchronizationContext.Current;
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending = null;
        }

        _timer?.Dispose();
    }

    private void Flush()
    {
        Action? action;
        SynchronizationContext? context;
        lock (_lock)
        {
            action = _pending;
            context = _context;
            _pending = null;
        }

        if (action is null)
        {
            return;
        }

        if (context is null)
        {
            action();
        }
        else
        {
            context.Post(static state => ((Action)state!)(), action);
        }
    }
}
