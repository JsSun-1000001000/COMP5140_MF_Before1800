@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "LIN_GAME=%~dp0Before1800.exe"
set "LIN_SERVICE=%~dp0service"
if exist "%~dp0play_with_lin.py" (
    set "LIN_GAME=%~dp0..\Builds\Before1800-Clock\Before1800.exe"
    set "LIN_SERVICE=%~dp0."
)
if not "%~1"=="" set "LIN_GAME=%~f1"
set "LIN_LOCAL=%LIN_SERVICE%\.venv\Scripts\python.exe"
set "LIN_BOOT="
if not exist "%LIN_GAME%" goto missing_files
if not exist "%LIN_SERVICE%\play_with_lin.py" goto missing_files
pushd "%LIN_SERVICE%" || goto failed

call :probe "%LIN_LOCAL%" && goto launch
call :probe "%~dp0..\..\service\.venv\Scripts\python.exe" && goto launch
for %%P in (python.exe python3.exe) do (
    if not "%%~$PATH:P"=="" call :probe "%%~$PATH:P" bootstrap && goto launch
)
for %%P in (py.exe) do if not "%%~$PATH:P"=="" (
    for %%V in (-3.12 -3.13 -3.14) do call :probe "%%~$PATH:P" bootstrap %%V && goto launch
)
if not defined LIN_BOOT (
    echo Install Python 3.12, 3.13 or 3.14, then reopen this launcher.
    goto failed_here
)
"%LIN_BOOT%" %LIN_BOOT_VERSION% -m venv "%LIN_SERVICE%\.venv" || goto failed_here
"%LIN_LOCAL%" -m pip install --disable-pip-version-check -r requirements.txt || goto failed_here
call :probe "%LIN_LOCAL%" || goto failed_here

:launch
"%LIN_PY%" %LIN_VERSION% play_with_lin.py --game "%LIN_GAME%"
set "LIN_EXIT=%ERRORLEVEL%"
popd
if not "%LIN_EXIT%"=="0" goto failed
exit /b 0

:probe
if not exist "%~1" exit /b 1
"%~1" %~3 -B -c "import sys; sys.exit(not ((3,12) <= sys.version_info[:2] <= (3,14)))" >nul 2>&1 || exit /b 1
if "%~2"=="bootstrap" if not defined LIN_BOOT (
    set "LIN_BOOT=%~1"
    set "LIN_BOOT_VERSION=%~3"
)
"%~1" %~3 -B -c "import fastapi, httpx, uvicorn, dotenv; from app.schemas import DecisionRequest" >nul 2>&1 || exit /b 1
set "LIN_PY=%~1"
set "LIN_VERSION=%~3"
exit /b 0

:missing_files
echo Supply the path to the Windows player or keep the launcher next to Before1800.exe and the service folder.
goto failed
:failed_here
popd
:failed
pause
exit /b 1
