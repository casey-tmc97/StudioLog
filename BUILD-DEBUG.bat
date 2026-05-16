@echo off
echo ============================================
echo   StudioLog - Debug Build (with console)
echo ============================================
echo.

powershell -ExecutionPolicy Bypass -File "%~dp0publish.ps1" -Debug

echo.
pause
