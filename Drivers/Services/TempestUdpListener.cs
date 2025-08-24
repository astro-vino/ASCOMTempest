using ASCOMTempest.Drivers.Models;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Runtime.InteropServices;

namespace ASCOMTempest.Drivers.Services
{
    /// <summary>
    /// UDP listener service for receiving real-time weather data from Tempest weather station
    /// Listens on port 50222 for UDP broadcasts from local Tempest hub
    /// </summary>
    [ComVisible(false)]
    public class TempestUdpListener : BaseINPC, IDisposable
    {
        private const int TEMPEST_UDP_PORT = 50222;
        private UdpClient udpClient;
        private CancellationTokenSource cancellationTokenSource;
        private Task listenerTask;
        private bool isListening;

        public event EventHandler<TempestObservation> ObservationReceived;
        public event EventHandler<TempestWindData> RapidWindReceived;
        public event EventHandler<TempestDeviceStatus> DeviceStatusReceived;
        public event EventHandler<string> ErrorOccurred;

        private TempestWeatherData latestWeatherData;
        private TempestWindData latestWindData;
        private TempestDeviceStatus latestDeviceStatus;
        private DateTime lastDataReceived;

        public TempestWeatherData LatestWeatherData
        {
            get => latestWeatherData;
            private set
            {
                latestWeatherData = value;
                RaisePropertyChanged();
            }
        }

        public TempestWindData LatestWindData
        {
            get => latestWindData;
            private set
            {
                latestWindData = value;
                RaisePropertyChanged();
            }
        }

        public TempestDeviceStatus LatestDeviceStatus
        {
            get => latestDeviceStatus;
            private set
            {
                latestDeviceStatus = value;
                RaisePropertyChanged();
            }
        }

        public DateTime LastDataReceived
        {
            get => lastDataReceived;
            private set
            {
                lastDataReceived = value;
                RaisePropertyChanged();
            }
        }

        public bool IsListening
        {
            get => isListening;
            private set
            {
                isListening = value;
                RaisePropertyChanged();
            }
        }

        public bool IsConnected => IsListening && (DateTime.Now - LastDataReceived).TotalMinutes < 5;

        /// <summary>
        /// Start listening for UDP broadcasts from Tempest weather station
        /// </summary>
        public async Task<bool> StartListening()
        {
            try
            {
                if (IsListening)
                    return true;

                udpClient = new UdpClient();
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, TEMPEST_UDP_PORT));
                cancellationTokenSource = new CancellationTokenSource();
                
                IsListening = true;
                listenerTask = ListenForMessages(cancellationTokenSource.Token);

                Logger.Info("Tempest UDP listener started on port " + TEMPEST_UDP_PORT);
                
                // Wait a short time to ensure the listener task starts properly
                await Task.Delay(100);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to start Tempest UDP listener: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Failed to start UDP listener: {ex.Message}");
                IsListening = false;
                return false;
            }
        }

        /// <summary>
        /// Stop listening for UDP broadcasts
        /// </summary>
        public async Task StopListening()
        {
            try
            {
                IsListening = false;
                
                if (cancellationTokenSource != null)
                {
                    cancellationTokenSource.Cancel();
                }

                if (listenerTask != null)
                {
                    await listenerTask.ConfigureAwait(false);
                }

                udpClient?.Close();
                udpClient?.Dispose();
                udpClient = null;

                Logger.Info("Tempest UDP listener stopped");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error stopping Tempest UDP listener: {ex.Message}");
            }
        }

        /// <summary>
        /// Main listening loop for UDP messages
        /// </summary>
        private async Task ListenForMessages(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && udpClient != null)
            {
                try
                {
                    var result = await udpClient.ReceiveAsync();
                    var message = Encoding.UTF8.GetString(result.Buffer);
                    
                    Logger.Trace($"Received UDP message from {result.RemoteEndPoint}: {message}");
                    
                    await ProcessMessage(message);
                    LastDataReceived = DateTime.Now;
                }
                catch (ObjectDisposedException)
                {
                    // Expected when stopping the listener
                    break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    // Expected when cancelling
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error receiving UDP message: {ex.Message}");
                    ErrorOccurred?.Invoke(this, $"UDP receive error: {ex.Message}");
                    
                    // Wait a bit before retrying to avoid spam
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Process received UDP message and parse weather data
        /// </summary>
        private Task ProcessMessage(string message)
        {
            try
            {
                // Determine message type by parsing the type field
                var baseMessage = JsonConvert.DeserializeObject<dynamic>(message);
                string messageType = baseMessage?.type;

                switch (messageType)
                {
                    case "obs_st": // Tempest observation
                        var observation = JsonConvert.DeserializeObject<TempestObservation>(message);
                        if (observation != null)
                        {
                            LatestWeatherData = observation.GetLatestWeatherData();
                            ObservationReceived?.Invoke(this, observation);
                            if (LatestWeatherData != null) 
                            {
                                Logger.Debug($"Received Tempest observation: Temp={LatestWeatherData.AirTemperature:F1}°C, Humidity={LatestWeatherData.RelativeHumidity:F1}%, Pressure={LatestWeatherData.StationPressure:F1}mb");
                            }
                        }
                        break;

                    case "rapid_wind": // Rapid wind data
                        var rapidWind = JsonConvert.DeserializeObject<TempestRapidWind>(message);
                        var windData = rapidWind?.GetWindData();
                        if (windData != null)
                        {
                            LatestWindData = windData;
                            RapidWindReceived?.Invoke(this, windData);
                            Logger.Debug($"Received rapid wind: Speed={windData.WindSpeed:F1}m/s, Direction={windData.WindDirection:F0}°");
                        }
                        break;

                    case "device_status": // Device status
                        var deviceStatus = JsonConvert.DeserializeObject<TempestDeviceStatus>(message);
                        if (deviceStatus != null)
                        {
                            LatestDeviceStatus = deviceStatus;
                            DeviceStatusReceived?.Invoke(this, deviceStatus);
                            Logger.Debug($"Received device status: Battery={deviceStatus.Voltage:F2}V, RSSI={deviceStatus.Rssi}dBm");
                        }
                        break;

                    case "evt_precip": // Rain start event
                        Logger.Info("Rain event detected");
                        break;

                    case "evt_strike": // Lightning strike event
                        Logger.Info("Lightning strike detected");
                        break;

                    case "hub_status": // Hub status
                        Logger.Debug("Hub status received");
                        break;

                    default:
                        Logger.Trace($"Unknown message type: {messageType}");
                        break;
                }
            }
            catch (JsonException ex)
            {
                Logger.Error($"Failed to parse JSON message: {ex.Message}");
                Logger.Trace($"Problematic message: {message}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error processing message: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            StopListening().Wait(5000);
            cancellationTokenSource?.Dispose();
        }
    }
}
