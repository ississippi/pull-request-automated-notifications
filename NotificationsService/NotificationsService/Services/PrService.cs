using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace NotificationsService.Services
{
    public class PrService
    {
        private readonly ConcurrentDictionary<string, PrItem> _prs = new();
        private readonly List<WebSocket> _sockets = new();
        private readonly object _lock = new();

        public PrService()
        {
            // Add some fake PRs
            _prs.TryAdd("1", new PrItem { Id = "1", Title = "Initial PR" });
            Debug.WriteLine("PrService starting...");
            // Start background task to simulate new PRs
            Task.Run(SimulateNewPrs);
        }

        public IEnumerable<PrItem> GetAllPrs() => _prs.Values;

        public async Task HandleWebSocketAsync(WebSocket socket)
        {
            try
            {
                var buffer = new byte[1024 * 4];

                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                    // you can also ignore input messages if you're only pushing
                }
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine($"WebSocket error: {ex.Message}");
                // optionally log or ignore
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error in WebSocket handler: {ex}");
            }
        }


        private async Task SimulateNewPrs()
        {
            int id = 2;
            while (true)
            {
                await Task.Delay(10000); // Every 10 sec, fake a new PR

                var pr = new PrItem { Id = id.ToString(), Title = $"New PR {id}" };
                _prs.TryAdd(pr.Id, pr);
                await BroadcastNewPr(pr);

                id++;
            }
        }

        private async Task BroadcastNewPr(PrItem pr)
        {
            var message = JsonSerializer.Serialize(pr);
            var bytes = Encoding.UTF8.GetBytes(message);

            List<WebSocket> deadSockets = new();

            lock (_lock)
            {
                foreach (var socket in _sockets)
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    else
                    {
                        deadSockets.Add(socket);
                    }
                }

                foreach (var dead in deadSockets)
                {
                    _sockets.Remove(dead);
                }
            }
        }
    }

    public class PrItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
    }

}
