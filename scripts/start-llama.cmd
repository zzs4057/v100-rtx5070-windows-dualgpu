@echo off
setlocal

if not exist "%~dp0runtime\bin\llama-server.exe" (
    echo llama-server.exe has not been built.
    echo Run BUILD-LLAMA.cmd first.
    pause
    exit /b 1
)

powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0llama-portable-gui.ps1"
endlocal
