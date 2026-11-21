param([ValidateRange(1024,65535)][int]$Port = 8000)
$ErrorActionPreference = "Stop"
$venvPython = Join-Path $PSScriptRoot ".venv\Scripts\python.exe"
if (-not (Test-Path -LiteralPath $venvPython)) { throw "Run service/setup.ps1 first." }
Push-Location -LiteralPath $PSScriptRoot
try {
    # Loopback only; a single worker preserves process-local idempotence.
    & $venvPython -m uvicorn app.main:app --host 127.0.0.1 --port $Port --workers 1 --no-access-log
    if ($LASTEXITCODE -ne 0) { throw "Service exited with code $LASTEXITCODE." }
} finally {
    Pop-Location
}
