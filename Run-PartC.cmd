@echo off
setlocal
cd /d "%~dp0"
if not exist "service\.venv\Scripts\python.exe" (
    echo Run service\setup.ps1 first.
    pause
    exit /b 1
)
"service\.venv\Scripts\python.exe" "service\launch_demo.py"
if errorlevel 1 pause
