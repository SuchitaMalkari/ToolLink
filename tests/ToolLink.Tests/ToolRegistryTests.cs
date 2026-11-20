using ToolLink.Core;

namespace ToolLink.Tests;

public class ToolRegistryTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static StatusMessage Message(string id, DateTimeOffset at, double temp = 60) =>
        new(id, ToolState.Running, temp, 1.0, at);

    [Fact]
    public void Keeps_the_latest_status_per_tool()
    {
        var registry = new ToolRegistry();

        registry.Update(Message("TOOL-01", Start, temp: 50));
        registry.Update(Message("TOOL-01", Start.AddSeconds(1), temp: 55));

        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGet("TOOL-01", out var status));
        Assert.Equal(55, status!.Temperature);
    }

    [Fact]
    public void Silent_tool_raises_heartbeat_alarm_only_once()
    {
        var registry = new ToolRegistry();
        registry.Update(Message("TOOL-01", Start));

        var first = registry.CheckHeartbeats(Start.AddSeconds(6), Timeout);
        var second = registry.CheckHeartbeats(Start.AddSeconds(7), Timeout);

        var alarm = Assert.Single(first);
        Assert.Equal(AlarmType.HeartbeatTimeout, alarm.Type);
        Assert.Empty(second);
    }

    [Fact]
    public void Tool_within_timeout_is_not_flagged()
    {
        var registry = new ToolRegistry();
        registry.Update(Message("TOOL-01", Start));

        Assert.Empty(registry.CheckHeartbeats(Start.AddSeconds(4), Timeout));
    }

    [Fact]
    public void Tool_that_reports_again_can_be_flagged_again_later()
    {
        var registry = new ToolRegistry();
        registry.Update(Message("TOOL-01", Start));
        Assert.Single(registry.CheckHeartbeats(Start.AddSeconds(6), Timeout));

        registry.Update(Message("TOOL-01", Start.AddSeconds(10)));      // tool comes back
        Assert.Empty(registry.CheckHeartbeats(Start.AddSeconds(11), Timeout));

        Assert.Single(registry.CheckHeartbeats(Start.AddSeconds(20), Timeout)); // goes silent again
    }

    [Fact]
    public void Handles_concurrent_updates_from_many_threads()
    {
        var registry = new ToolRegistry();

        Parallel.For(0, 10_000, i =>
        {
            string id = $"TOOL-{i % 10:D2}";
            registry.Update(Message(id, Start.AddMilliseconds(i)));
        });

        Assert.Equal(10, registry.Count);
        Assert.Equal(10, registry.Snapshot().Count);
    }
}
