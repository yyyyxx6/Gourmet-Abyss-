using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Modules.Editor
{
    public static class DecorationBrushCore
    {
        public static string PrefabError(GameObject prefab)
        {
            if (prefab == null) return "资源已丢失，请重新拖入预制体。";
            if (!EditorUtility.IsPersistent(prefab) || !AssetDatabase.GetAssetPath(prefab).EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                || AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(prefab)) != prefab)
                return "请拖入 Project 中的完整预制体。";
            if (prefab.GetComponentInChildren<ModuleWorld>(true) != null || prefab.GetComponentInChildren<DecorationBrushGroup>(true) != null)
                return "请使用单个装饰或装饰组合，不能刷入整张地图或装饰组。";
            if (prefab.GetComponentInChildren<Renderer>(true) == null) return "预制体没有可显示的图片或模型。";
            if (!prefab.activeSelf) return "源预制体处于关闭状态，请先启用源预制体。";
            foreach (var item in prefab.GetComponentsInChildren<PlacementItem>(true))
                if (item.contact == null || item.visualRoot == null || item.art == null || item.art.sprite == null || item.sorter == null)
                    return "场景物件的图片或接地点不完整，请先修复源预制体。";
            return null;
        }

        public static string TargetError(DecorationBrushGroup group)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "请停止运行后编辑场景。";
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) return "请返回场景编辑；第一版在场景中绘制。";
            if (group == null) return "先创建或选择一个装饰组。";
            if (EditorUtility.IsPersistent(group) || !group.gameObject.scene.IsValid() || !group.gameObject.scene.isLoaded)
                return "请选择已打开场景中的装饰组。";
            if (!group.gameObject.activeInHierarchy) return "装饰组已隐藏，请先启用它。";
            if (Vector3.Distance(group.transform.lossyScale, Vector3.one) > .001f)
                return "装饰组及父节点的缩放需要为 (1,1,1)，以保持原图尺寸。";
            if (Mathf.Abs(Vector3.Dot(group.Normal, Vector3.up)) < .99f && Mathf.Abs(Vector3.Dot(group.Normal, Vector3.forward)) < .99f)
                return "请选择平坦的地图平面；第一版支持 XY / XZ 地面。";
            if (SceneVisibilityManager.instance.IsHidden(group.gameObject) || SceneVisibilityManager.instance.IsPickingDisabled(group.gameObject))
                return "装饰组在 Hierarchy 中已隐藏或锁定，请先恢复可见和可选。";
            return null;
        }

        public static Vector3 Contact(GameObject root)
        {
            var item = root.GetComponent<PlacementItem>();
            return item != null && item.contact != null ? item.contact.position : root.transform.position;
        }

        public static DecorationBrushGroup CreateGroup(Transform parent, DecorationBrushGroup.GroundPlane plane)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("请在非运行状态的场景中创建装饰组。");
            if (parent != null && (EditorUtility.IsPersistent(parent) || !parent.gameObject.scene.IsValid()
                || Vector3.Distance(parent.lossyScale, Vector3.one) > .001f))
                throw new InvalidOperationException("请选择场景中缩放为 1 的父节点。");
            var go = new GameObject("装饰组");
            if (parent != null)
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
                go.transform.SetParent(parent, false);
            }
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            var group = go.AddComponent<DecorationBrushGroup>();
            group.plane = plane;
            var world = parent != null ? parent.GetComponentInParent<ModuleWorld>() : null;
            if (world != null && world.view != null) group.groundFrame = world.view.frame;
            Undo.RegisterCreatedObjectUndo(go, "创建装饰组");
            EditorSceneManager.MarkSceneDirty(go.scene);
            return group;
        }
    }

    /// <summary>Distance sampling makes a stroke independent of editor event rate.</summary>
    internal sealed class DecorationBrushPath
    {
        Vector2 previous;
        float untilNext;
        readonly float step;
        public DecorationBrushPath(Vector2 start, float spacing) { previous = start; step = Mathf.Max(.02f, spacing); untilNext = step; }
        public void Move(Vector2 end, Action<Vector2> sample)
        {
            Vector2 delta = end - previous;
            float distance = delta.magnitude;
            if (distance < .00001f) return;
            Vector2 direction = delta / distance;
            float travelled = untilNext;
            // Cap pathological jumps to protect the editor; continue at the new cursor position.
            int count = 0;
            while (travelled <= distance && count++ < 256)
            {
                sample(previous + direction * travelled);
                travelled += step;
            }
            untilNext = count >= 256 ? step : travelled - distance;
            previous = end;
        }
    }

    internal sealed class DecorationBrushSpatialIndex
    {
        readonly float cellSize;
        readonly Dictionary<Vector2Int, List<Vector2>> cells = new Dictionary<Vector2Int, List<Vector2>>();
        public DecorationBrushSpatialIndex(float spacing) { cellSize = Mathf.Max(.1f, spacing); }
        Vector2Int Cell(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize));
        public void Add(Vector2 p)
        {
            var key = Cell(p);
            if (!cells.TryGetValue(key, out var values)) cells[key] = values = new List<Vector2>();
            values.Add(p);
        }
        public bool Occupied(Vector2 p)
        {
            var key = Cell(p);
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
                if (cells.TryGetValue(key + new Vector2Int(x, y), out var values))
                    foreach (var other in values) if ((p - other).sqrMagnitude < cellSize * cellSize) return true;
            return false;
        }
    }

    internal sealed class DecorationBrushStroke : IDisposable
    {
        public const int ObjectLimit = 1000;
        readonly DecorationBrushGroup group;
        readonly DecorationBrushSettings settings;
        readonly List<DecorationBrushEntry> entries;
        readonly System.Random random;
        readonly DecorationBrushSpatialIndex index;
        readonly int undoGroup;
        readonly bool erase;
        readonly List<GameObject> created = new List<GameObject>();
        bool ended;
        bool firstStamp = true;
        bool eraseRecorded;
        public int Changed { get; private set; }
        public int Blocked { get; private set; }

        public DecorationBrushStroke(DecorationBrushGroup target, DecorationBrushSettings options, bool eraseMode, int seed)
        {
            string error = DecorationBrushCore.TargetError(target);
            if (error != null) throw new InvalidOperationException(error);
            group = target; settings = options.Copy(); settings.Sanitize(); erase = eraseMode;
            entries = settings.entries.Where(e => e.enabled && DecorationBrushCore.PrefabError(e.prefab) == null).ToList();
            random = new System.Random(seed);
            index = new DecorationBrushSpatialIndex(settings.minSpacing);
            foreach (var root in group.paintedObjects)
                if (group.IsDirectChild(root)) index.Add(group.ToPlane(DecorationBrushCore.Contact(root)));
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(erase ? "擦除装饰（一笔）" : "绘制装饰（一笔）");
        }

        public void Stamp(Vector2 center, bool single = false)
        {
            if (ended || group == null || Changed >= ObjectLimit) return;
            if (erase) { Erase(center); return; }
            if (entries.Count == 0) return;
            // Samples overlap at radius / 2. Allocate density by swept area, not event count.
            float expected = single ? 1 : settings.density * settings.radius * settings.radius * (firstStamp ? Mathf.PI : 1);
            int attempts = single ? 1 : Mathf.Clamp((int)expected + (random.NextDouble() < expected % 1 ? 1 : 0), firstStamp ? 1 : 0, 128);
            firstStamp = false;
            for (int i = 0; i < attempts && Changed < ObjectLimit; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2;
                float radius = single ? 0 : Mathf.Sqrt((float)random.NextDouble()) * settings.radius;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (index.Occupied(point)) { Blocked++; continue; }
                Place(Pick(), point);
                index.Add(point);
            }
        }

        GameObject Pick()
        {
            float sum = entries.Sum(e => e.weight);
            double roll = random.NextDouble() * sum;
            foreach (var entry in entries) { roll -= entry.weight; if (roll <= 0) return entry.prefab; }
            return entries[entries.Count - 1].prefab;
        }

        void Place(GameObject prefab, Vector2 point)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
            try
            {
                root.transform.position += group.ToWorld(point) - DecorationBrushCore.Contact(root);
                foreach (var item in root.GetComponentsInChildren<PlacementItem>(true))
                {
                    item.xzGround = group.IsXZ;
                    item.sorter.frame = group.groundFrame;
                    item.sorter.xzGround = group.IsXZ;
                    item.sorter.useWorldAxesWhenUnbound = true;
                    item.visualRoot.localRotation = item.ExpectedVisualRotation;
                    item.sorter.Refresh();
                    foreach (var obj in new UnityEngine.Object[] { item, item.sorter, item.visualRoot, item.art })
                        PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
                Undo.RegisterCreatedObjectUndo(root, "绘制装饰");
                created.Add(root);
                Changed++;
            }
            catch { UnityEngine.Object.DestroyImmediate(root); throw; }
        }

        void Erase(Vector2 center)
        {
            var remove = group.paintedObjects.Where(o => group.IsDirectChild(o) &&
                !SceneVisibilityManager.instance.IsHidden(o) && !SceneVisibilityManager.instance.IsPickingDisabled(o) &&
                (group.ToPlane(DecorationBrushCore.Contact(o)) - center).sqrMagnitude <= settings.radius * settings.radius)
                .Distinct().Take(ObjectLimit - Changed).ToArray();
            if (remove.Length == 0) return;
            // Capture the intact reference list once, before any object is destroyed.
            // Per-stamp diffs can resurrect null references when a multi-object erase is redone.
            if (!eraseRecorded)
            {
                Undo.RegisterCompleteObjectUndo(group, "记录装饰归属");
                eraseRecorded = true;
            }
            foreach (var root in remove) { group.paintedObjects.Remove(root); Undo.DestroyObjectImmediate(root); Changed++; }
            EditorUtility.SetDirty(group);
            PrefabUtility.RecordPrefabInstancePropertyModifications(group);
        }

        public void Dispose()
        {
            if (ended) return;
            ended = true;
            // Record references after ALL creation operations. Interleaving reference-list diffs
            // with RegisterCreatedObjectUndo loses earlier object references on Unity 2022 redo.
            if (!erase && group != null && created.Count > 0)
            {
                Undo.RecordObject(group, "记录装饰归属");
                group.paintedObjects.RemoveAll(o => o == null);
                group.paintedObjects.AddRange(created.Where(o => o != null));
                EditorUtility.SetDirty(group);
                PrefabUtility.RecordPrefabInstancePropertyModifications(group);
            }
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(undoGroup);
            if (Changed > 0 && group != null) EditorSceneManager.MarkSceneDirty(group.gameObject.scene);
        }
    }
}
