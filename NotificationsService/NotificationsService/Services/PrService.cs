using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NotificationsService.Models;

namespace NotificationsService.Services
{
    public class PrService : IDisposable
    {
        private readonly ILogger<PrService> _logger;
        private readonly ConcurrentDictionary<string, PrItem> _prs = new();

        // Use ConcurrentDictionary instead of ConcurrentBag for better client management
        private static readonly ConcurrentDictionary<Guid, WebSocketClient> _clients = new();
        private readonly SemaphoreSlim _broadcastLock = new(1, 1);
        private readonly Timer _heartbeatTimer;
        private bool _disposed = false;

        // Class to track both the WebSocket and its cancellation token
        private class WebSocketClient
        {
            public WebSocket Socket { get; set; }
            public CancellationTokenSource CancellationSource { get; set; }
            public DateTime LastActivity { get; set; }
            // Added for bi-directional heartbeat system with proper initialization
            public string PendingHeartbeatId { get; set; } = null;
            public DateTime HeartbeatSentTime { get; set; } = DateTime.MinValue;
        }

        public PrService(ILogger<PrService> logger)
        {
            _logger = logger;
            // Add some fake PRs
            _prs.TryAdd("1", new PrItem { id = "1", title = "Initial PR" });

            // Uncomment if you want to simulate new PRs
            // Task.Run(SimulateNewPrs);

            // Setup a timer to send heartbeats to keep connections alive
            // 20 seconds interval is common for most network equipment timeout settings
            _heartbeatTimer = new Timer(SendHeartbeats, null, TimeSpan.Zero, TimeSpan.FromSeconds(20));
        }

        public IEnumerable<PrItem> GetAllPrs() => _prs.Values;

