using System;
using System.Collections.Generic;
using GourmetAbyss.CameraSystem;
using UnityEditor;
using UnityEngine;

namespace Game.Modules.Editor
{
    // Isolated preview scene: never writes to the gameplay camera or source transforms.
    public sealed class PlacementPreviewWindow : EditorWindow
    {
        [SerializeField] GameObject source;
        [SerializeField] string sourceAssetPath, sourceObjectPath;
        GameObject clone;
        PreviewRenderUtility preview;
        public Texture LastPreview { get; private set; }
        readonly List<FacingSnapshot> facingSnapshots = new List<FacingSnapshot>();
        struct FacingSnapshot
        {
            public Transform copy;
            public CameraFacingVisual facing;
            public Vector3 position, facingPosition;
            public Quaternion rotation, facingRotation;
        }
        internal void ApplyFacingPreview(Camera camera)
        {
            foreach (var snapshot in facingSnapshots)
            {
                if (snapshot.copy == null) continue;
                Quaternion delta = snapshot.facing != null && snapshot.facing.enabled
                    ? snapshot.facing.RotationFor(camera) * Quaternion.Inverse(snapshot.facingRotation)
                    : Quaternion.identity;
                snapshot.copy.SetPositionAndRotation(
                    snapshot.facingPosition + delta * (snapshot.position - snapshot.facingPosition),
                    delta * snapshot.rotation);
            }
        }
        float aspect = 16f / 9f;
        public static void Open(GameObject selected)
        {
            if (selected == null) throw new InvalidOperationException("先选择一个场景物件或预制体。");
            var window = GetWindow<PlacementPreviewWindow>("固定镜头预览");
            window.minSize = new Vector2(640, 400);
            if (window.position.width < 640 || window.position.height < 400)
                window.position = new Rect(200, 140, 1000, 650);
            var world = selected.GetComponentInParent<ModuleWorld>();
            window.source = world != null ? world.gameObject : selected;
            window.sourceAssetPath = AssetDatabase.GetAssetPath(window.source);
            window.sourceObjectPath = "";
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && window.source.scene == stage.scene)
            {
                window.sourceAssetPath = stage.assetPath;
                window.sourceObjectPath = AnimationUtility.CalculateTransformPath(window.source.transform, stage.prefabContentsRoot.transform);
            }
            window.Rebuild(); window.Show();
        }
        void Rebuild()
        {
            Cleanup();
            RestoreSource();
            if (source == null) return;
            preview = new PreviewRenderUtility();
            preview.ambientColor = Color.white;
            clone = new GameObject("PlacementPreview");
            preview.AddSingleGO(clone);
            // Copy rendering data only. Instantiating an enemy/functional prefab would execute Awake/OnEnable
            // before it could be disabled, even inside a preview scene.
            foreach (var renderer in source.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (!renderer.enabled) continue;
                var child = new GameObject(renderer.name);
                child.transform.SetParent(clone.transform, false);
                child.transform.SetPositionAndRotation(renderer.transform.position,renderer.transform.rotation);
                child.transform.localScale=renderer.transform.lossyScale;
                var copy=child.AddComponent<SpriteRenderer>();
                EditorUtility.CopySerialized(renderer,copy);
                var facing = renderer.GetComponentInParent<CameraFacingVisual>();
                facingSnapshots.Add(new FacingSnapshot
                {
                    copy = child.transform, facing = facing,
                    position = renderer.transform.position, rotation = renderer.transform.rotation,
                    facingPosition = facing != null ? facing.transform.position : Vector3.zero,
                    facingRotation = facing != null ? facing.transform.rotation : Quaternion.identity
                });
            }
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.12f,.15f,.17f);
            preview.camera.nearClipPlane = .03f; preview.camera.farClipPlane = 500;
        }
        void OnEnable() => EditorApplication.delayCall += RestorePreview;
        void RestoreSource()
        {
            if (string.IsNullOrEmpty(sourceAssetPath)) return;
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            GameObject root = stage != null && stage.assetPath == sourceAssetPath
                ? stage.prefabContentsRoot : null;
            if (root == null && source == null) root = AssetDatabase.LoadAssetAtPath<GameObject>(sourceAssetPath);
            if (root == null) return;
            var child = string.IsNullOrEmpty(sourceObjectPath) ? root.transform : root.transform.Find(sourceObjectPath);
            source = child != null ? child.gameObject : null;
        }
        void RestorePreview()
        {
            if (this == null) return;
            RestoreSource();
            if (source != null && preview == null) { Rebuild(); Repaint(); }
        }
        void OnDisable()
        {
            EditorApplication.delayCall -= RestorePreview;
            Cleanup();
        }
        void Cleanup() { if (preview != null) preview.Cleanup(); preview = null; clone = null; LastPreview=null; facingSnapshots.Clear(); }
        void OnGUI()
        {
            if (source == null && !string.IsNullOrEmpty(sourceAssetPath)) RestorePreview();
            EditorGUILayout.LabelField("固定视角图片预览；中性照明，不含 HUD/动画/业务。修改摆放后点刷新，最终光照看 Game。", EditorStyles.wordWrappedLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新摆放")) Rebuild();
                if (GUILayout.Button("16:9")) aspect=16f/9;
                if (GUILayout.Button("4:3")) aspect=4f/3;
                if (GUILayout.Button("21:9")) aspect=21f/9;
            }
            if (source == null || preview == null || clone == null) return;
            var space = GUILayoutUtility.GetRect(100,10000,100,10000);
            float width = Mathf.Min(space.width,space.height*aspect);
            var rect = new Rect(space.x+(space.width-width)/2,space.y,width,width/aspect);
            if (Event.current.type != EventType.Repaint) return;
            var world = source.GetComponent<ModuleWorld>();
            CameraPose pose;
            if (world != null) pose = world.view.Pose(Vector2.zero);
            else
            {
                var item = source.GetComponentInChildren<PlacementItem>(true);
                var standard = item != null ? item.standard : AssetDatabase.LoadAssetAtPath<WorldViewStandard>(PlacementTools.StandardPath);
                var rotation = standard.ArtworkRotation(item != null && item.xzGround);
                var renderers = clone.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(source.transform.position,Vector3.one);
                foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float distance = Mathf.Max(6,bounds.size.magnitude*1.7f);
                pose = new CameraPose(bounds.center-rotation*Vector3.forward*distance,rotation,10,true,standard.verticalFieldOfView);
            }
            preview.BeginPreview(rect,GUIStyle.none);
            preview.camera.orthographic=false; preview.camera.fieldOfView=pose.FieldOfView;
            preview.cameraFieldOfView=pose.FieldOfView;
            preview.camera.transform.SetPositionAndRotation(pose.Position,pose.Rotation);
            ApplyFacingPreview(preview.camera);
            preview.Render(true,false);
            LastPreview=preview.EndPreview();
            GUI.DrawTexture(rect,LastPreview,ScaleMode.StretchToFill,false);
        }
    }
}
