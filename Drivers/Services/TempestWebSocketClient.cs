using Newtonsoft.Json;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ASCOMTempest.Drivers.Models;

namespace ASCOMTempest.Drivers.Services
{
    /// <summary>
    /// WebSocket client for real-time Tempest weather data
    /// Connects to WeatherFlow's WebSocket API for live observations
    /// </summary>
    public class TempestWebSocketClient : IDisposable
    {
        private const string WEBSOCKET_URL = "wss://ws.weatherflow.com/swd/data?token=";
        private ClientWebSocket webSocket;
        private CancellationTokenSource cancellationTokenSource;
        private Task receiveTask;
        private string accessToken;
        private bool isConnected;

        public event EventHandler<TempestObservation> ObservationReceived;
        public event EventHandler<TempestWindData> RapidWindReceived;
        public event EventHandler<string> ErrorOccurred;
        public event EventHandler<bool> ConnectionStatusChanged;

        public bool IsConnected
        {
            get => isConnected;
            private set
            {
                if (isConnected != value)
                {
                    isConnected = value;
                    ConnectionStatusChanged?.Invoke(this, value);
                }
            }
        }

        public void SetAccessToken(string token)
        {
            accessToken = token;
        }

        /// <summary>
        /// Connect to the Tempest WebSocket API
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(accessToken))
                {
                    Logger.Error("Cannot connect to WebSocket - no access token provided");
                    ErrorOccurred?.Invoke(this, "No access token provided");
                    return false;
                }

                webSocket = new ClientWebSocket();
                
                cancellationTokenSource = new CancellationTokenSource();
                
                await webSocket.ConnectAsync(new Uri(WEBSOCKET_URL + accessToken), cancellationTokenSource.Token);
                
                IsConnected = true;
                receiveTask = ReceiveLoop(cancellationTokenSource.Token);
                
                Logger.Info("Connected to Tempest WebSocket API");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to connect to Tempest WebSocket: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Connection failed: {ex.Message}");
                IsConnected = false;
                return false;
            }
        }

        /// <summary>
        /// Disconnect from the WebSocket
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                IsConnected = false;
                
                if (cancellationTokenSource != null)
                {
                    cancellationTokenSource.Cancel();
                }

                if (webSocket?.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
                }

                if (receiveTask != null)
                {
                    await receiveTask;
                }

                Logger.Info("Disconnected from Tempest WebSocket API");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error disconnecting from WebSocket: {ex.Message}");
            }
        }

        /// <summary>
        /// Start listening for observations from a specific device
        /// </summary>
        public async Task StartListeningToDevice(int deviceId)
        {
            if (!IsConnected)
            {
                Logger.Error("Cannot start listening - not connected to WebSocket");
                return;
            }

            try
            {
                var listenMessage = new
                {
                    type = "listen_start",
                    device_id = deviceId,
                    id = Guid.NewGuid().ToString()
                };

                await SendMessage(listenMessage);
                Logger.Info($"Started listening to device {deviceId}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error starting device listener: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Failed to start listening: {ex.Message}");
            }
        }

        /// <summary>
        /// Start listening for rapid wind data from a specific device
        /// </summary>
        public async Task StartRapidWindListening(int deviceId)
        {
            if (!IsConnected)
            {
                Logger.Error("Cannot start rapid wind listening - not connected to WebSocket");
                return;
            }

            try
            {
                var listenMessage = new
                {
                    type = "listen_rapid_start",
                    device_id = deviceId,
                    id = Guid.NewGuid().ToString()
                };

                await SendMessage(listenMessage);
                Logger.Info($"Started rapid wind listening for device {deviceId}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error starting rapid wind listener: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Failed to start rapid wind listening: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop listening to a specific device
        /// </summary>
        public async Task StopListeningToDevice(int deviceId)
        {
            if (!IsConnected)
                return;

            try
            {
                var stopMessage = new
                {
                    type = "listen_stop",
                    device_id = deviceId,
                    id = Guid.NewGuid().ToString()
                };

                await SendMessage(stopMessage);
                Logger.Info($"Stopped listening to device {deviceId}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error stopping device listener: {ex.Message}");
            }
        }

        private async Task SendMessage(object message)
        {
            var json = JsonConvert.SerializeObject(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            var buffer = new ArraySegment<byte>(bytes);
            
            await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
            Logger.Trace($"Sent WebSocket message: {json}");
        }

        private async Task ReceiveLoop(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            
            while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Logger.Trace($"Received WebSocket message: {message}");
                        
                        await ProcessMessage(message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Logger.Info("WebSocket connection closed by server");
                        IsConnected = false;
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    Logger.Error($"WebSocket error: {ex.Message}");
                    ErrorOccurred?.Invoke(this, $"WebSocket error: {ex.Message}");
                    IsConnected = false;
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in WebSocket receive loop: {ex.Message}");
                    ErrorOccurred?.Invoke(this, $"Receive error: {ex.Message}");
                }
            }
        }

        private Task ProcessMessage(string message)
        {
            try
            {
                var baseMessage = JsonConvert.DeserializeObject<dynamic>(message);
                string messageType = baseMessage?.type;

                switch (messageType)
                {
                    case "ack":
                        Logger.Debug($"WebSocket acknowledgment received: {baseMessage?.id}");
                        break;

                    case "obs_st": // Tempest observation
                            var observation = JsonConvert.DeserializeObject<TempestObservation>(message);
                            if (observation != null)
                            {
                                ObservationReceived?.Invoke(this, observation);
                            }
                            Logger.Debug($"WebSocket weather data received");
                        break;

                    case "rapid_wind": // Rapid wind data
                        var rapidWind = JsonConvert.DeserializeObject<TempestWebSocketRapidWind>(message);
                        var windData = ConvertToWindData(rapidWind);
                        if (windData != null)
                        {
                            RapidWindReceived?.Invoke(this, windData);
                            Logger.Debug($"WebSocket rapid wind: Speed={windData.WindSpeed:F1}m/s, Direction={windData.WindDirection:F0}°");
                        }
                        break;

                    case "evt_precip":
                        Logger.Info("Rain event detected via WebSocket");
                        break;

                    case "evt_strike":
                        Logger.Info("Lightning strike detected via WebSocket");
                        break;

                    default:
                        Logger.Trace($"Unknown WebSocket message type: {messageType}");
                        break;
                }
            }
            catch (JsonException ex)
            {
                Logger.Error($"Failed to parse WebSocket JSON message: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error processing WebSocket message: {ex.Message}");
            }

            return Task.CompletedTask;
        }


        private TempestWindData ConvertToWindData(TempestWebSocketRapidWind rapidWind)
        {
            if (rapidWind?.Observation == null || rapidWind.Observation.Length < 3)
                return null;

            return new TempestWindData
            {
                Timestamp = DateTimeOffset.FromUnixTimeSeconds((long)rapidWind.Observation[0]).DateTime,
                WindSpeed = rapidWind.Observation[1],
                WindDirection = rapidWind.Observation[2]
            };
        }

        public void Dispose()
        {
            DisconnectAsync().Wait(5000);
            webSocket?.Dispose();
            cancellationTokenSource?.Dispose();
        }
    }

    #region WebSocket Message Models


    public class TempestWebSocketRapidWind
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("device_id")]
        public int DeviceId { get; set; }

        [JsonProperty("ob")]
        public double[] Observation { get; set; }
    }

    #endregion WebSocket Message Models
}
