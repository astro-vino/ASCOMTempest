using System;
using System.Windows.Forms;
using System.Reflection;
using System.Diagnostics;
using System.Security.Principal;
using System.IO;

namespace ASCOMTempest
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0)
            {
                string arg = args[0].ToLowerInvariant();
                if (arg.Contains("reg"))
                {
                    if (!IsAdministrator())
                    {
                        RelaunchAsAdmin(args);
                        return;
                    }

                    if (arg == "/register" || arg == "/regserver")
                    {
                        if (RunRegisterExe("register"))
                        {
                            SafetyMonitor.RegUnregASCOM(true);
                            MessageBox.Show("ASCOM Tempest Safety Monitor registered successfully.", "ASCOM Registration", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    else if (arg == "/unregister" || arg == "/unregserver")
                    {
                        if (RunRegisterExe("unregister"))
                        {
                            SafetyMonitor.RegUnregASCOM(false);
                            MessageBox.Show("ASCOM Tempest Safety Monitor unregistered successfully.", "ASCOM Registration", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                }
            }
            else
            {
                using (var setupDialog = new SetupDialogForm())
                {
                    Application.Run(setupDialog);
                }
            }
        }

        private static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static void RelaunchAsAdmin(string[] args)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = Assembly.GetExecutingAssembly().Location,
                    Arguments = string.Join(" ", args),
                    Verb = "runas"
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to relaunch with administrator privileges.\nError: {ex.Message}", "Admin Elevation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool RunRegisterExe(string command)
        {
            string output = "";
            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                string registerExePath = Path.Combine(Path.GetDirectoryName(exePath), "Register.exe");

                if (!File.Exists(registerExePath))
                {
                    MessageBox.Show($"Registration helper not found: {registerExePath}", "Registration Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo(registerExePath, $"{command} \"{exePath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    output = process.StandardOutput.ReadToEnd();
                    output += process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        return true;
                    }
                    else
                    {
                        MessageBox.Show($"Registration failed with exit code {process.ExitCode}.\n\nOutput:\n{output}", "Registration Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred during registration.\nError: {ex.Message}\n\nOutput:\n{output}", "Registration Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }
}
