using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lock = System.Threading.Lock;

namespace ClassicUO.Game.Managers
{
    public sealed class OpilandClientManager : IDisposable
    {
        private static readonly Lazy<OpilandClientManager> _instance = new(() => new OpilandClientManager());
        public static OpilandClientManager Instance => _instance.Value;

        private readonly Lock _clientLock = new();
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _receiveTask;
        private bool _disposed;
        private bool _isChangingState; // Prevent circular references

        public bool IsConnected => _webSocket?.State == WebSocketState.Open;
        public string CurrentAddress { get; private set; }
        public int CurrentPort { get; private set; } = 5055;

        public event EventHandler<ClientStateChangedEventArgs> StateChanged;
        public event EventHandler<ClientMessageReceivedEventArgs> MessageReceived;

        private OpilandClientManager()
        {
            // Subscribe to connection event for autostart
            EventSink.OnConnected += OnGameConnected;
        }

        private void OnGameConnected(object sender, EventArgs e)
        {
            // Try autostart when player connects to game
            _ = TryAutoStartAsync();
        }

        /// <summary>
        /// Attempts to auto-connect the client based on profile settings.
        /// Safe to call multiple times - will only connect if not already connected.
        /// </summary>
        public async Task TryAutoStartAsync()
        {
            if (ProfileManager.CurrentProfile?.OpilandClientAutostart == true && !IsConnected)
            {
                string address = ProfileManager.CurrentProfile.OpilandClientIp;
                if (string.IsNullOrWhiteSpace(address))
                    address = "127.0.0.1";

                int port = 5055;
                if (int.TryParse(ProfileManager.CurrentProfile.OpilandClientPort, out int parsedPort))
                    port = parsedPort;

                string password = ProfileManager.CurrentProfile.OpilandServerPassword ?? "";

                Log.Trace($"Opiland client auto-connecting to {address}:{port}");
                await ConnectAsync(address, port, password);
            }
        }

        public async Task<bool> ConnectAsync(string address, int port, string password = null)
        {
            lock (_clientLock)
            {
                if (IsConnected)
                {
                    Log.Warn("Opiland client is already connected");
                    return false;
                }

                // Prevent circular reference during state change
                if (_isChangingState)
                    return false;
            }

            // Only client OR server can be used at one time
            if (OpilandServerManager.Instance.IsRunning)
            {
                _isChangingState = true;
                try
                {
                    OpilandServerManager.Instance.StopServer();
                }
                finally
                {
                    _isChangingState = false;
                }
            }

            try
            {
                _isChangingState = true;

                _cancellationTokenSource = new CancellationTokenSource();
                _webSocket = new ClientWebSocket();

                string uri = $"ws://{address}:{port}/";
                CurrentAddress = address;
                CurrentPort = port;

                Log.Trace($"Opiland client connecting to {uri}...");

                await _webSocket.ConnectAsync(new Uri(uri), _cancellationTokenSource.Token);

                Log.Trace("Opiland client connected successfully");

                // Start receiving messages on background thread
                _receiveTask = Task.Run(() => ReceiveMessagesAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

                // Notify UI on main thread
                MainThreadQueue.EnqueueAction(() =>
                {
                    StateChanged?.Invoke(this, new ClientStateChangedEventArgs(true, $"Connected to {address}:{port}"));
                });

                // Send authentication if password is provided
                if (!string.IsNullOrWhiteSpace(password))
                {
                    await SendMessageAsync($"AUTH:{password}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to connect Opiland client: {ex}");

                _webSocket?.Dispose();
                _webSocket = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                MainThreadQueue.EnqueueAction(() =>
                {
                    StateChanged?.Invoke(this, new ClientStateChangedEventArgs(false, $"Connection failed: {ex.Message}"));
                });

                return false;
            }
            finally
            {
                _isChangingState = false;
            }
        }

        public void Disconnect()
        {
            lock (_clientLock)
            {
                if (!IsConnected && _webSocket == null)
                {
                    Log.Warn("Opiland client is not connected");
                    return;
                }

                // Prevent circular reference during state change
                if (_isChangingState)
                    return;

                try
                {
                    _isChangingState = true;

                    // Cancel all pending operations
                    _cancellationTokenSource?.Cancel();

                    // Close WebSocket gracefully
                    if (_webSocket?.State == WebSocketState.Open)
                    {
                        try
                        {
                            _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", CancellationToken.None).Wait(TimeSpan.FromSeconds(5));
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"Error closing WebSocket gracefully: {ex.Message}");
                        }
                    }

                    // Wait for receive task to complete (with timeout)
                    _receiveTask?.Wait(TimeSpan.FromSeconds(5));

                    _webSocket?.Dispose();
                    _webSocket = null;

                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;

                    Log.Trace("Opiland client disconnected");

                    // Notify UI on main thread
                    MainThreadQueue.EnqueueAction(() =>
                    {
                        StateChanged?.Invoke(this, new ClientStateChangedEventArgs(false, "Disconnected"));
                    });
                }
                catch (Exception ex)
                {
                    Log.Error($"Error disconnecting Opiland client: {ex}");
                }
                finally
                {
                    _isChangingState = false;
                }
            }
        }

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[4096];

            try
            {
                while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closing connection", CancellationToken.None);
                        
                        MainThreadQueue.EnqueueAction(() =>
                        {
                            StateChanged?.Invoke(this, new ClientStateChangedEventArgs(false, "Server closed connection"));
                        });
                        
                        break;
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Log.Trace($"Opiland client received: {message}");

                        // Notify on main thread
                        MainThreadQueue.EnqueueAction(() =>
                        {
                            MessageReceived?.Invoke(this, new ClientMessageReceivedEventArgs(message));
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during disconnect
            }
            catch (WebSocketException ex)
            {
                Log.Warn($"WebSocket error in Opiland client: {ex.Message}");
                
                MainThreadQueue.EnqueueAction(() =>
                {
                    StateChanged?.Invoke(this, new ClientStateChangedEventArgs(false, $"Connection error: {ex.Message}"));
                });
            }
            catch (Exception ex)
            {
                Log.Error($"Error receiving messages in Opiland client: {ex}");
                
                MainThreadQueue.EnqueueAction(() =>
                {
                    StateChanged?.Invoke(this, new ClientStateChangedEventArgs(false, $"Error: {ex.Message}"));
                });
            }
        }

        public async Task<bool> SendMessageAsync(string message)
        {
            if (!IsConnected)
            {
                Log.Warn("Cannot send message: Opiland client is not connected");
                return false;
            }

            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(buffer),
                    WebSocketMessageType.Text,
                    true,
                    _cancellationTokenSource.Token
                );
                
                Log.Trace($"Opiland client sent: {message}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error sending message in Opiland client: {ex}");
                return false;
            }
        }

        public void SendMessage(string message)
        {
            // Fire and forget - for synchronous contexts
            _ = Task.Run(() => SendMessageAsync(message));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Unsubscribe from events
            EventSink.OnConnected -= OnGameConnected;

            Disconnect();
        }
    }

    #region Event Args

    public class ClientStateChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; }
        public string Message { get; }

        public ClientStateChangedEventArgs(bool isConnected, string message)
        {
            IsConnected = isConnected;
            Message = message;
        }
    }

    public class ClientMessageReceivedEventArgs : EventArgs
    {
        public string Message { get; }

        public ClientMessageReceivedEventArgs(string message)
        {
            Message = message;
        }
    }

    #endregion
}
