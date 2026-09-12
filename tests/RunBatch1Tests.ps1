#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$taskRepoRoot = Split-Path -Parent $PSScriptRoot
$taskScripts = Join-Path $taskRepoRoot 'extraction-shooter/UnityProject/ExtractionShooter/Assets/Scripts'
$taskSources = @(
    (Join-Path $taskScripts 'Manager/ResourceType.cs'),
    (Join-Path $taskScripts 'Pet/PetType.cs'),
    (Join-Path $taskScripts 'Bag/ResourceStorageRules.cs'),
    (Join-Path $taskScripts 'Bag/InventorySnapshot.cs'),
    (Join-Path $taskScripts 'Bag/InventoryPacking.cs'),
    (Join-Path $taskScripts 'Bag/RunIngredientStore.cs'),
    (Join-Path $taskScripts 'Bag/RunSessionData.cs'),
    (Join-Path $PSScriptRoot 'InventoryPackingTests.cs'),
    (Join-Path $PSScriptRoot 'RunSessionDataTests.cs'),
    (Join-Path $PSScriptRoot 'RunResourceTests.cs')
)
Add-Type -Path $taskSources
[InventoryPackingTests]::Run()
Write-Output 'PASS InventoryPackingTests (including 128 conservation cases)'
[RunSessionDataTests]::Run()
Write-Output 'PASS RunSessionDataTests'
[RunResourceTests]::Run()
Write-Output 'PASS RunResourceTests'
