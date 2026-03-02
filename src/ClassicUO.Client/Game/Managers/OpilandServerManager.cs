using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;
using CUO_API;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lock = System.Threading.Lock;

namespace ClassicUO.Game.Managers
{
    public sealed class OpilandServerManager : IDisposable
    {
        private static readonly Lazy<OpilandServerManager> _instance = new(() => new OpilandServerManager());
        public static OpilandServerManager Instance => _instance.Value;

        private readonly Lock _serverLock = new();
        private HttpListener _httpListener;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _listenerTask;
        private readonly ConcurrentDictionary<string, WebSocket> _clients = new();
        private bool _disposed;
        private bool _isChangingState; // Prevent circular references

        public bool IsRunning { get; private set; }
        public string CurrentAddress { get; private set; }
        public int CurrentPort { get; private set; } = 5055;
        public int ConnectedClientsCount => _clients.Count;

        public event EventHandler<ServerStateChangedEventArgs> StateChanged;
        public event EventHandler<ClientConnectedEventArgs> ClientConnected;
        public event EventHandler<ClientDisconnectedEventArgs> ClientDisconnected;
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public event EventHandler<ChatMessageReceivedEventArgs> ChatMessageReceived;

        private OpilandServerManager()
        {
            // Subscribe to connection event for autostart
            EventSink.OnConnected += OnGameConnected;
        }

        private void OnGameConnected(object sender, EventArgs e)
        {
            // Try autostart when player connects to game
            TryAutoStart();
        }

        /// <summary>
        /// Attempts to auto-start the server based on profile settings.
        /// Safe to call multiple times - will only start if not already running.
        /// </summary>
        public void TryAutoStart()
        {
            if (ProfileManager.CurrentProfile?.OpilandServerAutostart == true && !IsRunning)
            {
                string address = ProfileManager.CurrentProfile.OpilandServerIp;
                if (string.IsNullOrWhiteSpace(address))
                    address = "127.0.0.1";

                int port = 5055;
                if (int.TryParse(ProfileManager.CurrentProfile.OpilandServerPort, out int parsedPort))
                    port = parsedPort;

                Log.Trace($"Opiland server auto-starting on {address}:{port}");
                StartServer(address, port);
            }
        }

        public void StartServer(string address, int port)
        {
            lock (_serverLock)
            {
                if (IsRunning)
                {
                    Log.Warn("Opiland server is already running");
                    return;
                }

                // Prevent circular reference during state change
                if (_isChangingState)
                    return;

                try
                {
                    _isChangingState = true;

                    // Only client OR server can be used at one time
                    if (OpilandClientManager.Instance.IsConnected)
                        OpilandClientManager.Instance.Disconnect();

                    _cancellationTokenSource = new CancellationTokenSource();
                    _httpListener = new HttpListener();

                    // Setup listener
                    string prefix = $"http://{address}:{port}/";
                    _httpListener.Prefixes.Add(prefix);
                    _httpListener.Start();

                    CurrentAddress = address;
                    CurrentPort = port;
                    IsRunning = true;

                    // Start accepting connections on background thread
                    _listenerTask = Task.Run(() => ListenForConnectionsAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

                    Log.Trace($"Opiland WebSocket server started on {prefix}");

                    // Notify UI on main thread
                    MainThreadQueue.EnqueueAction(() =>
                    {
                        StateChanged?.Invoke(this, new ServerStateChangedEventArgs(true, $"Server started on {address}:{port}"));
                    });
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to start Opiland server: {ex}");
                    IsRunning = false;
                    _httpListener?.Stop();
                    _httpListener = null;

                    MainThreadQueue.EnqueueAction(() =>
                    {
                        StateChanged?.Invoke(this, new ServerStateChangedEventArgs(false, $"Failed to start: {ex.Message}"));
                    });
                }
                finally
                {
                    _isChangingState = false;
                }
            }
        }

        public void StopServer()
        {
            lock (_serverLock)
            {
                if (!IsRunning)
                {
                    Log.Warn("Opiland server is not running");
                    return;
                }

                // Prevent circular reference during state change
                if (_isChangingState)
                    return;

                try
                {
                    _isChangingState = true;
                    IsRunning = false;

                    // Cancel all pending operations
                    _cancellationTokenSource?.Cancel();

                    // Close all client connections
                    foreach (var kvp in _clients)
                    {
                        try
                        {
                            kvp.Value?.Abort();
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"Error closing client connection: {ex.Message}");
                        }
                    }
                    _clients.Clear();

                    // Stop listener
                    _httpListener?.Stop();
                    _httpListener?.Close();
                    _httpListener = null;

                    // Wait for listener task to complete (with timeout)
                    _listenerTask?.Wait(TimeSpan.FromSeconds(5));

                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;

                    Log.Trace("Opiland WebSocket server stopped");

                    // Notify UI on main thread
                    MainThreadQueue.EnqueueAction(() =>
                    {
                        StateChanged?.Invoke(this, new ServerStateChangedEventArgs(false, "Server stopped"));
                    });
                }
                catch (Exception ex)
                {
                    Log.Error($"Error stopping Opiland server: {ex}");
                }
                finally
                {
                    _isChangingState = false;
                }
            }
        }

