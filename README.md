# GTMP Server

A multiplayer game server written from scratch in C# / .NET 8.

It speaks its own wire protocol, runs on Windows and Linux, and its game logic is
written in a small Pawn-like language.

> This is an independent implementation. It is not compatible with the GT-MP client,
> and it does not embed any GTA V code.

## Layout

| Project | Purpose |
|---|---|
| `src/GTMP.Shared` | Data model and the wire protocol contract, shared by client and server |
| `src/GTMP.Server.Core` | Tick loop, entity registry, networking, replication, server API |
| `src/GTMP.Server.Host` | Console server executable |
| `src/GTMP.Server.MasterApi` | Master, status and forum HTTP endpoints the launcher calls |
| `src/GTMP.Scripting` | Lexer, parser and interpreter for the Pawn subset |
| `src/GTMP.Client` | Reference client, headless, used to exercise the protocol |
| `test/GTMP.Server.Tests` | Unit and end-to-end tests |
| `test/GTMP.Server.MasterApi.Tests` | Endpoint and contract tests for the master API |
| `test/GTMP.Scripting.Tests` | Language and engine tests |

## Build and test

```bash
dotnet test GTMP-Server.sln
dotnet build GTMP-Server.sln -c Release
```

## Run

```bash
# server
dotnet run --project src/GTMP.Server.Host -- --port 4499

# client, in a second terminal
dotnet run --project src/GTMP.Client -- --port 4499 --name Alice
```

Server options: `--port`, `--tick-rate`, `--run-for <seconds>`.
Client options: `--host`, `--port`, `--name`, `--password`, `--interval`.

`--run-for` bounds the lifetime of the process. It exists so a shutdown can be
exercised from CI or a container health check without sending a console signal.

Settings live in `server.json` next to the executable and are created with defaults on
first start.

## Master API

The GT-MP launcher expects a master server, a status server and a forum API before it
shows anything useful. `GTMP.Server.MasterApi` implements all three over one HTTP
listener; they differ by path, not by port.

```bash
dotnet run --project src/GTMP.Server.MasterApi -- --port 4599
```

| Path | Purpose |
|---|---|
| `GET /servers/detailed` | Server list shown in the launcher |
| `GET /servers/verified` | Addresses that count as verified |
| `GET /stats/masterlist` | Aggregate counters |
| `GET /api/v1/components` | Status page components |
| `GET /?news` | News for the start screen |
| `GET /?action=getPost&hash=<key>&value=<id>` | Single troubleshooting article |
| `GET /update/{channel}/{game,cef,launcher,client}/version` | Version, plain text |
| `GET /update/{channel}/{launcher,client}/hash` | SHA-256 of the archive, plain text |
| `GET /update/{channel}/{launcher,client}/files` | Update archive |

Anything not published returns `404` rather than an empty success, so the launcher
reports "nothing available" instead of a malformed payload.

### Configuration

`masterapi.json` sits next to the executable and is created with defaults on first
start. **Field names are the wire names, not the C# property names** - several
properties carry an explicit rename (`ip`, `thread_id`, `create_at`, ...), and
System.Text.Json matches only the effective name. A config that spells
`IpPortAddress` loads without error and publishes a server with an empty address.
The host validates the file at startup and prints every such problem instead of
refusing to start: a typo in a data file should not take the master server offline.

Update artifacts live in the `artifacts` directory, resolved relative to the
executable:

```
artifacts/update/{channel}/{kind}/version     plain version string
artifacts/update/{channel}/{kind}/files.zip   update archive
```

The hash is computed from the archive rather than read from a file, so the two can
never disagree. `game/version` is intentionally unpublished: a guessed supported GTA
V version would be worse than none, and the launcher degrades cleanly to
"Failed to check currently supported GTA 5 version" when it is missing.

### Verifying the launcher contract

The launcher reads these responses with Newtonsoft using default settings, so a wrong
field name does not throw - it yields an empty object that looks like "no servers".
`tools/verify-launcher-contract.ps1` closes that gap by loading the built launcher
assembly and deserializing live responses into the launcher's own types.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File tools\verify-launcher-contract.ps1 `
    -BaseUrl http://127.0.0.1:4599 `
    -LauncherBinDirectory F:\GTMP\GTMP-Launcher\src\GTANetworkLauncher.WPF\bin\Debug
```

It must run under **Windows PowerShell 5.1**. PowerShell 7 runs on .NET and cannot
load a .NET Framework WPF assembly.

## Protocol

Every datagram starts with an 8 byte header, little endian:

```
0  magic (ushort)   0x4754, guards against unrelated UDP traffic
1  protocol version bumped on incompatible changes
2  channel          handshake | entity sync | event | scripting
3  message type     scoped to the channel
4  payload length   ushort, capped at 1400 bytes so a datagram never fragments
```

Entities are replicated with a field delta mask: the server compares each entity
against the state it last sent and transmits only what changed. An idle server is
silent.

## Game modes

Game logic is written in a Pawn subset - expressions, variables, `if/else`, `while`,
`for`, `foreach`, `break/continue`, functions, recursion, arrays, compound assignment.
No classes, no type tags, no preprocessor.

Scripts live in `resources/**/*.pwn` and are loaded at startup.

```
OnScriptLoad()
{
    CreateVehicle("adder", 0.0, 0.0, 0.0);
    return true;
}

OnTick(tick)
{
    return true;
}

OnPlayerConnect(playerId, name)
{
    GivePlayerMoney(playerId, 5000);
    SendPlayerMessage(playerId, "Welcome, " + name + "!");
    return true;
}

OnPlayerChat(playerId, name, text)
{
    BroadcastChat(playerId, text);
    return false;   // false lets the server forward it to everyone
}
```

Two safety properties are deliberate: every invocation runs against a step budget, so
`while (true) {}` is aborted instead of stalling the tick, and a script that throws is
logged and skipped instead of taking the server down.

## Server API available to scripts

`GetServerName`, `GetPlayerCount`, `GetMaxPlayers`, `GetTickCount`, `GetUptime`,
`GetPlayerName`, `GetPlayerId`, `GetPlayerMoney`, `SetPlayerMoney`, `GivePlayerMoney`,
`GetPlayerHealth`, `SetPlayerHealth`, `CreateVehicle`, `GetEntityPosition`,
`SetEntityPosition`, `GetEntityModel`, `DeleteEntity`, `BroadcastChat`,
`SendPlayerMessage`, `Log`.

## Design decisions worth knowing

- **Handles are per entity type and never recycled.** A late packet from a deleted
  entity must not be able to land on a freshly created one.
- **The tick loop is single threaded on purpose.** Ordering between replication,
  scripting and persistence is part of the contract.
- **The receive thread never touches the world.** It queues packets; the tick thread
  drains them.
- **A client may only write to its own player handle.** Anything else is rejected, so
  no client can teleport another.
- **Sessions are reaped by timeout**, because UDP has no disconnect packet.
- **The master API answers with PascalCase**, because that is what the launcher's
  Newtonsoft defaults expect. Minimal APIs default to camelCase, so the serializer is
  configured explicitly; getting this wrong produces a launcher that silently shows
  nothing.