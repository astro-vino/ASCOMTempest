using ASCOMTempest.Drivers.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ASCOMTempest.Drivers.Services
{
    [ComVisible(false)]
    public class TempestApiManager : BaseINPC, IDisposable
    {
        private static readonly Lazy<TempestApiManager> lazy = new Lazy<TempestApiManager>(() => new TempestApiManager());
        public static TempestApiManager Instance => lazy.Value;

        private readonly SemaphoreSlim connectionLock = new SemaphoreSlim(1, 1);
        private TempestRestClient restClient;
        private TempestWebSocketClient webSocketClient;
        private TempestUdpListener udpListener;
        private Timer summaryRefreshTimer;

        private string accessToken;
        private TempestConnectionMode connectionMode;
        private TempestStation selectedStation;
        private List<TempestStation> availableStations;

        public event EventHandler<TempestWeatherData> WeatherDataReceived;
        public event EventHandler<TempestWindData> RapidWindReceived;
        public event EventHandler<string> ErrorOccurred;
        public event EventHandler<string> StatusChanged;

        public TempestConnectionMode ConnectionMode
        {
            get => connectionMode;
            set
            {
                connectionMode = value;
                _ = Task.Run(async () => await UpdateConnectionMode());
            }
        }

        public double StationElevationMeters { get; set; }

        public event EventHandler SelectedStationChanged;

        public TempestStation SelectedStation
        {
            get => selectedStation;
            set
            {
                selectedStation = value;
                if (selectedStation != null)
                {
                    Logger.Debug($"ApiManager.SelectedStation set to: {selectedStation.Name}, Elevation: {selectedStation.Elevation}");
                }
                _ = Task.Run(async () =>
                {
                    await UpdateSelectedStation();
                    SelectedStationChanged?.Invoke(this, EventArgs.Empty);
                });
            }
        }

        public List<TempestStation> AvailableStations => availableStations ?? new List<TempestStation>();

        public string ActiveDataSource { get; private set; } = "None";

        public bool IsRunning { get; private set; }

        public bool IsApiConnected => webSocketClient?.IsConnected == true || restClient?.IsAuthenticated == true;

        public bool IsUdpListening => udpListener?.IsListening == true;

        public TempestWeatherData LatestWeatherData { get; private set; }
        public TempestObservationSummary LatestWeatherSummary { get; private set; }

        public TempestWindData LatestWindData { get; private set; }

        public DateTime LastDataReceived { get; private set; }

        private TempestApiManager()
        {
            availableStations = new List<TempestStation>();
            connectionMode = TempestConnectionMode.LocalUdpOnly;
            IsRunning = false;

            InitializeClients();
        }

        private void InitializeClients()
        {
            restClient = new TempestRestClient();
            webSocketClient = new TempestWebSocketClient();
            webSocketClient.ObservationReceived += OnApiWeatherDataReceived;
            webSocketClient.RapidWindReceived += OnApiRapidWindReceived;
            webSocketClient.ErrorOccurred += OnApiErrorOccurred;
            webSocketClient.ConnectionStatusChanged += OnWebSocketConnectionChanged;

            udpListener = new TempestUdpListener();
            udpListener.ObservationReceived += OnUdpWeatherDataReceived;
            udpListener.RapidWindReceived += OnUdpRapidWindReceived;
            udpListener.ErrorOccurred += OnUdpErrorOccurred;
        }

        public void SetAccessToken(string token)
        {
            accessToken = token;
            restClient?.SetAccessToken(token);
            webSocketClient?.SetAccessToken(token);

            if (!string.IsNullOrEmpty(token))
            {
                _ = Task.Run(async () => await RefreshStationsAsync());
            }
        }

        public async Task<List<TempestStation>> RefreshStationsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(accessToken))
                {
                    Logger.Warning("Cannot refresh stations - no access token provided");
                    StatusChanged?.Invoke(this, "No access token provided");
                    return new List<TempestStation>();
                }

                StatusChanged?.Invoke(this, "Refreshing stations...");
                availableStations = await restClient.GetStationsAsync();
                foreach (var station in availableStations)
                {
                    Logger.Debug($"Found station: {station.Name}, Elevation: {station.Elevation}");
                }

                if (availableStations.Any())
                {
                    Logger.Info($"Found {availableStations.Count} Tempest stations");
                    StatusChanged?.Invoke(this, $"Found {availableStations.Count} stations");

                    if (selectedStation == null)
                    {
                        selectedStation = availableStations.First();
                    }
                }
                else
                {
                    Logger.Warning("No Tempest stations found for this account");
                    StatusChanged?.Invoke(this, "No stations found");
                }

                return availableStations;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error refreshing stations: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Failed to refresh stations: {ex.Message}");
                return new List<TempestStation>();
            }
        }

        public async Task<bool> StartAsync()
        {
            await connectionLock.WaitAsync();
            try
            {
                if (IsRunning) return true;

                if (connectionMode != TempestConnectionMode.LocalUdpOnly && (selectedStation == null || !availableStations.Any()))
                {
                    await RefreshStationsAsync();
                }

                Logger.Info($"Starting Tempest API Manager in {connectionMode} mode");
                bool success;
                switch (connectionMode)
                {
                    case TempestConnectionMode.LocalUdpOnly:
                        success = await StartUdpOnly();
                        break;
                    case TempestConnectionMode.ApiWithUdpFallback:
                        success = await StartApiWithFallback();
                        break;
                    case TempestConnectionMode.ApiOnly:
                        success = await StartApiOnly();
                        break;
                    default:
                        success = false;
                        break;
                }
                IsRunning = success;
                if (success && connectionMode != TempestConnectionMode.LocalUdpOnly)
                {
                    summaryRefreshTimer = new Timer(async _ => await RefreshObservationSummaryAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
                }
                return success;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error starting Tempest API Manager: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Failed to start: {ex.Message}");
                IsRunning = false;
                return false;
            }
            finally
            {
                connectionLock.Release();
            }
        }

        public async Task StopAsync()
        {
            await connectionLock.WaitAsync();
            try
            {
                if (!IsRunning) return;

                Logger.Info("Stopping Tempest API Manager");

                if (webSocketClient != null)
                {
                    await webSocketClient.DisconnectAsync();
                }

                if (udpListener != null)
                {
                    await udpListener.StopListening();
                }

                summaryRefreshTimer?.Dispose();
                summaryRefreshTimer = null;

                ActiveDataSource = "None";
                IsRunning = false;
                StatusChanged?.Invoke(this, "Stopped");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error stopping Tempest API Manager: {ex.Message}");
            }
            finally
            {
                connectionLock.Release();
            }
        }

        private async Task<bool> StartUdpOnly()
        {
            ActiveDataSource = "Local UDP";
            StatusChanged?.Invoke(this, "Starting UDP listener...");

            var success = await udpListener.StartListening();
            if (success)
            {
                StatusChanged?.Invoke(this, "UDP listener active");
                return true;
            }
            else
            {
                StatusChanged?.Invoke(this, "UDP listener failed");
                return false;
            }
        }

        private async Task<bool> StartApiOnly()
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                ErrorOccurred?.Invoke(this, "API mode requires access token");
                return false;
            }

            if (selectedStation == null)
            {
                await RefreshStationsAsync();
                if (selectedStation == null)
                {
                    ErrorOccurred?.Invoke(this, "No station selected");
                    return false;
                }
            }

            ActiveDataSource = "Tempest API";
            StatusChanged?.Invoke(this, "Connecting to Tempest API...");

            var success = await webSocketClient.ConnectAsync();
            if (success)
            {
                await StartListeningToSelectedStation();
                StatusChanged?.Invoke(this, "API connection active");
                return true;
            }
            else
            {
                StatusChanged?.Invoke(this, "API connection failed");
                return false;
            }
        }

        private async Task<bool> StartApiWithFallback()
        {
            var apiSuccess = false;
            if (!string.IsNullOrEmpty(accessToken) && selectedStation != null)
            {
                apiSuccess = await StartApiOnly();
            }

            var udpSuccess = await udpListener.StartListening();

            if (apiSuccess)
            {
                ActiveDataSource = "Tempest API (UDP Backup)";
                StatusChanged?.Invoke(this, "API active with UDP backup");
            }
            else if (udpSuccess)
            {
                ActiveDataSource = "Local UDP (API Failed)";
                StatusChanged?.Invoke(this, "Using UDP fallback");
            }
            else
            {
                ActiveDataSource = "None";
                StatusChanged?.Invoke(this, "All connections failed");
                return false;
            }

            return true;
        }

        private async Task StartListeningToSelectedStation()
        {
            if (selectedStation?.Devices == null) return;

            foreach (var device in selectedStation.Devices.Where(d => d.DeviceType == "ST"))
            {
                await webSocketClient.StartListeningToDevice(device.DeviceId);
                await webSocketClient.StartRapidWindListening(device.DeviceId);
                Logger.Info($"Started listening to Tempest device {device.DeviceId}");
            }
        }

        private async Task UpdateConnectionMode()
        {
            if (connectionMode != TempestConnectionMode.LocalUdpOnly && string.IsNullOrEmpty(accessToken))
            {
                Logger.Warning("API modes require access token, falling back to UDP only");
                connectionMode = TempestConnectionMode.LocalUdpOnly;
            }

            await StopAsync();
            await StartAsync();
        }

        public async Task RefreshObservationSummaryAsync()
        {
            if (selectedStation == null || restClient == null) return;

            try
            {
                var tempestDevice = selectedStation.Devices?.FirstOrDefault(d => d.DeviceType == "ST");
                if (tempestDevice == null) return;

                var deviceObservation = await restClient.GetLatestDeviceObservationAsync(tempestDevice.DeviceId);
                if (deviceObservation != null)
                {
                    var observation = new TempestObservation
                    {
                        SerialNumber = selectedStation.Devices.FirstOrDefault()?.SerialNumber,
                        HubSerialNumber = selectedStation.Devices.FirstOrDefault(d => d.DeviceType == "HB")?.SerialNumber,
                        Summary = deviceObservation.Summary,
                        Observations = deviceObservation.Observations?.Select(o => o.Cast<double>().ToArray()).ToArray()
                    };
                    OnApiWeatherDataReceived(this, observation);
                    Logger.Debug("Successfully refreshed observation summary via REST.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to refresh observation summary: {ex.Message}");
            }
        }

        private async Task UpdateSelectedStation()
        {
            if (connectionMode == TempestConnectionMode.LocalUdpOnly || selectedStation == null) return;

            if (webSocketClient?.IsConnected != true)
            {
                var connected = await webSocketClient.ConnectAsync();
                if (!connected)
                {
                    Logger.Error("Failed to connect to WebSocket after selecting a station.");
                    StatusChanged?.Invoke(this, "API connection failed");
                    return;
                }
            }

            await StartListeningToSelectedStation();
            await RefreshObservationSummaryAsync();
        }

        #region Event Handlers

        private void OnApiWeatherDataReceived(object sender, TempestObservation observation)
        {
            if (observation == null) return;

            LatestWeatherData = observation.GetLatestWeatherData();
            LatestWeatherSummary = observation.Summary;

            if (LatestWeatherData != null)
            {
                LastDataReceived = DateTime.Now;
                WeatherDataReceived?.Invoke(this, LatestWeatherData);
                Logger.Debug("Weather data and summary received from API");
            }
        }

        private void OnApiRapidWindReceived(object sender, TempestWindData windData)
        {
            LatestWindData = windData;
            LastDataReceived = DateTime.Now;
            RapidWindReceived?.Invoke(this, windData);
            Logger.Debug("Rapid wind data received from API");
        }

        private void OnUdpWeatherDataReceived(object sender, TempestObservation observation)
        {
            if (connectionMode == TempestConnectionMode.LocalUdpOnly ||
                (connectionMode == TempestConnectionMode.ApiWithUdpFallback && !IsApiConnected))
            {
                OnApiWeatherDataReceived(sender, observation);
            }
        }

        private void OnUdpRapidWindReceived(object sender, TempestWindData windData)
        {
            if (connectionMode == TempestConnectionMode.LocalUdpOnly ||
                (connectionMode == TempestConnectionMode.ApiWithUdpFallback && !IsApiConnected))
            {
                LatestWindData = windData;
                LastDataReceived = DateTime.Now;
                RapidWindReceived?.Invoke(this, windData);
                Logger.Debug("Rapid wind data received from UDP");
            }
        }

        private void OnWeatherDataReceived(TempestWeatherData data)
        {
            LastDataReceived = DateTime.Now;
            WeatherDataReceived?.Invoke(this, data);
        }

        private void OnApiErrorOccurred(object sender, string error)
        {
            Logger.Error($"API error: {error}");
            ErrorOccurred?.Invoke(this, $"API: {error}");

            if (connectionMode == TempestConnectionMode.ApiWithUdpFallback)
            {
                ActiveDataSource = "Local UDP (API Error)";
                StatusChanged?.Invoke(this, "Switched to UDP fallback");
            }
        }

        private void OnUdpErrorOccurred(object sender, string error)
        {
            Logger.Error($"UDP error: {error}");
            ErrorOccurred?.Invoke(this, $"UDP: {error}");
        }

        private void OnWebSocketConnectionChanged(object sender, bool isConnected)
        {
            if (isConnected)
            {
                Logger.Info("WebSocket connected");
                if (connectionMode == TempestConnectionMode.ApiWithUdpFallback)
                {
                    ActiveDataSource = "Tempest API (UDP Backup)";
                }
            }
            else
            {
                Logger.Warning("WebSocket disconnected");
                if (connectionMode == TempestConnectionMode.ApiWithUdpFallback)
                {
                    ActiveDataSource = "Local UDP (API Disconnected)";
                    StatusChanged?.Invoke(this, "API disconnected, using UDP");
                }
            }
        }

        #endregion

        public void Dispose()
        {
            StopAsync().Wait(5000);
            restClient?.Dispose();
            webSocketClient?.Dispose();
            udpListener?.Dispose();
        }
    }

    public enum TempestConnectionMode
    {
        LocalUdpOnly,
        ApiWithUdpFallback,
        ApiOnly
    }
}
