# ToolLink

**A multi-threaded equipment telemetry and alarm server in C# / .NET 8.**

ToolLink receives live status from many pieces of equipment (simulated "tools") over TCP at the same time, keeps the latest status of every tool, and raises alarms when a tool reports an unsafe value, enters a fault state, or stops reporting. It is a small model of the kind of software that monitors machines on a factory floor.

![CI](https://github.com/SuchitaMalkari/ToolLink/actions/workflows/ci.yml/badge.svg)

## What it does

- Accepts **10+ concurrent TCP clients**, each handled by its own async task, so one slow or broken tool never blocks the others.
- Stores the latest status per tool in a **thread-safe `ConcurrentDictionary`**.
- Raises alarms for **out-of-range temperature or pressure** and **FAULT** states.
- Detects tools that **stop reporting** with a heartbeat timeout, raising the alarm once per outage.
- Rejects malformed messages with a clear reason **without dropping the connection**.
- Prints a live status table every 5 seconds.
- **32 xUnit tests** cover the parser, alarm rules, concurrent updates, and end-to-end TCP behavior (10 simultaneous clients, bad input, disconnects, and heartbeat timeouts using a fake clock). GitHub Actions runs them on every push.

## Architecture

```
 ┌──────────────┐  TCP lines   ┌────────────────────────── ToolLink.Server ──────────────────────────┐
 │  Simulator   │ ───────────▶ │  TelemetryServer                                                    │
 │ TOOL-01..10  │              │   ├─ accept loop ──▶ one async task per client                      │
 └──────────────┘              │   │                    └─ MessageParser ─▶ ToolRegistry (concurrent)│
                               │   │                                     └─▶ AlarmRules ─▶ alarms    │
                               │   └─ heartbeat loop (every 1s) ─▶ ToolRegistry.CheckHeartbeats      │
                               └────────────────────────────────────────────────────────────────────┘
```

| Project | Purpose |
|---|---|
| `src/ToolLink.Core` | Parser, alarm rules, thread-safe registry and the TCP server (all the logic) |
| `src/ToolLink.Server` | Console app that runs the server and prints alarms and a status table |
| `src/ToolLink.Simulator` | Starts N simulated tools that stream status once per second |
| `tests/ToolLink.Tests` | xUnit unit and integration tests |

## Message protocol

One line per status update, UTF-8, newline-terminated:

```
TOOL_ID|STATE|TEMPERATURE|PRESSURE
TOOL-03|RUNNING|72.5|1.02
```

`STATE` is `RUNNING`, `IDLE` or `FAULT`. Numbers use `.` as the decimal separator.

## Alarm rules

| Alarm | Default trigger |
|---|---|
| `TemperatureHigh` / `TemperatureLow` | above 85.0 / below 15.0 |
| `PressureHigh` / `PressureLow` | above 2.0 / below 0.5 |
| `ToolFault` | tool reports `FAULT` |
| `HeartbeatTimeout` | no message for more than 5 seconds (raised once until the tool reports again) |

## Run it

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
# terminal 1: start the server
dotnet run --project src/ToolLink.Server

# terminal 2: start 10 simulated tools
dotnet run --project src/ToolLink.Simulator -- 10
```

The last simulated tool goes silent after 10 seconds, so you will see a `HeartbeatTimeout` alarm about 5 seconds later. Occasional overheat, pressure spike and fault values trigger the other alarms.

## Test it

```bash
dotnet test
```

## Design decisions

- **Async tasks instead of one thread per client.** Each connection awaits I/O instead of blocking a thread, so the server scales to many tools with few threads.
- **`ConcurrentDictionary` for the registry.** Many client tasks write at once; it gives safe concurrent reads and writes without hand-written locks.
- **Pure alarm rules.** `AlarmRules` has no I/O, which makes every rule easy to unit test.
- **Injectable clock.** The server takes a `Func<DateTimeOffset>`, so time-based behavior like the heartbeat can be tested deterministically.
- **Fail soft on bad input.** A malformed line is counted and logged, and the connection keeps working.
- **Disconnects.** When a tool closes or drops its connection, its task ends cleanly and removes itself from the server's list of running connections, so a long-running server doesn't build up finished tasks. The heartbeat check then flags the tool if it doesn't reconnect.

## Known limitations

- Out-of-range alarms are raised on every bad reading, so a tool that stays overheated repeats its alarm each second. A real system would raise once and clear when the value recovers, the way heartbeat alarms already work here.
- The server listens on 127.0.0.1 only and trusts every client. There is no TLS or authentication.
- There is no maximum line length, so a client that never sends a newline could make the server buffer without limit.

## Possible next steps

- Raise out-of-range alarms once per episode and clear them on recovery.
- Persist history to a database and expose a REST API or dashboard.
- Configurable thresholds per tool type.
- TLS and authentication for tool connections.
