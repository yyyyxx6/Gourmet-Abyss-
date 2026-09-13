using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Modules;
using GourmetAbyss.CameraSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Modules.Editor
{
    public static class RestaurantModuleBuilder
    {
        public const string Folder = "Assets/Modules/Restaurant";
        const string Art = "Assets/NewVersion/map/餐厅1.2/";
        const string UIArt = "Assets/NewVersion/UI/餐厅ui/";
        static readonly Vector3 Origin = new Vector3(319.4f, 87.6f, -62f);
        static readonly List<ModuleWorld.Anchor> anchors = new List<ModuleWorld.Anchor>();
        static ModuleWorld world;
        static Transform visualRoot;
        static PlanarPerspectiveProfile profile;

        [MenuItem("Tools/Modules/Build Restaurant")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before building.");
            if (Object.FindObjectOfType<RestaurantModuleAdapter>(true) != null)
                throw new InvalidOperationException("Restaurant is already installed. Edit prefab instances; do not rebuild over edits.");
            var entry = Object.FindObjectOfType<RestaurantEntryPoint>(true);
            var restaurant = Object.FindObjectOfType<RestaurantPanel>(true);
            if (entry == null || restaurant == null) throw new InvalidOperationException("Open UpGround first.");
            Directory.CreateDirectory("Library/ModuleBackup");
            File.Copy("Assets/Scenes/UpGround.unity", "Library/ModuleBackup/UpGround-before-modules.unity", false);
            Directory.CreateDirectory(Folder + "/Sprites");
            AssetDatabase.Refresh();
            profile = ScriptableObject.CreateInstance<PlanarPerspectiveProfile>();
            profile.distance = 27f; profile.tiltFromNormal = 45f; profile.fieldOfView = 40f;
            profile.viewStandard = AssetDatabase.LoadAssetAtPath<WorldViewStandard>(PlacementTools.StandardPath);
            profile.panLimit = 1.5f;
            AssetDatabase.CreateAsset(profile, Folder + "/RestaurantPerspective.asset");
            BuildWorld();
            var worldAsset = PrefabUtility.SaveAsPrefabAsset(world.gameObject, Folder + "/RestaurantWorld.prefab");
            Object.DestroyImmediate(world.gameObject);
            var hud = BuildHUD();
            var hudAsset = PrefabUtility.SaveAsPrefabAsset(hud.gameObject, Folder + "/RestaurantHUD.prefab");
            Object.DestroyImmediate(hud.gameObject);
            var definition = ScriptableObject.CreateInstance<ModuleDefinition>();
            definition.moduleId = "restaurant";
            definition.worldPrefab = worldAsset.GetComponent<ModuleWorld>();
            definition.hudPrefab = hudAsset.GetComponent<ModuleHUD>();
            definition.requiredAnchorIds = "entry\nplayer-seat\ncook/0\ncook/1\nplate/0\nplate/1\ncounter";
            AssetDatabase.CreateAsset(definition, Folder + "/RestaurantModule.asset");
            var pair = ModuleTools.Install(definition, Origin);
            var adapter = pair.gameObject.AddComponent<RestaurantModuleAdapter>();
            adapter.pair = pair; adapter.entry = entry; adapter.restaurant = restaurant;
            adapter.shop = restaurant.GetComponent<ShopInteraction>();
            adapter.decoration = Object.FindObjectOfType<RestaurantDecorationPanelUI>(true);
            var entrySO = new SerializedObject(entry);
            adapter.legacyHUD = entrySO.FindProperty("restaurantActiveContent").objectReferenceValue as GameObject;
            entrySO.FindProperty("playerSeatAnchor").objectReferenceValue = pair.world.GetAnchor("player-seat");
            entrySO.ApplyModifiedPropertiesWithoutUndo();

            // Preserve old gameplay roots, object references and events. Hide only replaced art.
            var all = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var oldArt = all.First(t => t.name == "RestaurantArt_1_2");
            foreach (var r in oldArt.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var oldNew = all.First(t => t.name == "RestaurantNew");
            foreach (var r in oldNew.GetComponentsInChildren<SpriteRenderer>(true)) r.enabled = false;
            foreach (var r in restaurant.GetComponentsInChildren<SpriteRenderer>(true))
                if (r.sprite != null && AssetDatabase.GetAssetPath(r.sprite).Contains("NewVersion")) r.enabled = false;
            if (adapter.legacyHUD != null)
                foreach (var b in adapter.legacyHUD.GetComponentsInChildren<Button>(true))
                    if (b.transform.parent == adapter.legacyHUD.transform) b.gameObject.SetActive(false);
            // Native screen-space presentation can be reused without moving world-space recipe widgets.
            var oldSlots = restaurant.allDishQueueSlots.ToArray();
            for (int i = 0; i < oldSlots.Length && i < 5; i++)
            {
                var slot = oldSlots[i]; if (slot == null) continue;
                var rt = slot.GetComponent<RectTransform>();
                rt.SetParent(pair.hud.GetRegion("cooking-queue"), false);
                Rect(rt, new Vector2(0.5f,0.5f), new Vector2(-268f + i * 134f, 0), new Vector2(112,112));
                var so = new SerializedObject(slot);
                var bg = so.FindProperty("slotBackground").objectReferenceValue as Image;
                if (bg != null) bg.color = Color.clear;
                var count = so.FindProperty("itemCountText").objectReferenceValue as Text;
                if (count != null) { count.color = new Color(.25f,.12f,.04f); count.fontSize = 24; }
            }
            // Existing queue slots remain scene overrides: their serialized logic/event references survive.
            EditorSceneManager.MarkSceneDirty(entry.gameObject.scene);
            EditorSceneManager.SaveScene(entry.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Modules] Restaurant pair installed. Validate via Tools/Modules/Validate Selected Pair.");
            Selection.activeGameObject = pair.gameObject;
            RestaurantModuleMigration.Upgrade();
        }

        static void BuildWorld()
        {
            anchors.Clear();
            world = new GameObject("RestaurantWorld").AddComponent<ModuleWorld>();
            visualRoot = new GameObject("VisualRoot").transform; visualRoot.SetParent(world.transform, false);
            var frame = new GameObject("GroundFrame").transform; frame.SetParent(world.transform, false);
            world.view = world.gameObject.AddComponent<PlanarPerspectiveView>();
            world.view.frame = frame; world.view.profile = profile;
            // Keep the complete artist source as the only background scale reference.
            // Do not create normalized/cropped intermediate sprites from an imported texture.
            Sprite background = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "餐厅背景.png");
            if (background == null) throw new InvalidOperationException("餐厅背景必须作为完整 Sprite 导入。");
            Place("Ground", background, new Vector2(0,0), 23.5f, false);
            for(int i=0;i<2;i++)
            {
                Place("Stove"+i, Load("灶台1.png"), new Vector2(-6.2f+i*2.5f,4.3f), 2.2f, false);
                Anchor("cook/"+i, new Vector2(-6.2f+i*2.5f,3.8f));
                Place("CookMat"+i,Load("员工工位.png"),new Vector2(-6.2f+i*2.5f,3.3f),1.8f,true);
                Place("ServingTable"+i,Load("摆菜台.png"),new Vector2(-9f,1.4f-i*2.7f),1.65f,false);
                Anchor("plate/"+i,new Vector2(-8.2f,1.4f-i*2.7f));
                Place("ServingMat"+i,Load("员工工位.png"),new Vector2(-7.5f,1.2f-i*2.7f),1.8f,true);
            }
            Place("Cabinet",Load("柜子.png"),new Vector2(-1.2f,4.3f),1.65f,false);
            Place("Fridge",Load("柜子（冰箱）.png"),new Vector2(.9f,4.3f),1.65f,false);
            for(int row=0;row<2;row++) for(int col=0;col<3;col++)
            {
                int index=row*3+col; float x=-.4f+col*4.1f, y=1.3f-row*3.5f;
                var tableRoot=new GameObject("TableSet"+index).transform;
                tableRoot.SetParent(visualRoot,false); tableRoot.localPosition=new Vector3(x,y,0);
                var table=Place("Table",Load("餐桌.png"),new Vector2(x,y-1),1.7f,false);
                table.SetParent(tableRoot,true);
                Anchor("table/"+index,new Vector2(x,y));
                for(int seat=0;seat<4;seat++)
                {
                    Vector2 pos=new Vector2(x+(seat%2==0?-1.4f:1.4f),y+(seat<2?.8f:-.8f));
                    var chair=Place("Chair"+seat,Load("座位.png"),pos,.95f,false);
                    chair.SetParent(tableRoot,true); Anchor("seat/"+index+"/"+seat,pos);
                }
            }
            Place("DeliveryCounter",Load("外卖台.png"),new Vector2(-5.5f,-6.3f),4.3f,false);
            Anchor("counter",new Vector2(-5.5f,-5.5f));
            Anchor("entry",new Vector2(0,-7)); Anchor("player-seat",new Vector2(-3,-4.5f));
            world.anchors=anchors.ToArray();
        }
        static Sprite Load(string file)
        {
            var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+file);
            return Slice(tex,Path.GetFileNameWithoutExtension(file),new UnityEngine.Rect(0,0,tex.width,tex.height),new Vector2(.5f,0));
        }
        static Sprite Slice(Texture2D texture,string name,UnityEngine.Rect rect,Vector2 pivot)
        {
            string path=Folder+"/Sprites/"+name+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Sprite>(path); if(existing!=null)return existing;
            var sprite=Sprite.Create(texture,rect,pivot,100f,0,SpriteMeshType.FullRect);
            sprite.name=name; AssetDatabase.CreateAsset(sprite,path); return sprite;
        }
        static Transform Place(string name,Sprite sprite,Vector2 point,float width,bool ground,float depth=0)
        {
            var go=new GameObject(name);go.transform.SetParent(visualRoot,false);
            go.transform.localPosition=new Vector3(point.x,point.y,ground?.02f:0f);
            var sr=go.AddComponent<SpriteRenderer>();sr.sprite=sprite;
            // Author once in the prefab: artwork faces the fixed view, ground remains on the gameplay plane.
            // PlanarSprite only sorts; it never changes this rotation in edit or play mode.
            go.transform.rotation=ground?world.view.frame.rotation:world.view.Pose(Vector2.zero).Rotation;
            go.transform.localScale=new Vector3(width/sprite.bounds.size.x,
                depth>0?depth/sprite.bounds.size.y:width/sprite.bounds.size.x,1);
            var facing=go.AddComponent<PlanarSprite>();facing.frame=world.view.frame;
            facing.profile=profile;facing.visual=sr;facing.ground=ground;facing.Refresh();return go.transform;
        }
        static void Anchor(string id,Vector2 point)
        {
            var t=new GameObject(id.Replace('/','_')).transform;t.SetParent(world.transform,false);
            t.localPosition=new Vector3(point.x,point.y,0);
            anchors.Add(new ModuleWorld.Anchor{id=id,point=t});
        }
        static ModuleHUD BuildHUD()
        {
            var root=new GameObject("RestaurantHUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=20;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var hud=root.AddComponent<ModuleHUD>();
            UI(root.transform,"MoneyPanel","左上角展示框.png",new Vector2(0,1),new Vector2(153,-105),new Vector2(238,225));
            var money=Text(root.transform,"Money",new Vector2(0,1),new Vector2(164,-93),new Vector2(170,36));
            money.text="0";hud.texts=new[]{new ModuleHUD.TextElement{id="money",text=money}};
            var queueRoot=UI(root.transform,"CookingQueue","制作框.png",new Vector2(.5f,1),new Vector2(0,-82),new Vector2(700,160)).rectTransform;
            hud.regions=new[]{new ModuleHUD.Region{id="cooking-queue",root=queueRoot}};
            UI(root.transform,"ProgressBase","进度条底.png",new Vector2(.5f,1),new Vector2(-265,-180),new Vector2(150,25));
            var progress=UI(root.transform,"Progress","进度条.png",new Vector2(.5f,1),new Vector2(-265,-180),new Vector2(150,25));
            progress.type=Image.Type.Filled;progress.fillMethod=Image.FillMethod.Horizontal;progress.fillAmount=0;
            hud.images=new[]{new ModuleHUD.ImageElement{id="progress",image=progress}};
            UI(root.transform,"ProgressFrame","进度条框.png",new Vector2(.5f,1),new Vector2(-265,-180),new Vector2(150,25));
            UI(root.transform,"Messages","餐厅消息框.png",new Vector2(1,1),new Vector2(-170,-205),new Vector2(280,360));
            UI(root.transform,"Bag","背包.png",new Vector2(.5f,0),new Vector2(40,68),new Vector2(540,128));
            hud.actions=new [] {
                Button(hud,"decoration","装修.png",new Vector2(-340,75)),
                Button(hud,"management","管理.png",new Vector2(-210,75)),
                Button(hud,"recipes","菜谱.png",new Vector2(-80,75)),
                Button(hud,"exit","出门.png",new Vector2(-80,202))};
            return hud;
        }
        static ModuleHUD.ActionButton Button(ModuleHUD hud,string id,string file,Vector2 pos)
        {
            var image=UI(hud.transform,id,file,new Vector2(1,0),pos,new Vector2(116,116));
            image.raycastTarget=true;
            return new ModuleHUD.ActionButton{id=id,button=image.gameObject.AddComponent<Button>()};
        }
        static Image UI(Transform parent,string name,string file,Vector2 anchor,Vector2 position,Vector2 size)
        {
            string path=UIArt+file;var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.SaveAndReimport();
            var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            go.transform.SetParent(parent,false);var image=go.GetComponent<Image>();
            image.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);image.preserveAspect=true;image.raycastTarget=false;
            Rect(image.rectTransform,anchor,position,size);return image;
        }
        static Text Text(Transform parent,string name,Vector2 anchor,Vector2 pos,Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Text));go.transform.SetParent(parent,false);
            var text=go.GetComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize=30;text.color=new Color(.25f,.12f,.04f);text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;
            Rect(text.rectTransform,anchor,pos,size);return text;
        }
        static void Rect(RectTransform t,Vector2 anchor,Vector2 pos,Vector2 size)
        {
            t.anchorMin=t.anchorMax=anchor;t.pivot=new Vector2(.5f,.5f);t.anchoredPosition=pos;t.sizeDelta=size;
            t.localRotation=Quaternion.identity;t.localScale=Vector3.one;
        }
    }
}
