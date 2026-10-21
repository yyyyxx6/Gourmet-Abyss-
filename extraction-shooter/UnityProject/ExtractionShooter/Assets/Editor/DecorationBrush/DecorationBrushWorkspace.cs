using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Game.Modules.Editor
{
    internal static class DecorationBrushWorkspace
    {
        public const string Root = "Assets/ArtWorkspace";
        public const string Presets = Root + "/Editor/BrushPresets";
        public const string LocalTests = Root + "/LocalTests";

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, path.Substring(separator + 1))))
                throw new InvalidOperationException("无法创建目录：" + path);
        }

        [MenuItem("场景/笔刷工作区/共享预设")]
        public static void ShowPresets() => ShowFolder(Presets);

        [MenuItem("场景/笔刷工作区/本地测试")]
        public static void ShowLocalTests() => ShowFolder(LocalTests);

        static void ShowFolder(string path)
        {
            EnsureFolder(path);
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(path);
            EditorGUIUtility.PingObject(Selection.activeObject);
            EditorUtility.FocusProjectWindow();
        }

        public static void ValidateBuildPaths(IEnumerable<string> paths)
        {
            var forbidden = paths.Where(p => !string.IsNullOrEmpty(p)
                && p.Replace('\\', '/').StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
            if (forbidden.Length > 0)
                throw new BuildFailedException("美术工作区仅用于编辑和测试，以下内容不能进入游戏包。请从打包列表移除测试场景，或将需要发布的资源移到正式目录：\n"
                    + string.Join("\n", forbidden));
        }
    }

    // Check the project's build scene list and its dependencies before any player build work.
    internal sealed class DecorationBrushWorkspaceBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report) => ValidateScenes(EditorBuildSettings.scenes);

        internal static void ValidateScenes(IEnumerable<EditorBuildSettingsScene> buildScenes)
        {
            var scenes = buildScenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            DecorationBrushWorkspace.ValidateBuildPaths(scenes);
            DecorationBrushWorkspace.ValidateBuildPaths(AssetDatabase.GetDependencies(scenes, true));
        }
    }
}
