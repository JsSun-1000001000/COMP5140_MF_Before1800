param([string]$PythonExe = "python")
$ErrorActionPreference = "Stop"
$serviceRoot = $PSScriptRoot
$venvPython = Join-Path $serviceRoot ".venv\Scripts\python.exe"
$venvDirectory = Join-Path $serviceRoot ".venv"
if (Test-Path -LiteralPath $venvPython) {
    try {
        & $venvPython -c "import sys; assert (3, 12) <= sys.version_info < (3, 15)" 2>$null
        $venvIsValid = $LASTEXITCODE -eq 0
    } catch { $venvIsValid = $false }
    if (-not $venvIsValid) {
        $resolvedVenv = (Resolve-Path -LiteralPath $venvDirectory).Path
        $resolvedService = (Resolve-Path -LiteralPath $serviceRoot).Path
        if ($resolvedVenv -ne (Join-Path $resolvedService '.venv')) { throw 'Unexpected virtual environment path.' }
        $archiveRoot = Join-Path (Split-Path $resolvedService -Parent) 'artifacts'
        New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null
        $archivePath = Join-Path $archiveRoot ('venv-unavailable-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        if (Test-Path -LiteralPath $archivePath) { throw 'Virtual environment archive already exists.' }
        Move-Item -LiteralPath $resolvedVenv -Destination $archivePath
        Write-Host 'The unavailable Python environment was archived; creating a fresh environment.'
    }
}
if (-not (Test-Path -LiteralPath $venvPython)) {
    & $PythonExe -c "import sys; assert (3, 12) <= sys.version_info < (3, 15), 'Python 3.12-3.14 is required'"
    if ($LASTEXITCODE -ne 0) { throw "Python version check failed." }
    & $PythonExe -m venv (Join-Path $serviceRoot ".venv")
    if ($LASTEXITCODE -ne 0) { throw "Virtual environment creation failed." }
}
& $venvPython -m pip install --requirement (Join-Path $serviceRoot "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "Dependency installation failed." }
$localEnv = Join-Path $serviceRoot ".env"
if (-not (Test-Path -LiteralPath $localEnv)) {
    Copy-Item -LiteralPath (Join-Path $serviceRoot ".env.example") -Destination $localEnv
}
Write-Host "Setup complete. Configure service/.env, then run start.ps1. No API key was generated."
