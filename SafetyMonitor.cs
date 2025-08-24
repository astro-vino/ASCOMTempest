using ASCOM.DeviceInterface;
using ASCOM.Utilities;
using ASCOMTempest.Drivers.Models;
using ASCOMTempest.Drivers.Services;
using System;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading.Tasks;
using ASCOM;

namespace ASCOMTempest
{
    [Guid("E4B3A3F0-4A88-4B5A-9A9B-3A3E3F0E3F0E")]
    [ProgId("ASCOM.Tempest.SafetyMonitor")]
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class SafetyMonitor : ISafetyMonitor
    {
        internal const string DriverId = "ASCOM.Tempest.SafetyMonitor";
        private const string DriverDescription = "ASCOM SafetyMonitor Driver for WeatherFlow Tempest";

        private TempestApiManager apiManager;
        private TempestWeatherData lastWeatherData;

        // ASCOM Profile settings
        internal const string traceStateProfileName = "Trace Level";
        internal const string accessTokenProfileName = "Access Token";
        internal const string connectionModeProfileName = "Connection Mode";
        internal const string windGustThresholdProfileName = "Wind Gust Threshold";

        private bool traceState;
        private string accessToken;
        private TempestConnectionMode connectionMode;
        private double maxWindGustMph;

        public SafetyMonitor()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                Logger.Info($"ASCOM.Tempest.SafetyMonitor driver version {version} starting. BUILD_ID_20250823_A");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error getting driver version: {ex.Message}");
            }

            ReadProfile(); // Load settings from ASCOM profile

        }

        private void ReadProfile()
        {
            using (Profile P = new Profile()) 
            {
                P.DeviceType = "SafetyMonitor";
                traceState = Convert.ToBoolean(P.GetValue(DriverId, traceStateProfileName, string.Empty, "false"));
                accessToken = P.GetValue(DriverId, accessTokenProfileName, string.Empty, string.Empty);
                connectionMode = (TempestConnectionMode)Convert.ToInt32(P.GetValue(DriverId, connectionModeProfileName, string.Empty, "0"));
                maxWindGustMph = Convert.ToDouble(P.GetValue(DriverId, windGustThresholdProfileName, string.Empty, "30.0"));
            }
        }

        private void WriteProfile()
        {
            using (Profile P = new Profile())
            {
                P.DeviceType = "SafetyMonitor";
                P.WriteValue(DriverId, traceStateProfileName, traceState.ToString());
                P.WriteValue(DriverId, accessTokenProfileName, accessToken);
                P.WriteValue(DriverId, connectionModeProfileName, ((int)connectionMode).ToString());
                P.WriteValue(DriverId, windGustThresholdProfileName, maxWindGustMph.ToString());
            }
        }

        [ComRegisterFunction]
        public static void RegisterASCOM(Type t)
        {
            RegUnregASCOM(true);
        }

        [ComUnregisterFunction]
        public static void UnregisterASCOM(Type t)
        {
            RegUnregASCOM(false);
        }

        private static void RegUnregASCOM(bool bRegister)
        {
            using (var P = new Profile())
            {
                P.DeviceType = "SafetyMonitor";
                if (bRegister)
                {
                    try
                    {
                        P.Register(DriverId, DriverDescription);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to register ASCOM profile for {DriverId}: {ex.Message}");
                        // Do not re-throw, to allow registration to continue if possible
                    }
                }
                else
                {
                    try
                    {
                        P.Unregister(DriverId);
                    }
                    catch (Exception ex)
                    {
                        // Log the error but don't re-throw, to allow unregistration to continue
                        Logger.Error($"Failed to unregister ASCOM profile for {DriverId}: {ex.Message}");
                    }
                }
            }
        }

        public System.Collections.ArrayList SupportedActions => new System.Collections.ArrayList();

        public string Action(string actionName, string actionParameters) => throw new ASCOM.MethodNotImplementedException("Action");

        public void CommandBlind(string command, bool raw) => throw new ASCOM.MethodNotImplementedException("CommandBlind");

        public bool CommandBool(string command, bool raw) => throw new ASCOM.MethodNotImplementedException("CommandBool");

        public string CommandString(string command, bool raw) => throw new ASCOM.MethodNotImplementedException("CommandString");

        public void Dispose()
        {
            // Do not dispose the singleton API manager
        }

        public bool Connected
        {
            get => apiManager?.IsRunning ?? false;
            set
            {
                if (value == Connected) return;

                var mre = new System.Threading.ManualResetEventSlim(false);

                if (value)
                {
                    Logger.Info("Connection requested by client.");
                    apiManager = TempestApiManager.Instance;
                    apiManager.WeatherDataReceived -= OnWeatherDataReceived; // Prevent duplicate subscriptions
                    apiManager.WeatherDataReceived += OnWeatherDataReceived;
                    apiManager.SetAccessToken(accessToken);
                    apiManager.ConnectionMode = connectionMode;

                    Task.Run(async () =>
                    {
                        try
                        {
                            await apiManager.StartAsync();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Exception during connection sequence: {ex.Message}");
                        }
                        finally
                        {
                            mre.Set();
                        }
                    });
                }
                else
                {
                    Logger.Info("Disconnection requested by client.");
                    Task.Run(async () =>
                    {
                        try
                        {
                            if (apiManager != null) await apiManager.StopAsync();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Exception during StopAsync: {ex.Message}");
                        }
                        finally
                        {
                            mre.Set();
                        }
                    });
                }
                mre.Wait(TimeSpan.FromSeconds(15)); // Wait for operation to complete, with a timeout
                Logger.Info($"Connection status after operation: {Connected}");
            }
        }

        public string Description => DriverDescription;

        public string DriverInfo => $"Data Source: {apiManager?.ActiveDataSource ?? "None"}, Last Update: {apiManager?.LastDataReceived}";

        public string DriverVersion => "1.0";

        public short InterfaceVersion => 2;

        private void OnWeatherDataReceived(object sender, TempestWeatherData data)
        {
            lastWeatherData = data;
        }

        public string Name => "Tempest Safety Monitor";

        public void SetupDialog()
        {
            using (var f = new SetupDialogForm())
            {
                f.textBoxToken.Text = accessToken;
                f.comboBoxConnection.DataSource = Enum.GetValues(typeof(TempestConnectionMode));
                f.comboBoxConnection.SelectedItem = connectionMode;
                f.chkTrace.Checked = traceState;
                f.numericUpDownWindGust.Value = (decimal)maxWindGustMph;

                if (f.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    accessToken = f.textBoxToken.Text;
                    connectionMode = (TempestConnectionMode)f.comboBoxConnection.SelectedItem;
                    traceState = f.chkTrace.Checked;
                    maxWindGustMph = (double)f.numericUpDownWindGust.Value;
                    WriteProfile();
                }
            }
        }

        public bool IsSafe
        {
            get
            {
                if (!Connected || lastWeatherData == null) return false;

                // Safety checks based on the latest received weather data
                bool isRaining = lastWeatherData.PrecipType > 0;

                // Convert configured MPH to m/s for comparison
                double maxWindGustMs = maxWindGustMph * 0.44704;
                bool isWindy = lastWeatherData.WindGust > maxWindGustMs;

                // Unsafe if it's raining or too windy
                return !isRaining && !isWindy;
            }
        }
    }
}
