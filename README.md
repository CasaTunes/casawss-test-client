# CasaWSS Test Client

An interactive console application for testing the [CasaTunes WebSocket Server (CasaWSS)](https://github.com/CasaTunes/casawss). It connects to a running CasaWSS instance and lets you send protocol messages and observe real-time events from the command line.

## Prerequisites

- Windows with [.NET Framework 4.8](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48)
- Visual Studio 2019/2022 **or** the MSBuild command-line tools
- A running CasaWSS server (default: `ws://localhost:7008`)

## Build

```
msbuild CasaWSSTestClient.sln /p:Configuration=Release "/p:Platform=Any CPU"
```

The executable is written to `CasaWSSTestClient\bin\Release\CasaWSSTestClient.exe`.

NuGet packages are restored automatically by MSBuild (PackageReference). No manual `nuget restore` step is needed.

## Run

```
CasaWSSTestClient.exe [ws://host:port]
```

The server URL defaults to `ws://localhost:7008` (set in `app.config`). Pass a URL on the command line to connect to a different server:

```
CasaWSSTestClient.exe ws://192.168.1.10:7008
```

## Startup Behaviour

On launch the client:

1. Connects to the WebSocket server (retries every 3 seconds on failure).
2. Automatically sends `core.init` on each successful connection.
3. On a successful `core.init` response, automatically subscribes to the `server`, `avSwitch`, and `mediaPlayer` topics.

You will then receive push events for any state changes on the server without issuing any further commands.

## Output Format

All output is colour-coded and prefixed with a tag:

| Tag | Colour | Meaning |
|-----|--------|---------|
| `SND` | dark grey | Message sent to the server |
| `RSP` | green | Response received (contains `result`) |
| `EVT` | cyan | Event received (contains `event`) |
| `ERR` | red | Error received (contains `error`) |
| `---` | yellow | Client status info |
| `!!!` | red | Client-side error |

## Command Reference

Type `help` or `?` at the `>` prompt to display this list at any time.

---

### Server

| Command | Protocol method |
|---------|----------------|
| `get server` | `server.get` |
| `get tasks` | `server.tasks.get` |
| `task invoke <taskName\|taskId>` | `server.task.invoke` |
| `get chimes` | `server.chimes.get` |
| `chime` | `server.chimes.play` — default chime, all paging rooms |
| `chime <name>` | `server.chimes.play` — named chime, all paging rooms |
| `chime <zoneId>` | `server.chimes.play` — default chime, specific zone |
| `chime <zoneId> <name>` | `server.chimes.play` — named chime, specific zone |
| `tts <text>` | `server.tts.play` — TTS in all paging rooms |
| `tts <zoneId> <text...>` | `server.tts.play` — TTS in specific zone (first token = zone id) |

---

### AV Switch — System Mode (zones)

| Command | Protocol method |
|---------|----------------|
| `get zones` | `avSwitch.zone.getAll` |
| `get zone <id>` | `avSwitch.zone.get` |
| `zone power <id> on\|off\|toggle` | `avSwitch.zone.setPower` |
| `zone mute <id> on\|off\|toggle` | `avSwitch.zone.setMute` |
| `zone vol <id> <0-100>` | `avSwitch.zone.setVolume` |
| `zone adj <id> <delta>` | `avSwitch.zone.adjustVolume` (e.g. `5` or `-5`) |
| `zone input <id> <inputId>` | `avSwitch.zone.setInput` |
| `zone maxvol <id> <0-100>` | `avSwitch.zone.setMaxVolume` |
| `zone group <id> <targetId>` | `avSwitch.zone.group` |
| `zone ungroup <id>` | `avSwitch.zone.ungroup` |
| `zone sleep <id> <seconds>` | `avSwitch.zone.setSleepTimer` (0 = power off immediately) |

---

### AV Switch — Streamer Mode (streams)

Use these commands when `server.get` returns `"mode": "streamer"`.

| Command | Protocol method |
|---------|----------------|
| `get streams` | `avSwitch.stream.getAll` |
| `get stream <id>` | `avSwitch.stream.get` |
| `stream mute <id> on\|off\|toggle` | `avSwitch.stream.setMute` |
| `stream vol <id> <0-100>` | `avSwitch.stream.setVolume` |
| `stream adj <id> <delta>` | `avSwitch.stream.adjustVolume` |
| `stream maxvol <id> <0-100>` | `avSwitch.stream.setMaxVolume` |

---

### Media Player

#### Now Playing & Transport

| Command | Protocol method |
|---------|----------------|
| `get np [inputId]` | `mediaPlayer.getAll` or `mediaPlayer.get` |
| `mp play <inputId>` | `mediaPlayer.play` |
| `mp pause <inputId>` | `mediaPlayer.pause` |
| `mp stop <inputId>` | `mediaPlayer.stop` |
| `mp toggle <inputId>` | `mediaPlayer.playPause` |
| `mp next <inputId>` | `mediaPlayer.next` |
| `mp prev <inputId>` | `mediaPlayer.previous` |
| `mp thumbsup <inputId>` | `mediaPlayer.thumbsUp` |
| `mp thumbsdown <inputId>` | `mediaPlayer.thumbsDown` |
| `mp shuffle <inputId> on\|off` | `mediaPlayer.shuffle` |
| `mp repeat <inputId> on\|off\|once` | `mediaPlayer.repeat` |
| `mp featured <inputId> on\|off` | `mediaPlayer.featured` |
| `mp pos <inputId> <seconds>` | `mediaPlayer.position` (seek to absolute position) |
| `mp jump <inputId> <delta>` | `mediaPlayer.jump` (relative seek, e.g. `30` or `-15`) |

#### Queue

| Command | Protocol method |
|---------|----------------|
| `get queue <inputId>` | `mediaPlayer.queue.get` |
| `mp queue get <inputId>` | `mediaPlayer.queue.get` |
| `mp queue clear <inputId>` | `mediaPlayer.queue.clear` |
| `mp queue save <inputId> <name>` | `mediaPlayer.queue.save` |
| `mp queue play <inputId> <index>` | `mediaPlayer.queue.playItem` |
| `mp queue del <inputId> <index>` | `mediaPlayer.queue.deleteItem` |

#### Featured / Bookmarks

| Command | Protocol method |
|---------|----------------|
| `get featured <inputId>` | `mediaPlayer.featured.get` |

#### Progress Tracking

The server does **not** send periodic progress events. Instead, the `mediaPlayer.changed` event carries `progress` and `duration` as the authoritative re-sync anchor. Real clients are expected to maintain a local 1-second timer and advance the position themselves. This test client makes no attempt to do that — it simply displays the `progress` value from each `mediaPlayer.changed` event as it arrives.

Rules for a real client implementation:
- Start incrementing `progress` by 1 each second when `status == 2` (playing) and `progressBar.isAvailable == true`.
- Stop incrementing and clamp the display at `duration`.
- For live/streaming sources, `duration` is `-1` and `progressBar.isAvailable` is `false` — do not run the timer.
- Re-sync `progress`, `duration`, and `status` on every `mediaPlayer.changed` event.
- After a `mp pos` or `mp jump` command the response already contains the updated position — no event is needed.

#### Media Browsing

| Command | Protocol method |
|---------|----------------|
| `mp browse <inputId>` | `mediaPlayer.media.getRoot` — browse root for an input |
| `mp col <mediaId>` | `mediaPlayer.media.getCollection` — open a collection by ID |
| `mp search <mediaId> <text>` | `mediaPlayer.media.search` — search within a collection |
| `mp mplay <inputId> <mediaId>` | `mediaPlayer.media.play` (addToQueue: playNow) |

#### Forms

Some collections contain a `form` object instead of a list of items (used for authentication and music-service-specific input). Use `mp form` to submit a form button:

```
mp form <buttonId> [key=value ...]
```

**Important:** You must include **all** fields from the form response in your submission, including `hidden` fields. Copy the `key` and `value` of each hidden field exactly as they appear in the `RSP` output.

Example:

```
mp form btn_login username=myuser password=secret hidden_token=abc123
```

The response will be a new collection (which may itself contain another form if the server requires further input).

---

### Connection & Subscriptions

| Command | Description |
|---------|-------------|
| `sub [topic\|all]` | Subscribe to a topic (`server`, `avSwitch`, `mediaPlayer`) |
| `unsub [topic\|all]` | Unsubscribe from a topic |
| `init` | Re-send `core.init` |
| `pong` | Send `core.pong` (reply to a server ping) |
| `status` | Show current connection and init state |
| `reconnect` | Force a disconnect and reconnect |
| `{...}` | Send a raw JSON message directly |
| `help` / `?` | Show the command list |
| `quit` / `q` | Exit |

---

## Related

- [CasaTunes WebSocket Server (CasaWSS)](https://github.com/CasaTunes/casawss) — the server this client connects to
- [CasaTunes WebSocket Protocol Specification](https://github.com/CasaTunes/casawss/blob/main/casatunes-websocket-protocol.md) — full protocol reference
