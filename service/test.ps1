$ErrorActionPreference = "Stop"
$venvPython = Join-Path $PSScriptRoot ".venv\Scripts\python.exe"
Push-Location -LiteralPath $PSScriptRoot
try {
    & $venvPython -m pytest -q
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
} finally {
    Pop-Location
}
