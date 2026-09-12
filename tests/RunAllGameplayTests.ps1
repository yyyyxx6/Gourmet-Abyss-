#requires -Version 7.0
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'RunBatch3Tests.ps1')
Write-Output 'PASS all pure gameplay regression tests'
Write-Output 'Real-scene regression entry points: Batch2UnityValidation.Run, Batch3UnityValidation.Run, FinalGameplayValidation.Run'
