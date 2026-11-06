@echo off
setlocal
set "MONITOR_EXE=%~dp0V100Monitor.exe"

if not exist "%MONITOR_EXE%" (
    echo V100Monitor.exe was not found.
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$shell = New-Object -ComObject WScript.Shell;" ^
  "$desktop = [Environment]::GetFolderPath('Desktop');" ^
  "$linkPath = Join-Path $desktop 'GPU Sentinel.lnk';" ^
  "$link = $shell.CreateShortcut($linkPath);" ^
  "$link.TargetPath = '%MONITOR_EXE%';" ^
  "$link.WorkingDirectory = Split-Path -Parent '%MONITOR_EXE%';" ^
  "$link.IconLocation = '%MONITOR_EXE%,0';" ^
  "$link.Description = 'RTX 5070 + Tesla V100 GPU monitor';" ^
  "$link.Save();" ^
  "Write-Host ('Shortcut created: ' + $linkPath)"

if errorlevel 1 (
    echo Failed to create the shortcut.
    pause
    exit /b 1
)

echo Done.
