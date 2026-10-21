using System;
using System.IO;
using System.Linq;
using Game.Modules;
using GourmetAbyss.CameraSystem;
using UnityEditor;
using UnityEngine;

namespace Game.Modules.Editor
{
    /// <summary>
    /// Creates the standard artwork prefab structure and migrates existing prefabs.
    /// Artist-facing workflow: right-click a Sprite or prefab in the Project window.
    /// </summary>
    public static class ArtworkPrefabTools
    {
        const string MenuRoot = "Assets/场景物件/";
        const string GeneratedFolder = "Assets/Modules/GeneratedPrefabs";
        const string StandardPath = "Assets/Modules/Shared/WorldViewStandard.asset";
        static readonly string[] LegacySampleNames =
        {
            "Cabinet", "Chair", "DeliveryCounter", "Fridge", "Ground", "GroundMat",
            "ServingTable", "Stove", "Table", "TableSet"
        };

        [MenuItem(MenuRoot + "创建场景物件预制体", false, 210)]
        static void CreatePrefabFromSprite()
        {
            var sprite = SelectedSprite();
            if (sprite == null) return;

            int choice = EditorUtility.DisplayDialogComplex(
                "创建场景物件预制体",
                "预制体保存位置？\n源 PNG 不会被复制或修改。",
                "与源资源同目录",
                "取消",
                "统一 Prefab 目录");
            if (choice == 1) return;

            string sourcePath = AssetDatabase.GetAssetPath(sprite);
            string folder = choice == 0
                ? Path.GetDirectoryName(sourcePath).Replace('\\', '/')
                : GeneratedFolderForSource(sourcePath);
            EnsureFolder(folder);

            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(
                folder + "/" + SanitizeName(sprite.name) + ".prefab");
            int facingChoice = EditorUtility.DisplayDialogComplex(
                "场景物件视角",
                "这个物件是否需要始终面向镜头？",
                "跟随镜头",
                "取消",
                "保持世界平面");
            if (facingChoice == 1) return;
            var root = BuildPrefabRoot(sprite, followCamera: facingChoice == 0);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
            Debug.Log("[ArtworkPrefab] 已创建：" + prefabPath + "（源资源未移动或修改）");
        }

        [MenuItem(MenuRoot + "创建场景物件预制体", true)]
        static bool ValidateCreatePrefabFromSprite() => SelectedSprite() != null;

        [MenuItem(MenuRoot + "设置为跟随镜头", false, 220)]
        static void SetSelectedPrefabFacing()
        {
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    continue;
                MigratePrefabOrSources(path, true);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem(MenuRoot + "设置为跟随镜头", true)]
        static bool ValidateSetSelectedPrefabFacing() =>
            Selection.objects.Length > 0 && Selection.objects.All(o =>
            {
                string p = AssetDatabase.GetAssetPath(o);
                return !string.IsNullOrEmpty(p) && p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
            });

        [MenuItem(MenuRoot + "取消跟随镜头", false, 221)]
        static void SetSelectedPrefabNotFacing()
        {
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    continue;
                MigratePrefabOrSources(path, false);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem(MenuRoot + "取消跟随镜头", true)]
        static bool ValidateSetSelectedPrefabNotFacing() => ValidateSetSelectedPrefabFacing();

        [MenuItem("GameObject/场景物件/设置为跟随镜头", false, 210)]
        static void SetSelectedSceneFacing()
        {
            foreach (var obj in Selection.gameObjects)
                SetFacingOnHierarchy(obj, true);
        }

        [MenuItem("GameObject/场景物件/设置为跟随镜头", true)]
        static bool ValidateSetSelectedSceneFacing() => Selection.gameObjects != null && Selection.gameObjects.Length > 0;

        [MenuItem("GameObject/场景物件/取消跟随镜头", false, 211)]
        static void SetSelectedSceneNotFacing()
        {
            foreach (var obj in Selection.gameObjects)
                SetFacingOnHierarchy(obj, false);
        }

        [MenuItem("GameObject/场景物件/取消跟随镜头", true)]
        static bool ValidateSetSelectedSceneNotFacing() => Selection.gameObjects != null && Selection.gameObjects.Length > 0;

