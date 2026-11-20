using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using ToolLink.Core;

namespace ToolLink.Tests;

/// <summary>End-to-end tests over a real TCP connection on a random free port.</summary>
public class TelemetryServerTests
{
    [Fact]
    public async Task Stores_status_sent_over_tcp()
    {
        var registry = new ToolRegistry();
        await using var server = new TelemetryServer(registry, new AlarmThresholds(), port: 0);
        server.Start();

        await SendLinesAsync(server.Port, "TOOL-01|RUNNING|60.0|1.10", "TOOL-02|IDLE|45.5|0.95");

        await WaitUntilAsync(() => registry.Count == 2);
        Assert.True(registry.TryGet("TOOL-02", out var status));
        Assert.Equal(ToolState.Idle, status!.State);
    }

    [Fact]
    public async Task Raises_alarm_for_out_of_range_reading()
    {
        var alarms = new ConcurrentQueue<Alarm>();
        await using var server = new TelemetryServer(new ToolRegistry(), new AlarmThresholds(), port: 0);
        server.AlarmRaised += alarms.Enqueue;
        server.Start();

        await SendLinesAsync(server.Port, "TOOL-05|RUNNING|95.0|1.00");

        await WaitUntilAsync(() => !alarms.IsEmpty);
        Assert.Contains(alarms, a => a.ToolId == "TOOL-05" && a.Type == AlarmType.TemperatureHigh);
    }

    [Fact]
    public async Task Rejects_bad_lines_without_dropping_the_connection()
    {
        var registry = new ToolRegistry();
        await using var server = new TelemetryServer(registry, new AlarmThresholds(), port: 0);
        server.Start();

        await SendLinesAsync(server.Port, "garbage", "TOOL-01|RUNNING|60|1.0");

        // Wait on the counters, not registry.Count: the registry is updated a moment
        // before MessagesProcessed is incremented, so checking Count first is a race.
        await WaitUntilAsync(() => server.MessagesProcessed == 1 && server.MessagesRejected == 1);
        Assert.Equal(1, registry.Count);
        Assert.Equal(1, server.MessagesRejected);
        Assert.Equal(1, server.MessagesProcessed);
    }

    [Fact]
    public async Task Handles_ten_clients_at_the_same_time()
    {
        var registry = new ToolRegistry();
        await using var server = new TelemetryServer(registry, new AlarmThresholds(), port: 0);
        server.Start();

        var clients = Enumerable.Range(1, 10)
            .Select(i => SendLinesAsync(server.Port, $"TOOL-{i:D2}|RUNNING|60|1.0"));
        await Task.WhenAll(clients);

        await WaitUntilAsync(() => registry.Count == 10);
        Assert.Equal(10, registry.Count);
    }

    [Fact]
    public async Task Raises_heartbeat_alarm_when_a_tool_goes_silent()
    {
        // The injected clock lets the test jump ahead in time instead of sleeping for 5 seconds.
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        var alarms = new ConcurrentQueue<Alarm>();
        await using var server = new TelemetryServer(
            new ToolRegistry(), new AlarmThresholds(), port: 0, clock: () => clock.Now);
        server.AlarmRaised += alarms.Enqueue;
        server.Start();

        await SendLinesAsync(server.Port, "TOOL-07|RUNNING|60|1.0");
        await WaitUntilAsync(() => server.MessagesProcessed == 1);
        Assert.DoesNotContain(alarms, a => a.Type == AlarmType.HeartbeatTimeout);

        clock.Advance(TimeSpan.FromSeconds(6));

        await WaitUntilAsync(() => alarms.Any(a => a.Type == AlarmType.HeartbeatTimeout));
        Assert.Contains(alarms, a => a.ToolId == "TOOL-07" && a.Type == AlarmType.HeartbeatTimeout);
    }

    [Fact]
    public async Task Connection_count_drops_when_a_tool_disconnects()
    {
        await using var server = new TelemetryServer(new ToolRegistry(), new AlarmThresholds(), port: 0);
        server.Start();

        using (var client = new TcpClient())
        {
            await client.ConnectAsync("127.0.0.1", server.Port);
            await WaitUntilAsync(() => server.ActiveConnections == 1);
        }

        await WaitUntilAsync(() => server.ActiveConnections == 0);
    }

    private static async Task SendLinesAsync(int port, params string[] lines)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        await using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
        foreach (string line in lines)
        {
            await writer.WriteLineAsync(line);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("condition was not met in time");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>A thread-safe clock the test can move forward by hand.</summary>
    private sealed class FakeClock
    {
        private long _ticks;

        public FakeClock(DateTimeOffset start) => _ticks = start.UtcTicks;

        public DateTimeOffset Now => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }
}
