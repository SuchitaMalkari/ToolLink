using ToolLink.Core;

namespace ToolLink.Tests;

public class MessageParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_a_valid_status_line()
    {
        bool ok = MessageParser.TryParse("TOOL-03|RUNNING|72.5|1.02", Now, out var message, out string error);

        Assert.True(ok, error);
        Assert.NotNull(message);
        Assert.Equal("TOOL-03", message.ToolId);
        Assert.Equal(ToolState.Running, message.State);
        Assert.Equal(72.5, message.Temperature);
        Assert.Equal(1.02, message.Pressure);
        Assert.Equal(Now, message.ReceivedAt);
    }

    [Fact]
    public void Accepts_lowercase_state_and_extra_spaces()
    {
        bool ok = MessageParser.TryParse("  TOOL-01 | idle | 50 | 1.0 ", Now, out var message, out _);

        Assert.True(ok);
        Assert.Equal("TOOL-01", message!.ToolId);
        Assert.Equal(ToolState.Idle, message.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TOOL-01|RUNNING|70")]              // too few fields
    [InlineData("TOOL-01|RUNNING|70|1.0|extra")]    // too many fields
    [InlineData("|RUNNING|70|1.0")]                 // missing tool id
    [InlineData("TOOL-01|BROKEN|70|1.0")]           // unknown state
    [InlineData("TOOL-01|5|70|1.0")]                // numeric state
    [InlineData("TOOL-01|RUNNING|hot|1.0")]         // non-numeric temperature
    [InlineData("TOOL-01|RUNNING|NaN|1.0")]         // not a finite number
    [InlineData("TOOL-01|RUNNING|70|Infinity")]     // not a finite number
    public void Rejects_malformed_lines(string line)
    {
        bool ok = MessageParser.TryParse(line, Now, out var message, out string error);

        Assert.False(ok);
        Assert.Null(message);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Rejects_tool_id_that_is_too_long()
    {
        string longId = new('X', MessageParser.MaxToolIdLength + 1);

        bool ok = MessageParser.TryParse($"{longId}|RUNNING|70|1.0", Now, out _, out _);

        Assert.False(ok);
    }
}
