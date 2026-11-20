param([ValidateRange(1024,65535)][int]$Port = 8001, [ValidateRange(1024,65535)][int]$FixturePort = 8011)
$ErrorActionPreference = "Stop"
$venvPython = Join-Path $PSScriptRoot ".venv\Scripts\python.exe"
# This script explicitly selects the separate fixture. It never writes .env.
$testVariables = @{
    LLM_BASE_URL = "http://127.0.0.1:$FixturePort/v1"
    LLM_API_KEY = "test-fixture-not-a-secret"
    LLM_MODEL = "deterministic-test-not-llm"
    LLM_PROVIDER = "TEST_FIXTURE"
    LLM_RESPONSE_FORMAT = "json_object"
    LLM_TOKEN_LIMIT_PARAMETER = "max_tokens"
    LOG_DIR = "logs/test-fixture"
}
$previousValues = @{}
foreach ($variableName in $testVariables.Keys) {
    $previousValues[$variableName] = [Environment]::GetEnvironmentVariable($variableName, "Process")
    [Environment]::SetEnvironmentVariable($variableName, $testVariables[$variableName], "Process")
}
Write-Warning "EXPLICIT TEST GATEWAY: provider=TEST_FIXTURE, model=deterministic-test-not-llm. Product .env is unchanged."
Push-Location -LiteralPath $PSScriptRoot
try {
    & $venvPython -m uvicorn app.main:app --host 127.0.0.1 --port $Port --workers 1 --no-access-log
} finally {
    Pop-Location
    foreach ($variableName in $testVariables.Keys) {
        [Environment]::SetEnvironmentVariable($variableName, $previousValues[$variableName], "Process")
    }
}
