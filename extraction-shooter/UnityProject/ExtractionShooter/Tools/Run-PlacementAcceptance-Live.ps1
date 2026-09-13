param([string]$ServerUrl='http://127.0.0.1:8080')
$ErrorActionPreference='Stop'
$placementName=Split-Path (Split-Path $PSScriptRoot -Parent) -Leaf
$placementInstances=Invoke-RestMethod "$ServerUrl/api/instances" -TimeoutSec 10
$placementMatches=@($placementInstances.instances | Where-Object {$_.project -eq $placementName})
if($placementMatches.Count -ne 1){throw 'Expected one connected Unity instance for this project.'}
$placementInstance="$($placementMatches[0].project)@$($placementMatches[0].hash)"
function Invoke-PlacementCode([string]$Code){
    $payload=@{type='execute_code';unity_instance=$placementInstance;params=@{action='execute';code=$Code}} | ConvertTo-Json -Depth 6
    $response=Invoke-RestMethod "$ServerUrl/api/command" -Method Post -ContentType 'application/json' -Body $payload -TimeoutSec 40
    if(!$response.result.success){throw ($response | ConvertTo-Json -Depth 8)}
    $response.result.data.result
}
Invoke-PlacementCode 'if(UnityEditor.EditorApplication.isPlaying)throw new System.InvalidOperationException("Stop Play before acceptance."); for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new System.InvalidOperationException("Save scene edits first."); foreach(var w in UnityEngine.Resources.FindObjectsOfTypeAll<Game.Modules.Editor.PlacementPreviewWindow>())w.Close();return "Ready";'
Write-Host 'Stage 1/2: placement contracts, camera framework and real production scenes.'
& (Join-Path $PSScriptRoot 'Run-CameraAcceptance-Live.ps1') -ServerUrl $ServerUrl
if(!$?){throw 'Camera/placement tests failed.'}
Write-Host 'Stage 2/2: watch restaurant entry, pan, UI, exit and abnormal close.'
try {
    Invoke-PlacementCode 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/UpGround.unity");UnityEditor.EditorApplication.isPlaying=true;return "Starting UpGround";'
    $placementReady=$false
    for($placementAttempt=0;$placementAttempt -lt 30;$placementAttempt++){
        Start-Sleep -Seconds 2
        try {
            $placementReady=Invoke-PlacementCode 'return UnityEditor.EditorApplication.isPlaying && UnityEngine.Object.FindObjectOfType<RestaurantModuleAdapter>()!=null && GourmetAbyss.CameraSystem.CameraService.Active!=null;'
            if($placementReady -eq $true){break}
        } catch { if($placementAttempt -eq 29){throw} }
    }
    if($placementReady -ne $true){throw 'UpGround did not become ready.'}
    & (Join-Path $PSScriptRoot 'Run-ModuleAcceptance-Live.ps1') -ServerUrl $ServerUrl
    if(!$?){throw 'Restaurant acceptance failed.'}
    Write-Host 'PASS. Reports: Library/CameraAcceptanceResults and Library/ModuleAcceptance. Game remains in town for manual checking.'
} catch {
    try {Invoke-PlacementCode 'Game.Modules.Editor.ModuleAcceptanceWindow.StopAndRestore();return "Restored";'} catch {}
    throw
}
