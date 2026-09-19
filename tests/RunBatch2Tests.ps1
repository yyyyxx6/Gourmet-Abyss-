#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$taskRepoRoot = Split-Path -Parent $PSScriptRoot
$taskScripts = Join-Path $taskRepoRoot 'extraction-shooter/UnityProject/ExtractionShooter/Assets/Scripts'
$taskSources = [System.Collections.Generic.List[string]]::new()
$taskBatch1Types = @(
    'ResourceType', 'PetType', 'ResourceStorageRules', 'InventorySnapshot',
    'InventoryPacking', 'RunIngredientStore', 'RunSessionData',
    'InventoryPackingTests', 'RunSessionDataTests', 'RunResourceTests'
)
$taskLoadedBatch1 = @($taskBatch1Types | Where-Object { $null -ne ($_ -as [type]) }).Count
if ($taskLoadedBatch1 -ne 0 -and $taskLoadedBatch1 -ne $taskBatch1Types.Count) {
    throw 'Only part of the batch 1 types are already loaded. Run this script in a fresh PowerShell 7 process.'
}
if ($taskLoadedBatch1 -eq 0) {
    foreach ($taskRelativePath in @(
        'Manager/ResourceType.cs', 'Pet/PetType.cs', 'Bag/ResourceStorageRules.cs',
        'Bag/InventorySnapshot.cs', 'Bag/InventoryPacking.cs', 'Bag/RunIngredientStore.cs',
        'Bag/RunSessionData.cs'
    )) {
        $taskSources.Add((Join-Path $taskScripts $taskRelativePath))
    }
    foreach ($taskTestPath in @('InventoryPackingTests.cs', 'RunSessionDataTests.cs', 'RunResourceTests.cs')) {
        $taskSources.Add((Join-Path $PSScriptRoot $taskTestPath))
    }
}
$taskFlowTypes = @('RunEndFlow', 'RunEndFlowTests')
$taskLoadedFlow = @($taskFlowTypes | Where-Object { $null -ne ($_ -as [type]) }).Count
if ($taskLoadedFlow -ne 0 -and $taskLoadedFlow -ne $taskFlowTypes.Count) {
    throw 'Only part of the run-end test types are already loaded. Run this script in a fresh PowerShell 7 process.'
}
if ($taskLoadedFlow -eq 0) {
    $taskSources.Add((Join-Path $taskScripts 'Bag/RunEndFlow.cs'))
    $taskSources.Add((Join-Path $PSScriptRoot 'RunEndFlowTests.cs'))
}
if ($taskSources.Count -gt 0) {
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
