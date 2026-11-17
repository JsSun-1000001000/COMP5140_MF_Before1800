@echo off
cd /d "%~dp0"
"service\.venv\Scripts\python.exe" "tools\run_prototype.py" --test
if errorlevel 1 (
  echo If Python is unavailable, run .\service\setup.ps1 in PowerShell, then retry.
  pause
)