        [MenuItem("Tools/Modules/Placement/迁移旧平面预制体到统一格式")]
        public static void MigrateLegacyPlaneVariants()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("请先退出运行模式再迁移预制体。");

            string folder = PlacementTools.SampleFolder;
            foreach (string name in LegacySampleNames)
            {
                string neutralPath = folder + "/" + name + ".prefab";
                string xyPath = folder + "/" + name + "_XY.prefab";
                string xzPath = folder + "/" + name + "_XZ.prefab";

                if (AssetDatabase.LoadAssetAtPath<GameObject>(neutralPath) == null &&
                    AssetDatabase.LoadAssetAtPath<GameObject>(xyPath) != null)
                {
                    string error = AssetDatabase.MoveAsset(xyPath, neutralPath);
                    if (!string.IsNullOrEmpty(error))
                        throw new InvalidOperationException($"移动 {xyPath} 失败：{error}");
                }

                if (AssetDatabase.LoadAssetAtPath<GameObject>(xzPath) != null && !AssetDatabase.DeleteAsset(xzPath))
                    throw new InvalidOperationException("删除旧方向变体失败：" + xzPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            foreach (string name in LegacySampleNames)
            {
                string path = folder + "/" + name + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    throw new InvalidOperationException("缺少迁移后的预制体：" + path);
                NormalizeMigratedPrefab(path, name, name != "TableSet");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            string[] legacyPaths = AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith("_XY.prefab", StringComparison.OrdinalIgnoreCase) ||
                            p.EndsWith("_XZ.prefab", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (legacyPaths.Length > 0)
                throw new InvalidOperationException("仍有旧平面预制体：" + string.Join(", ", legacyPaths));

            Debug.Log($"[ArtworkPrefab] 旧 _XY/_XZ 样例已迁移为 {LegacySampleNames.Length} 个中性预制体；GUID 与场景引用保持不变。");
        }

        [MenuItem("Tools/Modules/Placement/清理场景旧视觉覆盖")]
        public static void ClearLegacyVisualOverrides()
        {
            string[] paths =
            {
                "Assets/Modules/Restaurant/RestaurantWorld.prefab",
                PlacementTools.SampleFolder + "/TableSet.prefab"
            };
            int reverted = 0;
            foreach (string path in paths)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                    reverted += ClearVisualOverrides(path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[ArtworkPrefab] 已清理 {reverted} 个旧视觉覆盖；场景根位置保持不变。");
        }

        static int ClearVisualOverrides(string path)
        {
            int reverted = 0;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var instanceRoot in root.GetComponentsInChildren<Transform>(true)
                    .Where(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)))
                {
                    // 旧流程曾把 CameraFacingVisual 直接加在地图中的预制体实例上。
                    // 源预制体完成统一配置后，移除这些 Added Component override，
                    // 让地图重新继承源预制体；否则后续更新主预制体不会可靠传播。
                    foreach (var component in instanceRoot.GetComponentsInChildren<CameraFacingVisual>(true)
                        .Where(PrefabUtility.IsAddedComponentOverride).ToArray())
                    {
                        PrefabUtility.RevertAddedComponent(component, InteractionMode.AutomatedAction);
                        reverted++;
                    }

                    var modifications = PrefabUtility.GetPropertyModifications(instanceRoot.gameObject);
                    if (modifications == null) continue;
                    bool tableAssembly = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot.gameObject) ==
                        PlacementTools.SampleFolder + "/TableSet.prefab";
                    foreach (var modification in modifications.ToArray())
                    {
                        var target = modification.target;
                        // GetPropertyModifications returns source-asset objects. Map each
                        // source target back to the matching object in this instance, then
                        // revert its serialized property on the instance itself.
                        var instanceTarget = FindInstanceTarget(instanceRoot, target);
                        if (instanceTarget == null) continue;
                        bool visualTarget = target is PlacementItem || target is SpriteRenderer ||
                            target is PlanarSprite || target is CameraFacingVisual;
                        if (!visualTarget && target is Transform targetTransform)
                            visualTarget = targetTransform.name == "VisualRoot" || targetTransform.name == "Art";
                        // TableSet 内部桌、椅和座位锚点属于组合预制体的固定编排。
                        // RestaurantWorld 只保存六个 TableSet 根的位置，不重复覆盖组合内部布局。
                        if (!visualTarget && tableAssembly && instanceTarget is Transform instanceTransform &&
                            instanceTransform != instanceRoot)
                            visualTarget = true;
                        if (!visualTarget) continue;
                        var serialized = new SerializedObject(instanceTarget);
                        var property = serialized.FindProperty(modification.propertyPath);
                        if (property == null) continue;
                        PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
                        reverted++;
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return reverted;
        }

        static UnityEngine.Object FindInstanceTarget(Transform instanceRoot, UnityEngine.Object sourceTarget)
        {
            foreach (var child in instanceRoot.GetComponentsInChildren<Transform>(true))
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == sourceTarget)
                    return child.gameObject;
                foreach (var component in child.GetComponents<Component>())
                    if (component != null && PrefabUtility.GetCorrespondingObjectFromSource(component) == sourceTarget)
                        return component;
            }
            return null;
        }

        static Sprite SelectedSprite()
        {
            if (Selection.objects.Length != 1) return null;
            var selected = Selection.activeObject;
            if (selected is Sprite sprite) return sprite;
            if (selected is Texture2D texture)
                return AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(texture))
                    .OfType<Sprite>().FirstOrDefault();
            return null;
        }

