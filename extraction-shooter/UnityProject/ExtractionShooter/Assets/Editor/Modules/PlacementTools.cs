using System;
using System.Collections.Generic;
using System.Linq;
using GourmetAbyss.CameraSystem;
using UnityEditor;
using UnityEngine;

namespace Game.Modules.Editor
{
    public static class PlacementTools
    {
        public const string StandardPath = "Assets/Modules/Shared/WorldViewStandard.asset";
        public const string SampleFolder = "Assets/Modules/PlacementSamples";

        public static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        public static PlacementItem Create(Sprite sprite, WorldViewStandard standard, bool ground, bool xz,
            float width, float depth = 1)
        {
            if (sprite == null || standard == null) throw new InvalidOperationException("请选择图片和镜头规范。");
            var item = new GameObject(sprite.name).AddComponent<PlacementItem>();
            item.standard = standard; item.xzGround = xz;
            item.surface = ground ? PlacementItem.Surface.Ground : PlacementItem.Surface.Artwork;
            item.contact = Child(item.transform, "Contact");
            item.visualRoot = Child(item.transform, "VisualRoot");
            item.art = Child(item.visualRoot, "Art").gameObject.AddComponent<SpriteRenderer>();
            item.art.sprite = sprite;
            item.physicsRoot = Child(item.transform, "Physics");
            item.anchorsRoot = Child(item.transform, "Anchors");
            item.sorter = item.art.gameObject.AddComponent<PlanarSprite>();
            item.width = width; item.groundDepth = depth;
            item.spriteContact = ground ? new Vector2(.5f, .5f) : new Vector2(.5f, 0);
            item.footprint = new Vector2(width, ground ? depth : Mathf.Max(.25f, width * .5f));
            item.ApplyArtwork();
            return item;
        }

