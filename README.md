# ASCOM SafetyMonitor Driver for WeatherFlow Tempest

This repository contains an ASCOM SafetyMonitor driver for the WeatherFlow Tempest weather station. The driver allows you to connect your Tempest device to ASCOM-compatible astronomy software to monitor weather conditions and trigger safety alerts.

## Features

- **Real-time Weather Data**: Fetches live weather data from your Tempest station via local UDP broadcasts or the WeatherFlow REST API.
- **ASCOM SafetyMonitor Compliance**: Implements the `ISafetyMonitor` interface, reporting safe or unsafe conditions based on user-defined thresholds.
- **Configurable Safety Thresholds**: Set a maximum wind gust speed to determine when conditions become unsafe for imaging.
- **Rain Detection**: Automatically reports an unsafe condition if rain is detected.
- **Flexible Connection Modes**: Choose between local UDP for real-time data or REST API for remote access.

## Requirements

- Windows 10 or later
- ASCOM Platform 6.5 or later
- .NET Framework 4.7.2

## Installation

1.  Download the latest `ASCOMTempestSetup.exe` from the [Releases](https://github.com/YOUR_USERNAME/YOUR_REPOSITORY/releases) page.
2.  Run the installer. It will automatically register the driver with the ASCOM Platform.

## Configuration

1.  Open your ASCOM-compatible software (e.g., N.I.N.A., SGP) and select the **Tempest Safety Monitor** from the list of available SafetyMonitor devices.
2.  Click **Properties** to open the setup dialog.
3.  Enter your WeatherFlow **Access Token**.
4.  Select your preferred **Connection Mode**:
    *   `LocalUdp`: Listens for UDP broadcasts from your Tempest hub on the local network. This is the recommended mode for real-time data.
    *   `RestApi`: Fetches data from the WeatherFlow cloud API. Use this if the driver is not on the same network as your Tempest hub.
5.  Set the **Wind Gust Threshold** in MPH. If the wind gust speed exceeds this value, the driver will report an unsafe condition.
6.  Click **OK** to save the settings.

## Building from Source

To build the driver from the source code, you will need:

- Visual Studio 2022 with the .NET desktop development workload.
- ASCOM Platform 6 Developer Components.
- NSIS (Nullsoft Scriptable Install System) for creating the installer.

1.  Clone the repository.
2.  Open the `ASCOMTempest.sln` file in Visual Studio.
3.  Build the solution in `Release` mode for the `x64` platform.
4.  The compiled `ASCOM.Tempest.SafetyMonitor.dll` and its dependencies (e.g., `Newtonsoft.Json.dll`) will be located in the `bin\x64\Release` directory.
5.  To create the installer, compile the `installer.nsi` script using the NSIS compiler. This will generate the `ASCOMTempestSetup.exe` file.

## Safety Logic

The driver determines the safety status based on the following conditions:

- **IsSafe = `false`** if:
  - Rain is detected (`PrecipType > 0`).
  - The wind gust speed exceeds the configured threshold.
  - The driver is not connected or has not yet received data.

- **IsSafe = `true`** only if it is not raining AND the wind is below the threshold.