        static GameObject BuildPrefabRoot(Sprite sprite, bool followCamera)
        {
            // 根节点就是布局节点：美术在普通二维平面中摆放它，
            // 视觉朝向和逻辑分支由下面的标准子节点分别处理。
            var root = new GameObject(sprite.name);
            var visual = new GameObject("VisualRoot");
            visual.transform.SetParent(root.transform, false);
            var artGo = new GameObject("Art");
            artGo.transform.SetParent(visual.transform, false);
            var contact = new GameObject("ContactRoot");
            contact.transform.SetParent(root.transform, false);
            var physics = new GameObject("PhysicsRoot");
            physics.transform.SetParent(root.transform, false);
            var anchors = new GameObject("AnchorsRoot");
            anchors.transform.SetParent(root.transform, false);

            var renderer = artGo.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.drawMode = SpriteDrawMode.Simple;

            var item = root.AddComponent<PlacementItem>();
            item.standard = AssetDatabase.LoadAssetAtPath<WorldViewStandard>(StandardPath);
            item.surface = PlacementItem.Surface.Artwork;
            item.contact = contact.transform;
            item.visualRoot = visual.transform;
            item.art = renderer;
            item.physicsRoot = physics.transform;
            item.anchorsRoot = anchors.transform;
            item.spriteContact = new Vector2(.5f, 0f);
            // 新预制体保留源 Sprite 的原始像素比例；项目倍率只用于镜头/布局校验，
            // 不在创建时把 Art 节点统一缩小。
            item.width = sprite.bounds.size.x;
            item.groundDepth = sprite.bounds.size.y;
            item.footprint = new Vector2(item.width, Mathf.Max(.25f, item.width * .5f));
            item.useSourceDimensions = true;

            var sorter = artGo.AddComponent<PlanarSprite>();
            sorter.visual = renderer;
            sorter.contactPoint = contact.transform;
            sorter.useWorldAxesWhenUnbound = true;
            item.sorter = sorter;

            if (followCamera) AddFacing(visual);
            item.ApplyArtwork();
            return root;
        }

        static void MigratePrefab(string path, bool followCamera)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var items = root.GetComponentsInChildren<PlacementItem>(true);
                if (items.Length == 0)
                {
                    var sprite = root.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(r => r.sprite != null);
                    if (sprite != null) EnsurePlacementStructure(root, sprite);
                    items = root.GetComponentsInChildren<PlacementItem>(true);
                }

