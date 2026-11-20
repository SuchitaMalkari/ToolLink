using ToolLink.Core;

// Usage: dotnet run --project src/ToolLink.Server [port]
int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 5050;

var registry = new ToolRegistry();
var thresholds = new AlarmThresholds();
var consoleLock = new object();

await using var server = new TelemetryServer(registry, thresholds, port);

server.Log += message =>
{
    lock (consoleLock)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
};

server.AlarmRaised += alarm =>
{
    lock (consoleLock)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{alarm.RaisedAt.ToLocalTime():HH:mm:ss}] ALARM {alarm.Type} on {alarm.ToolId}: {alarm.Message}");
        Console.ResetColor();
    }
};

server.Start();
Console.WriteLine($"ToolLink server listening on 127.0.0.1:{server.Port}. Press Ctrl+C to stop.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Print a status table every 5 seconds until Ctrl+C.
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
try
{
    while (await timer.WaitForNextTickAsync(cts.Token))
    {
        lock (consoleLock)
        {
            PrintDashboard(registry, server, thresholds);
        }
    }
}
catch (OperationCanceledException)
{
}

Console.WriteLine("Shutting down...");

static void PrintDashboard(ToolRegistry registry, TelemetryServer server, AlarmThresholds thresholds)
{
    Console.WriteLine();
    Console.WriteLine($"--- {registry.Count} tools | {server.ActiveConnections} connected | " +
                      $"{server.MessagesProcessed} messages | {server.MessagesRejected} rejected ---");
    Console.WriteLine($"{"TOOL",-10}{"STATE",-10}{"TEMP",8}{"PRESSURE",10}{"LAST SEEN",12}");

    var now = DateTimeOffset.UtcNow;
    foreach (var tool in registry.Snapshot())
    {
        // A tool that has gone silent shows as LOST instead of its last reported state.
        string state = now - tool.LastSeen > thresholds.HeartbeatTimeout ? "LOST" : tool.State.ToString();
        Console.WriteLine($"{tool.ToolId,-10}{state,-10}{tool.Temperature,8:F1}{tool.Pressure,10:F2}" +
                          $"{tool.LastSeen.ToLocalTime(),12:HH:mm:ss}");
    }

    Console.WriteLine();
}