        private async Task ListenForConnectionsAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && IsRunning)
                {
                    try
                    {
                        HttpListenerContext context = await _httpListener.GetContextAsync();

                        if (context.Request.IsWebSocketRequest)
                        {
                            // Handle WebSocket connection on separate task
                            _ = Task.Run(() => HandleWebSocketConnectionAsync(context, cancellationToken), cancellationToken);
                        }
                        else
                        {
                            context.Response.StatusCode = 400;
                            context.Response.Close();
                        }
                    }
                    catch (HttpListenerException ex) when (ex.ErrorCode == 995) // Operation aborted
                    {
                        // Expected when stopping the server
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            Log.Error($"Error accepting connection: {ex}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Fatal error in listener loop: {ex}");
            }
        }

        private async Task HandleWebSocketConnectionAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            WebSocket webSocket = null;
            string clientId = Guid.NewGuid().ToString();

            try
            {
                HttpListenerWebSocketContext webSocketContext = await context.AcceptWebSocketAsync(null);
                webSocket = webSocketContext.WebSocket;

                // Add to clients collection
                _clients.TryAdd(clientId, webSocket);

                string clientAddress = context.Request.RemoteEndPoint?.ToString() ?? "Unknown";
                Log.Trace($"Opiland client connected: {clientId} from {clientAddress}");

                // Notify on main thread
                MainThreadQueue.EnqueueAction(() =>
                {
                    ClientConnected?.Invoke(this, new ClientConnectedEventArgs(clientId, clientAddress));
                });

                // Handle messages from this client
                await HandleClientMessagesAsync(webSocket, clientId, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Error($"Error handling WebSocket connection {clientId}: {ex}");
            }
            finally
            {
                // Remove client
                _clients.TryRemove(clientId, out _);

                // Close WebSocket
                if (webSocket != null && webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Error closing WebSocket {clientId}: {ex.Message}");
                    }
                }

                webSocket?.Dispose();

                Log.Trace($"Opiland client disconnected: {clientId}");

                // Notify on main thread
                MainThreadQueue.EnqueueAction(() =>
                {
                    ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(clientId));
                });
            }
        }

        private async Task HandleClientMessagesAsync(WebSocket webSocket, string clientId, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];

            try
            {
                while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Log.Trace($"Opiland received from {clientId}: {message}");

                        // Notify on main thread
                        MainThreadQueue.EnqueueAction(() =>
                        {
                            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(clientId, message));
                            EventSink.InvokeOnOpilandMessage(this, new OpilandMessageEventArgs(clientId, message));
                        });

                        // Parse and handle specific message types
                        try
                        {
                            OpilandMessage opilandMsg = OpilandMessage.Deserialize(message);

                            if (opilandMsg is ChatMessage chatMsg)
                            {
                                // Broadcast chat message to all clients except the sender
                                _ = Task.Run(async () =>
                                {
                                    await BroadcastMessageExceptAsync(clientId, message);

                                    // Also fire the event on main thread
                                    MainThreadQueue.EnqueueAction(() =>
                                    {
                                        ChatMessageReceived?.Invoke(this, new ChatMessageReceivedEventArgs(clientId, chatMsg));
                                    });
                                });
                            }
                            else if (opilandMsg is MobilePositionMessage positionMessage)
                            {
                                // Broadcast chat message to all clients except the sender
                                _ = Task.Run(async () =>
                                {
                                    await BroadcastMessageExceptAsync(clientId, message);
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"Failed to parse Opiland message from {clientId}: {ex.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (WebSocketException ex)
            {
                Log.Warn($"WebSocket error for client {clientId}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error handling messages for client {clientId}: {ex}");
            }
        }

        public async Task<bool> SendMessageAsync(string clientId, string message)
        {
            if (!_clients.TryGetValue(clientId, out WebSocket webSocket))
                return false;

            if (webSocket.State != WebSocketState.Open)
                return false;

            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(message);
                await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error sending message to client {clientId}: {ex}");
                return false;
            }
        }

        public async Task BroadcastMessageAsync(string message)
        {
            var tasks = new List<Task>();

            foreach (var clientId in _clients.Keys.ToList())
            {
                tasks.Add(SendMessageAsync(clientId, message));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Broadcast message to all clients except the specified one
        /// </summary>
        public async Task BroadcastMessageExceptAsync(string excludeClientId, string message)
        {
            var tasks = new List<Task>();

            foreach (var clientId in _clients.Keys.ToList())
            {
                if (clientId != excludeClientId)
                {
                    tasks.Add(SendMessageAsync(clientId, message));
                }
            }

            await Task.WhenAll(tasks);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Unsubscribe from events
            EventSink.OnConnected -= OnGameConnected;

            StopServer();
        }
    }

    #region Event Args

    public class ServerStateChangedEventArgs : EventArgs
    {
        public bool IsRunning { get; }
        public string Message { get; }

        public ServerStateChangedEventArgs(bool isRunning, string message)
        {
            IsRunning = isRunning;
            Message = message;
        }
    }

    public class ClientConnectedEventArgs : EventArgs
    {
        public string ClientId { get; }
        public string Address { get; }

        public ClientConnectedEventArgs(string clientId, string address)
        {
            ClientId = clientId;
            Address = address;
        }
    }

    public class ClientDisconnectedEventArgs : EventArgs
    {
        public string ClientId { get; }

        public ClientDisconnectedEventArgs(string clientId)
        {
            ClientId = clientId;
        }
    }

    public class MessageReceivedEventArgs : EventArgs
    {
        public string ClientId { get; }
        public string Message { get; }

        public MessageReceivedEventArgs(string clientId, string message)
        {
            ClientId = clientId;
            Message = message;
        }
    }

    #endregion
}
