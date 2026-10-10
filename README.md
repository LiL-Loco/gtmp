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
| `src/GTMP.Scripting` | Lexer, parser and interpreter for the Pawn subset |
| `src/GTMP.Client` | Reference client, headless, used to exercise the protocol |
| `test/GTMP.Server.Tests` | Unit and end-to-end tests |
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