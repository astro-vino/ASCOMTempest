using System;
using System.Diagnostics;

namespace ASCOMTempest
{
    /// <summary>
    /// A simple logger class to replace N.I.N.A.'s logger.
    /// </summary>
    public static class Logger
    {
        static Logger()
        {
            try
            {
                string logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ASCOM.Tempest.log");
                System.Diagnostics.Trace.Listeners.Add(new TextWriterTraceListener(logPath));
                System.Diagnostics.Trace.AutoFlush = true;
                Info("Logger initialized. Logging to: " + logPath);
            }
            catch (Exception ex)
            {
                // If file logging fails, fall back to system debug output.
                System.Diagnostics.Debug.WriteLine("Failed to initialize file logger: " + ex.ToString());
            }
        }

        public static void Trace(string message)
        {
            System.Diagnostics.Trace.WriteLine($"{DateTime.UtcNow:O} [TRACE] {message}");
        }

        public static void Info(string message)
        {
            System.Diagnostics.Trace.WriteLine($"{DateTime.UtcNow:O} [INFO] {message}");
        }

        public static void Debug(string message)
        {
            System.Diagnostics.Trace.WriteLine($"{DateTime.UtcNow:O} [DEBUG] {message}");
        }

        public static void Warning(string message)
        {
            System.Diagnostics.Trace.WriteLine($"{DateTime.UtcNow:O} [WARNING] {message}");
        }

        public static void Error(string message)
        {
            System.Diagnostics.Trace.WriteLine($"{DateTime.UtcNow:O} [ERROR] {message}");
        }

        public static void Error(Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"{DateTime.UtcNow:O} [ERROR] {ex}");
        }
    }
}
