using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CasaTunes.TestClient
{
    public class WssClient
    {
        private readonly string _url;
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private volatile bool _stopping;

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnMessage;

        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public WssClient(string url)
        {
            _url = url;
        }

        public void Start()
        {
            _stopping = false;
            Task.Run(ConnectLoop);
        }

        public void Stop()
        {
            _stopping = true;
            _cts?.Cancel();
        }

        private async Task ConnectLoop()
        {
            while (!_stopping)
            {
                _cts = new CancellationTokenSource();
                _ws  = new ClientWebSocket();
                try
                {
                    Display.Info($"Connecting to {_url}...");
                    await _ws.ConnectAsync(new Uri(_url), _cts.Token);
                    OnConnected?.Invoke();
                    await ReceiveLoop();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Display.Error($"Connection error: {ex.Message}");
                }
                finally
                {
                    OnDisconnected?.Invoke();
                    _ws.Dispose();
                    _ws = null;
                }

                if (!_stopping)
                {
                    Display.Info("Reconnecting in 3s...");
                    await Task.Delay(3000);
                }
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];

            while (_ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                using (var ms = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    OnMessage?.Invoke(Encoding.UTF8.GetString(ms.ToArray()));
                }
            }
        }

        public async Task SendAsync(string json)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open)
            {
                Display.Error("Not connected.");
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}
