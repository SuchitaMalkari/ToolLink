using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ToolLink.Core;

/// <summary>
/// Parses the line-based wire protocol used by the tools:
///
///     TOOL_ID|STATE|TEMPERATURE|PRESSURE
///     e.g.  TOOL-03|RUNNING|72.5|1.02
///
/// Any malformed line is rejected with a reason instead of crashing the server.
/// </summary>
public static class MessageParser
{
    public const int MaxToolIdLength = 32;

    public static bool TryParse(
        string? line,
        DateTimeOffset receivedAt,
        [NotNullWhen(true)] out StatusMessage? message,
        out string error)
    {
        message = null;

        if (string.IsNullOrWhiteSpace(line))
        {
            error = "empty message";
            return false;
        }

        string[] parts = line.Split('|');
        if (parts.Length != 4)
        {
            error = $"expected 4 fields but got {parts.Length}";
            return false;
        }

        string toolId = parts[0].Trim();
        if (toolId.Length == 0 || toolId.Length > MaxToolIdLength)
        {
            error = "tool id is missing or too long";
            return false;
        }

        // Enum.TryParse also accepts numbers like "5", so IsDefined guards against unknown values.
        if (!Enum.TryParse(parts[1].Trim(), ignoreCase: true, out ToolState state) ||
            !Enum.IsDefined(state) ||
            int.TryParse(parts[1].Trim(), out _))
        {
            error = $"unknown state '{parts[1].Trim()}'";
            return false;
        }

        if (!TryParseFinite(parts[2], out double temperature))
        {
            error = $"invalid temperature '{parts[2].Trim()}'";
            return false;
        }

        if (!TryParseFinite(parts[3], out double pressure))
        {
            error = $"invalid pressure '{parts[3].Trim()}'";
            return false;
        }

        message = new StatusMessage(toolId, state, temperature, pressure, receivedAt);
        error = string.Empty;
        return true;
    }

    private static bool TryParseFinite(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value);
}
