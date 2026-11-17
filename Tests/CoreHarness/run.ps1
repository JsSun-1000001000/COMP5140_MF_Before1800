param([string]$UnityEditorRoot = 'C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor')
$ErrorActionPreference = 'Stop'
$testRoot = $PSScriptRoot
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $testRoot '..\..'))
$scripts = Join-Path $projectRoot 'Unity\Assets\_Before1800\Scripts'
$dataRoot = Join-Path $UnityEditorRoot 'Data'
$compiler = Join-Path $dataRoot 'DotNetSdkRoslyn\csc.dll'
$dotnetRuntime = Join-Path $dataRoot 'NetCoreRuntime\dotnet.exe'
$monoRuntime = Join-Path $dataRoot 'MonoBleedingEdge\bin\mono.exe'
$referencesRoot = Join-Path $dataRoot 'MonoBleedingEdge\lib\mono\4.8-api'
$newtonsoft = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Unity\Library\PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json*' | ForEach-Object { Join-Path $_.FullName 'Runtime\Newtonsoft.Json.dll' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$newtonsoft) { throw 'Unity Newtonsoft package must be resolved first.' }
$buildRoot = Join-Path $testRoot 'bin'
New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
$exe = Join-Path $buildRoot 'CoreHarness.exe'
$compilerArgs = @('/nologo','/target:exe','/langversion:9.0','/nostdlib+',('/out:' + $exe))
$references = Get-ChildItem -LiteralPath $referencesRoot -Filter '*.dll'
$facades = Get-ChildItem -LiteralPath (Join-Path $referencesRoot 'Facades') -Filter '*.dll'
$compilerArgs += @($references + $facades | ForEach-Object { '/reference:' + $_.FullName })
$compilerArgs += '/reference:' + $newtonsoft
$compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $scripts 'Core') -Filter '*.cs' | ForEach-Object FullName)
$compilerArgs += Join-Path $scripts 'AI\Contracts.cs'
$compilerArgs += Join-Path $scripts 'AI\DecisionValidator.cs'
$compilerArgs += Join-Path $testRoot 'Program.cs'
$responsePath = Join-Path $buildRoot 'compiler.rsp'
$compilerArgs | ForEach-Object { '"' + $_ + '"' } | Set-Content -LiteralPath $responsePath -Encoding utf8
& $dotnetRuntime $compiler ('@' + $responsePath)
if ($LASTEXITCODE -ne 0) { throw 'Core compilation failed.' }
Copy-Item -LiteralPath $newtonsoft -Destination (Join-Path $buildRoot 'Newtonsoft.Json.dll') -Force
& $monoRuntime $exe
if ($LASTEXITCODE -ne 0) { throw 'Core invariant tests failed.' }
