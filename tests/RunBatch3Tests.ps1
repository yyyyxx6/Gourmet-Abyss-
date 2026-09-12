#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$taskRepoRoot = Split-Path -Parent $PSScriptRoot
$taskScripts = Join-Path $taskRepoRoot 'extraction-shooter/UnityProject/ExtractionShooter/Assets/Scripts'
$taskTypes = @(
    'ResourceType', 'ResourceKind', 'PetType', 'RunResourceCategory', 'ResourceStorageRules',
    'InventorySlotSnapshot', 'InventorySnapshot', 'InventoryPackResult', 'InventoryPacking',
    'RunIngredientStore', 'RunSessionData', 'RunResultSnapshot',
    'RunEndReason', 'RunEndPhase', 'RunEndFlow', 'DeathDropResult', 'DeathDropCalculator',
    'InventoryPackingTests', 'RunSessionDataTests', 'RunResourceTests',
    'RunEndFlowTests', 'DeathDropCalculatorTests'
)
$taskLoadedTypes = @($taskTypes | Where-Object { $null -ne ($_ -as [type]) }).Count
if ($taskLoadedTypes -ne 0 -and $taskLoadedTypes -ne $taskTypes.Count) {
    # Add-Type cannot reliably reference these previously generated in-memory assemblies.
    # Keep all sources in one compilation by retrying without inherited types or profiles.
    $taskPowerShell = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
    Write-Output 'Some batch 3 types are already loaded; running in a fresh PowerShell 7 process.'
    & $taskPowerShell -NoLogo -NoProfile -NonInteractive -File $PSCommandPath
    if ($LASTEXITCODE -ne 0) {
        throw "Batch 3 tests failed in the fresh PowerShell 7 process (exit code $LASTEXITCODE)."
    }
    return
}
if ($taskLoadedTypes -eq 0) {
    $taskSources = [System.Collections.Generic.List[string]]::new()
    foreach ($taskRelativePath in @(
        'Manager/ResourceType.cs', 'Pet/PetType.cs', 'Bag/ResourceStorageRules.cs',
        'Bag/InventorySnapshot.cs', 'Bag/InventoryPacking.cs', 'Bag/RunIngredientStore.cs',
        'Bag/RunSessionData.cs', 'Bag/RunEndFlow.cs', 'Bag/DeathDropCalculator.cs'
    )) {
        $taskSources.Add((Join-Path $taskScripts $taskRelativePath))
    }
    foreach ($taskTestPath in @(
        'InventoryPackingTests.cs', 'RunSessionDataTests.cs', 'RunResourceTests.cs',
        'RunEndFlowTests.cs', 'DeathDropCalculatorTests.cs'
    )) {
        $taskSources.Add((Join-Path $PSScriptRoot $taskTestPath))
    }
    Add-Type -Path $taskSources.ToArray()
}
[InventoryPackingTests]::Run()
Write-Output 'PASS InventoryPackingTests (including 128 conservation cases)'
[RunSessionDataTests]::Run()
Write-Output 'PASS RunSessionDataTests'
[RunResourceTests]::Run()
Write-Output 'PASS RunResourceTests'
[RunEndFlowTests]::Run()
Write-Output 'PASS RunEndFlowTests'
[DeathDropCalculatorTests]::Run()
Write-Output 'PASS DeathDropCalculatorTests (including 128 conservation cases)'