        public async Task HandleWebSocketAsync(WebSocket socket)
        {
            // Create a cancellation token source for this specific client
            var cts = new CancellationTokenSource();
            var clientId = Guid.NewGuid();

            var client = new WebSocketClient
            {
                Socket = socket,
                CancellationSource = cts,
                LastActivity = DateTime.UtcNow
                // PendingHeartbeatId and HeartbeatSentTime already have default values
            };

            _clients[clientId] = client;

            _logger.LogInformation("WebSocket client {ClientId} connected. Total clients: {Count}",
                clientId, _clients.Count);

            var buffer = new byte[1024 * 4];

            try
            {
                // Start a background task to handle timeouts
                _ = MonitorClientTimeoutAsync(clientId, cts.Token);

                while (socket.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
                {
                    // Use a timeout for the receive operation to detect stalled connections
                    var receiveTask = socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cts.Token);

                    // Wait for either the receive to complete or a timeout
                    if (await Task.WhenAny(receiveTask, Task.Delay(TimeSpan.FromMinutes(2), cts.Token)) == receiveTask)
                    {
                        var result = await receiveTask;

                        // Update last activity timestamp
                        if (_clients.TryGetValue(clientId, out var existingClient))
                        {
                            existingClient.LastActivity = DateTime.UtcNow;
                        }

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.LogInformation("WebSocket client {ClientId} requested close", clientId);
                            await socket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "Closed by client",
                                cts.Token);
                            break;
                        }
                        else if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            // Check if this is a heartbeat response (a single null byte)
                            if (result.Count == 1 && buffer[0] == 0x00)
                            {
                                // This is a heartbeat response, we can ignore it
                                _logger.LogDebug("Received heartbeat response from client {ClientId}", clientId);
                                continue;
                            }

                            // Handle other binary messages
                            _logger.LogDebug("Received binary message from client {ClientId} with {Count} bytes",
                                clientId, result.Count);

                            // Process binary message if needed
                        }
                        else if (result.MessageType == WebSocketMessageType.Text)
                        {
                            // Process text message
                            _logger.LogDebug("Received text message from client {ClientId}", clientId);

                            // Process the text message
                            var receivedMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);

                            try
                            {
                                // Try to parse as JSON
                                using var jsonDoc = JsonDocument.Parse(receivedMessage);
                                var root = jsonDoc.RootElement;

                                // Check if this is a heartbeat response
                                if (root.TryGetProperty("type", out var typeProperty) &&
                                    typeProperty.GetString() == "heartbeat-response")
                                {
                                    if (root.TryGetProperty("id", out var idProperty))
                                    {
                                        var responseHeartbeatId = idProperty.GetString();

                                        // Verify this matches our pending heartbeat - use clientWithHeartbeat to avoid any shadowing
                                        if (_clients.TryGetValue(clientId, out var clientWithHeartbeat) &&
                                            clientWithHeartbeat.PendingHeartbeatId == responseHeartbeatId)
                                        {
                                            // Clear the pending heartbeat and update activity
                                            clientWithHeartbeat.PendingHeartbeatId = null;
                                            clientWithHeartbeat.LastActivity = DateTime.UtcNow;

                                            _logger.LogDebug("Received valid heartbeat response from client {ClientId} for heartbeat {HeartbeatId}",
                                                clientId, responseHeartbeatId);
                                        }
                                        else
                                        {
                                            _logger.LogWarning("Received invalid heartbeat response from client {ClientId}. Expected: {Expected}, Got: {Received}",
                                                clientId, clientWithHeartbeat?.PendingHeartbeatId, responseHeartbeatId);
                                        }
                                    }
                                }

                                // Handle other message types as needed
                            }
                            catch (JsonException)
                            {
                                _logger.LogWarning("Received non-JSON text message from client {ClientId}", clientId);
                                // Handle non-JSON messages if needed
                            }
                        }
                    }
                    else
                    {
                        // Receive timed out, make sure the client is still alive
                        if (socket.State != WebSocketState.Open)
                        {
                            _logger.LogInformation("WebSocket client {ClientId} connection state changed to {State} during receive wait",
                                clientId, socket.State);
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("WebSocket operation for client {ClientId} was cancelled", clientId);
            }
            catch (WebSocketException ex)
            {
                _logger.LogError(ex, "WebSocket error for client {ClientId}: {Message}", clientId, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling WebSocket for client {ClientId}", clientId);
            }
            finally
            {
                // Cancel any pending operations
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }

                // Always remove the client when the connection ends for any reason
                if (_clients.TryRemove(clientId, out var removedClient))
                {
                    _logger.LogInformation("Removed WebSocket client {ClientId}. Remaining clients: {Count}",
                        clientId, _clients.Count);

                    // Dispose the cancellation token source
                    removedClient.CancellationSource.Dispose();
                }

                // Ensure the socket is closed if it's still open
                if (socket.State != WebSocketState.Closed && socket.State != WebSocketState.Aborted)
                {
                    try
                    {
                        await socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Connection closed by server",
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error closing WebSocket for client {ClientId}", clientId);
                    }
                }
            }
        }

