using System;
using System.Configuration;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CasaTunes.TestClient
{
    internal static class Program
    {
        private static WssClient _client;
        private static int _msgId = 1;
        private static volatile bool _initialized;

        static void Main(string[] args)
        {
            var url = args.Length > 0
                ? args[0]
                : ConfigurationManager.AppSettings["ServerUrl"] ?? "ws://localhost:7008";

            Console.Title = "CasaWSS Test Client";
            PrintBanner(url);

            _client = new WssClient(url);
            _client.OnConnected    += OnConnected;
            _client.OnDisconnected += OnDisconnected;
            _client.OnMessage      += OnMessage;
            _client.Start();

            RunCommandLoop();

            _client.Stop();
            Thread.Sleep(500);
        }

        // ── WebSocket callbacks ──────────────────────────────────────────────────

        private static void OnConnected()
        {
            _initialized = false;
            Display.Info("Connected. Sending core.init...");
            Send(Build("core", "core.init", new { version = "1.0" }));
        }

        private static void OnDisconnected()
        {
            _initialized = false;
            Display.Info("Disconnected.");
        }

        private static void OnMessage(string json)
        {
            Display.Message(json);

            if (_initialized) return;

            try
            {
                var obj = JObject.Parse(json);
                if ((string)obj["method"] == "core.init" && obj["result"] != null && obj["error"] == null)
                {
                    _initialized = true;
                    Display.Info("Init successful. Auto-subscribing to all topics...");
                    foreach (var topic in new[] { "server", "avSwitch", "mediaPlayer" })
                        Send(Build("core", "core.subscribe", new { topic }));
                }
            }
            catch { }
        }

        // ── Command loop ─────────────────────────────────────────────────────────

        private static void RunCommandLoop()
        {
            PrintHelp();
            Console.Write("> ");

            while (true)
            {
                var line = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(line)) { Console.Write("> "); continue; }

                // Split into up to 4 parts for commands that need more depth (e.g. mp queue get <id>)
                var parts = line.Split(new[] { ' ' }, 4, StringSplitOptions.RemoveEmptyEntries);
                var cmd   = parts[0].ToLowerInvariant();
                var arg1  = parts.Length > 1 ? parts[1].Trim() : null;
                var arg2  = parts.Length > 2 ? parts[2].Trim() : null;
                var arg3  = parts.Length > 3 ? parts[3].Trim() : null;

                switch (cmd)
                {
                    case "q":
                    case "quit":
                    case "exit":
                        return;

                    case "?":
                    case "help":
                        PrintHelp();
                        break;

                    case "status":
                        PrintStatus();
                        break;

                    case "get":
                        HandleGet(arg1, arg2, arg3);
                        break;

                    case "task":
                        HandleTask(arg1, arg2);
                        break;

                    case "zone":
                        HandleZone(arg1, Rejoin(arg2, arg3));
                        break;

                    case "stream":
                        HandleStream(arg1, Rejoin(arg2, arg3));
                        break;

                    case "mp":
                        HandleMp(arg1, arg2, arg3);
                        break;

                    case "sub":
                        HandleSubscribe(arg1 ?? "all", subscribe: true);
                        break;

                    case "unsub":
                        HandleSubscribe(arg1 ?? "all", subscribe: false);
                        break;

                    case "init":
                        Send(Build("core", "core.init", new { version = "1.0" }));
                        break;

                    case "pong":
                        Send(Build("core", "core.pong"));
                        break;

                    case "reconnect":
                        Display.Info("Forcing reconnect...");
                        _client.Stop();
                        Thread.Sleep(600);
                        _client.Start();
                        break;

                    default:
                        if (line.TrimStart().StartsWith("{"))
                            Send(line);
                        else
                            Display.Error($"Unknown command '{cmd}'. Type 'help' for list.");
                        break;
                }

                Console.Write("> ");
            }
        }

        // ── get ──────────────────────────────────────────────────────────────────

        private static void HandleGet(string arg1, string arg2, string arg3)
        {
            switch (arg1?.ToLowerInvariant())
            {
                case "server":
                    Send(Build("server", "server.get"));
                    break;
                case "zones":
                    Send(Build("avSwitch", "avSwitch.zone.getAll"));
                    break;
                case "zone":
                    if (string.IsNullOrEmpty(arg2))
                        Display.Error("Usage: get zone <zoneId>");
                    else
                        Send(Build("avSwitch", "avSwitch.zone.get", new { zoneId = arg2 }));
                    break;
                case "streams":
                    Send(Build("avSwitch", "avSwitch.stream.getAll"));
                    break;
                case "stream":
                    if (string.IsNullOrEmpty(arg2))
                        Display.Error("Usage: get stream <streamId>");
                    else
                        Send(Build("avSwitch", "avSwitch.stream.get", new { streamId = arg2 }));
                    break;
                case "np":
                case "nowplaying":
                    if (string.IsNullOrEmpty(arg2))
                        Send(Build("mediaPlayer", "mediaPlayer.getAll"));
                    else
                        Send(Build("mediaPlayer", "mediaPlayer.get", new { inputId = arg2 }));
                    break;
                case "queue":
                    if (string.IsNullOrEmpty(arg2))
                        Display.Error("Usage: get queue <inputId>");
                    else
                        Send(Build("mediaPlayer", "mediaPlayer.queue.get", new { inputId = arg2 }));
                    break;
                case "featured":
                    if (string.IsNullOrEmpty(arg2))
                        Display.Error("Usage: get featured <inputId>");
                    else
                        Send(Build("mediaPlayer", "mediaPlayer.featured.get", new { inputId = arg2 }));
                    break;
                case "tasks":
                    Send(Build("server", "server.tasks.get"));
                    break;
                default:
                    Display.Error("Usage: get server|zones|zone <id>|streams|stream <id>|np [id]|queue <id>|featured <id>|tasks");
                    break;
            }
        }

        // ── zone ─────────────────────────────────────────────────────────────────

        private static void HandleZone(string subcmd, string rest)
        {
            var restParts = rest?.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var zoneId    = restParts?.Length > 0 ? restParts[0] : null;
            var valueStr  = restParts?.Length > 1 ? restParts[1] : null;

            switch (subcmd?.ToLowerInvariant())
            {
                case "power":
                    if (zoneId == null || valueStr == null)
                        Display.Error("Usage: zone power <zoneId> on|off|toggle");
                    else
                        Send(Build("avSwitch", "avSwitch.zone.setPower", new { zoneId, power = valueStr }));
                    break;
                case "mute":
                    if (zoneId == null || valueStr == null)
                        Display.Error("Usage: zone mute <zoneId> on|off|toggle");
                    else
                        Send(Build("avSwitch", "avSwitch.zone.setMute", new { zoneId, mute = valueStr }));
                    break;
                case "vol":
                case "volume":
                    if (zoneId == null || valueStr == null) { Display.Error("Usage: zone vol <zoneId> <0-100>"); break; }
                    int vol;
                    if (!int.TryParse(valueStr, out vol)) Display.Error("volume must be a number 0-100.");
                    else Send(Build("avSwitch", "avSwitch.zone.setVolume", new { zoneId, volume = vol }));
                    break;
                case "adj":
                case "adjust":
                    if (zoneId == null || valueStr == null) { Display.Error("Usage: zone adj <zoneId> <delta>"); break; }
                    int delta;
                    if (!int.TryParse(valueStr, out delta)) Display.Error("delta must be a number (e.g. 5 or -5).");
                    else Send(Build("avSwitch", "avSwitch.zone.adjustVolume", new { zoneId, delta }));
                    break;
                case "input":
                    if (zoneId == null || valueStr == null)
                        Display.Error("Usage: zone input <zoneId> <inputId>");
                    else
                        Send(Build("avSwitch", "avSwitch.zone.setInput", new { zoneId, inputId = valueStr }));
                    break;
                case "maxvol":
                    if (zoneId == null || valueStr == null) { Display.Error("Usage: zone maxvol <zoneId> <0-100>"); break; }
                    int maxvol;
                    if (!int.TryParse(valueStr, out maxvol)) Display.Error("value must be a number 0-100.");
                    else Send(Build("avSwitch", "avSwitch.zone.setMaxVolume", new { zoneId, value = maxvol }));
                    break;
                case "group":
                    if (zoneId == null || valueStr == null)
                        Display.Error("Usage: zone group <zoneId> <targetZoneId>");
                    else
                        Send(Build("avSwitch", "avSwitch.zone.group", new { zoneId, targetZoneId = valueStr }));
                    break;
                case "ungroup":
                    if (zoneId == null)
                        Display.Error("Usage: zone ungroup <zoneId>");
                    else
                        Send(Build("avSwitch", "avSwitch.zone.ungroup", new { zoneId }));
                    break;
                default:
                    Display.Error("Unknown zone sub-command. Type 'help' for list.");
                    break;
            }
        }

        // ── stream ───────────────────────────────────────────────────────────────

        private static void HandleStream(string subcmd, string rest)
        {
            var restParts = rest?.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var streamId  = restParts?.Length > 0 ? restParts[0] : null;
            var valueStr  = restParts?.Length > 1 ? restParts[1] : null;

            switch (subcmd?.ToLowerInvariant())
            {
                case "mute":
                    if (streamId == null || valueStr == null)
                        Display.Error("Usage: stream mute <streamId> on|off|toggle");
                    else
                        Send(Build("avSwitch", "avSwitch.stream.setMute", new { streamId, mute = valueStr }));
                    break;
                case "vol":
                case "volume":
                    if (streamId == null || valueStr == null) { Display.Error("Usage: stream vol <streamId> <0-100>"); break; }
                    int vol;
                    if (!int.TryParse(valueStr, out vol)) Display.Error("volume must be a number 0-100.");
                    else Send(Build("avSwitch", "avSwitch.stream.setVolume", new { streamId, volume = vol }));
                    break;
                case "adj":
                case "adjust":
                    if (streamId == null || valueStr == null) { Display.Error("Usage: stream adj <streamId> <delta>"); break; }
                    int delta;
                    if (!int.TryParse(valueStr, out delta)) Display.Error("delta must be a number (e.g. 5 or -5).");
                    else Send(Build("avSwitch", "avSwitch.stream.adjustVolume", new { streamId, delta }));
                    break;
                case "maxvol":
                    if (streamId == null || valueStr == null) { Display.Error("Usage: stream maxvol <streamId> <0-100>"); break; }
                    int maxvol;
                    if (!int.TryParse(valueStr, out maxvol)) Display.Error("value must be a number 0-100.");
                    else Send(Build("avSwitch", "avSwitch.stream.setMaxVolume", new { streamId, value = maxvol }));
                    break;
                default:
                    Display.Error("Unknown stream sub-command. Type 'help' for list.");
                    break;
            }
        }

        // ── mp ───────────────────────────────────────────────────────────────────

        private static void HandleMp(string subcmd, string arg2, string arg3)
        {
            switch (subcmd?.ToLowerInvariant())
            {
                // ── Transport (inputId only) ──────────────────────────────────────
                case "play":
                case "pause":
                case "stop":
                case "next":
                case "prev":
                case "previous":
                case "toggle":
                case "thumbsup":
                case "thumbsdown":
                    HandleMpSimpleTransport(subcmd.ToLowerInvariant(), arg2);
                    break;

                // ── Transport with mode ───────────────────────────────────────────
                case "shuffle":
                    if (string.IsNullOrEmpty(arg2) || string.IsNullOrEmpty(arg3))
                        Display.Error("Usage: mp shuffle <inputId> on|off");
                    else
                        Send(Build("mediaPlayer", "mediaPlayer.shuffle", new { inputId = arg2, mode = arg3 }));
                    break;

                case "repeat":
                    if (string.IsNullOrEmpty(arg2) || string.IsNullOrEmpty(arg3))
                        Display.Error("Usage: mp repeat <inputId> on|off|once");
                    else
                        Send(Build("mediaPlayer", "mediaPlayer.repeat", new { inputId = arg2, mode = arg3 }));
                    break;

                case "featured":
                    if (string.IsNullOrEmpty(arg2) || string.IsNullOrEmpty(arg3))
                        Display.Error("Usage: mp featured <inputId> on|off");
                    else
                        Send(Build("mediaPlayer", "mediaPlayer.featured", new { inputId = arg2, mode = arg3 }));
                    break;

                // ── Position / jump ───────────────────────────────────────────────
                case "pos":
                case "position":
                    if (string.IsNullOrEmpty(arg2) || string.IsNullOrEmpty(arg3))
                    { Display.Error("Usage: mp pos <inputId> <seconds>"); break; }
                    int pos;
                    if (!int.TryParse(arg3, out pos)) Display.Error("seconds must be a number.");
                    else Send(Build("mediaPlayer", "mediaPlayer.position", new { inputId = arg2, position = pos }));
                    break;

                case "jump":
                    if (string.IsNullOrEmpty(arg2) || string.IsNullOrEmpty(arg3))
                    { Display.Error("Usage: mp jump <inputId> <delta>  (e.g. 30 or -15)"); break; }
                    int jdelta;
                    if (!int.TryParse(arg3, out jdelta)) Display.Error("delta must be a number.");
                    else Send(Build("mediaPlayer", "mediaPlayer.jump", new { inputId = arg2, delta = jdelta }));
                    break;

                // ── Queue ─────────────────────────────────────────────────────────
                case "queue":
                    HandleMpQueue(arg2, arg3);
                    break;

                // ── Browse ────────────────────────────────────────────────────────
                case "browse":
                    if (string.IsNullOrEmpty(arg2))
                        Display.Error("Usage: mp browse <inputId>");
                    else
                        Send(Build("mediaPlayer", "mediaPlayer.media.getRoot", new { inputId = arg2 }));
                    break;

                case "col":
                    if (string.IsNullOrEmpty(arg2))
                        Display.Error("Usage: mp col <mediaId>");
                    else
                        Send(Build("mediaPlayer", "mediaPlayer.media.getCollection", new { mediaId = arg2 }));
                    break;

                case "search":
                    if (string.IsNullOrEmpty(arg2) || string.IsNullOrEmpty(arg3))
                        Display.Error("Usage: mp search <mediaId> <text>");
                    else
                        Send(Build("mediaPlayer", "mediaPlayer.media.search", new { mediaId = arg2, searchText = arg3 }));
                    break;

                case "mplay":
                    // mp mplay <inputId> <mediaId>  — uses playNow
                    if (string.IsNullOrEmpty(arg2) || string.IsNullOrEmpty(arg3))
                        Display.Error("Usage: mp mplay <inputId> <mediaId>  (addToQueue defaults to playNow)");
                    else
                        Send(Build("mediaPlayer", "mediaPlayer.media.play", new { inputId = arg2, mediaId = arg3, addToQueue = "playNow" }));
                    break;

                case "form":
                    HandleMpForm(arg2, arg3);
                    break;

                default:
                    Display.Error("Unknown mp sub-command. Type 'help' for list.");
                    break;
            }
        }

        private static void HandleMpSimpleTransport(string subcmd, string inputId)
        {
            if (string.IsNullOrEmpty(inputId))
            {
                Display.Error($"Usage: mp {subcmd} <inputId>");
                return;
            }
            string method;
            switch (subcmd)
            {
                case "play":       method = "mediaPlayer.play";       break;
                case "pause":      method = "mediaPlayer.pause";      break;
                case "stop":       method = "mediaPlayer.stop";       break;
                case "toggle":     method = "mediaPlayer.playPause";  break;
                case "next":       method = "mediaPlayer.next";       break;
                case "prev":
                case "previous":   method = "mediaPlayer.previous";   break;
                case "thumbsup":   method = "mediaPlayer.thumbsUp";   break;
                case "thumbsdown": method = "mediaPlayer.thumbsDown"; break;
                default:           method = "mediaPlayer." + subcmd;  break;
            }
            Send(Build("mediaPlayer", method, new { inputId }));
        }

        private static void HandleMpQueue(string subcmd, string rest)
        {
            var restParts = rest?.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var inputId   = restParts?.Length > 0 ? restParts[0] : null;
            var valueStr  = restParts?.Length > 1 ? restParts[1] : null;

            switch (subcmd?.ToLowerInvariant())
            {
                case "get":
                    if (string.IsNullOrEmpty(inputId))
                        Display.Error("Usage: mp queue get <inputId>");
                    else
                        Send(Build("mediaPlayer", "mediaPlayer.queue.get", new { inputId }));
                    break;
                case "clear":
                    if (string.IsNullOrEmpty(inputId))
                        Display.Error("Usage: mp queue clear <inputId>");
                    else
                        Send(Build("mediaPlayer", "mediaPlayer.queue.clear", new { inputId }));
                    break;
                case "save":
                    if (string.IsNullOrEmpty(inputId) || string.IsNullOrEmpty(valueStr))
                        Display.Error("Usage: mp queue save <inputId> <name>");
                    else
                        Send(Build("mediaPlayer", "mediaPlayer.queue.save", new { inputId, name = valueStr }));
                    break;
                case "play":
                    if (string.IsNullOrEmpty(inputId) || string.IsNullOrEmpty(valueStr))
                    { Display.Error("Usage: mp queue play <inputId> <index>"); break; }
                    int pIdx;
                    if (!int.TryParse(valueStr, out pIdx)) Display.Error("index must be a number.");
                    else Send(Build("mediaPlayer", "mediaPlayer.queue.playItem", new { inputId, index = pIdx }));
                    break;
                case "del":
                case "delete":
                    if (string.IsNullOrEmpty(inputId) || string.IsNullOrEmpty(valueStr))
                    { Display.Error("Usage: mp queue del <inputId> <index>"); break; }
                    int dIdx;
                    if (!int.TryParse(valueStr, out dIdx)) Display.Error("index must be a number.");
                    else Send(Build("mediaPlayer", "mediaPlayer.queue.deleteItem", new { inputId, index = dIdx }));
                    break;
                default:
                    Display.Error("Usage: mp queue get|clear|save|play|del <inputId> [args]");
                    break;
            }
        }

        private static void HandleMpForm(string buttonId, string fieldArgs)
        {
            if (string.IsNullOrEmpty(buttonId))
            {
                Display.Error("Usage: mp form <buttonId> [key=value ...]");
                return;
            }
            var fields = new JArray();
            if (!string.IsNullOrEmpty(fieldArgs))
            {
                foreach (var pair in fieldArgs.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = pair.IndexOf('=');
                    if (eq < 1)
                    {
                        Display.Error($"Invalid field '{pair}' — expected key=value.");
                        return;
                    }
                    fields.Add(new JObject
                    {
                        ["key"]   = pair.Substring(0, eq),
                        ["value"] = pair.Substring(eq + 1)
                    });
                }
            }
            var obj = new JObject
            {
                ["id"]     = (_msgId++).ToString(),
                ["ns"]     = "mediaPlayer",
                ["method"] = "mediaPlayer.media.submitForm",
                ["params"] = new JObject
                {
                    ["buttonId"] = buttonId,
                    ["fields"]   = fields
                }
            };
            Send(obj.ToString(Formatting.None));
        }

        // ── Subscribe ─────────────────────────────────────────────────────────────

        private static void HandleTask(string subcmd, string rest)
        {
            switch (subcmd?.ToLowerInvariant())
            {
                case "invoke":
                    if (string.IsNullOrEmpty(rest))
                        Display.Error("Usage: task invoke <taskName|taskId>");
                    else
                        Send(Build("server", "server.task.invoke", new { task = rest }));
                    break;
                default:
                    Display.Error("Usage: task invoke <taskName|taskId>");
                    break;
            }
        }

        private static void HandleSubscribe(string arg, bool subscribe)
        {
            var method = subscribe ? "core.subscribe" : "core.unsubscribe";
            var topics = arg == "all"
                ? new[] { "server", "avSwitch", "mediaPlayer" }
                : new[] { arg };
            foreach (var topic in topics)
                Send(Build("core", method, new { topic }));
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static string Rejoin(string a, string b)
        {
            if (a == null) return null;
            if (b == null) return a;
            return a + " " + b;
        }

        private static void Send(string json)
        {
            try
            {
                Display.Sent(json);
                _client.SendAsync(json).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Display.Error($"Send failed: {ex.Message}");
            }
        }

        private static string Build(string ns, string method, object @params = null)
        {
            var obj = new JObject
            {
                ["id"]     = (_msgId++).ToString(),
                ["ns"]     = ns,
                ["method"] = method
            };
            if (@params != null)
                obj["params"] = JObject.FromObject(@params);
            return obj.ToString(Formatting.None);
        }

        // ── Output ───────────────────────────────────────────────────────────────

        private static void PrintBanner(string url)
        {
            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════════╗");
            Console.WriteLine("║         CasaTunes WebSocket Test Client          ║");
            Console.WriteLine("╚══════════════════════════════════════════════════╝");
            Console.WriteLine($"  Server : {url}");
            Console.WriteLine("  Tip    : Pass a different URL as a command-line arg");
            Console.WriteLine("           e.g. CasaWSSTestClient.exe ws://192.168.1.10:7008");
            Console.WriteLine();
            Console.WriteLine("  On connect: core.init is sent automatically.");
            Console.WriteLine("  On init success: subscribes to server, avSwitch, mediaPlayer.");
            Console.WriteLine();
        }

        private static void PrintHelp()
        {
            Console.WriteLine("  ──────────────────────────────────────────────────────────────────");
            Console.WriteLine("  get server                       server.get");
            Console.WriteLine("  get zones                        avSwitch.zone.getAll  (system mode)");
            Console.WriteLine("  get zone <id>                    avSwitch.zone.get     (system mode)");
            Console.WriteLine("  get streams                      avSwitch.stream.getAll (streamer mode)");
            Console.WriteLine("  get stream <id>                  avSwitch.stream.get    (streamer mode)");
            Console.WriteLine("  get np [inputId]                 mediaPlayer.getAll or .get");
            Console.WriteLine("  get queue <inputId>              mediaPlayer.queue.get");
            Console.WriteLine("  get featured <inputId>           mediaPlayer.featured.get");
            Console.WriteLine("  get tasks                        server.tasks.get");
            Console.WriteLine("  task invoke <taskName|taskId>    server.task.invoke");
            Console.WriteLine("  ──────────────────────────────────────────────────────────────────");
            Console.WriteLine("  zone power   <id> on|off|toggle");
            Console.WriteLine("  zone mute    <id> on|off|toggle");
            Console.WriteLine("  zone vol     <id> <0-100>        setVolume");
            Console.WriteLine("  zone adj     <id> <delta>        adjustVolume (e.g. 5 or -5)");
            Console.WriteLine("  zone input   <id> <inputId>      setInput");
            Console.WriteLine("  zone maxvol  <id> <0-100>        setMaxVolume");
            Console.WriteLine("  zone group   <id> <targetId>     link zone to group");
            Console.WriteLine("  zone ungroup <id>                remove zone from group");
            Console.WriteLine("  ──────────────────────────────────────────────────────────────────");
            Console.WriteLine("  [Streamer mode only]");
            Console.WriteLine("  stream mute   <id> on|off|toggle avSwitch.stream.setMute");
            Console.WriteLine("  stream vol    <id> <0-100>       avSwitch.stream.setVolume");
            Console.WriteLine("  stream adj    <id> <delta>       avSwitch.stream.adjustVolume");
            Console.WriteLine("  stream maxvol <id> <0-100>       avSwitch.stream.setMaxVolume");
            Console.WriteLine("  ──────────────────────────────────────────────────────────────────");
            Console.WriteLine("  mp play      <inputId>           mediaPlayer.play");
            Console.WriteLine("  mp pause     <inputId>           mediaPlayer.pause");
            Console.WriteLine("  mp stop      <inputId>           mediaPlayer.stop");
            Console.WriteLine("  mp toggle    <inputId>           mediaPlayer.playPause (toggle)");
            Console.WriteLine("  mp next      <inputId>           mediaPlayer.next");
            Console.WriteLine("  mp prev      <inputId>           mediaPlayer.previous");
            Console.WriteLine("  mp thumbsup  <inputId>           mediaPlayer.thumbsUp");
            Console.WriteLine("  mp thumbsdown <inputId>          mediaPlayer.thumbsDown");
            Console.WriteLine("  mp shuffle   <inputId> on|off    mediaPlayer.shuffle");
            Console.WriteLine("  mp repeat    <inputId> on|off|once");
            Console.WriteLine("  mp featured  <inputId> on|off    add/remove featured bookmark");
            Console.WriteLine("  mp pos       <inputId> <secs>    seek to position");
            Console.WriteLine("  mp jump      <inputId> <delta>   relative seek (e.g. 30 or -15)");
            Console.WriteLine("  mp queue get   <inputId>         mediaPlayer.queue.get");
            Console.WriteLine("  mp queue clear <inputId>         mediaPlayer.queue.clear");
            Console.WriteLine("  mp queue save  <inputId> <name>  mediaPlayer.queue.save");
            Console.WriteLine("  mp queue play  <inputId> <idx>   mediaPlayer.queue.playItem");
            Console.WriteLine("  mp queue del   <inputId> <idx>   mediaPlayer.queue.deleteItem");
            Console.WriteLine("  mp browse    <inputId>           mediaPlayer.media.getRoot");
            Console.WriteLine("  mp col       <mediaId>           mediaPlayer.media.getCollection");
            Console.WriteLine("  mp search    <mediaId> <text>    mediaPlayer.media.search");
            Console.WriteLine("  mp mplay     <inputId> <mediaId> mediaPlayer.media.play (playNow)");
            Console.WriteLine("  mp form      <buttonId> [k=v ..] mediaPlayer.media.submitForm");
            Console.WriteLine("               Include ALL fields from the form response, including");
            Console.WriteLine("               hidden fields (copy their value from the JSON output).");
            Console.WriteLine("  ──────────────────────────────────────────────────────────────────");
            Console.WriteLine("  sub [topic|all]                  subscribe to topic");
            Console.WriteLine("  unsub [topic|all]                unsubscribe from topic");
            Console.WriteLine("  pong                             send core.pong");
            Console.WriteLine("  init                             re-send core.init");
            Console.WriteLine("  status                           show connection state");
            Console.WriteLine("  reconnect                        force reconnect");
            Console.WriteLine("  {json}                           send raw JSON");
            Console.WriteLine("  help / ?                         show this list");
            Console.WriteLine("  quit / q                         exit");
            Console.WriteLine("  ──────────────────────────────────────────────────────────────────");
            Console.WriteLine();
        }

        private static void PrintStatus()
        {
            Console.WriteLine($"  Connected   : {_client.IsConnected}");
            Console.WriteLine($"  Initialized : {_initialized}");
            Console.WriteLine();
        }
    }
}
