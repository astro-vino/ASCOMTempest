!define DRIVER_NAME "ASCOM Tempest Driver"
!define DRIVER_DLL "ASCOM.Tempest.SafetyMonitor.dll"
!define REGASM_PATH "$WINDIR\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

# Installer properties
Name "${DRIVER_NAME}"
OutFile "ASCOMTempestSetup.exe"
InstallDir "$PROGRAMFILES64\ASCOM\Tempest"
RequestExecutionLevel admin ; Required to write to Program Files and register COM component

# Pages
Page directory
Page instfiles

UninstPage uninstConfirm
UninstPage instfiles

# Installation Section
Section "Install"
  SetOutPath $INSTDIR

  # Specify the relative path to the compiled DLLs
  File "bin\x64\Release\${DRIVER_DLL}"
  File "bin\x64\Release\Newtonsoft.Json.dll"

  # Register the COM component for ASCOM
  ExecWait `"${REGASM_PATH}" /codebase "$INSTDIR\${DRIVER_DLL}"`

  # Write uninstaller information to the registry
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${DRIVER_NAME}" "DisplayName" "${DRIVER_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${DRIVER_NAME}" "UninstallString" "$INSTDIR\uninstall.exe"
  WriteUninstaller "$INSTDIR\uninstall.exe"
SectionEnd

# Uninstallation Section
Section "Uninstall"
  # Unregister the COM component
  ExecWait `"${REGASM_PATH}" /unregister "$INSTDIR\${DRIVER_DLL}"`

  # Remove files and directories
  Delete "$INSTDIR\${DRIVER_DLL}"
  Delete "$INSTDIR\Newtonsoft.Json.dll"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"

  # Remove registry keys
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${DRIVER_NAME}"
SectionEnd