        // Method to monitor client activity and close inactive connections
        private async Task MonitorClientTimeoutAsync(Guid clientId, CancellationToken cancellationToken)
        {
            try
            {
                // Run until cancelled
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Check every minute
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);

                    if (_clients.TryGetValue(clientId, out var client))
                    {
                        bool shouldCloseConnection = false;
                        string closeReason = string.Empty;

                        // Check for general inactivity (5 minutes)
                        if (DateTime.UtcNow - client.LastActivity > TimeSpan.FromMinutes(5))
                        {
                            shouldCloseConnection = true;
                            closeReason = "Connection closed due to inactivity";
                            _logger.LogWarning("WebSocket client {ClientId} has been inactive for too long.", clientId);
                        }
                        // Check for pending heartbeat responses (30 seconds timeout)
                        else if (!string.IsNullOrEmpty(client.PendingHeartbeatId) &&
                                 DateTime.UtcNow - client.HeartbeatSentTime > TimeSpan.FromSeconds(30))
                        {
                            shouldCloseConnection = true;
                            closeReason = "Connection closed due to missed heartbeat response";
                            _logger.LogWarning("WebSocket client {ClientId} failed to respond to heartbeat {HeartbeatId}.",
                                clientId, client.PendingHeartbeatId);
                        }

                        // Close the connection if needed
                        if (shouldCloseConnection)
                        {
                            try
                            {
                                // Cancel the client's token to terminate its processing
                                client.CancellationSource.Cancel();

                                // Close the socket
                                if (client.Socket.State == WebSocketState.Open)
                                {
                                    await client.Socket.CloseAsync(
                                        WebSocketCloseStatus.NormalClosure,
                                        closeReason,
                                        CancellationToken.None);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error closing inactive WebSocket client {ClientId}", clientId);
                            }
                            finally
                            {
                                // Remove the client from the dictionary
                                _logger.LogInformation($"MonitorClientTimeoutAsync: Removing client.");
                                _clients.TryRemove(clientId, out _);
                            }

                            // Exit the monitoring loop
                            break;
                        }
                    }
                    else
                    {
                        // Client has already been removed, no need to keep monitoring
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // This is expected when the token is cancelled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring WebSocket client {ClientId}", clientId);
            }
        }

        // Method to send heartbeats to all connected clients
        private void SendHeartbeats(object state)
        {
            _ = SendHeartbeatsAsync();
        }

        private async Task SendHeartbeatsAsync()
        {
            try
            {
                //var ecsTaskId = GetEcsTaskId();
                //await LogEcsInformation();
                //_logger.LogInformation("[] Sending heartbeats to {Count} clients at {ticks}.", _clients.Count,DateTime.Now.Ticks);

                // Create a JSON heartbeat message with a unique ID to track responses
                var heartbeatId = Guid.NewGuid().ToString("N");
                var heartbeatMessage = JsonSerializer.Serialize(new
                {
                    type = "heartbeat",
                    id = heartbeatId,
                    timestamp = DateTime.UtcNow
                });
                var heartbeatBytes = Encoding.UTF8.GetBytes(heartbeatMessage);
                var heartbeatSegment = new ArraySegment<byte>(heartbeatBytes);

                // List to track clients that need removal
                var clientsToRemove = new List<Guid>();

                foreach (var kvp in _clients)
                {
                    var clientId = kvp.Key;
                    var client = kvp.Value;

                    if (client.Socket.State == WebSocketState.Open)
                    {
                        try
                        {
                            // Send a JSON text heartbeat message
                            await client.Socket.SendAsync(
                                heartbeatSegment,
                                WebSocketMessageType.Text,
                                true,
                                CancellationToken.None);

                            // Store the heartbeat ID for this client to track response
                            client.PendingHeartbeatId = heartbeatId;
                            client.HeartbeatSentTime = DateTime.UtcNow;

                            //_logger.LogInformation("Sent heartbeat {HeartbeatId} to client {ClientId}",heartbeatId, clientId);

                            // Note: We don't update LastActivity here anymore
                            // It will only be updated when the client responds to the heartbeat
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error sending heartbeat to client {ClientId}. Marking for removal", clientId);
                            clientsToRemove.Add(clientId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Client {ClientId} is in state {State}. Marking for removal",
                            clientId, client.Socket.State);
                        clientsToRemove.Add(clientId);
                    }
                }

                // Clean up any clients that had errors
                foreach (var clientId in clientsToRemove)
                {
                    if (_clients.TryRemove(clientId, out var removedClient))
                    {
                        _logger.LogInformation("SendHeartbeatsAsync: Removed dead client {ClientId} during heartbeat", clientId);

                        try
                        {
                            // Cancel the client's operations
                            removedClient.CancellationSource.Cancel();
                            removedClient.CancellationSource.Dispose();

                            // Try to close the socket if it's not already closed
                            if (removedClient.Socket.State != WebSocketState.Closed &&
                                removedClient.Socket.State != WebSocketState.Aborted)
                            {
                                await removedClient.Socket.CloseAsync(
                                    WebSocketCloseStatus.NormalClosure,
                                    "Connection closed by server",
                                    CancellationToken.None);
                            }
                        }
                        catch
                        {
                            // Just ignore any errors during cleanup
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending heartbeats to WebSocket clients");
            }
        }

        public async Task BroadcastNewPrAsync(PrItem newPr)
        {
            if (newPr == null)
            {
                _logger.LogWarning("Attempted to broadcast null PR");
                return;
            }

            try
            {
                // Add the PR to our dictionary
                _prs.TryAdd(newPr.id, newPr);

                // Serialize message outside the lock to minimize lock time
                var message = JsonSerializer.Serialize(newPr);
                //message = message.Replace("\n", "").Replace("\r", "");

                if (message.Length >= 128 * 1024)
                {
                    _logger.LogError("PR #{PrId} message size {Size} bytes exceeds WebSocket limit",
                        newPr.id, message.Length);
                    return;
                }

                var bytes = Encoding.UTF8.GetBytes(message);
                var segment = new ArraySegment<byte>(bytes);

                _logger.LogInformation("Broadcasting PR #{PrId} to {Count} clients",
                    newPr.id, _clients.Count);

                // Use SemaphoreSlim to control concurrent access during broadcast
                await _broadcastLock.WaitAsync();
                try
                {
                    // Create a list of tasks for concurrent sending
                    var sendTasks = new List<Task>();
                    var clientsToRemove = new List<Guid>();

                    foreach (var kvp in _clients)
                    {
                        var clientId = kvp.Key;
                        var client = kvp.Value;

                        if (client.Socket.State == WebSocketState.Open)
                        {
                            sendTasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    await client.Socket.SendAsync(
                                        segment,
                                        WebSocketMessageType.Text,
                                        true,
                                        CancellationToken.None);

                                    // Update last activity timestamp
                                    client.LastActivity = DateTime.UtcNow;

                                    _logger.LogDebug("Sent PR #{PrId} to client {ClientId}",
                                        newPr.id, clientId);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Error sending to client {ClientId}. Marking for removal",
                                        clientId);
                                    clientsToRemove.Add(clientId);
                                }
                            }));
                        }
                        else
                        {
                            // Client socket is not open, mark for removal
                            clientsToRemove.Add(clientId);
                        }
                    }

                    // Wait for all sends to complete
                    await Task.WhenAll(sendTasks);

                    // Clean up any clients that had errors or closed connections
                    foreach (var clientId in clientsToRemove)
                    {
                        if (_clients.TryRemove(clientId, out var removedClient))
                        {
                            _logger.LogInformation("BroadcastNewPrAsync: Removed dead client {ClientId}", clientId);
                            try
                            {
                                // Cancel the client's operations
                                removedClient.CancellationSource.Cancel();
                                removedClient.CancellationSource.Dispose();

                                // Try to close the socket if it's not already closed
                                if (removedClient.Socket.State != WebSocketState.Closed &&
                                    removedClient.Socket.State != WebSocketState.Aborted)
                                {
                                    await removedClient.Socket.CloseAsync(
                                        WebSocketCloseStatus.NormalClosure,
                                        "Connection closed by server",
                                        CancellationToken.None);
                                }
                            }
                            catch
                            {
                                // Just ignore any errors during cleanup
                            }
                        }
                    }

                    _logger.LogDebug("Completed broadcasting PR #{PrId}. Active clients: {Count}", newPr.id, _clients.Count);
                }
                finally
                {
                    _broadcastLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting PR #{PrId}", newPr.id);
            }
        }

        private async Task SimulateNewPrs()
        {
            int id = 2;
            while (!_disposed)
            {
                await Task.Delay(30000); // Every 30 sec, fake a new PR

                var pr = new PrItem
                {
                    id = id.ToString(),
                    title = $"Review for PR #{id} in /ississippi/repo: Fixing terrible bug.",
                    author = "johndoe",
                    date = DateTime.UtcNow.ToString("o"),
                    repo = "ississippi%2Fpull-request-test-repo",
                };
                await BroadcastNewPrAsync(pr);

                id++;
            }
        }

        // Implement IDisposable to properly clean up resources
        public void Dispose()
        {
            _logger.LogInformation($"Entered dispose-1");
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            _logger.LogInformation($"Entered dispose-2");
            if (_disposed) return;

            if (disposing)
            {
                // Stop the heartbeat timer
                _heartbeatTimer?.Dispose();

                // Clean up all websocket connections
                CleanupWebSockets().GetAwaiter().GetResult();

                // Release the semaphore
                _broadcastLock?.Dispose();
            }

            _disposed = true;
        }

        private async Task CleanupWebSockets()
        {
            try
            {
                _logger.LogInformation("Cleaning up {Count} WebSocket connections on service shutdown", _clients.Count);

                // Create a list of tasks for concurrent closing
                var closeTasks = new List<Task>();

                foreach (var kvp in _clients)
                {
                    var clientId = kvp.Key;
                    var client = kvp.Value;

                    closeTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            // Cancel any pending operations
                            client.CancellationSource.Cancel();

                            // Close the socket if it's still open
                            if (client.Socket.State == WebSocketState.Open)
                            {
                                await client.Socket.CloseAsync(
                                    WebSocketCloseStatus.EndpointUnavailable,
                                    "Service shutting down",
                                    CancellationToken.None);
                            }

                            // Clean up resources
                            client.CancellationSource.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error closing WebSocket client {ClientId} during cleanup", clientId);
                        }
                    }));
                }

                // Wait for all close operations to complete with a timeout
                var completedTask = await Task.WhenAny(
                    Task.WhenAll(closeTasks),
                    Task.Delay(TimeSpan.FromSeconds(5))
                );

                if (completedTask != Task.WhenAll(closeTasks))
                {
                    _logger.LogWarning("WebSocket cleanup timed out after 5 seconds");
                }

                // Clear the clients dictionary
                _clients.Clear();

                _logger.LogInformation("WebSocket connections cleanup completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during WebSocket connections cleanup");
            }
        }

        private async Task<string> GetEcsTaskId()
        {
            var metadataUri = Environment.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI_V4");
            var result = "No Task Id";
            if (!string.IsNullOrEmpty(metadataUri))
            {
                using (var httpClient = new HttpClient())
                {
                    try
                    {
                        // Get task metadata
                        var taskMetadataUri = $"{metadataUri}/task";
                        var taskResponse = await httpClient.GetStringAsync(taskMetadataUri);
                        var taskMetadata = JsonDocument.Parse(taskResponse);
                        var taskArn = taskMetadata.RootElement
                            .GetProperty("TaskARN").GetString();

                        // Parse task ID from ARN
                        var taskId = taskArn?.Split('/').LastOrDefault();
                        result = taskId;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to retrieve ECS metadata: {Message}", ex.Message);
                    }
                }
            }
            return result;
        }
        private async Task LogEcsInformation()
        {
            var metadataUri = Environment.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI_V4");

            if (!string.IsNullOrEmpty(metadataUri))
            {
                _logger.LogInformation("Running in ECS container");

                using (var httpClient = new HttpClient())
                {
                    try
                    {
                        // Get container metadata
                        var containerResponse = await httpClient.GetStringAsync(metadataUri);
                        _logger.LogDebug("Container metadata: {Metadata}", containerResponse);

                        var containerMetadata = JsonDocument.Parse(containerResponse);
                        string containerId = null;

                        // Safely try to get ContainerID
                        if (containerMetadata.RootElement.TryGetProperty("ContainerID", out var containerIdElement))
                        {
                            containerId = containerIdElement.GetString();
                        }
                        else if (containerMetadata.RootElement.TryGetProperty("DockerId", out var dockerIdElement))
                        {
                            containerId = dockerIdElement.GetString();
                        }
                        else
                        {
                            _logger.LogWarning("Container ID not found in metadata");
                        }

                        // Try to get task metadata
                        try
                        {
                            var taskMetadataUri = $"{metadataUri}/task";
                            var taskResponse = await httpClient.GetStringAsync(taskMetadataUri);
                            _logger.LogDebug("Task metadata: {Metadata}", taskResponse);

                            var taskMetadata = JsonDocument.Parse(taskResponse);
                            string taskId = null;

                            // Safely try to get TaskARN
                            if (taskMetadata.RootElement.TryGetProperty("TaskARN", out var taskArnElement))
                            {
                                var taskArn = taskArnElement.GetString();
                                taskId = taskArn?.Split('/').LastOrDefault();
                            }

                            _logger.LogInformation("Container ID: {ContainerId}, Task ID: {TaskId}",
                                containerId ?? "Unknown", taskId ?? "Unknown");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to retrieve task metadata");
                            _logger.LogInformation("Container ID: {ContainerId}", containerId ?? "Unknown");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to retrieve ECS metadata");
                    }
                }
            }
        }
    }
}