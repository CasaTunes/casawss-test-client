# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

```
msbuild CasaWSSTestClient.sln /p:Configuration=Release "/p:Platform=Any CPU"
```

Output: `CasaWSSTestClient\bin\Release\CasaWSSTestClient.exe`

NuGet packages (Newtonsoft.Json 13.0.3) are restored automatically by MSBuild via PackageReference — no manual restore step needed.

Debug build:
```
msbuild CasaWSSTestClient.sln /p:Configuration=Debug "/p:Platform=Any CPU"
```

## Run

```
CasaWSSTestClient.exe [ws://host:port]
```

Defaults to `ws://localhost:7008` (configured in `app.config`). Requires a running [CasaWSS](https://github.com/CasaTunes/casawss) server.

## Architecture

This is a small .NET Framework 4.8 console application (`RootNamespace: CasaTunes.TestClient`) with three source files:

### `WssClient.cs` — WebSocket transport layer
- Wraps `System.Net.WebSockets.ClientWebSocket`
- Runs a `ConnectLoop` on a background `Task.Run` thread; auto-retries every 3 seconds on failure
- Fires `OnConnected`, `OnDisconnected`, and `OnMessage` events
- `SendAsync` marshals JSON strings to UTF-8 bytes; callers block on it via `.GetAwaiter().GetResult()`

### `Program.cs` — Command loop and protocol logic
- `Main` wires up `WssClient` event handlers, then calls `RunCommandLoop()` which blocks on `Console.ReadLine()`
- `OnConnected` sends `core.init` automatically; `OnMessage` watches for a successful `core.init` response and auto-subscribes to `server`, `avSwitch`, and `mediaPlayer` topics
- `RunCommandLoop` splits input into up to 4 parts and dispatches to `HandleGet`, `HandleZone`, `HandleStream`, `HandleMp`, `HandleTask`, or `HandleSubscribe`
- `Build(ns, method, params)` constructs the JSON protocol envelope `{id, ns, method, params}` with an auto-incrementing `_msgId`; raw `{...}` input bypasses this and is sent as-is
- `HandleMpForm` is the only handler that builds a `JObject` directly (instead of using `Build`) because it constructs a `fields` array

### `Display.cs` — Thread-safe coloured console output
- All writes are locked on `_lock` to prevent interleaving with the async receive callbacks
- Detects message type by inspecting `error`, `event`, or `result` keys in the JSON
- After every received message or status line it re-prints `> ` to restore the prompt

### Protocol message format
All messages sent to the server use:
```json
{ "id": "<incrementing int>", "ns": "<namespace>", "method": "<ns.method>", "params": { ... } }
```
Namespaces: `core`, `server`, `avSwitch`, `mediaPlayer`
