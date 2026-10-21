param([string]$UnityEditorPath = 'E:\Unity\2022.3.62f2c1\Editor')
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskScripts = Join-Path $taskRepo 'extraction-shooter/UnityProject/ExtractionShooter/Assets/Scripts'
$taskMono = Join-Path $UnityEditorPath 'Data/MonoBleedingEdge/bin/mono.exe'
$taskCompiler = Join-Path $UnityEditorPath 'Data/MonoBleedingEdge/lib/mono/4.5/csc.exe'
if (!(Test-Path -LiteralPath $taskMono) -or !(Test-Path -LiteralPath $taskCompiler)) { throw 'Pass the installed Unity Editor directory using -UnityEditorPath.' }
$taskOutput = Join-Path (Split-Path -Parent $taskRepo) '.validation/RestaurantStage1Pure'
[void](New-Item -ItemType Directory -Path $taskOutput -Force)
$taskAssembly = Join-Path $taskOutput 'tests.exe'
$taskSources = @(
    (Join-Path $taskScripts 'Manager/ResourceType.cs'),
    (Join-Path $taskScripts 'Bag/ResourceStorageRules.cs'),
    (Join-Path $taskScripts 'Restaurant/RestaurantIngredientStore.cs'),
    (Join-Path $PSScriptRoot 'RestaurantIngredientStoreTests.cs'),
    (Join-Path $PSScriptRoot 'RestaurantIngredientStoreRunner.cs')
)
& $taskMono $taskCompiler /nologo /target:exe "/out:$taskAssembly" $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Restaurant pure test compilation failed.' }
& $taskMono $taskAssembly
if ($LASTEXITCODE -ne 0) { throw 'Restaurant pure tests failed.' }
