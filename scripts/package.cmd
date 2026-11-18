@echo off
rem Runs package.ps1 without depending on the machine's PowerShell execution policy.
setlocal
set "SCRIPT=%~dp0package.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
exit /b %ERRORLEVEL%
