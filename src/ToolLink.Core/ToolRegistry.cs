using System.Collections.Concurrent;

namespace ToolLink.Core;

/// <summary>
/// Thread-safe store of the latest status for every tool.
///
/// Many client connections update it at the same time, so it is built on
/// ConcurrentDictionary instead of a Dictionary guarded by locks.
/// </summary>
public sealed class ToolRegistry
{
    private readonly ConcurrentDictionary<string, ToolStatus> _tools = new();

    // Tools that already have an active heartbeat alarm, so we raise it only once.
    private readonly ConcurrentDictionary<string, byte> _timedOut = new();

    public int Count => _tools.Count;

    public ToolStatus Update(StatusMessage message)
    {
        var status = new ToolStatus(
            message.ToolId,
            message.State,
            message.Temperature,
            message.Pressure,
            message.ReceivedAt);

        _tools[message.ToolId] = status;

        // The tool is talking again, so a future silence should raise a new alarm.
        _timedOut.TryRemove(message.ToolId, out _);

        return status;
    }

    public bool TryGet(string toolId, out ToolStatus? status)
    {
        bool found = _tools.TryGetValue(toolId, out var value);
        status = value;
        return found;
    }

    /// <summary>A point-in-time copy, safe to enumerate while updates continue.</summary>
    public IReadOnlyList<ToolStatus> Snapshot() =>
        _tools.Values.OrderBy(t => t.ToolId, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Raises a HeartbeatTimeout alarm for each tool that has gone silent.
    /// Each silent tool is reported once until it starts reporting again.
    /// </summary>
    public IReadOnlyList<Alarm> CheckHeartbeats(DateTimeOffset now, TimeSpan timeout)
    {
        var alarms = new List<Alarm>();

        foreach (string toolId in AlarmRules.FindStaleTools(_tools.Values, now, timeout))
        {
            if (_timedOut.TryAdd(toolId, 0))
            {
                alarms.Add(new Alarm(
                    toolId,
                    AlarmType.HeartbeatTimeout,
                    $"no status received for more than {timeout.TotalSeconds:F0}s",
                    now));
            }
        }

        return alarms;
    }
}
