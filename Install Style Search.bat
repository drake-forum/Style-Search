@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Style-Search.ps1"
if errorlevel 1 goto failed
echo.
echo Style Search is installed. Open it from the Start Menu or Desktop.
pause
exit /b 0
:failed
echo.
echo Installation failed. Check the message above.
pause
exit /b 1
