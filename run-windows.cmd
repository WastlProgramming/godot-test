@echo off
rem Doppelklick-Start unter Windows; Argumente gehen an run-windows.ps1.
set "PS=powershell"
where pwsh >nul 2>nul && set "PS=pwsh"
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-windows.ps1" %*
if errorlevel 1 pause