param([ValidateRange(1024,65535)][int]$Port = 8011)
$ErrorActionPreference = "Stop"
$venvPython = Join-Path $PSScriptRoot ".venv\Scripts\python.exe"
Write-Warning "EXPLICIT TEST FIXTURE: deterministic responses, not an LLM; not live-LLM acceptance evidence."
Push-Location -LiteralPath $PSScriptRoot
try {
    & $venvPython -m uvicorn tests.fixture_provider:app --host 127.0.0.1 --port $Port --workers 1 --no-access-log
} finally { Pop-Location }
