using System.Collections.Generic;
using System.Linq;
using Game.Modules;
using GourmetAbyss.CameraSystem;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Game.Modules.Editor
{
    [Overlay(typeof(SceneView), "场景镜头", true)]
    public sealed class SceneViewCameraWorkbenchOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.minWidth = 300;
            root.Add(new IMGUIContainer(SceneViewCameraWorkbench.DrawPanel));
            return root;
        }
    }

    /// <summary>
    /// Scene 视图只保留三个入口：自由观察、二维布局、游戏镜头。
    /// 游戏镜头优先直接读取运行中的 Camera；编辑态直接读取 PlanarPerspectiveView.Pose，
    /// 避免编辑器和运行时各自推算一套结果。
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneViewCameraWorkbench
    {
        enum ViewMode
        {
            Free,
            Layout2D,
            GameCamera
        }

        const string PreferencePrefix = "Game.Modules.SceneCamera.V2.";
        const string StandardPath = "Assets/Modules/Shared/WorldViewStandard.asset";

        static readonly Dictionary<Transform, Quaternion> PreviewRotations = new Dictionary<Transform, Quaternion>();
        static ViewMode mode;
        static float elevation;
        static float fieldOfView;
        static float distance;
        static float layoutSize;
        static Vector3 pivot;
        static Vector2 compositionOffset;
        static bool customParameters;
        static bool runtimeOverride;
        static bool helperFoldout;
        static bool saveFoldout;
        static bool previewFacing;
        static bool showMarkers;
        static bool showNames;
        static bool showGameFrame;
        static bool selectedModuleOnly;
        static RuntimeTuningShot runtimeShot;
        static CameraShotLease runtimeLease;
        static CameraDirector runtimeDirector;
        static string status;

        sealed class RuntimeTuningShot : ScriptableObject, ICameraShotSource
        {
            public bool TryEvaluate(in CameraEvaluationContext context, out CameraShotResult result)
            {
                result = default;
                if (!runtimeOverride || !TryBuildCustomPose(out CameraPose pose, out CameraPlane plane))
                    return false;
                result = new CameraShotResult(pose, new CameraDamping(0f, 0f, 0f), plane,
                    CameraShotPolicy.UseUnscaledTime);
                return true;
            }
        }

        static SceneViewCameraWorkbench()
        {
            // 每次脚本重载和进入编辑器都回到 Unity 原生自由视图。
            mode = ViewMode.Free;
            elevation = EditorPrefs.GetFloat(PreferencePrefix + "Elevation", 45f);
            fieldOfView = EditorPrefs.GetFloat(PreferencePrefix + "FieldOfView", 40f);
            distance = EditorPrefs.GetFloat(PreferencePrefix + "Distance", 40f);
            layoutSize = EditorPrefs.GetFloat(PreferencePrefix + "LayoutSize", 18f);
            previewFacing = EditorPrefs.GetBool(PreferencePrefix + "PreviewFacing", false);
            showMarkers = EditorPrefs.GetBool(PreferencePrefix + "ShowMarkers", false);
            showNames = EditorPrefs.GetBool(PreferencePrefix + "ShowNames", false);
            showGameFrame = EditorPrefs.GetBool(PreferencePrefix + "ShowGameFrame", true);
            selectedModuleOnly = EditorPrefs.GetBool(PreferencePrefix + "SelectedModuleOnly", true);

            SceneView.duringSceneGui += DuringSceneGui;
            Camera.onPreCull += BeginBuiltInCamera;
            Camera.onPostRender += EndBuiltInCamera;
            RenderPipelineManager.beginCameraRendering += BeginScriptableRenderPipelineCamera;
            RenderPipelineManager.endCameraRendering += EndScriptableRenderPipelineCamera;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.update += UpdateRuntimeOverride;
            EditorApplication.playModeStateChanged += _ =>
            {
                StopRuntimeOverride();
                RestorePreviewRotations();
                SceneView.RepaintAll();
            };
        }

        internal static void DrawPanel()
        {
            EditorGUILayout.LabelField(Application.isPlaying ? "运行模式" : "编辑模式", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("选择一种工作视图。自由视图不会接管 Scene 相机。", EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawModeButton(ViewMode.Free, "自由视图");
                DrawModeButton(ViewMode.Layout2D, "2D 布局");
                DrawModeButton(ViewMode.GameCamera, "游戏镜头");
            }

            switch (mode)
            {
                case ViewMode.Free:
                    EditorGUILayout.HelpBox("使用 Unity 原生的移动、旋转和缩放，不自动修改 Scene 视角。", MessageType.None);
                    break;
                case ViewMode.Layout2D:
                    DrawLayoutControls();
                    break;
                case ViewMode.GameCamera:
                    DrawGameCameraControls();
                    break;
            }

            DrawRuntimeControls();

            helperFoldout = EditorGUILayout.Foldout(helperFoldout, "辅助标记", true);
            if (helperFoldout) DrawHelperControls();

            if (!Application.isPlaying)
            {
                saveFoldout = EditorGUILayout.Foldout(saveFoldout, "保存正式配置", true);
                if (saveFoldout) DrawSaveControls();
            }

            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
        }

        static void DrawModeButton(ViewMode target, string label)
        {
            bool pressed = GUILayout.Toggle(mode == target, label, "Button");
            if (!pressed || mode == target) return;
            mode = target;
            customParameters = false;
            if (target == ViewMode.GameCamera)
                LoadOfficialCamera();
            else if (target == ViewMode.Layout2D && ResolveFrame() != null)
                pivot = ResolveFrame().position;
            SavePreferences();
            ApplyToSceneView(SceneView.lastActiveSceneView);
        }

        static void DrawLayoutControls()
        {
            EditorGUI.BeginChangeCheck();
            layoutSize = EditorGUILayout.Slider(new GUIContent("可视范围", "二维布局模式的缩放范围。"), layoutSize, 1f, 100f);
            if (EditorGUI.EndChangeCheck())
            {
                SavePreferences();
                ApplyToSceneView(SceneView.lastActiveSceneView);
            }
            DrawFocusButtons();
        }

        static void DrawGameCameraControls()
        {
            string source = Application.isPlaying && RuntimeCamera() != null && !runtimeOverride
                ? "当前运行镜头"
                : ResolveView() != null ? "选中模块的正式镜头" : "未找到镜头来源";
            EditorGUILayout.LabelField("镜头来源", source);
            Camera sceneCamera = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
            if (sceneCamera != null)
                EditorGUILayout.LabelField("画幅同步", $"Game {GameAspect():0.00}:1 · Scene {sceneCamera.aspect:0.00}:1（已自动换算）");
            EditorGUILayout.HelpBox("验收时请对照 Scene 视图中的青色 Game 画幅与右侧 Game 窗口。Unity 选中 Camera 的浮动 Camera Preview 可能显示的是组件预览，不代表最终 Game 输出。", MessageType.None);

            EditorGUI.BeginChangeCheck();
            elevation = EditorGUILayout.Slider(new GUIContent("俯角", "90° 为正俯视。"), elevation, 15f, 75f);
            fieldOfView = EditorGUILayout.Slider("FOV", fieldOfView, 15f, 70f);
            distance = EditorGUILayout.Slider("距离", distance, 1f, 100f);
            compositionOffset = EditorGUILayout.Vector2Field(new GUIContent("构图偏移", "X 为横向，Y 为纵深方向。"), compositionOffset);
            if (EditorGUI.EndChangeCheck())
            {
                customParameters = true;
                // 运行中调整参数时直接同步到实际 Game 相机，避免 Scene 视图改了而 Game 画面不动。
                if (Application.isPlaying && !runtimeOverride)
                    StartRuntimeOverride();
                SavePreferences();
                ApplyToSceneView(SceneView.lastActiveSceneView);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("恢复正式镜头")) LoadOfficialCamera();
                if (GUILayout.Button("对准选中")) FocusSelection();
                if (GUILayout.Button("对准模块")) FocusModule();
            }
        }

        static void DrawFocusButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("对准选中")) FocusSelection();
                if (GUILayout.Button("对准模块")) FocusModule();
            }
        }

        static void DrawRuntimeControls()
        {
            if (!Application.isPlaying) return;
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Game 镜头临时调试", EditorStyles.boldLabel);
            if (mode != ViewMode.GameCamera)
            {
                EditorGUILayout.HelpBox("切换到“游戏镜头”后可以调整并临时应用到 Game 画面。", MessageType.Info);
                return;
            }
            if (runtimeOverride)
            {
                EditorGUILayout.HelpBox("面板参数正在实时驱动 Game 镜头，仅本次运行有效。停止播放或关闭接管后会恢复游戏自己的镜头。", MessageType.Warning);
                if (GUILayout.Button("停止接管并恢复游戏镜头")) StopRuntimeOverride();
            }
            else
            {
                EditorGUILayout.HelpBox("拖动“俯角 / FOV / 距离”会自动实时同步到 Game；也可以先点下面按钮。", MessageType.Info);
                using (new EditorGUI.DisabledScope(CameraService.Active == null || ResolveFrame() == null))
                    if (GUILayout.Button("临时接管 Game 镜头")) StartRuntimeOverride();
            }
        }

        static void DrawHelperControls()
        {
            EditorGUI.BeginChangeCheck();
            previewFacing = EditorGUILayout.ToggleLeft("Scene 中模拟跟随镜头图片", previewFacing);
            showGameFrame = EditorGUILayout.ToggleLeft("显示 Game 画幅框", showGameFrame);
            showMarkers = EditorGUILayout.ToggleLeft("显示物件分类标记", showMarkers);
            using (new EditorGUI.DisabledScope(!showMarkers))
            {
                showNames = EditorGUILayout.ToggleLeft("显示物件名称", showNames);
                selectedModuleOnly = EditorGUILayout.ToggleLeft("只显示选中模块", selectedModuleOnly);
            }
            if (EditorGUI.EndChangeCheck())
            {
                SavePreferences();
                SceneView.RepaintAll();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLegendSwatch(new Color(.25f, .75f, 1f), "地面");
                DrawLegendSwatch(new Color(1f, .62f, .15f), "透视");
                DrawLegendSwatch(new Color(.35f, 1f, .45f), "保形");
                DrawLegendSwatch(new Color(1f, .25f, .25f), "错误");
            }
            DrawSelectedDifference();
        }

        static void DrawSaveControls()
        {
            var view = ResolveView();
            EditorGUILayout.HelpBox("只在方案确认后保存。俯角/FOV 属于共享规范，距离属于当前模块；构图偏移不会写入资源。", MessageType.Info);
            using (new EditorGUI.DisabledScope(view == null || view.profile == null || !customParameters))
                if (GUILayout.Button("把当前参数写回选中模块")) SaveSelectedModule();
        }

        static void DuringSceneGui(SceneView sceneView)
        {
            if (mode != ViewMode.Free && Event.current.type == EventType.Layout)
                ApplyToSceneView(sceneView, false);
            if (showMarkers) DrawPlacementMarkers(sceneView);
            if (showGameFrame && mode == ViewMode.GameCamera) DrawGameFrameGuide(sceneView);
        }

        static void ApplyToSceneView(SceneView sceneView, bool repaint = true)
        {
            if (sceneView == null || mode == ViewMode.Free) return;
            if (mode == ViewMode.Layout2D)
            {
                Transform frame = ResolveFrame();
                Quaternion rotation = frame != null ? frame.rotation : Quaternion.identity;
                sceneView.LookAt(pivot, rotation, Mathf.Max(.01f, layoutSize), true, true);
            }
            else
            {
                CameraPose pose;
                Vector3 target;
                if (customParameters || runtimeOverride)
                {
                    if (!TryBuildCustomPose(out pose, out _)) return;
                    target = CustomTarget();
                }
                else if (!TryGetOfficialPose(out pose, out target))
                {
                    status = "请先选择一个包含正式镜头配置的模块。";
                    return;
                }
                ApplyPoseToSceneView(sceneView, pose, target);
            }
            if (repaint) sceneView.Repaint();
        }

        static void ApplyPoseToSceneView(SceneView sceneView, CameraPose pose, Vector3 target)
        {
            float focusDistance = Mathf.Max(.01f,
                Vector3.Dot(target - pose.Position, pose.Rotation * Vector3.forward));
            float size = pose.OrthographicSize;
            if (pose.Perspective)
            {
                // SceneView.size 是包住球体半径的缩放值，不是相机距离。
                // 同时 Scene 视口经常不是 Game 的 16:9，直接使用 Game 的垂直 FOV
                // 会让两个窗口出现明显的放大差异。按实际显示画幅换算后再交给
                // SceneView，保证画幅框内的水平/垂直视野与 Game 相机一致。
                float sceneAspect = sceneView.camera != null && sceneView.camera.aspect > .01f
                    ? sceneView.camera.aspect : 16f / 9f;
                float gameAspect = GameAspect();
                float gameVerticalFov = Mathf.Clamp(pose.FieldOfView, 1f, 179f);
                float desiredSceneVerticalFov = gameVerticalFov;
                if (sceneAspect < gameAspect)
                {
                    float horizontalTan = Mathf.Tan(Mathf.Deg2Rad * gameVerticalFov * .5f) * gameAspect;
                    desiredSceneVerticalFov = 2f * Mathf.Rad2Deg * Mathf.Atan(horizontalTan / sceneAspect);
                }

                // Unity 会把 SceneView 的 fieldOfView 当作 aspect-neutral FOV；窄视口
                // 还会在内部除以 aspect，因此这里反算回应填的值。
                float sceneSettingFov = sceneAspect < 1f
                    ? 2f * Mathf.Rad2Deg * Mathf.Atan(
                        Mathf.Tan(Mathf.Deg2Rad * desiredSceneVerticalFov * .5f) * sceneAspect)
                    : desiredSceneVerticalFov;
                sceneView.cameraSettings.fieldOfView = sceneSettingFov;
                float actualSceneVerticalFov = sceneAspect < 1f
                    ? 2f * Mathf.Rad2Deg * Mathf.Atan(
                        Mathf.Tan(Mathf.Deg2Rad * sceneSettingFov * .5f) / sceneAspect)
                    : sceneSettingFov;
                size = focusDistance * Mathf.Sin(Mathf.Deg2Rad * actualSceneVerticalFov * .5f);
            }
            sceneView.LookAt(target, pose.Rotation, size, !pose.Perspective, true);
        }

        static float GameAspect()
        {
            Camera camera = RuntimeCamera();
            if (camera != null && camera.aspect > .01f)
                return camera.aspect;
            return 16f / 9f;
        }

        static bool TryGetOfficialPose(out CameraPose pose, out Vector3 target)
        {
            Camera camera = Application.isPlaying ? RuntimeCamera() : null;
            if (camera != null && !runtimeOverride)
            {
                float focusDistance = Mathf.Max(1f, distance);
                Transform frame = ResolveFrame();
                if (frame != null)
                {
                    var plane = new Plane(-frame.forward, frame.position);
                    if (plane.Raycast(new Ray(camera.transform.position, camera.transform.forward), out float hit))
                        focusDistance = Mathf.Max(.01f, hit);
                }
                target = camera.transform.position + camera.transform.forward * focusDistance;
                pose = new CameraPose(camera.transform.position, camera.transform.rotation,
                    camera.orthographicSize, !camera.orthographic, camera.fieldOfView);
                return true;
            }

            PlanarPerspectiveView view = ResolveView();
            if (view == null || view.frame == null || view.profile == null)
            {
                pose = default;
                target = default;
                return false;
            }
            pose = view.Pose(view.PanOffset);
            target = view.frame.position + view.frame.right * view.PanOffset.x + view.frame.up * view.PanOffset.y;
            return true;
        }

        static bool TryBuildCustomPose(out CameraPose pose, out CameraPlane plane)
        {
            Transform frame = ResolveFrame();
            if (frame == null)
            {
                pose = default;
                plane = default;
                return false;
            }
            Vector3 target = CustomTarget();
            Quaternion rotation = frame.rotation * Quaternion.Euler(-(90f - elevation), 0f, 0f);
            pose = new CameraPose(target - rotation * Vector3.forward * distance,
                rotation, 9f, true, fieldOfView);
            plane = new CameraPlane(frame.position, -frame.forward, frame.right, frame.up);
            return true;
        }

        static Vector3 CustomTarget()
        {
            Transform frame = ResolveFrame();
            return frame == null ? pivot : pivot + frame.right * compositionOffset.x + frame.up * compositionOffset.y;
        }

        static void LoadOfficialCamera()
        {
            StopRuntimeOverride();
            customParameters = false;
            compositionOffset = Vector2.zero;
            PlanarPerspectiveView view = ResolveView();
            if (Application.isPlaying && RuntimeCamera() != null)
            {
                Camera camera = RuntimeCamera();
                fieldOfView = camera.fieldOfView;
                if (view != null && view.profile != null)
                {
                    distance = view.profile.distance;
                    elevation = view.profile.viewStandard != null
                        ? view.profile.viewStandard.elevation : 90f - view.profile.tiltFromNormal;
                }
            }
            else if (view != null && view.profile != null)
            {
                elevation = view.profile.viewStandard != null
                    ? view.profile.viewStandard.elevation : 90f - view.profile.tiltFromNormal;
                fieldOfView = view.profile.EffectiveFieldOfView;
                distance = view.profile.distance;
            }
            else
            {
                var standard = AssetDatabase.LoadAssetAtPath<WorldViewStandard>(StandardPath);
                if (standard != null)
                {
                    elevation = standard.elevation;
                    fieldOfView = standard.verticalFieldOfView;
                }
            }

            if (TryGetOfficialPose(out _, out Vector3 target)) pivot = target;
            else if (ResolveFrame() != null) pivot = ResolveFrame().position;
            status = view != null || Application.isPlaying ? "已恢复正式游戏镜头。" : "未找到模块镜头，请先选择模块。";
            SavePreferences();
            ApplyToSceneView(SceneView.lastActiveSceneView);
        }

        static void StartRuntimeOverride()
        {
            if (!Application.isPlaying || CameraService.Active == null || ResolveFrame() == null)
            {
                status = "运行中的 CameraDirector 或模块 Frame 不可用。";
                return;
            }
            mode = ViewMode.GameCamera;
            customParameters = true;
            runtimeOverride = true;
            EnsureRuntimeLease();
            status = "Game 镜头已由面板临时接管。";
            SavePreferences();
        }

        static void StopRuntimeOverride()
        {
            runtimeOverride = false;
            runtimeLease?.Dispose();
            runtimeLease = null;
            runtimeDirector = null;
            if (runtimeShot != null) Object.DestroyImmediate(runtimeShot);
            runtimeShot = null;
        }

        static void UpdateRuntimeOverride()
        {
            if (!runtimeOverride) return;
            if (!Application.isPlaying)
            {
                StopRuntimeOverride();
                return;
            }
            EnsureRuntimeLease();
        }

        static void EnsureRuntimeLease()
        {
            CameraDirector director = CameraService.Active;
            if (director == null) return;
            if (runtimeLease != null && runtimeLease.IsValid && runtimeDirector == director) return;
            runtimeLease?.Dispose();
            runtimeDirector = director;
            if (runtimeShot == null)
            {
                runtimeShot = ScriptableObject.CreateInstance<RuntimeTuningShot>();
                runtimeShot.hideFlags = HideFlags.HideAndDontSave;
            }
            runtimeLease = director.AcquireShot(runtimeShot, runtimeShot,
                new CameraShotOptions(10000, 0f, 0f, "Scene Camera Tuning"));
        }

        static void FocusSelection()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                status = "请先选择场景物件或组合。";
                return;
            }
            var item = selected.GetComponentInParent<PlacementItem>();
            if (item != null && item.contact != null)
                pivot = item.contact.position;
            else
            {
                var renderers = selected.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) pivot = selected.transform.position;
                else
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                    pivot = bounds.center;
                    layoutSize = Mathf.Max(1f, bounds.extents.magnitude * 1.25f);
                }
            }
            compositionOffset = Vector2.zero;
            customParameters = mode == ViewMode.GameCamera;
            SavePreferences();
            ApplyToSceneView(SceneView.lastActiveSceneView);
        }

        static void FocusModule()
        {
            Transform frame = ResolveFrame();
            if (frame == null)
            {
                status = "当前选择没有唯一的模块 Frame。";
                return;
            }
            pivot = frame.position;
            compositionOffset = Vector2.zero;
            customParameters = mode == ViewMode.GameCamera;
            SavePreferences();
            ApplyToSceneView(SceneView.lastActiveSceneView);
        }

        static void SaveSelectedModule()
        {
            var view = ResolveView();
            if (view == null || view.profile == null) return;
            var standard = view.profile.viewStandard;
            var changed = new List<Object> { view.profile };
            if (standard != null) changed.Add(standard);
            Undo.RecordObjects(changed.ToArray(), "写回场景镜头配置");
            if (standard != null)
            {
                standard.elevation = elevation;
                standard.verticalFieldOfView = fieldOfView;
                EditorUtility.SetDirty(standard);
            }
            else
            {
                view.profile.tiltFromNormal = 90f - elevation;
                view.profile.fieldOfView = fieldOfView;
            }
            view.profile.distance = distance;
            EditorUtility.SetDirty(view.profile);
            AssetDatabase.SaveAssets();
            customParameters = false;
            status = "当前俯角、FOV 和距离已写回正式配置。";
        }

        static void DrawGameFrameGuide(SceneView sceneView)
        {
            if (sceneView == null) return;
            Camera camera = RuntimeCamera();
            float aspect = camera != null && camera.aspect > .01f ? camera.aspect : 16f / 9f;
            Rect area = new Rect(0f, 22f, sceneView.position.width, Mathf.Max(1f, sceneView.position.height - 22f));
            Rect frame = area;
            if (area.width / area.height > aspect)
            {
                frame.width = area.height * aspect;
                frame.x = (area.width - frame.width) * .5f;
            }
            else
            {
                frame.height = area.width / aspect;
                frame.y = area.y + (area.height - frame.height) * .5f;
            }

            Handles.BeginGUI();
            Color mask = new Color(0f, 0f, 0f, .24f);
            if (frame.x > area.x)
            {
                EditorGUI.DrawRect(new Rect(area.x, area.y, frame.x - area.x, area.height), mask);
                EditorGUI.DrawRect(new Rect(frame.xMax, area.y, area.xMax - frame.xMax, area.height), mask);
            }
            if (frame.y > area.y)
            {
                EditorGUI.DrawRect(new Rect(area.x, area.y, area.width, frame.y - area.y), mask);
                EditorGUI.DrawRect(new Rect(area.x, frame.yMax, area.width, area.yMax - frame.yMax), mask);
            }
            Handles.color = new Color(.15f, .9f, .95f, .9f);
            Handles.DrawAAPolyLine(2f,
                new Vector3(frame.x, frame.y), new Vector3(frame.xMax, frame.y),
                new Vector3(frame.xMax, frame.yMax), new Vector3(frame.x, frame.yMax),
                new Vector3(frame.x, frame.y));
            GUI.Label(new Rect(frame.x + 6f, frame.y + 5f, 180f, 20f), $"Game 画幅  {aspect:0.00}:1", EditorStyles.miniBoldLabel);
            Handles.EndGUI();
        }

        static void DrawPlacementMarkers(SceneView sceneView)
        {
            var selected = SelectedItem();
            foreach (var item in VisibleItems())
            {
                if (item == null || item.contact == null) continue;
                bool invalid = IsInvalid(item);
                Color color = invalid ? new Color(1f, .25f, .25f) : item.surface == PlacementItem.Surface.Ground
                    ? new Color(.25f, .75f, 1f) : item.UsesCameraFacingVisual
                        ? new Color(.35f, 1f, .45f) : new Color(1f, .62f, .15f);
                Vector3 point = item.contact.position;
                float size = HandleUtility.GetHandleSize(point);
                Handles.color = color;
                Handles.DrawSolidDisc(point, item.xzGround ? Vector3.up : Vector3.back, size * .025f);
                Vector3 right = item.transform.right * item.footprint.x * .5f;
                Vector3 depth = (item.xzGround ? item.transform.forward : item.transform.up) * item.footprint.y * .5f;
                Handles.DrawAAPolyLine(2f, point - right - depth, point + right - depth,
                    point + right + depth, point - right + depth, point - right - depth);
                if (!showNames && item != selected) continue;
                string label = showNames ? $"{item.name} · {CategoryLabel(item)}" : CategoryLabel(item);
                if (item == selected && sceneView.camera != null && item.art != null && item.art.sprite != null)
                {
                    ProjectionDifference difference = MeasureProjection(item, sceneView.camera, previewFacing);
                    label += $"\n比例 {difference.AspectPercent:0.#}% / 边缘 {difference.TrapezoidPercent:0.#}%";
                }
                Handles.Label(point + (item.xzGround ? Vector3.up : Vector3.back) * size * .08f,
                    label, MarkerStyle(color));
            }
        }

        static void DrawLegendSwatch(Color color, string label)
        {
            Rect rect = GUILayoutUtility.GetRect(12, 16, GUILayout.Width(12));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 3, 10, 10), color);
            GUILayout.Label(label, EditorStyles.miniLabel);
        }

        static void DrawSelectedDifference()
        {
            var item = SelectedItem();
            var sceneView = SceneView.lastActiveSceneView;
            if (item == null || sceneView == null || sceneView.camera == null || item.art == null || item.art.sprite == null)
                return;
            ProjectionDifference difference = MeasureProjection(item, sceneView.camera, previewFacing);
            MessageType type = IsInvalid(item) ? MessageType.Error :
                difference.MaxPercent < 2f ? MessageType.Info : MessageType.Warning;
            EditorGUILayout.HelpBox($"{item.name} · {CategoryLabel(item)}\n比例变化 {difference.AspectPercent:0.0}% · 边缘差 {difference.TrapezoidPercent:0.0}%", type);
        }

        static GUIStyle MarkerStyle(Color color)
        {
            return new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = color },
                padding = new RectOffset(4, 4, 2, 2)
            };
        }

        static string CategoryLabel(PlacementItem item)
        {
            if (IsInvalid(item)) return "配置错误";
            if (item.surface == PlacementItem.Surface.Ground) return "地面·接受透视";
            return item.UsesCameraFacingVisual ? "跟随镜头·保持图形" : "世界平面·产生透视";
        }

        static bool IsInvalid(PlacementItem item)
        {
            return item.standard == null || item.contact == null || item.visualRoot == null || item.art == null ||
                   item.art.sprite == null || item.physicsRoot == null || item.anchorsRoot == null ||
                   Vector3.Distance(item.transform.lossyScale, Vector3.one) > .001f ||
                   Vector3.Distance(item.visualRoot.localScale, Vector3.one) > .001f;
        }

        internal readonly struct ProjectionDifference
        {
            public readonly float AspectPercent;
            public readonly float TrapezoidPercent;
            public float MaxPercent => Mathf.Max(AspectPercent, TrapezoidPercent);
            public ProjectionDifference(float aspectPercent, float trapezoidPercent)
            {
                AspectPercent = aspectPercent;
                TrapezoidPercent = trapezoidPercent;
            }
        }

        internal static ProjectionDifference MeasureProjection(PlacementItem item, Camera camera, bool simulateFacing)
        {
            if (item == null || item.art == null || item.art.sprite == null || camera == null)
                return new ProjectionDifference(0f, 0f);
            Bounds bounds = item.art.sprite.bounds;
            Matrix4x4 matrix = item.art.transform.localToWorldMatrix;
            var facing = item.visualRoot != null ? item.visualRoot.GetComponent<CameraFacingVisual>() : null;
            if (simulateFacing && facing != null)
            {
                Matrix4x4 visual = Matrix4x4.TRS(item.visualRoot.position,
                    facing.RotationFor(camera), item.visualRoot.lossyScale);
                matrix = visual * Matrix4x4.TRS(item.art.transform.localPosition,
                    item.art.transform.localRotation, item.art.transform.localScale);
            }
            Vector3 bl = camera.WorldToScreenPoint(matrix.MultiplyPoint3x4(new Vector3(bounds.min.x, bounds.min.y, 0f)));
            Vector3 br = camera.WorldToScreenPoint(matrix.MultiplyPoint3x4(new Vector3(bounds.max.x, bounds.min.y, 0f)));
            Vector3 tl = camera.WorldToScreenPoint(matrix.MultiplyPoint3x4(new Vector3(bounds.min.x, bounds.max.y, 0f)));
            Vector3 tr = camera.WorldToScreenPoint(matrix.MultiplyPoint3x4(new Vector3(bounds.max.x, bounds.max.y, 0f)));
            if (Mathf.Min(Mathf.Min(bl.z, br.z), Mathf.Min(tl.z, tr.z)) <= 0f)
                return new ProjectionDifference(100f, 100f);
            float bottom = Vector2.Distance(bl, br);
            float top = Vector2.Distance(tl, tr);
            float left = Vector2.Distance(bl, tl);
            float right = Vector2.Distance(br, tr);
            float screenWidth = Mathf.Max(.0001f, (bottom + top) * .5f);
            float screenHeight = Mathf.Max(.0001f, (left + right) * .5f);
            float worldWidth = matrix.MultiplyVector(Vector3.right * bounds.size.x).magnitude;
            float worldHeight = Mathf.Max(.0001f, matrix.MultiplyVector(Vector3.up * bounds.size.y).magnitude);
            float aspect = Mathf.Abs((screenWidth / screenHeight) /
                Mathf.Max(.0001f, worldWidth / worldHeight) - 1f) * 100f;
            float trapezoid = Mathf.Max(
                Mathf.Abs(top - bottom) / Mathf.Max(.0001f, (top + bottom) * .5f),
                Mathf.Abs(left - right) / Mathf.Max(.0001f, (left + right) * .5f)) * 100f;
            return new ProjectionDifference(aspect, trapezoid);
        }

        static IEnumerable<PlacementItem> VisibleItems()
        {
            ModuleWorld world = SelectedWorld();
            IEnumerable<PlacementItem> items = selectedModuleOnly && world != null
                ? world.GetComponentsInChildren<PlacementItem>(true)
                : Resources.FindObjectsOfTypeAll<PlacementItem>();
            return items.Where(item => item != null && !EditorUtility.IsPersistent(item) &&
                                       item.gameObject.scene.IsValid() && item.gameObject.scene.isLoaded);
        }

        static IEnumerable<CameraFacingVisual> VisibleFacingVisuals()
        {
            return VisibleItems().Where(item => item.visualRoot != null)
                .Select(item => item.visualRoot.GetComponent<CameraFacingVisual>())
                .Where(component => component != null && component.enabled && component.gameObject.activeInHierarchy);
        }

        static PlacementItem SelectedItem()
        {
            return Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<PlacementItem>() : null;
        }

        static ModuleWorld SelectedWorld()
        {
            return Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<ModuleWorld>() : null;
        }

        static PlanarPerspectiveView ResolveView()
        {
            var world = SelectedWorld();
            if (world != null && world.view != null) return world.view;
            if (Selection.activeGameObject != null)
            {
                var selectedView = Selection.activeGameObject.GetComponentInParent<PlanarPerspectiveView>();
                if (selectedView != null) return selectedView;
            }
            var views = Resources.FindObjectsOfTypeAll<PlanarPerspectiveView>()
                .Where(view => view != null && !EditorUtility.IsPersistent(view) &&
                               view.gameObject.scene.IsValid() && view.gameObject.scene.isLoaded).ToArray();
            return views.Length == 1 ? views[0] : null;
        }

        static Transform ResolveFrame()
        {
            var view = ResolveView();
            return view != null ? view.frame : null;
        }

        static Camera RuntimeCamera()
        {
            return CameraService.Active != null && CameraService.Active.Camera != null
                ? CameraService.Active.Camera : Camera.main;
        }

        static void BeginBuiltInCamera(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null) BeginSceneCamera(camera);
        }

        static void EndBuiltInCamera(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null) EndSceneCamera(camera);
        }

        static void BeginScriptableRenderPipelineCamera(ScriptableRenderContext _, Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline != null) BeginSceneCamera(camera);
        }

        static void EndScriptableRenderPipelineCamera(ScriptableRenderContext _, Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline != null) EndSceneCamera(camera);
        }

        static void BeginSceneCamera(Camera camera)
        {
            if (!previewFacing || camera == null || camera.cameraType != CameraType.SceneView || PreviewRotations.Count > 0)
                return;
            try
            {
                foreach (var facing in VisibleFacingVisuals())
                {
                    PreviewRotations[facing.transform] = facing.transform.rotation;
                    facing.transform.rotation = facing.RotationFor(camera);
                }
            }
            catch
            {
                RestorePreviewRotations();
                throw;
            }
        }

        static void EndSceneCamera(Camera camera)
        {
            if (camera != null && camera.cameraType == CameraType.SceneView) RestorePreviewRotations();
        }

        static void RestorePreviewRotations()
        {
            foreach (var pair in PreviewRotations)
                if (pair.Key != null) pair.Key.rotation = pair.Value;
            PreviewRotations.Clear();
        }

        static void Cleanup()
        {
            StopRuntimeOverride();
            RestorePreviewRotations();
        }

        static void SavePreferences()
        {
            EditorPrefs.SetFloat(PreferencePrefix + "Elevation", elevation);
            EditorPrefs.SetFloat(PreferencePrefix + "FieldOfView", fieldOfView);
            EditorPrefs.SetFloat(PreferencePrefix + "Distance", distance);
            EditorPrefs.SetFloat(PreferencePrefix + "LayoutSize", layoutSize);
            EditorPrefs.SetBool(PreferencePrefix + "PreviewFacing", previewFacing);
            EditorPrefs.SetBool(PreferencePrefix + "ShowMarkers", showMarkers);
            EditorPrefs.SetBool(PreferencePrefix + "ShowNames", showNames);
            EditorPrefs.SetBool(PreferencePrefix + "ShowGameFrame", showGameFrame);
            EditorPrefs.SetBool(PreferencePrefix + "SelectedModuleOnly", selectedModuleOnly);
        }
    }
}
