using ToolLink.Core;

namespace ToolLink.Tests;

public class AlarmRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly AlarmThresholds Thresholds = new();

    private static StatusMessage Message(ToolState state = ToolState.Running, double temp = 60, double pressure = 1.0) =>
        new("TOOL-01", state, temp, pressure, Now);

    [Fact]
    public void Normal_reading_raises_no_alarms()
    {
        Assert.Empty(AlarmRules.Evaluate(Message(), Thresholds));
    }

    [Theory]
    [InlineData(90.0, AlarmType.TemperatureHigh)]
    [InlineData(10.0, AlarmType.TemperatureLow)]
    public void Out_of_range_temperature_raises_alarm(double temperature, AlarmType expected)
    {
        var alarm = Assert.Single(AlarmRules.Evaluate(Message(temp: temperature), Thresholds));
        Assert.Equal(expected, alarm.Type);
        Assert.Equal("TOOL-01", alarm.ToolId);
    }

    [Theory]
    [InlineData(2.5, AlarmType.PressureHigh)]
    [InlineData(0.2, AlarmType.PressureLow)]
    public void Out_of_range_pressure_raises_alarm(double pressure, AlarmType expected)
    {
        var alarm = Assert.Single(AlarmRules.Evaluate(Message(pressure: pressure), Thresholds));
        Assert.Equal(expected, alarm.Type);
    }

    [Fact]
    public void Values_exactly_on_the_limit_are_allowed()
    {
        var atLimits = Message(temp: Thresholds.MaxTemperature, pressure: Thresholds.MinPressure);
        Assert.Empty(AlarmRules.Evaluate(atLimits, Thresholds));
    }

    [Fact]
    public void Fault_state_raises_tool_fault_alarm()
    {
        var alarm = Assert.Single(AlarmRules.Evaluate(Message(state: ToolState.Fault), Thresholds));
        Assert.Equal(AlarmType.ToolFault, alarm.Type);
    }

    [Fact]
    public void Several_problems_raise_several_alarms()
    {
        var alarms = AlarmRules.Evaluate(Message(ToolState.Fault, temp: 95, pressure: 2.4), Thresholds);

        Assert.Equal(3, alarms.Count);
        Assert.Contains(alarms, a => a.Type == AlarmType.TemperatureHigh);
        Assert.Contains(alarms, a => a.Type == AlarmType.PressureHigh);
        Assert.Contains(alarms, a => a.Type == AlarmType.ToolFault);
    }
}