        public static List<string> Validate(PlacementItem item)
        {
            var errors = new List<string>();
            void Check(bool condition, string message) { if (!condition) errors.Add(item.name + ": " + message); }
            Check(item.standard != null, "缺少共享镜头规范");
            Check(item.contact != null && item.visualRoot != null && item.art != null &&
                item.physicsRoot != null && item.anchorsRoot != null && item.sorter != null, "预制体层级/引用不完整");
            if (errors.Count > 0) return errors;
            Check(item.art.sprite != null, "缺少 Sprite");
            Check(item.contact.parent == item.transform && item.visualRoot.parent == item.transform &&
                item.physicsRoot.parent == item.transform && item.anchorsRoot.parent == item.transform,
                "Contact、VisualRoot、Physics、Anchors 必须是逻辑根节点的直接子节点");
            Check(item.art.transform.parent == item.visualRoot, "Art 必须在 VisualRoot 下");
            Check(Vector3.Distance(item.transform.localScale, Vector3.one) < .0001f, "根节点缩放须为 1");
            var normal = item.xzGround ? Vector3.up : Vector3.back;
            Check(item.surface == PlacementItem.Surface.Ground
                ? Vector3.Angle(item.transform.localRotation * normal, normal) < .01f
                : Quaternion.Angle(item.transform.localRotation, Quaternion.identity) < .01f,
                "物件根节点不能任意倾斜；立起图片用方向变体，地块仅能在地面内旋转");
            Check(Vector3.Distance(item.transform.lossyScale, Vector3.one) < .0001f, "父节点缩放破坏世界尺寸，请移出缩放组");
            var world = item.GetComponentInParent<ModuleWorld>();
            if (item.surface == PlacementItem.Surface.Artwork)
            {
                var expected = world != null && world.view != null ? world.view.Pose(Vector2.zero).Rotation : item.standard.ArtworkRotation(item.xzGround);
                Check(Quaternion.Angle(item.visualRoot.rotation, expected) < .01f, "父级旋转/坐标体系与模块镜头不一致");
            }
            var frame = item.sorter.frame != null ? item.sorter.frame : world != null && world.view != null ? world.view.frame : null;
            float sortingDepth = frame != null ? Vector3.Dot(item.contact.position-frame.position,frame.up)
                : Vector3.Dot(item.contact.position,item.xzGround?Vector3.forward:Vector3.up);
            Check(Mathf.Abs(-sortingDepth*100f+item.sorter.orderOffset) < (frame!=null?900:31000),
                "超出当前排序分区，请由程序设置新的局部排序区域，不要继续扩大同一区域");
            Check(Quaternion.Angle(item.visualRoot.localRotation, item.ExpectedVisualRotation) < .01f, "视觉倾角与镜头规范不一致，执行应用图片配置");
            Check(Vector3.Distance(item.visualRoot.localScale, Vector3.one) < .0001f, "VisualRoot 缩放须为 1，尺寸放在 Art 层");
            Check(item.width > 0 && item.groundDepth > 0 && item.footprint.x > 0 && item.footprint.y > 0, "尺寸/占地必须大于 0");
            Check(item.spriteContact.x >= 0 && item.spriteContact.x <= 1 && item.spriteContact.y >= 0 && item.spriteContact.y <= 1, "图片接地点须在 0..1 范围内");
            Check(Mathf.Abs(Vector3.Dot(item.contact.localPosition, normal)) < .001f, "Contact 必须位于逻辑根节点的地面平面上");
            Check(item.sorter.contactPoint == item.contact && item.sorter.visual == item.art &&
                item.sorter.ground == (item.surface == PlacementItem.Surface.Ground), "排序必须绑定实际接地点和显示类型");
            if (item.art.sprite != null)
            {
                Check(Vector3.Distance(item.art.transform.TransformPoint(item.SpriteContactLocal), item.contact.position) < .001f,
                    "图片接地点没有落在 Contact 上");
                Check(Quaternion.Angle(item.art.transform.localRotation, Quaternion.identity) < .01f, "Art 有额外旋转");
                float sx = item.width / item.art.sprite.bounds.size.x;
                float sy = item.surface == PlacementItem.Surface.Ground ? item.groundDepth / item.art.sprite.bounds.size.y : sx;
                Check(Vector3.Distance(item.art.transform.localScale, new Vector3(sx, sy, 1)) < .001f, "图片尺寸与配置不一致或被额外拉伸");
            }
            Check(item.visualRoot.GetComponentsInChildren<Collider>(true).Length == 0 &&
                item.visualRoot.GetComponentsInChildren<Collider2D>(true).Length == 0 &&
                item.visualRoot.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                item.visualRoot.GetComponentsInChildren<Rigidbody2D>(true).Length == 0, "视觉分支不能承载碰撞/刚体");
            Check(item.visualRoot.GetComponentsInChildren<CameraFacingVisual>(true).Length == 0, "固定图片模板不可再叠加动态朝向组件");
            return errors;
        }

        public static void ValidateTree(GameObject root)
        {
            var items = root.GetComponentsInChildren<PlacementItem>(true);
            if (items.Length == 0) throw new InvalidOperationException("选中对象下没有标准摆放物件。");
            var errors = items.SelectMany(Validate).ToArray();
            if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
        }

        public static void ApplyWithUndo(PlacementItem item)
        {
            if (Application.isPlaying) throw new InvalidOperationException("请在非运行状态编辑预制体。");
            Undo.RecordObjects(new UnityEngine.Object[] { item, item.visualRoot, item.art.transform, item.sorter }, "应用图片配置");
            item.ApplyArtwork();
            foreach (var obj in new UnityEngine.Object[] { item, item.visualRoot, item.art.transform, item.sorter })
            {
                EditorUtility.SetDirty(obj);
                if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            }
        }

        public static void UseNativeSize(PlacementItem item)
        {
            if (item.art == null || item.art.sprite == null) return;
            Undo.RecordObject(item, "使用原图尺寸");
            item.width = item.art.sprite.rect.width / item.art.sprite.pixelsPerUnit;
            item.groundDepth = item.art.sprite.rect.height / item.art.sprite.pixelsPerUnit;
            ApplyWithUndo(item);
        }

