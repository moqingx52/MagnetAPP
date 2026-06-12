using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AndreasReitberger.API.Moonraker;
using log4net;

namespace MotorControl
{
    public class KlipperCommunicator : IDisposable
    {
        private MoonrakerClient? _server;
        private readonly ILog _log = LogManager.GetLogger(typeof(KlipperCommunicator));
        private Dictionary<DateTime, string> _websocketMessages;

        // 定义事件用于通知UI层
        public event Action<string>? OnMessageReceived;
        public event Action<string>? OnError;
        public event Action<bool>? OnConnectionStateChanged;
        public event EventHandler? StatusUpdated;

        private readonly string _host;
        private readonly int _port;
        private readonly bool _ssl;

        public bool IsConnected => _server?.WebSocket?.State == WebSocket4Net.WebSocketState.Open;

        public KlipperCommunicator(string host, int port = 7125, bool ssl = false)
        {
            _host = host;
            _port = port;
            _ssl = ssl;
            _websocketMessages = new Dictionary<DateTime, string>();
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                NotifyMessage("Connecting to Klipper server...");

                _server = new MoonrakerClient(
                    serverAddress: _host,
                    api: "",
                    port: _port,
                    isSecure: _ssl
                );

                await _server.CheckOnlineAsync();
                if (!_server.IsOnline)
                {
                    NotifyError("Server is not online");
                    _server.Dispose();
                    return false;
                }

                await _server.RefreshAllAsync();
                await _server.StartListeningAsync();

                RegisterEventHandlers();
                NotifyMessage("Connected successfully!");
                OnConnectionStateChanged?.Invoke(true);

                return true;
            }
            catch (Exception ex)
            {
                NotifyError($"Error connecting: {ex.Message}");
                _log.Error($"Connection error: {ex}");
                return false;
            }
        }

        private void RegisterEventHandlers()
        {
            if (_server == null) return;

            _server.WebSocketConnectionIdChanged += (o, args) =>
            {
                var id = args.ConnectionId;
                if (id != null && id > 0)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await _server.SubscribeAllPrinterObjectStatusAsync(id);
                            NotifyMessage($"Subscribed to printer objects with ID: {id}");
                        }
                        catch (Exception ex)
                        {
                            NotifyError($"Subscription error: {ex.Message}");
                            _log.Error($"Subscription error: {ex}");
                        }
                    });
                }
                else
                {
                    NotifyError("Invalid connection ID received");
                    _log.Error("Invalid connection ID");
                }
            };

            _server.WebSocketMessageReceived += (o, args) =>
            {
                if (!string.IsNullOrEmpty(args.Message))
                {
                    _websocketMessages.Add(DateTime.Now, args.Message);
                    NotifyMessage($"Received: {args.Message}");
                    StatusUpdated?.Invoke(o, args);
                }
            };

            _server.WebSocketDataReceived += (o, args) => { };

            _server.Error += (o, args) =>
            {
                NotifyError("Server error, unhandled exception");
                _log.Error("Server error, unhandled exception");
            };

            _server.ServerWentOffline += (o, args) =>
            {
                NotifyError($"Server went offline: {args}");
                _log.Error($"Server went offline: {args}");
                OnConnectionStateChanged?.Invoke(false);
            };

            _server.WebSocketError += (o, args) =>
            {
                NotifyError($"WebSocket error: {args}");
                _log.Error($"WebSocket error: {args}");
                OnConnectionStateChanged?.Invoke(false);
            };

            _server.WebSocketDisconnected += (o, args) =>
            {
                NotifyError($"WebSocket disconnected: {args}");
                _log.Error($"WebSocket disconnected: {args}");
                OnConnectionStateChanged?.Invoke(false);
            };
        }

        public void Disconnect()
        {
            try
            {
                if (_server != null)
                {
                    _server.Dispose();
                    _server = null;
                    OnConnectionStateChanged?.Invoke(false);
                    NotifyMessage("Disconnected from server.");
                }
            }
            catch (Exception ex)
            {
                NotifyError($"Error disconnecting: {ex.Message}");
                _log.Error($"Disconnection error: {ex}");
            }
        }

        public void ClearMessages()
        {
            _websocketMessages.Clear();
        }

        private void NotifyMessage(string message)
        {
            OnMessageReceived?.Invoke($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            _log.Info(message);
        }

        private void NotifyError(string error)
        {
            OnError?.Invoke($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {error}");
            _log.Error(error);
        }

        public Dictionary<DateTime, string> GetMessageHistory()
        {
            return new Dictionary<DateTime, string>(_websocketMessages);
        }

        public void Dispose()
        {
            _server?.Dispose();
            _server = null;
        }

        public async Task<bool> SendCommandAsync(string command)
        {
            try
            {
                if (_server == null || !IsConnected)
                {
                    NotifyError("Not connected to server");
                    return false;
                }

                await _server.RunGcodeScriptAsync(command);
                NotifyMessage($"Command sent: {command}");
                return true;
            }
            catch (Exception ex)
            {
                NotifyError($"Error sending command: {ex.Message}");
                _log.Error($"Command error: {ex}");
                return false;
            }
        }
    }
}