                foreach (var item in items)
                {
                    if (item.visualRoot == null) continue;
                    if (followCamera) AddFacing(item.visualRoot.gameObject);
                    else RemoveFacing(item.visualRoot.gameObject);
                    NormalizeSourceDimensions(item);
                    // 显式选择“跟随镜头”时尊重预制体用途，不再按 Surface 强制跳过地面图片。
                    // 这样已经画好完整 2D 视角的背景、地垫和家具可以采用同一规则：
                    // 编辑态保持二维平面，运行时由 VisualRoot 统一接管朝向。
                    EditorUtility.SetDirty(item.visualRoot);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void MigratePrefabOrSources(string path, bool followCamera)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var sourcePaths = asset == null ? Array.Empty<string>() : asset
                .GetComponentsInChildren<PlacementItem>(true)
                .Select(PlacementPrefabLinks.SourcePath)
                .Where(p => !string.IsNullOrEmpty(p) && p != path)
                .Distinct()
                .ToArray();
            if (sourcePaths.Length == 0)
                MigratePrefab(path, followCamera);
            else
                foreach (string sourcePath in sourcePaths)
                    MigratePrefab(sourcePath, followCamera);
        }

        static void NormalizeMigratedPrefab(string path, string neutralName, bool normalizeItems)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name = neutralName;
                if (normalizeItems)
                {
                    foreach (var item in root.GetComponentsInChildren<PlacementItem>(true))
                    {
                        NormalizeNodeNames(item);
                        if (item.art == null || item.art.sprite == null)
                        {
                            Debug.LogWarning("[ArtworkPrefab] 跳过缺少 Sprite 的旧预制体：" + path);
                            continue;
                        }
                        NormalizeSourceDimensions(item);
                        // 历史 PlacementSamples 是餐厅 2.5D 世界平面资源。
                        // 迁移命名和层级时不得顺带改变其呈现模式。
                        RemoveFacing(item.visualRoot.gameObject);
                        item.ApplyArtwork();
                        EditorUtility.SetDirty(item);
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void NormalizeNodeNames(PlacementItem item)
        {
            if (item.contact != null) item.contact.name = "ContactRoot";
            if (item.visualRoot != null) item.visualRoot.name = "VisualRoot";
            if (item.art != null) item.art.name = "Art";
            if (item.physicsRoot != null) item.physicsRoot.name = "PhysicsRoot";
            if (item.anchorsRoot != null) item.anchorsRoot.name = "AnchorsRoot";
            if (item.sorter != null)
            {
                item.sorter.frame = null;
                item.sorter.profile = null;
                item.sorter.useWorldAxesWhenUnbound = true;
                EditorUtility.SetDirty(item.sorter);
            }
        }

        static void NormalizeSourceDimensions(PlacementItem item)
        {
            if (item.art == null || item.art.sprite == null) return;
            item.useSourceDimensions = true;
            item.width = item.art.sprite.bounds.size.x;
            item.groundDepth = item.art.sprite.bounds.size.y;
            item.footprint = item.surface == PlacementItem.Surface.Ground
                ? new Vector2(item.width, item.groundDepth)
                : new Vector2(item.width, Mathf.Max(.25f, item.width * .5f));
            item.ApplyArtwork();
            EditorUtility.SetDirty(item);
        }

        static void SetFacingOnHierarchy(GameObject selected, bool followCamera)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("请先退出运行模式再修改预制体。");
            var items = selected.GetComponentsInChildren<PlacementItem>(true);
            var sourcePaths = items.Select(PlacementPrefabLinks.SourcePath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToArray();
            foreach (string sourcePath in sourcePaths)
                MigratePrefab(sourcePath, followCamera);
            foreach (var item in items.Where(i => string.IsNullOrEmpty(PlacementPrefabLinks.SourcePath(i))))
            {
                if (item.visualRoot == null) continue;
                if (followCamera) AddFacing(item.visualRoot.gameObject);
                else RemoveFacing(item.visualRoot.gameObject);
                item.ApplyArtwork();
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void AddFacing(GameObject visualRoot)
        {
            var facing = visualRoot.GetComponent<CameraFacingVisual>() ?? visualRoot.AddComponent<CameraFacingVisual>();
            var so = new SerializedObject(facing);
            var mode = so.FindProperty("updateMode");
            if (mode != null) mode.intValue = (int)CameraFacingVisual.UpdateMode.EveryLateUpdate;
            // 不把某个场景里的 Camera 保存进可复用预制；运行时统一取当前活动镜头。
            var target = so.FindProperty("targetCamera");
            if (target != null) target.objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(facing);
        }

        static void RemoveFacing(GameObject visualRoot)
        {
            var facing = visualRoot.GetComponent<CameraFacingVisual>();
            if (facing != null) UnityEngine.Object.DestroyImmediate(facing, true);
        }

        static void EnsurePlacementStructure(GameObject root, SpriteRenderer renderer)
        {
            var visual = root.transform.Find("VisualRoot");
            if (visual == null)
            {
                var go = new GameObject("VisualRoot");
                visual = go.transform;
                visual.SetParent(root.transform, false);
            }
            if (renderer.transform != visual)
            {
                var art = new GameObject("Art");
                art.transform.SetParent(visual, false);
                var copy = art.AddComponent<SpriteRenderer>();
                EditorUtility.CopySerialized(renderer, copy);
                renderer.enabled = false;
                renderer = copy;
            }
            var contact = root.transform.Find("ContactRoot") ?? new GameObject("ContactRoot").transform;
            contact.SetParent(root.transform, false);
            var physics = root.transform.Find("PhysicsRoot") ?? new GameObject("PhysicsRoot").transform;
            physics.SetParent(root.transform, false);
            var item = root.GetComponent<PlacementItem>() ?? root.AddComponent<PlacementItem>();
            bool created = item.standard == null && item.contact == null && item.visualRoot == null;
            item.standard = AssetDatabase.LoadAssetAtPath<WorldViewStandard>(StandardPath);
            item.surface = PlacementItem.Surface.Artwork;
            item.contact = contact;
            item.visualRoot = visual;
            item.art = renderer;
            item.physicsRoot = physics;
            var anchors = root.transform.Find("AnchorsRoot") ?? root.transform.Find("Anchors") ?? new GameObject("AnchorsRoot").transform;
            anchors.SetParent(root.transform, false);
            item.anchorsRoot = anchors;
            if (created)
            {
                item.width = renderer.sprite != null ? renderer.sprite.bounds.size.x : 1f;
                item.groundDepth = renderer.sprite != null ? renderer.sprite.bounds.size.y : 1f;
                item.spriteContact = new Vector2(.5f, 0f);
                item.footprint = new Vector2(item.width, Mathf.Max(.25f, item.width * .5f));
            }
            var sorter = renderer.GetComponent<PlanarSprite>() ?? renderer.gameObject.AddComponent<PlanarSprite>();
            sorter.visual = renderer;
            sorter.contactPoint = contact;
            sorter.ground = item.surface == PlacementItem.Surface.Ground;
            sorter.xzGround = item.xzGround;
            sorter.useWorldAxesWhenUnbound = true;
            item.sorter = sorter;
            EditorUtility.SetDirty(item);
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static string GeneratedFolderForSource(string sourcePath)
        {
            sourcePath = sourcePath.Replace('\\', '/');
            string module = "Shared";
            string category = "Misc";

            const string modulesPrefix = "Assets/Modules/";
            if (sourcePath.StartsWith(modulesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var rest = sourcePath.Substring(modulesPrefix.Length);
                var parts = rest.Split('/');
                if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
                    module = SanitizeName(parts[0]);
                category = parts.Length > 1 && !string.IsNullOrEmpty(parts[1])
                    ? SanitizeName(parts[1]) : "Misc";
            }
            else if (sourcePath.IndexOf("/Environment/Vegetation/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                category = "Vegetation";
            }
            else if (sourcePath.IndexOf("/map/餐厅", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                module = "Restaurant";
                category = "Environment";
            }

            return GeneratedFolder + "/" + module + "/" + category;
        }

        static string SanitizeName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c.ToString(), "_");
            return value;
        }
    }
}
