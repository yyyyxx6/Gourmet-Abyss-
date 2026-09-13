using System;
using GourmetAbyss.CameraSystem;
using UnityEditor;
using UnityEngine;

namespace Game.Modules.Editor
{
    // Isolated preview scene: never writes to the gameplay camera or source transforms.
    public sealed class PlacementPreviewWindow : EditorWindow
    {
        GameObject source, clone;
        PreviewRenderUtility preview;
        public Texture LastPreview { get; private set; }
        float aspect = 16f / 9f;
        public static void Open(GameObject selected)
        {
            if (selected == null) throw new InvalidOperationException("先选择一个场景物件或预制体。");
            var window = GetWindow<PlacementPreviewWindow>("固定镜头预览");
            var world = selected.GetComponentInParent<ModuleWorld>();
            window.source = world != null ? world.gameObject : selected;
            window.Rebuild(); window.Show();
        }
        void Rebuild()
        {
            Cleanup();
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
            }
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.12f,.15f,.17f);
            preview.camera.nearClipPlane = .03f; preview.camera.farClipPlane = 500;
        }
        void OnDisable() => Cleanup();
        void Cleanup() { if (preview != null) preview.Cleanup(); preview = null; clone = null; LastPreview=null; }
        void OnGUI()
        {
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
            preview.Render(true,false);
            LastPreview=preview.EndPreview();
            GUI.DrawTexture(rect,LastPreview,ScaleMode.StretchToFill,false);
        }
    }
}
