using System;
using System.Collections.Generic;
using System.Linq;
using Game.Modules;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Modules.Editor
{
    public static class RestaurantModuleMigration
    {
        const string Folder = RestaurantModuleBuilder.Folder;
        const string UIArt = "Assets/NewVersion/UI/餐厅ui/";

        [MenuItem("Tools/Modules/Upgrade Restaurant Presentation")]
        public static void Upgrade()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
            var adapter = Object.FindObjectOfType<RestaurantModuleAdapter>(true);
            if (adapter == null) throw new InvalidOperationException("Build restaurant first.");
            if (adapter.pair.world.GetComponentInChildren<PlacementItem>(true) != null)
                throw new InvalidOperationException("Restaurant uses standard placement items. Edit prefabs; legacy upgrade cannot overwrite this hierarchy.");
            UpgradeWorld();
            UpgradeHUD();
            var pair = adapter.pair;
            var entrySO=new SerializedObject(adapter.entry);
            entrySO.FindProperty("moduleCameraView").objectReferenceValue=pair.world.view;
            entrySO.ApplyModifiedPropertiesWithoutUndo();
            var scope = pair.GetComponent<ModulePresentationScope>() ?? pair.gameObject.AddComponent<ModulePresentationScope>();
            pair.presentation = scope;
            scope.targetCamera = Camera.main;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) throw new InvalidOperationException("URP pipeline required.");
            InstallRenderer(pipeline, out int count);
            // Existing actors still use ProPixelizer materials. Keep their renderer until art migration.
            scope.rendererIndex = -1;
            scope.rendererCount = count;
            var suspend = new List<Behaviour>();
            foreach (var c in scope.targetCamera.GetComponents<MonoBehaviour>())
                if (c.GetType().FullName == "ProPixelizer.CameraSnapSRP") suspend.Add(c);
            foreach (var c in scope.targetCamera.GetComponentsInChildren<Camera>(true))
                if (c != scope.targetCamera) suspend.Add(c);
            var shopSO=new SerializedObject(adapter.shop);
            var shopGO=shopSO.FindProperty("shopUICanvas").objectReferenceValue as GameObject;
            if(shopGO!=null)
            {
                var shopCanvas=shopGO.GetComponent<Canvas>();suspend.Add(shopCanvas);
                var popup=pair.GetComponent<ModuleLegacyPopup>()??pair.gameObject.AddComponent<ModuleLegacyPopup>();
                popup.content=shopGO.transform.Find("shopPanel") as RectTransform;popup.sourceCanvas=shopCanvas;
                adapter.recipesPopup=popup;EditorUtility.SetDirty(popup);
            }
            if(adapter.decoration!=null)
            {
                var decorationSO=new SerializedObject(adapter.decoration);
                var panel=decorationSO.FindProperty("panelRoot").objectReferenceValue as GameObject;
                var popup=adapter.decorationPopup;
                if(popup==null)popup=pair.gameObject.AddComponent<ModuleLegacyPopup>();
                popup.content=panel.GetComponent<RectTransform>();popup.sourceCanvas=panel.GetComponentInParent<Canvas>();
                adapter.decorationPopup=popup;EditorUtility.SetDirty(popup);
            }
            var legacyWorld=adapter.restaurant.transform;
            while(legacyWorld.parent!=null)legacyWorld=legacyWorld.parent;
            suspend.AddRange(legacyWorld.GetComponentsInChildren<Canvas>(true));
            scope.hideRenderersWhileOpen=legacyWorld.GetComponentsInChildren<SpriteRenderer>(true).Cast<Renderer>().ToArray();
            scope.suspendWhileOpen = suspend.Distinct().ToArray();
            var mainUI = adapter.legacyHUD.transform.parent;
            scope.hideWhileOpen = mainUI.Cast<Transform>()
                .Where(t => t != adapter.legacyHUD.transform && t.name != "GlobalMessageParent")
                .Select(t => t.GetComponent<CanvasGroup>() ?? t.gameObject.AddComponent<CanvasGroup>()).ToArray();
            ConfigureBindings(adapter);
            EditorUtility.SetDirty(scope); EditorUtility.SetDirty(pair); EditorUtility.SetDirty(adapter);
            EditorSceneManager.MarkSceneDirty(adapter.gameObject.scene);
            EditorSceneManager.SaveScene(adapter.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Modules] Restaurant presentation upgraded; gameplay counts unchanged.");
        }

        static int InstallRenderer(UniversalRenderPipelineAsset pipeline, out int count)
        {
            const string path = "Assets/Modules/ModuleRenderer.asset";
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null)
            {
                data = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/URP-Balanced-Renderer.asset"));
                data.name = "ModuleRenderer";
                data.rendererFeatures.Clear();
                AssetDatabase.CreateAsset(data, path);
            }
            var so = new SerializedObject(pipeline);
            var list = so.FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) { count = list.arraySize; return i; }
            int index = list.arraySize; list.arraySize++;
            list.GetArrayElementAtIndex(index).objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(pipeline);
            count = list.arraySize; return index;
        }

        static void UpgradeWorld()
        {
            string path = Folder + "/RestaurantWorld.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var world = root.GetComponent<ModuleWorld>();
                var visual = root.transform.Find("VisualRoot");
                var background = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/NewVersion/map/餐厅1.2/餐厅背景.png");
                if (background == null) throw new InvalidOperationException("餐厅背景必须直接作为完整 Sprite 导入。");
                var ground = visual.Find("Ground");
                var groundRenderer = ground.GetComponent<SpriteRenderer>();
                groundRenderer.sprite = background;
                groundRenderer.enabled = true;
                ground.localScale = Vector3.one * (23.5f / background.bounds.size.x);
                ground.localPosition = new Vector3(0, 0, .15f);
                foreach (var layerName in new[] { "BackWall", "EntranceRail" })
                {
                    var layer = visual.Find(layerName);
                    if (layer == null) continue;
                    var renderer = layer.GetComponent<SpriteRenderer>();
                    if (renderer != null) { renderer.sprite = null; renderer.enabled = false; }
                    var sorter = layer.GetComponent<PlanarSprite>();
                    if (sorter != null) sorter.enabled = false;
                }
                foreach (var anchor in world.anchors)
                {
                    var parts=anchor.id.Split('/');
                    if (parts.Length < 2 || (parts[0] != "seat" && parts[0] != "table")) continue;
                    var set = visual.Find("TableSet" + parts[1]);
                    if (set != null) anchor.point.SetParent(set,true);
                }
                foreach(var facing in root.GetComponentsInChildren<PlanarSprite>())
                    if(facing.ground && facing.name!="Ground") {facing.orderOffset=10;facing.Refresh();}
                var surround=visual.Find("SurroundingGround");
                if (surround == null)
                {
                    var go=new GameObject("SurroundingGround");go.transform.SetParent(visual,false);go.transform.localPosition=new Vector3(0,0,.2f);go.AddComponent<SpriteRenderer>();surround=go.transform;
                }
                var grassSprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/NewVersion/map/餐厅/canting_0009_caopin.png");
                if (grassSprite == null) throw new InvalidOperationException("周边草地必须作为完整 Sprite 导入。");
                surround.GetComponent<SpriteRenderer>().sprite=grassSprite;
                surround.localScale=new Vector3(70f/grassSprite.bounds.size.x,50f/grassSprite.bounds.size.y,1);
                surround.GetComponent<SpriteRenderer>().sortingOrder=-1100;
                var front=visual.Find("EntranceRail");front.localPosition=new Vector3(0,-6,0);
                root.GetComponent<ModuleWorld>().view.profile.distance=31;
                EditorUtility.SetDirty(root.GetComponent<ModuleWorld>().view.profile);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static void UpgradeHUD()
        {
            string path=Folder+"/RestaurantHUD.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                AddImage(root.transform,"MoneyStrip","金币展示.png",new Vector2(0,1),new Vector2(153,-83),new Vector2(183,40));
                AddImage(root.transform,"SecondaryStrip","金币展示.png",new Vector2(0,1),new Vector2(153,-149),new Vector2(183,40));
                AddImage(root.transform,"CoinIcon","金币图标·.png",new Vector2(0,1),new Vector2(79,-83),new Vector2(45,45));
                var money=root.GetComponent<ModuleHUD>().GetText("money");money.rectTransform.anchoredPosition=new Vector2(164,-83);money.transform.SetAsLastSibling();
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static void AddImage(Transform parent,string name,string file,Vector2 anchor,Vector2 pos,Vector2 size)
        {
            if(parent.Find(name)!=null)return;
            var importer=AssetImporter.GetAtPath(UIArt+file) as TextureImporter;
            if(importer==null)throw new InvalidOperationException("Missing UI art: "+file);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.SaveAndReimport();
            var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));go.transform.SetParent(parent,false);
            var image=go.GetComponent<Image>();image.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(UIArt+file);image.raycastTarget=false;image.preserveAspect=true;
            var rt=image.rectTransform;rt.anchorMin=rt.anchorMax=anchor;rt.anchoredPosition=pos;rt.sizeDelta=size;
        }
        static void ConfigureBindings(RestaurantModuleAdapter adapter)
        {
            var pair=adapter.pair;
            var bindings=pair.GetComponent<ModuleAnchorBindings>()??pair.gameObject.AddComponent<ModuleAnchorBindings>();
            bindings.world=pair.world;
            var list=new List<ModuleAnchorBindings.Binding>();
            for(int i=0;i<Mathf.Min(2,adapter.restaurant.allPots.Count);i++)
                list.Add(new ModuleAnchorBindings.Binding{anchorId="cook/"+i,target=adapter.restaurant.allPots[i].transform});
            for(int i=0;i<Mathf.Min(2,adapter.restaurant.allPlates.Count);i++)
                list.Add(new ModuleAnchorBindings.Binding{anchorId="plate/"+i,target=adapter.restaurant.allPlates[i].transform});
            // Preserve each table's original unlock/seat ownership; never sort seats across tables.
            var tables=Object.FindObjectsOfType<Table>(true).Where(t=>t.GetComponentsInChildren<RestaurantSeat>(true).Length>0)
                .OrderBy(t=>t.transform.position.x).ToArray();
            for(int table=0;table<tables.Length && table<6;table++)
            {
                var seats=tables[table].GetComponentsInChildren<RestaurantSeat>(true);
                for(int seat=0;seat<seats.Length && seat<4;seat++)
                    list.Add(new ModuleAnchorBindings.Binding{anchorId="seat/"+table+"/"+seat,target=seats[seat].transform});
            }
            bindings.bindings=list.ToArray();bindings.Apply();EditorUtility.SetDirty(bindings);
        }
    }
}
