using Game.Core;
using System.Collections;
using System.Collections.Generic;
using TransitionsPlus;
using UnityEngine;

public class KeepMainCamera : MonoSingleton<KeepMainCamera>
{
    // 每个场景各一份，后加载的接管——沿用原来的裸赋值语义。
    protected override DuplicatePolicy Duplicate => DuplicatePolicy.OverwriteReference;

    /// <summary>兼容旧调用点的别名，等价于 Instance。</summary>
    public static KeepMainCamera instance => Instance;

    public TransitionAnimator transitionAnimator;
    public Canvas mainUICanvas;
    private const float DefaultScreenSpacePlaneDistance = 1f;

    private void OnEnable()
    {
        RefreshBindings();
    }

    public void tKeepMainCamera()
    {
        RefreshBindings();
    }

    private void RefreshBindings()
    {
        var mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("KeepMainCamera: 当前没有可用的 MainCamera，暂时跳过 UI 相机绑定。", this);
            return;
        }

        if (transitionAnimator != null)
        {
            transitionAnimator.mainCamera = mainCamera;
        }

        if (mainUICanvas == null || mainUICanvas.renderMode == UnityEngine.RenderMode.ScreenSpaceOverlay)
        {
            return;
        }

        mainUICanvas.worldCamera = mainCamera;
        if (mainUICanvas.renderMode == UnityEngine.RenderMode.ScreenSpaceCamera && mainUICanvas.planeDistance <= 0f)
        {
            mainUICanvas.planeDistance = DefaultScreenSpacePlaneDistance;
        }
    }
}
