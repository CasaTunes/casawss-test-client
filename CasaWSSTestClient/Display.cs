using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CasaTunes.TestClient
{
    public static class Display
    {
        private static readonly object _lock = new object();

        public static void Message(string json)
        {
            lock (_lock)
            {
                try
                {
                    var obj = JObject.Parse(json);
                    var pretty = obj.ToString(Formatting.Indented);

                    if (obj["error"] != null)
                        Write(ConsoleColor.Red,     "ERR ", pretty);
                    else if (obj["event"] != null)
                        Write(ConsoleColor.Cyan,    "EVT ", pretty);
                    else if (obj["result"] != null)
                        Write(ConsoleColor.Green,   "RSP ", pretty);
                    else
                        Write(ConsoleColor.Gray,    "??? ", pretty);
                }
                catch
                {
                    Write(ConsoleColor.Gray, "??? ", json);
                }

                Console.Write("> ");  // Restore prompt
            }
        }

        public static void Sent(string json)
        {
            lock (_lock)
            {
                try
                {
                    var pretty = JObject.Parse(json).ToString(Formatting.Indented);
                    Write(ConsoleColor.DarkGray, "SND ", pretty);
                }
                catch
                {
                    Write(ConsoleColor.DarkGray, "SND ", json);
                }
            }
        }

        public static void Info(string message)
        {
            lock (_lock)
            {
                Write(ConsoleColor.DarkYellow, "--- ", message);
                Console.Write("> ");
            }
        }

        public static void Error(string message)
        {
            lock (_lock)
            {
                Write(ConsoleColor.Red, "!!! ", message);
                Console.Write("> ");
            }
        }

        private static void Write(ConsoleColor color, string tag, string text)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine();
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} [{tag}] {text}");
            Console.ForegroundColor = prev;
        }
    }
}
