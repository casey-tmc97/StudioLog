@echo off
echo ============================================
echo   StudioLog - Quick Build
echo ============================================
echo.

REM Release build
powershell -ExecutionPolicy Bypass -File "%~dp0publish.ps1"

echo.
pause
