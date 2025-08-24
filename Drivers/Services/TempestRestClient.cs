using ASCOMTempest.Drivers.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace ASCOMTempest.Drivers.Services
{
    /// <summary>
    /// REST API client for WeatherFlow Tempest API
    /// Handles authentication and station data retrieval
    /// </summary>
    public class TempestRestClient : IDisposable
    {
        private const string BASE_URL = "https://swd.weatherflow.com/swd/rest/";
        private readonly HttpClient httpClient;
        private string accessToken;

        public TempestRestClient()
        {
            // Set modern TLS protocols and a User-Agent header for the API requests.
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11;
            httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(BASE_URL);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36");
        }

        public void SetAccessToken(string token)
        {
            accessToken = token;
            // Tempest API uses query parameter authentication, not Bearer tokens
        }

        public bool IsAuthenticated => !string.IsNullOrEmpty(accessToken);

        /// <summary>
        /// Get all stations for the authenticated user
        /// </summary>
        public async Task<List<TempestStation>> GetStationsAsync()
        {
            try
            {
                if (!IsAuthenticated)
                {
                    Logger.Error("Cannot get stations - no access token provided");
                    return new List<TempestStation>();
                }

                Logger.Debug($"AUTHENTICATED >>>>>: {accessToken}");
                var response = await httpClient.GetAsync($"stations?token={accessToken}");

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Logger.Error("HTTP error getting stations: 404 Not Found. This may indicate an incorrect API endpoint or an invalid access token. Please verify your token is correct in the setup dialog.");
                    }
                    else
                    {
                        Logger.Error($"HTTP error getting stations: Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).");
                    }
                    Logger.Error($"Request URL: {httpClient.BaseAddress}stations?token={accessToken}");
                    return new List<TempestStation>();
                }

                var json = await response.Content.ReadAsStringAsync();
                Logger.Debug($"Stations API response: {json}");
                
                var stationsResponse = JsonConvert.DeserializeObject<TempestStationsResponse>(json);
                
                Logger.Info($"Retrieved {stationsResponse?.Stations?.Count ?? 0} Tempest stations");
                if (stationsResponse?.Stations != null)
                {
                    foreach (var station in stationsResponse.Stations)
                    {
                        Logger.Info($"Station: {station.Name} (ID: {station.StationId})");
                    }
                }
                
                return stationsResponse?.Stations ?? new List<TempestStation>();
            }
            catch (HttpRequestException ex)
            {
                // Catch other potential network errors (e.g., DNS failure, connection refused)
                Logger.Error($"Network error getting stations: {ex.Message}");
                Logger.Error($"Request URL: {httpClient.BaseAddress}stations?token={accessToken}");
                return new List<TempestStation>();
            }
            catch (JsonException ex)
            {
                Logger.Error($"JSON parsing error getting stations: {ex.Message}");
                return new List<TempestStation>();
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected error getting stations: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                return new List<TempestStation>();
            }
        }

        /// <summary>
        /// Get latest observation for a specific station
        /// </summary>
        public async Task<TempestStationObservation> GetLatestObservationAsync(int stationId)
        {
            try
            {
                if (!IsAuthenticated)
                {
                    Logger.Error("Cannot get observation - no access token provided");
                    return null;
                }

                var response = await httpClient.GetAsync($"/observations/station/{stationId}?token={accessToken}");
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync();
                var observation = JsonConvert.DeserializeObject<TempestStationObservation>(json);
                
                Logger.Debug($"Retrieved latest observation for station {stationId}");
                return observation;
            }
            catch (HttpRequestException ex)
            {
                Logger.Error($"HTTP error getting observation for station {stationId}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error getting observation for station {stationId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get latest observation for a specific device
        /// </summary>
        public async Task<TempestDeviceObservation> GetLatestDeviceObservationAsync(int deviceId)
        {
            try
            {
                if (!IsAuthenticated)
                {
                    Logger.Error("Cannot get device observation - no access token provided");
                    return null;
                }

                var response = await httpClient.GetAsync($"/observations/device/{deviceId}?token={accessToken}");
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync();
                var observation = JsonConvert.DeserializeObject<TempestDeviceObservation>(json);
                
                Logger.Debug($"Retrieved latest observation for device {deviceId}");
                return observation;
            }
            catch (HttpRequestException ex)
            {
                Logger.Error($"HTTP error getting observation for device {deviceId}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error getting observation for device {deviceId}: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            httpClient?.Dispose();
        }
    }

    // Data models moved to TempestObservation.cs to avoid duplication
}
