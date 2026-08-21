@echo off
setlocal

where pwsh.exe >nul 2>&1
if errorlevel 1 (
    echo PowerShell 7.4 or later is required, but pwsh.exe was not found on PATH.
    exit /b 1
)

pwsh.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Install-Android.ps1" %*
exit /b %ERRORLEVEL%
