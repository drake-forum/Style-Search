@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-Style-Search.ps1"
if errorlevel 1 goto failed
echo.
echo Style Search has been uninstalled.
pause
exit /b 0
:failed
echo.
echo Uninstall failed. Check the message above.
pause
exit /b 1
