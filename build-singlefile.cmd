@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-singlefile.ps1"
set "RESULT=%ERRORLEVEL%"
echo.
pause
exit /b %RESULT%
