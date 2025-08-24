using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ASCOMTempest.Drivers.Models
{
    /// <summary>
    /// Represents a Tempest weather station observation from UDP broadcast
    /// Based on WeatherFlow Tempest UDP API v171
    /// </summary>
    public class TempestObservation
    {
        [JsonProperty("serial_number")]
        public string SerialNumber { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("hub_sn")]
        public string HubSerialNumber { get; set; }

        [JsonProperty("obs")]
        public double[][] Observations { get; set; }

        [JsonProperty("summary")]
        public TempestObservationSummary Summary { get; set; }

        [JsonProperty("firmware_revision")]
        public int FirmwareRevision { get; set; }

        /// <summary>
        /// Gets the latest observation data if available
        /// </summary>
        public TempestWeatherData GetLatestWeatherData()
        {
            if (Observations == null || Observations.Length == 0)
                return null;

            var obs = Observations[0]; // Latest observation
            if (obs.Length < 18)
                return null;

            return new TempestWeatherData
            {
                Timestamp = DateTimeOffset.FromUnixTimeSeconds((long)obs[0]).DateTime,
                WindLull = obs[1],
                WindAvg = obs[2],
                WindGust = obs[3],
                WindDirection = obs[4],
                WindSampleInterval = obs[5],
                StationPressure = obs[6],
                AirTemperature = obs[7],
                RelativeHumidity = obs[8],
                Illuminance = obs[9],
                UV = obs[10],
                SolarRadiation = obs[11],
                PrecipAccumulated = obs[12],
                PrecipType = (int)obs[13],
                LightningStrikeAvgDistance = obs[14],
                LightningStrikeCount = (int)obs[15],
                Battery = obs[16],
                ReportInterval = (int)obs[17]
            };
        }
    }

    /// <summary>
    /// Represents rapid wind data from Tempest station
    /// </summary>
    public class TempestRapidWind
    {
        [JsonProperty("serial_number")]
        public string SerialNumber { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("hub_sn")]
        public string HubSerialNumber { get; set; }

        [JsonProperty("ob")]
        public double[] Observation { get; set; }

        public TempestWindData GetWindData()
        {
            if (Observation == null || Observation.Length < 3)
                return null;

            return new TempestWindData
            {
                Timestamp = DateTimeOffset.FromUnixTimeSeconds((long)Observation[0]).DateTime,
                WindSpeed = Observation[1],
                WindDirection = Observation[2]
            };
        }
    }

    /// <summary>
    /// Represents device status from Tempest station
    /// </summary>
    public class TempestDeviceStatus
    {
        [JsonProperty("serial_number")]
        public string SerialNumber { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("hub_sn")]
        public string HubSerialNumber { get; set; }

        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }

        [JsonProperty("uptime")]
        public int Uptime { get; set; }

        [JsonProperty("voltage")]
        public double Voltage { get; set; }

        [JsonProperty("firmware_revision")]
        public int FirmwareRevision { get; set; }

        [JsonProperty("rssi")]
        public int Rssi { get; set; }

        [JsonProperty("hub_rssi")]
        public int HubRssi { get; set; }

        [JsonProperty("sensor_status")]
        public int SensorStatus { get; set; }

        [JsonProperty("debug")]
        public int Debug { get; set; }

        public DateTime GetTimestamp()
        {
            return DateTimeOffset.FromUnixTimeSeconds(Timestamp).DateTime;
        }
    }

    /// <summary>
    /// Processed weather data from Tempest observation
    /// </summary>
    public class TempestWeatherData
    {
        public DateTime Timestamp { get; set; }
        public double WindLull { get; set; }
        public double WindAvg { get; set; }
        public double WindGust { get; set; }
        public double WindDirection { get; set; }
        public double WindSampleInterval { get; set; }
        public double StationPressure { get; set; }
        public double AirTemperature { get; set; }
        public double RelativeHumidity { get; set; }
        public double Illuminance { get; set; }
        public double UV { get; set; }
        public double SolarRadiation { get; set; }
        public double PrecipAccumulated { get; set; }
        public int PrecipType { get; set; }
        public double LightningStrikeAvgDistance { get; set; }
        public int LightningStrikeCount { get; set; }
        public double Battery { get; set; }
        public int ReportInterval { get; set; }

        /// <summary>
        /// Calculate dew point from temperature and humidity
        /// </summary>
        public double CalculateDewPoint()
        {
            // Magnus formula for dew point calculation
            double a = 17.27;
            double b = 237.7;
            double alpha = ((a * AirTemperature) / (b + AirTemperature)) + Math.Log(RelativeHumidity / 100.0);
            return (b * alpha) / (a - alpha);
        }

        /// <summary>
        /// Convert station pressure to sea level pressure (requires elevation)
        /// </summary>
        public double CalculateSeaLevelPressure(double elevationMeters)
        {
            // Standard atmosphere calculation
            double tempK = AirTemperature + 273.15;
            double factor = Math.Pow(1 - (0.0065 * elevationMeters / tempK), 5.257);
            return StationPressure / factor;
        }
    }

    /// <summary>
    /// Rapid wind data
    /// </summary>
    public class TempestWindData
    {
        public DateTime Timestamp { get; set; }
        public double WindSpeed { get; set; }
        public double WindDirection { get; set; }
    }

    /// <summary>
    /// Tempest station information from API
    /// </summary>
    public class TempestStation
    {
        [JsonProperty("station_id")]
        public int StationId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("public_name")]
        public string PublicName { get; set; }

        [JsonProperty("latitude")]
        public double Latitude { get; set; }

        [JsonProperty("longitude")]
        public double Longitude { get; set; }

        [JsonProperty("timezone")]
        public string Timezone { get; set; }

        [JsonProperty("elevation")]
        public double Elevation { get; set; }

        [JsonProperty("is_public")]
        public bool IsPublic { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("created_epoch")]
        public long CreatedEpoch { get; set; }

        [JsonProperty("last_modified_epoch")]
        public long LastModifiedEpoch { get; set; }

        [JsonProperty("devices")]
        public List<TempestDevice> Devices { get; set; }

        public override string ToString()
        {
            return Name ?? PublicName ?? $"Station {StationId}";
        }
    }

    /// <summary>
    /// Tempest device information
    /// </summary>
    public class TempestDevice
    {
        [JsonProperty("device_id")]
        public int DeviceId { get; set; }

        [JsonProperty("device_type")]
        public string DeviceType { get; set; }

        [JsonProperty("hardware_revision")]
        public string HardwareRevision { get; set; }

        [JsonProperty("firmware_revision")]
        public string FirmwareRevision { get; set; }

        [JsonProperty("serial_number")]
        public string SerialNumber { get; set; }
    }

    /// <summary>
    /// API response wrapper for stations
    /// </summary>
    public class TempestStationsResponse
    {
        [JsonProperty("stations")]
        public List<TempestStation> Stations { get; set; }

        [JsonProperty("status")]
        public TempestApiStatus Status { get; set; }
    }

    /// <summary>
    /// API response wrapper for station observations
    /// </summary>
    public class TempestStationObservation
    {
        [JsonProperty("station_id")]
        public int StationId { get; set; }

        [JsonProperty("obs")]
        public List<List<object>> Observations { get; set; }

        [JsonProperty("status")]
        public TempestApiStatus Status { get; set; }
    }

    /// <summary>
    /// API status information
    /// </summary>
    public class TempestApiStatus
    {
        [JsonProperty("status_code")]
        public int StatusCode { get; set; }

        [JsonProperty("status_message")]
        public string StatusMessage { get; set; }
    }

    /// <summary>
    /// Device observation response from API
    /// </summary>
    public class TempestDeviceObservation
    {
        [JsonProperty("device_id")]
        public int DeviceId { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("summary")]
        public TempestObservationSummary Summary { get; set; }

        [JsonProperty("obs")]
        public List<List<object>> Observations { get; set; }

        [JsonProperty("status")]
        public TempestApiStatus Status { get; set; }
    }

    /// <summary>
    /// Observation summary data
    /// </summary>
    public class TempestObservationSummary
    {
        [JsonProperty("pressure_trend")]
        public string PressureTrend { get; set; }

        [JsonProperty("strike_count_1h")]
        public int StrikeCount1h { get; set; }

        [JsonProperty("strike_count_3h")]
        public int StrikeCount3h { get; set; }

        [JsonProperty("precip_total_1h")]
        public double PrecipTotal1h { get; set; }

        [JsonProperty("strike_last_dist")]
        public double StrikeLastDist { get; set; }

        [JsonProperty("strike_last_epoch")]
        public long StrikeLastEpoch { get; set; }

        [JsonProperty("precip_accum_local_yesterday")]
        public double PrecipAccumLocalYesterday { get; set; }

        [JsonProperty("precip_accum_local_yesterday_final")]
        public double PrecipAccumLocalYesterdayFinal { get; set; }

        [JsonProperty("precip_analysis_type_yesterday")]
        public int PrecipAnalysisTypeYesterday { get; set; }

        [JsonProperty("feels_like")]
        public double FeelsLike { get; set; }

        [JsonProperty("heat_index")]
        public double HeatIndex { get; set; }

        [JsonProperty("wind_chill")]
        public double WindChill { get; set; }

        [JsonProperty("wet_bulb_temperature")]
        public double? WetBulbTemperature { get; set; }

        [JsonProperty("delta_t")]
        public double? DeltaT { get; set; }
    }
}
