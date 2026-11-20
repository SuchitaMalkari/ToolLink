using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ToolLink.Core;

/// <summary>
/// Multi-threaded TCP server. Each connected tool is handled by its own async task,
/// and a background task checks for tools that stopped reporting.
/// </summary>
public sealed class TelemetryServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly ToolRegistry _registry;
    private readonly AlarmThresholds _thresholds;
    private readonly Func<DateTimeOffset> _clock;
    private readonly CancellationTokenSource _cts = new();
    // Running client handlers, keyed by connection id. Each one removes itself when it ends,
    // so a long-running server does not keep a reference to every connection it has ever had.
    private readonly ConcurrentDictionary<long, Task> _clientTasks = new();
    private long _nextClientId;

    private Task? _acceptTask;
    private Task? _heartbeatTask;
    private long _messagesProcessed;
    private long _messagesRejected;
    private int _activeConnections;

    public TelemetryServer(
        ToolRegistry registry,
        AlarmThresholds thresholds,
        int port = 5050,
        Func<DateTimeOffset>? clock = null)
    {
        _registry = registry;
        _thresholds = thresholds;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    /// <summary>Raised for every alarm. Handlers must be quick and thread-safe.</summary>
    public event Action<Alarm>? AlarmRaised;

    /// <summary>Raised for connection events and rejected messages.</summary>
    public event Action<string>? Log;

    /// <summary>The port actually in use (useful when started on port 0).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public long MessagesProcessed => Interlocked.Read(ref _messagesProcessed);
    public long MessagesRejected => Interlocked.Read(ref _messagesRejected);
    public int ActiveConnections => Volatile.Read(ref _activeConnections);

    public void Start()
    {
        _listener.Start();
        _acceptTask = AcceptLoopAsync(_cts.Token);
        _heartbeatTask = HeartbeatLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                break; // listener was stopped
            }
            catch (SocketException ex)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                Log?.Invoke($"accept failed: {ex.Message}");
                continue;
            }

            // Each client runs independently; one slow or broken tool cannot block the others.
            long clientId = Interlocked.Increment(ref _nextClientId);
            Task clientTask = HandleClientAsync(client, ct);
            _clientTasks[clientId] = clientTask;
            _ = clientTask.ContinueWith(
                completed => _clientTasks.TryRemove(clientId, out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        string endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        Interlocked.Increment(ref _activeConnections);
        Log?.Invoke($"tool connected from {endpoint}");

        try
        {
            using (client)
            using (var reader = new StreamReader(client.GetStream(), Encoding.UTF8))
            {
                while (!ct.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(ct);
                    if (line is null)
                    {
                        break; // the tool closed the connection
                    }

                    ProcessLine(line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // server is shutting down
        }
        catch (IOException ex)
        {
            // network drop or reset: log it and let the heartbeat check flag the tool
            Log?.Invoke($"connection from {endpoint} lost: {ex.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
            Log?.Invoke($"tool disconnected from {endpoint}");
        }
    }

    private void ProcessLine(string line)
    {
        if (!MessageParser.TryParse(line, _clock(), out var message, out string error))
        {
            Interlocked.Increment(ref _messagesRejected);
            Log?.Invoke($"rejected message: {error}");
            return;
        }

        _registry.Update(message);
        Interlocked.Increment(ref _messagesProcessed);

        foreach (var alarm in AlarmRules.Evaluate(message, _thresholds))
        {
            AlarmRaised?.Invoke(alarm);
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                foreach (var alarm in _registry.CheckHeartbeats(_clock(), _thresholds.HeartbeatTimeout))
                {
                    AlarmRaised?.Invoke(alarm);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // server is shutting down
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();

        var tasks = _clientTasks.Values.ToList();
        if (_acceptTask is not null) tasks.Add(_acceptTask);
        if (_heartbeatTask is not null) tasks.Add(_heartbeatTask);

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // every task already handles its own errors; ignore anything left during shutdown
        }

        _cts.Dispose();
    }
}
