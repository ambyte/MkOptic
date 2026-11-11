using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MkOptic.Services;

/// <summary>Coordinates application launches for the current user without starting another UI.</summary>
internal sealed partial class SingleInstanceService : IDisposable
{
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(10);
    private readonly Mutex _instance;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly NamedPipeServerStream? _server;
    private Task? _listener;
    private bool _disposed;

    public SingleInstanceService(string applicationId = "MkOptic")
    {
        _pipeName = CreatePipeName(applicationId, $"{Environment.UserDomainName}\\{Environment.UserName}");
        // Keep the named object alive rather than owning the mutex: disposal is then thread-independent.
        _instance = new Mutex(false, _pipeName, out var createdNew);
        IsPrimary = createdNew;
        if (IsPrimary)
        {
            try
            {
                _server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch
            {
                _instance.Dispose();
                _shutdown.Dispose();
                throw;
            }
        }
    }

    public bool IsPrimary { get; }

    internal static string CreatePipeName(string applicationId, string userIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentity);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{applicationId}\0{userIdentity}"));
        // macOS limits the entire Unix socket path to 104 bytes, including the temp directory
        // and CoreFxPipe_ prefix. A 128-bit identifier keeps the name short without exposing user data.
        return $"MkOptic-{Convert.ToHexString(hash.AsSpan(0, 16))}";
    }

    public void StartListening(Action<string[]> activate, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(activate);
        ArgumentNullException.ThrowIfNull(logger);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_server is null || _listener is not null)
        {
            throw new InvalidOperationException("Only the primary instance can start one activation listener.");
        }

        _listener = ListenAsync(activate, logger);
    }

    public async Task ActivatePrimaryAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ActivationTimeout);
        await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(client, leaveOpen: true);
        using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
        var processId = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        if (OperatingSystem.IsWindows() && int.TryParse(processId, CultureInfo.InvariantCulture, out var id))
        {
            // Windows otherwise prevents a background process from taking foreground focus.
            AllowSetForegroundWindow(id);
        }

        var absolutePaths = paths.Select(Path.GetFullPath).ToArray();
        await writer.WriteLineAsync(JsonSerializer.Serialize(absolutePaths).AsMemory(), timeout.Token)
            .ConfigureAwait(false);
        if (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) != "OK")
        {
            throw new IOException("The running instance did not acknowledge activation.");
        }
    }

    private async Task ListenAsync(Action<string[]> activate, ILogger logger)
    {
        var server = _server!;
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await server.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                requestTimeout.CancelAfter(ActivationTimeout);
                using var reader = new StreamReader(server, leaveOpen: true);
                using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
                    .AsMemory(), requestTimeout.Token).ConfigureAwait(false);
                var message = await reader.ReadLineAsync(requestTimeout.Token).ConfigureAwait(false);
                var paths = message is null ? null : JsonSerializer.Deserialize<string[]>(message);
                if (paths is null || paths.Any(string.IsNullOrWhiteSpace))
                {
                    throw new JsonException("Invalid activation request.");
                }

                activate(paths);
                await writer.WriteLineAsync("OK".AsMemory(), requestTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException or JsonException or OperationCanceledException)
            {
                LogActivationFailure(logger, exception);
            }
            finally
            {
                if (server.IsConnected)
                {
                    server.Disconnect();
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        // The listener never waits on the UI thread; join it before releasing the instance marker.
        _listener?.GetAwaiter().GetResult();
        _server?.Dispose();
        _shutdown.Dispose();
        _instance.Dispose();
    }

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unable to process an application activation request")]
    private static partial void LogActivationFailure(ILogger logger, Exception exception);
}
