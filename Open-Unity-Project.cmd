@echo off
if not exist "C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" (
  echo Install Unity 6000.3.25f1 with Unity Hub, then add the Unity folder beside this file.
  pause
  exit /b 1
)
start "" "C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -projectPath "%~dp0Unity"
