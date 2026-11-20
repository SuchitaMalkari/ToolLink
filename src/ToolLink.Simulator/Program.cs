using System.Globalization;
using System.Net.Sockets;
using System.Text;

// Simulates equipment that streams status to the ToolLink server.
// Usage: dotnet run --project src/ToolLink.Simulator [toolCount] [port]
int toolCount = args.Length > 0 && int.TryParse(args[0], out int n) ? n : 10;
int port = args.Length > 1 && int.TryParse(args[1], out int p) ? p : 5050;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine($"Starting {toolCount} simulated tools against 127.0.0.1:{port}. Press Ctrl+C to stop.");

// The last tool goes silent after 10 seconds so the server's heartbeat alarm can be seen.
var tools = Enumerable.Range(1, toolCount)
    .Select(i => RunToolAsync(
        toolId: $"TOOL-{i:D2}",
        port: port,
        goSilentAfter: i == toolCount ? TimeSpan.FromSeconds(10) : null,
        ct: cts.Token))
    .ToArray();

await Task.WhenAll(tools);
Console.WriteLine("Simulator stopped.");

static async Task RunToolAsync(string toolId, int port, TimeSpan? goSilentAfter, CancellationToken ct)
{
    using var client = new TcpClient();
    try
    {
        await client.ConnectAsync("127.0.0.1", port, ct);
    }
    catch (Exception ex) when (ex is SocketException or OperationCanceledException)
    {
        Console.WriteLine($"{toolId}: could not connect ({ex.Message})");
        return;
    }

    await using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
    var started = DateTime.UtcNow;

    try
    {
        while (!ct.IsCancellationRequested)
        {
            if (goSilentAfter is { } limit && DateTime.UtcNow - started > limit)
            {
                Console.WriteLine($"{toolId}: going silent to simulate a lost tool");
                await Task.Delay(Timeout.Infinite, ct);
            }

            await writer.WriteLineAsync(BuildStatusLine(toolId).AsMemory(), ct);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C
    }
    catch (IOException ex)
    {
        Console.WriteLine($"{toolId}: connection lost ({ex.Message})");
    }
}

static string BuildStatusLine(string toolId)
{
    var random = Random.Shared; // thread-safe shared instance

    double roll = random.NextDouble();
    string state = roll < 0.02 ? "FAULT" : roll < 0.15 ? "IDLE" : "RUNNING";

    double temperature = 40 + random.NextDouble() * 30;    // normal: 40-70
    if (random.NextDouble() < 0.05) temperature = 88 + random.NextDouble() * 10; // occasional overheat

    double pressure = 0.8 + random.NextDouble() * 0.8;     // normal: 0.8-1.6
    if (random.NextDouble() < 0.03) pressure = 2.2 + random.NextDouble() * 0.3; // occasional spike

    return string.Create(CultureInfo.InvariantCulture, $"{toolId}|{state}|{temperature:F1}|{pressure:F2}");
}
