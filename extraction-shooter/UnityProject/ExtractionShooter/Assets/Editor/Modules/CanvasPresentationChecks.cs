using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Modules.Editor
{
    /// <summary>
    /// Checks the common failure points that make a screen UI disappear after a
    /// camera or scene transition change. It reports only; it does not alter UI.
    /// </summary>
    public static class CanvasPresentationChecks
    {
        [MenuItem("场景/验证 UI 显示配置", false, 250)]
        public static void ValidateActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("UI 校验失败：当前没有有效场景。");
                return;
            }

            int checkedCount = 0;
            int errorCount = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                {
                    if (!canvas.isActiveAndEnabled || !canvas.gameObject.activeInHierarchy)
                        continue;

                    checkedCount++;
                    var scale = canvas.transform.localScale;
                    if (Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f) || Mathf.Approximately(scale.z, 0f))
                    {
                        Debug.LogError($"UI 校验：Canvas「{canvas.name}」根节点缩放为 0，UI 不可见。", canvas);
                        errorCount++;
                    }

                    if (canvas.renderMode != RenderMode.ScreenSpaceCamera)
                        continue;

                    if (canvas.worldCamera == null)
                    {
                        Debug.LogError($"UI 校验：Canvas「{canvas.name}」使用 Screen Space - Camera，但没有绑定相机。", canvas);
                        errorCount++;
                    }

                    if (canvas.planeDistance <= 0f)
                    {
                        Debug.LogError($"UI 校验：Canvas「{canvas.name}」的 Plane Distance 为 {canvas.planeDistance}，应为正数。", canvas);
                        errorCount++;
                    }
                }
            }

            if (errorCount == 0)
                Debug.Log($"UI 校验通过：已检查 {checkedCount} 个启用中的 Canvas。");
            else
                Debug.LogError($"UI 校验完成：检查 {checkedCount} 个 Canvas，发现 {errorCount} 项问题。");
        }
    }
}
