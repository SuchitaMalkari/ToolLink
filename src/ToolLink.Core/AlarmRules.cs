namespace ToolLink.Core;

/// <summary>
/// Pure functions that decide which alarms a status message should raise.
/// Kept free of I/O so they are easy to unit test.
/// </summary>
public static class AlarmRules
{
    public static IReadOnlyList<Alarm> Evaluate(StatusMessage message, AlarmThresholds thresholds)
    {
        var alarms = new List<Alarm>();

        if (message.Temperature > thresholds.MaxTemperature)
        {
            alarms.Add(Create(message, AlarmType.TemperatureHigh,
                $"temperature {message.Temperature:F1} is above {thresholds.MaxTemperature:F1}"));
        }
        else if (message.Temperature < thresholds.MinTemperature)
        {
            alarms.Add(Create(message, AlarmType.TemperatureLow,
                $"temperature {message.Temperature:F1} is below {thresholds.MinTemperature:F1}"));
        }

        if (message.Pressure > thresholds.MaxPressure)
        {
            alarms.Add(Create(message, AlarmType.PressureHigh,
                $"pressure {message.Pressure:F2} is above {thresholds.MaxPressure:F2}"));
        }
        else if (message.Pressure < thresholds.MinPressure)
        {
            alarms.Add(Create(message, AlarmType.PressureLow,
                $"pressure {message.Pressure:F2} is below {thresholds.MinPressure:F2}"));
        }

        if (message.State == ToolState.Fault)
        {
            alarms.Add(Create(message, AlarmType.ToolFault, "tool reported a FAULT state"));
        }

        return alarms;
    }

    /// <summary>Returns the ids of tools that have not reported within the timeout.</summary>
    public static IEnumerable<string> FindStaleTools(
        IEnumerable<ToolStatus> tools,
        DateTimeOffset now,
        TimeSpan timeout) =>
        tools.Where(t => now - t.LastSeen > timeout).Select(t => t.ToolId);

    private static Alarm Create(StatusMessage message, AlarmType type, string text) =>
        new(message.ToolId, type, text, message.ReceivedAt);
}
