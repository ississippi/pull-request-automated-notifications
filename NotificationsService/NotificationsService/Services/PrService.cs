using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NotificationsService.Models;

namespace NotificationsService.Services
{
    public class PrService
    {
        private readonly ILogger<PrService> _logger;
        private readonly ConcurrentDictionary<string, PrItem> _prs = new();
        private readonly List<WebSocket> _sockets = new();
        private readonly object _lock = new();
        private static readonly ConcurrentBag<WebSocket> _clients = new();

        public PrService(ILogger<PrService> logger)
        {
            _logger = logger;
            // Add some fake PRs
            _prs.TryAdd("1", new PrItem { id = "1", title = "Initial PR" });
            //Debug.WriteLine("PrService starting...");
            // Start background task to simulate new PRs
            //Task.Run(SimulateNewPrs);
        }

        public IEnumerable<PrItem> GetAllPrs() => _prs.Values;

        public async Task HandleWebSocketAsync(WebSocket socket)
        {
            _clients.Add(socket);
            _logger.LogInformation("WebSocket client connected. Total clients: {0}", _clients.Count);

            var buffer = new byte[1024 * 4];

            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("WebSocket client requested close.");
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                    }
                }
                _logger.LogInformation("**** HandleWebSocketAsync WebSocket is not in open state.");
            }
            catch (WebSocketException ex)
            {
                _logger.LogError(ex, $"WebSocket error in HandleWebSocketAsync: {ex.Message}");
                // optionally log or ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HandleWebSocketAsync");
            }
            finally
            {
                _logger.LogInformation("WebSocket connection closed.");
            }
        }

        public async Task BroadcastNewPrAsync(PrItem newPr)
        {
            try
            {
                _prs.TryAdd(newPr.id, newPr);
                //_logger.LogInformation($"BroadcastNewPrAsync received PR #{newPr.id} for broadcast.");
                var message = JsonSerializer.Serialize(newPr);
                message = message.Replace("\n", "").Replace("\r", "");
                //_logger.LogInformation($"BroadcastNewPrAsync received PR #{newPr.id} message:{message} (removed newlines).");
                if (message.Length >= 128 * 1024)
                {
                    _logger.LogError($"BroadcastNewPrAsync: message is:{message.Length} bytes. Greater than 128k cannot be sent via websockets.");
                    return;
                }
                //_logger.LogInformation($"BroadcaseNewPrAsync sending payload for PR #{newPr.id} with payload: {message}");
                //_logger.LogInformation($"BroadcastNewPrAsync: message size: {message.Length}.");
                var bytes = Encoding.UTF8.GetBytes(message);
                //_logger.LogInformation($"BroadcastNewPrAsync: bytes Length:{bytes.Count()}");
                var segment = new ArraySegment<byte>(bytes);
                //_logger.LogInformation($"BroadcastNewPrAsync PR #{newPr.id} message ready for delivery. segment Length:{segment.Count}");
                _logger.LogInformation($"BroadcastNewPrAsync received PR #{newPr.id} Total clients:{_clients.Count}.");
                foreach (var client in _clients.ToArray())
                {
                    _logger.LogInformation($"Processing websocket send for PR #{newPr.id}, Title:{newPr.title} state:{client.State.ToString()}.");
                    if (client.State == WebSocketState.Open)
                    {
                        try
                        {
                            await client.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                            _logger.LogInformation($"**** Broadcasted PR #{newPr.id} Title:{newPr.title} to client.");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error sending to WebSocket client. Removing client.");
                            // WebSocket will be removed implicitly if closed later
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, $"Someting wong broadcasting PR #{newPr.id}.");
            }
        }


        private async Task SimulateNewPrs()
        {
            int id = 2;
            while (true)
            {
                await Task.Delay(30000); // Every 10 sec, fake a new PR

                var pr = new PrItem
                {
                    id = id.ToString(),
                    title = $"Review for PR #{id} in /ississippi/repo: Fixing terrible bug.",
                    author = "johndoe",
                    date = DateTime.UtcNow.ToString("o"),
                    repo = "ississippi%2Fpull-request-test-repo",
                    //review = "Fake Review"
                };
                await BroadcastNewPrAsync(pr);

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

}