        [MenuItem("Tools/Modules/Placement/Validate Selected")]
        public static void ValidateSelected()
        {
            if (Selection.activeGameObject == null) throw new InvalidOperationException("先选择物件、组合或 World。");
            var item = Selection.activeGameObject.GetComponentInParent<PlacementItem>();
            ValidateTree(item != null ? item.gameObject : Selection.activeGameObject);
            var world=Selection.activeGameObject.GetComponentInParent<ModuleWorld>();
            if(world!=null)PlacementPrefabLinks.ValidateWorld(world);
            Debug.Log("[Placement] 摆放校验通过。");
        }
    }

    [CustomEditor(typeof(PlacementItem))]
    public sealed class PlacementItemInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var item = (PlacementItem)target;
            EditorGUILayout.HelpBox("换图默认采用原图尺寸（像素 / PPU），再点应用。复制保留美术缩放，布局由美术摆放。Physics/Anchors 不随图片改变。", MessageType.Info);
            string sourcePath=PlacementPrefabLinks.SourcePath(item);
            if(!string.IsNullOrEmpty(sourcePath))
            {
                EditorGUILayout.LabelField("共用资源",sourcePath,EditorStyles.wordWrappedLabel);
                if(!EditorUtility.IsPersistent(item))
                    EditorGUILayout.HelpBox("在地图实例上换图/调宽度会形成局部覆盖，不会更新其他实例。统一改外观请打开共用源预制体。",MessageType.Info);
                if(GUILayout.Button("打开共用源预制体（同步所有引用）"))AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUI.BeginChangeCheck();
                var sprite = (Sprite)EditorGUILayout.ObjectField("图片", item.art != null ? item.art.sprite : null, typeof(Sprite), false);
                float width = EditorGUILayout.FloatField("世界宽度", item.width);
                float depth = item.surface == PlacementItem.Surface.Ground ? EditorGUILayout.FloatField("地面深度", item.groundDepth) : item.groundDepth;
                var point = EditorGUILayout.Vector2Field("图片接地点 (0..1)", item.spriteContact);
                var footprint = EditorGUILayout.Vector2Field("占地宽/深（不改碰撞）", item.footprint);
                if (EditorGUI.EndChangeCheck() && item.art != null)
                {
                    Undo.RecordObjects(new UnityEngine.Object[] { item, item.art }, "调整图片配置");
                    if (sprite != item.art.sprite && sprite != null)
                    {
                        width = sprite.rect.width / sprite.pixelsPerUnit;
                        depth = sprite.rect.height / sprite.pixelsPerUnit;
                    }
                    item.art.sprite = sprite; item.width = Mathf.Max(.01f, width); item.groundDepth = Mathf.Max(.01f, depth);
                    item.spriteContact = point; item.footprint = footprint;
                    EditorUtility.SetDirty(item); EditorUtility.SetDirty(item.art);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item.art);
                }
                if (GUILayout.Button("应用图片配置（不动逻辑/碰撞）")) PlacementTools.ApplyWithUndo(item);
                if (GUILayout.Button("使用原图尺寸（Art 缩放恢复为 1）")) PlacementTools.UseNativeSize(item);
            }
            EditorGUILayout.LabelField("程序预设", item.surface + " / " + (item.xzGround ? "XZ" : "XY"));
            EditorGUILayout.ObjectField("共享镜头规范", item.standard, typeof(WorldViewStandard), false);
            foreach (var error in PlacementTools.Validate(item)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (GUILayout.Button("打开固定镜头预览")) PlacementPreviewWindow.Open(item.gameObject);
        }

        private void OnSceneGUI()
        {
            var item = (PlacementItem)target;
            if (item.contact == null) return;
            var point = item.contact.position;
            Handles.color = Color.cyan;
            Handles.DrawWireDisc(point, item.xzGround ? Vector3.up : Vector3.back, .12f);
            Handles.Label(point, "接地点");
            var right = item.transform.right * item.footprint.x * .5f;
            var depth = (item.xzGround ? item.transform.forward : item.transform.up) * item.footprint.y * .5f;
            Handles.DrawAAPolyLine(point-right-depth, point+right-depth, point+right+depth, point-right+depth, point-right-depth);
        }
    }

    public sealed class PlacementAuthoringWindow : EditorWindow
    {
        Sprite sprite;
        bool ground, xz;
        bool nativeSize = true;
        float width = 2, depth = 2;
        GameObject template;
        string message;
        [MenuItem("Tools/Modules/Placement/Authoring")]
        public static void Open() => GetWindow<PlacementAuthoringWindow>("物件摆放").Show();
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("选择场景父节点后创建；餐厅用 XY，战斗用 XZ。模板不自动新增经营席位或业务。", MessageType.Info);
            template = (GameObject)EditorGUILayout.ObjectField("已有预制体", template, typeof(GameObject), false);
            sprite = (Sprite)EditorGUILayout.ObjectField("新图片", sprite, typeof(Sprite), false);
            ground = EditorGUILayout.Toggle("贴地素材", ground);
            xz = EditorGUILayout.Toggle("XZ 地面（战斗）", xz);
            nativeSize = EditorGUILayout.Toggle("新图片保持原图尺寸", nativeSize);
            using (new EditorGUI.DisabledScope(nativeSize || template != null))
            {
                width = EditorGUILayout.FloatField("世界宽度", width);
                if (ground) depth = EditorGUILayout.FloatField("地面深度", depth);
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("创建到选中父节点")) Run(() =>
                {
                    var parent = Selection.activeTransform;
                    if (parent != null && EditorUtility.IsPersistent(parent)) throw new InvalidOperationException("请选择场景中的父节点。");
                    var world = parent != null ? parent.GetComponentInParent<ModuleWorld>() : null;
                    bool plane = world != null ? Vector3.Dot(world.view.frame.up, Vector3.forward) > .9f : xz;
                    GameObject go;
                    if (template != null)
                    {
                        go = (GameObject)PrefabUtility.InstantiatePrefab(template, parent);
                        Undo.RegisterCreatedObjectUndo(go, "放置标准预制体");
                        foreach (var item in go.GetComponentsInChildren<PlacementItem>(true))
                        {
                            item.xzGround = plane;
                            item.sorter.frame = null;
                            // A copied prefab keeps its authored scale and offsets. Only adapt its
                            // visual plane; ApplyArtwork would overwrite manual art adjustments.
                            item.visualRoot.localRotation = item.ExpectedVisualRotation;
                            item.sorter.xzGround = plane;
                            item.sorter.Refresh();
                            foreach (var obj in new UnityEngine.Object[] { item, item.sorter, item.visualRoot, item.art.transform })
                                PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
                        }
                    }
                    else
                    {
                        if (sprite == null) throw new InvalidOperationException("请选择图片。");
                        float newWidth = nativeSize ? sprite.rect.width / sprite.pixelsPerUnit : Mathf.Max(.01f, width);
                        float newDepth = nativeSize ? sprite.rect.height / sprite.pixelsPerUnit : Mathf.Max(.01f, depth);
                        var item = PlacementTools.Create(sprite, AssetDatabase.LoadAssetAtPath<WorldViewStandard>(PlacementTools.StandardPath), ground, plane, newWidth, newDepth);
                        go = item.gameObject; Undo.RegisterCreatedObjectUndo(go, "创建标准物件");
                        go.transform.SetParent(parent, false);
                        item.sorter.frame = null;
                        item.ApplyArtwork();
                    }
                    Selection.activeGameObject = go;
                });
            }
            if (GUILayout.Button("保存新物件为预制体并连接")) Run(PlacementPrefabLinks.SaveSelected);
            if (GUILayout.Button("打开在用物件库"))
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(PlacementTools.SampleFolder);
            if (GUILayout.Button("预览选中对象/所在模块")) Run(() => PlacementPreviewWindow.Open(Selection.activeGameObject));
            if (GUILayout.Button("校验选中对象/组合")) Run(PlacementTools.ValidateSelected);
            EditorGUILayout.LabelField(message ?? "", EditorStyles.wordWrappedLabel);
        }
        void Run(Action action) { try { action(); message = "完成"; } catch (Exception e) { message = e.Message; } }
    }
}
