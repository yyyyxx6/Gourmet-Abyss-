using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Modules.Editor
{
    public sealed class DecorationBrushWindow : EditorWindow
    {
        enum Mode { Paint, Single, Erase }
        DecorationBrushSettings settings;
        DecorationBrushGroup target;
        DecorationBrushPreset preset;
        DecorationBrushGroup.GroundPlane newPlane = DecorationBrushGroup.GroundPlane.XZ;
        Transform newParent;
        Mode mode;
        Vector2 scroll;
        bool active, help;
        Tool previousTool;
        string status = "准备好资源后，点击“开始绘制”。";
        DecorationBrushStroke stroke;
        DecorationBrushPath path;
        bool strokeErase;
        int control;
        double lastChange;

        [MenuItem("场景/装饰笔刷", false, 100)]
        public static void Open() => GetWindow<DecorationBrushWindow>("装饰笔刷").Show();

        [MenuItem("Assets/场景物件/加入装饰笔刷", false, 230)]
        static void AddSelection()
        {
            var window = GetWindow<DecorationBrushWindow>("装饰笔刷");
            window.AddResources(Selection.objects); window.Show();
        }

        public static void OpenFor(DecorationBrushGroup group)
        {
            var window = GetWindow<DecorationBrushWindow>("装饰笔刷");
            window.Stop(); window.target = group; window.Persist(); window.Show();
        }

        void OnEnable()
        {
            titleContent = new GUIContent("装饰笔刷");
            minSize = new Vector2(370, 520);
            var session = DecorationBrushSession.instance;
            settings = session.settings?.Copy() ?? new DecorationBrushSettings();
            settings.Sanitize();
            if (!string.IsNullOrEmpty(session.targetId) && GlobalObjectId.TryParse(session.targetId, out var id))
                target = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as DecorationBrushGroup;
            if (!string.IsNullOrEmpty(session.presetPath)) preset = AssetDatabase.LoadAssetAtPath<DecorationBrushPreset>(session.presetPath);
            SceneView.duringSceneGui += DuringScene;
            Undo.undoRedoPerformed += UndoRedo;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
        }

        void OnDisable()
        {
            Stop(); Persist();
            SceneView.duringSceneGui -= DuringScene;
            Undo.undoRedoPerformed -= UndoRedo;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
        }

        void PlayChanged(PlayModeStateChange state) => Stop();
        void UndoRedo() { EndStroke(); Repaint(); SceneView.RepaintAll(); }
        void Tick()
        {
            if (active && (EditorApplication.isPlayingOrWillChangePlaymode || Tools.current != Tool.None
                || PrefabStageUtility.GetCurrentPrefabStage() != null)) Stop();
            if (stroke != null && !(focusedWindow is SceneView)) EndStroke();
        }

        void Persist()
        {
            if (settings == null) return;
            var session = DecorationBrushSession.instance;
            session.settings = settings.Copy();
            session.targetId = target != null ? GlobalObjectId.GetGlobalObjectIdSlow(target).ToString() : "";
            session.presetPath = preset != null ? AssetDatabase.GetAssetPath(preset) : "";
            session.Store();
        }

        void OnGUI()
        {
            if (settings == null) return;
            EditorGUIUtility.labelWidth = Mathf.Min(130, position.width * .34f);
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                EditorGUILayout.LabelField("场景装饰", EditorStyles.boldLabel);
                help = GUILayout.Toggle(help, "使用说明", EditorStyles.miniButton, GUILayout.Width(72));
                GUILayout.Space(8);
            }
            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                int next = GUILayout.Toolbar((int)mode, new[] { "连续绘制", "单个放置", "擦除" }, GUILayout.Height(28));
                if (next != (int)mode) { EndStroke(); mode = (Mode)next; SceneView.RepaintAll(); }
                GUILayout.Space(8);
            }
            string error = ReadyError();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                Color old = GUI.backgroundColor;
                if (active) GUI.backgroundColor = new Color(.45f, .85f, .7f);
                using (new EditorGUI.DisabledScope(!active && error != null))
                    if (GUILayout.Button(active ? "结束绘制  ·  Esc" : "开始绘制", GUILayout.Height(32)))
                    { if (active) Stop(); else StartBrush(); }
                GUI.backgroundColor = old;
                GUILayout.Space(8);
            }
            GUILayout.Space(4);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (help)
                EditorGUILayout.HelpBox("① 创建装饰组或选择已有组。\n② 从 Project 拖入预制体并勾选要刷的资源。\n③ 开始绘制，在 Scene 中按住左键拖动。\n\nShift：临时擦除；[ / ]：缩小 / 放大；Esc：结束；Ctrl+Z：撤销整笔。Alt、右键和中键保留场景浏览操作。\n\n单个放置每次点击一个物件。擦除只影响当前组中由笔刷创建的物件。完成后正常保存场景。", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("1  绘制到哪里", EditorStyles.boldLabel);
                var nextTarget = (DecorationBrushGroup)EditorGUILayout.ObjectField("当前装饰组", target, typeof(DecorationBrushGroup), true);
                if (nextTarget != target) { Stop(); target = nextTarget; }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("使用场景中选中的组"))
                    {
                        var found = Selection.activeTransform != null ? Selection.activeTransform.GetComponentInParent<DecorationBrushGroup>() : null;
                        if (found != null) { Stop(); target = found; }
                        else status = "请在 Hierarchy 中选中装饰组或它内部的物件。";
                    }
                    using (new EditorGUI.DisabledScope(target == null))
                        if (GUILayout.Button("定位", GUILayout.Width(46))) EditorGUIUtility.PingObject(target.gameObject);
                }
                if (target == null)
                {
                    newParent = (Transform)EditorGUILayout.ObjectField(new GUIContent("放在哪个节点下", "留空会创建在当前场景根目录。"), newParent, typeof(Transform), true);
                    newPlane = (DecorationBrushGroup.GroundPlane)EditorGUILayout.Popup("地图平面", (int)newPlane, new[] { "平面地图（XY）", "地面地图（XZ）" });
                    if (GUILayout.Button("创建装饰组", GUILayout.Height(25))) CreateGroup();
                }
                else
                {
                    int count = target.paintedObjects.Count(o => target.IsDirectChild(o));
                    EditorGUILayout.LabelField($"{target.gameObject.scene.name}  ·  {count} 个笔刷物件  ·  {(target.IsXZ ? "XZ 地面" : "XY 平面")}", EditorStyles.miniLabel);
                    if (GUILayout.Button("另建一个装饰组"))
                    { Stop(); newParent = target.transform.parent; newPlane = target.plane; target = null; }
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("2  选择装饰", EditorStyles.boldLabel);
                DrawPalette();
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("3  笔刷设置", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(mode == Mode.Single))
                    settings.radius = EditorGUILayout.Slider(new GUIContent("半径", "Scene 中圆圈的半径，单位与场景一致。也可以按 [ / ] 调整。"), settings.radius, .1f, 30);
                using (new EditorGUI.DisabledScope(mode != Mode.Paint))
                    settings.density = EditorGUILayout.Slider(new GUIContent("密度", "越大越密；最小间距始终优先，避免在原地重复堆叠。"), settings.density, .05f, 10);
                using (new EditorGUI.DisabledScope(mode == Mode.Erase))
                    settings.minSpacing = EditorGUILayout.Slider(new GUIContent("最小间距", "与当前组已有笔刷物件的接地点保持距离，不是碰撞体避让。"), settings.minSpacing, .1f, 20);
                EditorGUILayout.LabelField("保留源预制体尺寸与朝向；按接地点放置。", EditorStyles.wordWrappedMiniLabel);
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("常用组合", EditorStyles.boldLabel);
                var nextPreset = (DecorationBrushPreset)EditorGUILayout.ObjectField("笔刷预设", preset, typeof(DecorationBrushPreset), false);
                if (nextPreset != preset)
                {
                    Stop(); preset = nextPreset;
                    if (preset != null) { settings = preset.settings.Copy(); settings.Sanitize(); status = "已载入：" + preset.name; }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("另存为预设…")) SavePreset(true);
                    using (new EditorGUI.DisabledScope(preset == null))
                        if (GUILayout.Button("更新当前预设")) SavePreset(false);
                }
                EditorGUILayout.LabelField("预设保存资源和笔刷参数，便于团队复用。", EditorStyles.wordWrappedMiniLabel);
            }
            if (EditorGUI.EndChangeCheck()) { settings.Sanitize(); Persist(); SceneView.RepaintAll(); }
            error = ReadyError();
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Info);
            EditorGUILayout.HelpBox(status, MessageType.None);
            EditorGUILayout.EndScrollView();
            GUILayout.Space(4);
            EditorGUILayout.LabelField("Shift 擦除    [ / ] 半径    Ctrl+Z 撤销    Esc 退出", EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(6);
        }

        void DrawPalette()
        {
            var drop = GUILayoutUtility.GetRect(0, 48, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "将装饰预制体拖到这里\n可以一次拖入多个", EditorStyles.helpBox);
            Event evt = Event.current;
            if (drop.Contains(evt.mousePosition) && (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); AddResources(DragAndDrop.objectReferences); }
                evt.Use();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("加入 Project 中选中的资源")) AddResources(Selection.objects);
                if (GUILayout.Button("全选", GUILayout.Width(44))) foreach (var entry in settings.entries) entry.enabled = true;
            }
            int remove = -1;
            for (int i = 0; i < settings.entries.Count; i++)
            {
                var entry = settings.entries[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.Width(16));
                    var texture = entry.prefab != null ? AssetPreview.GetAssetPreview(entry.prefab) ?? AssetPreview.GetMiniThumbnail(entry.prefab) : null;
                    GUILayout.Label(texture, GUILayout.Width(36), GUILayout.Height(36));
                    using (new EditorGUILayout.VerticalScope())
                    {
                        if (GUILayout.Button(new GUIContent(entry.prefab != null ? entry.prefab.name : "资源已丢失", "点击在 Project 中定位源预制体。"), EditorStyles.miniButton))
                            if (entry.prefab != null) EditorGUIUtility.PingObject(entry.prefab);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField("出现权重", GUILayout.Width(56));
                            entry.weight = EditorGUILayout.FloatField(entry.weight, GUILayout.MinWidth(35));
                        }
                    }
                    if (GUILayout.Button(new GUIContent("×", "从笔刷资源列表移除，不删除项目资源。"), GUILayout.Width(24), GUILayout.Height(32))) remove = i;
                }
                string invalid = DecorationBrushCore.PrefabError(entry.prefab);
                if (invalid != null) EditorGUILayout.HelpBox(invalid, MessageType.Warning);
            }
            if (remove >= 0) settings.entries.RemoveAt(remove);
            if (settings.entries.Count > 0)
                EditorGUILayout.LabelField("勾选多个资源时随机选取；权重越大，出现越多。", EditorStyles.wordWrappedMiniLabel);
        }

        void AddResources(UnityEngine.Object[] objects)
        {
            EndStroke();
            int added = 0;
            string reason = null;
            foreach (var obj in objects)
            {
                var prefab = obj as GameObject;
                if (prefab != null && !EditorUtility.IsPersistent(prefab)) prefab = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
                string error = DecorationBrushCore.PrefabError(prefab);
                if (error != null)
                {
                    reason = obj is Sprite || obj is Texture2D
                        ? "图片请先在 Project 中右键 → 场景物件 → 创建场景物件预制体，再拖入笔刷。" : error;
                    continue;
                }
                if (settings.entries.Any(e => e.prefab == prefab)) continue;
                settings.entries.Add(new DecorationBrushEntry { prefab = prefab }); added++;
            }
            status = $"加入 {added} 个资源。" + (reason == null ? "" : " " + reason);
            Persist(); Repaint();
        }

        void CreateGroup()
        {
            try { target = DecorationBrushCore.CreateGroup(newParent, newPlane); status = "装饰组已创建，可以改名后继续绘制。"; Persist(); }
            catch (Exception ex) { status = ex.Message; }
        }

        void SavePreset(bool create)
        {
            Stop(); settings.Sanitize();
            if (create)
            {
                DecorationBrushWorkspace.EnsureFolder(DecorationBrushWorkspace.Presets);
                string file = EditorUtility.SaveFilePanelInProject("保存笔刷组合", "装饰笔刷预设", "asset", "保存资源组合与参数，其他美术可以直接载入。", DecorationBrushWorkspace.Presets);
                if (string.IsNullOrEmpty(file)) return;
                // Never replace an unrelated asset if a path was selected manually.
                if (AssetDatabase.LoadMainAssetAtPath(file) != null) { status = "该路径已有资源，请另选名称；更新预设请用“更新当前预设”。"; return; }
                preset = CreateInstance<DecorationBrushPreset>();
                preset.settings = settings.Copy();
                AssetDatabase.CreateAsset(preset, file);
            }
            else { Undo.RecordObject(preset, "更新笔刷预设"); preset.settings = settings.Copy(); EditorUtility.SetDirty(preset); }
            AssetDatabase.SaveAssetIfDirty(preset); status = "预设已保存：" + preset.name; Persist();
        }

        string ReadyError()
        {
            string error = DecorationBrushCore.TargetError(target);
            if (error != null) return error;
            if (mode != Mode.Erase && !settings.entries.Any(e => e.enabled && DecorationBrushCore.PrefabError(e.prefab) == null))
                return "拖入预制体，并至少勾选一个可用资源。";
            return null;
        }

        void StartBrush()
        {
            if (ReadyError() != null) return;
            active = true; previousTool = Tools.current; Tools.current = Tool.None;
            status = "已开始。在 Scene 视图中绘制；按 Esc 返回选择。";
            SceneView.RepaintAll();
        }

        void Stop()
        {
            EndStroke();
            if (active && Tools.current == Tool.None) Tools.current = previousTool;
            active = false; SceneView.RepaintAll(); Repaint();
        }

        void EndStroke()
        {
            if (stroke != null)
            {
                int count = stroke.Changed, blocked = stroke.Blocked;
                stroke.Dispose(); stroke = null; path = null;
                status = $"本笔{(strokeErase ? "擦除" : "放置")} {count} 个物件。";
                if (blocked > 0) status += " 部分位置因最小间距而跳过。";
                if (count >= DecorationBrushStroke.ObjectLimit) status += " 已到单笔上限，请松开后继续。";
                Repaint();
            }
            if (control != 0 && GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
            control = 0;
        }

        void DuringScene(SceneView view)
        {
            if (!active) return;
            Event evt = Event.current;
            int id = GUIUtility.GetControlID("DecorationBrush".GetHashCode(), FocusType.Passive);
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            { Stop(); evt.Use(); return; }
            if (evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.LeftBracket || evt.keyCode == KeyCode.RightBracket))
            {
                EndStroke(); settings.radius *= evt.keyCode == KeyCode.LeftBracket ? .8f : 1.25f;
                settings.Sanitize(); Persist(); Repaint(); evt.Use(); view.Repaint(); return;
            }
            if (evt.rawType == EventType.MouseUp && evt.button == 0) { bool owned = stroke != null; EndStroke(); if (owned) evt.Use(); }
            if (evt.type == EventType.MouseLeaveWindow) { EndStroke(); return; }
            if (ReadyError() != null) { EndStroke(); return; }
            if (evt.alt || evt.control || evt.command || Tools.viewToolActive)
            { EndStroke(); return; }
            if (evt.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            if (!target.Ground.Raycast(ray, out float distance) || distance > 10000)
            {
                if (evt.type == EventType.Repaint) DrawSceneHint("视角与绘制平面平行，请调整 Scene 镜头。");
                return;
            }
            Vector3 point = ray.GetPoint(distance);
            bool erasing = mode == Mode.Erase || evt.shift;
            if (evt.type == EventType.Repaint)
            {
                Handles.color = erasing ? new Color(1, .4f, .3f, .9f) : new Color(.25f, .95f, .75f, .9f);
                float radius = mode == Mode.Single && !erasing ? .12f : settings.radius;
                Handles.DrawWireDisc(point, target.Normal, radius);
                Handles.DrawLine(point - target.Right * .12f, point + target.Right * .12f);
                Handles.DrawLine(point - target.Forward * .12f, point + target.Forward * .12f);
                if (mode == Mode.Single && !erasing)
                {
                    var available = settings.entries.Where(e => e.enabled && e.prefab != null).ToArray();
                    var entry = available.Length == 1 ? available[0] : null;
                    var item = entry?.prefab.GetComponent<PlacementItem>();
                    if (item != null)
                    {
                        var r = target.Right * item.footprint.x * .5f;
                        var f = target.Forward * item.footprint.y * .5f;
                        Handles.DrawAAPolyLine(point-r-f, point+r-f, point+r+f, point-r+f, point-r-f);
                    }
                }
                DrawSceneHint($"{(erasing ? "擦除" : mode == Mode.Single ? "单个放置" : "连续绘制")} → {target.name}    Shift 擦除 · Esc 结束");
            }
            try
            {
                if (evt.type == EventType.MouseDown && evt.button == 0 && HandleUtility.nearestControl == id)
                {
                    EndStroke(); control = id; GUIUtility.hotControl = id;
                    strokeErase = erasing;
                    stroke = new DecorationBrushStroke(target, settings, erasing, unchecked(Environment.TickCount));
                    var center = target.ToPlane(point);
                    path = new DecorationBrushPath(center, Mathf.Max(.05f, settings.radius * .5f));
                    stroke.Stamp(center, mode == Mode.Single); evt.Use(); view.Repaint();
                }
                else if (evt.type == EventType.MouseDrag && evt.button == 0 && stroke != null && GUIUtility.hotControl == id)
                {
                    if (erasing != strokeErase) { EndStroke(); evt.Use(); return; }
                    if (mode != Mode.Single || strokeErase) path.Move(target.ToPlane(point), p => stroke.Stamp(p));
                    evt.Use(); view.Repaint();
                }
            }
            catch (Exception ex) { Stop(); status = "本笔已停止：" + ex.Message; Debug.LogException(ex); }
            if (evt.type == EventType.MouseMove || evt.type == EventType.KeyUp || evt.type == EventType.KeyDown) view.Repaint();
            if (stroke != null && EditorApplication.timeSinceStartup - lastChange > .15)
            { lastChange = EditorApplication.timeSinceStartup; Repaint(); }
        }

        static void DrawSceneHint(string text)
        {
            Handles.BeginGUI();
            GUI.Label(new Rect(12, 12, 440, 38), text, EditorStyles.helpBox);
            Handles.EndGUI();
        }
    }

    [CustomEditor(typeof(DecorationBrushGroup))]
    internal sealed class DecorationBrushGroupInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var group = (DecorationBrushGroup)target;
            EditorGUILayout.HelpBox("笔刷只擦除该组中由笔刷创建的直属物件。移到组外的物件将不再被擦除；普通手动摆入的物件不会被擦除。", MessageType.Info);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("plane"), new GUIContent("地图平面"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("groundFrame"), new GUIContent("模块地面坐标（可选）"));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.LabelField("笔刷物件", group.paintedObjects.Count(o => group.IsDirectChild(o)).ToString());
            EditorGUILayout.HelpBox("组的位置决定绘制平面高度；有模块地面坐标时，方向由模块提供。开始大量摆放前确定平面。", MessageType.None);
            if (GUILayout.Button("在装饰笔刷中编辑", GUILayout.Height(28))) DecorationBrushWindow.OpenFor(group);
        }
    }
}
