namespace ToolLink.Core;

/// <summary>Operating state reported by a piece of equipment.</summary>
public enum ToolState
{
    Running,
    Idle,
    Fault
}

/// <summary>One status message sent by a tool, after it has been parsed and validated.</summary>
public sealed record StatusMessage(
    string ToolId,
    ToolState State,
    double Temperature,
    double Pressure,
    DateTimeOffset ReceivedAt);

/// <summary>The latest known status of a tool, as stored in the registry.</summary>
public sealed record ToolStatus(
    string ToolId,
    ToolState State,
    double Temperature,
    double Pressure,
    DateTimeOffset LastSeen);

public enum AlarmType
{
    TemperatureHigh,
    TemperatureLow,
    PressureHigh,
    PressureLow,
    ToolFault,
    HeartbeatTimeout
}

/// <summary>An alarm raised when a tool reports an unsafe value or stops reporting.</summary>
public sealed record Alarm(
    string ToolId,
    AlarmType Type,
    string Message,
    DateTimeOffset RaisedAt);

/// <summary>Safe operating limits. Values outside these limits raise alarms.</summary>
public sealed class AlarmThresholds
{
    public double MinTemperature { get; init; } = 15.0;
    public double MaxTemperature { get; init; } = 85.0;
    public double MinPressure { get; init; } = 0.5;
    public double MaxPressure { get; init; } = 2.0;

    /// <summary>A tool that sends nothing for this long is treated as lost.</summary>
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
