using System;
using System.IO;
using System.Linq;
using GourmetAbyss.CameraSystem;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Game.Modules.Editor
{
    public static class PrefabLinkAcceptanceChecks
    {
        const string Folder=PlacementTools.SampleFolder;
        static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        static ModuleWorld World()=>AssetDatabase.LoadAssetAtPath<GameObject>(RestaurantPrefabLinkMigration.WorldPath).GetComponent<ModuleWorld>();

        public static void RestaurantHasCompleteNestedLinks()
        {
            var world=World();Require(world.requirePrefabLinks,"Map link validation disabled.");
            PlacementPrefabLinks.ValidateWorld(world);
            var items=world.GetComponentsInChildren<PlacementItem>(true);
            Require(items.Length==42 && world.anchors.Length==37 && world.anchors.All(a=>a.point!=null),"Map content/reference count changed.");
            Require(world.GetComponentsInChildren<CameraFacingVisual>(true).Length==0,
                "Restaurant must stay on its authored world plane for the 2.5D presentation.");
            Require(!world.GetComponentsInChildren<MonoBehaviour>(true)
                .Any(component=>component!=null&&component.GetType().Name=="CameraFacingLayout"),
                "Restaurant must not contain a camera-facing layout.");
            Require(items.All(item=>item.visualRoot!=null&&
                Quaternion.Angle(item.visualRoot.localRotation,item.ExpectedVisualRotation)<.001f),
                "Restaurant visual roots must preserve the authored 2D plane.");
            Require(items.All(i=>AssetDatabase.GetAssetPath(i.art.sprite).StartsWith("Assets/NewVersion/map/",StringComparison.Ordinal)),
                "Restaurant items must reference original art Sprites, not generated copies.");
            Require(items.Count(i=>PlacementPrefabLinks.SourcePath(i)==Folder+"/Table.prefab")==6,"Tables do not inherit from shared Table prefab.");
            Require(items.Count(i=>PlacementPrefabLinks.SourcePath(i)==Folder+"/Chair.prefab")==24,"Chairs do not inherit through table assemblies.");
            var groups=world.transform.Find("VisualRoot").Cast<Transform>().Where(t=>t.name.StartsWith("TableSet")).ToArray();
            Require(groups.Length==6 && groups.All(t=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)==Folder+"/TableSet.prefab"),"Table assemblies are disconnected.");
        }

        public static void NoVisualOverridesBlockPropagation()
        {
            foreach(string path in new[]{RestaurantPrefabLinkMigration.WorldPath,Folder+"/TableSet.prefab"})
            {
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach(var t in asset.GetComponentsInChildren<Transform>(true).Where(t=>PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)))
                foreach(var m in PrefabUtility.GetPropertyModifications(t.gameObject)??Array.Empty<PropertyModification>())
                    Require(!(m.target is SpriteRenderer) && !(m.target is PlacementItem),
                        path+": visual/placement override blocks source edits: "+m.propertyPath);
            }
        }

        public static void StoveEditsReachMap()=>VerifyPropagation("Stove",2);
        public static void TableEditsReachAllSixAssemblies()=>VerifyPropagation("Table",6);
        public static void ChairEditsReachAllTwentyFourChairs()=>VerifyPropagation("Chair",24);
        public static void GroundEditsReachActualRestaurant()=>VerifyPropagation("Ground",1);
        static void VerifyPropagation(string name,int count)
        {
            string path=Folder+"/"+name+".prefab";
            byte[] before=File.ReadAllBytes(path);
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<PlacementItem>();
            float originalWidth=asset.width;Color originalColor=asset.art.color;
            var anchors=World().anchors.ToDictionary(a=>a.id,a=>a.point.position);
            Color probe=new Color(.31f,.73f,.47f,1);float width=originalWidth*1.03f;
            try
            {
                SetSource(path,width,probe,false);
                var items=World().GetComponentsInChildren<PlacementItem>(true).Where(i=>PlacementPrefabLinks.SourcePath(i)==path).ToArray();
                Require(items.Length==count,"Unexpected map consumer count: "+name);
                foreach(var item in items)
                {
                    Require(Mathf.Abs(item.width-width)<.0001f && item.art.color==probe,"Source change did not reach map: "+item.name);
                    Require(Mathf.Abs(item.art.sprite.bounds.size.x*item.art.transform.lossyScale.x-width)<.001f,"Art width did not propagate.");
                }
                foreach(var anchor in World().anchors)Require(Vector3.Distance(anchor.point.position,anchors[anchor.id])<.0001f,"Art edit moved gameplay anchor.");
            }
            finally {SetSource(path,originalWidth,originalColor,true);}
            Require(File.ReadAllBytes(path).SequenceEqual(before),"Source asset was not restored byte-for-byte: "+path);
            foreach(var item in World().GetComponentsInChildren<PlacementItem>(true).Where(i=>PlacementPrefabLinks.SourcePath(i)==path))
                Require(item.art.color==originalColor && Mathf.Abs(item.width-originalWidth)<.0001f,"Map did not restore original art.");
        }

        static void SetSource(string path,float width,Color color,bool sourceDimensions)
        {
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var item=root.GetComponent<PlacementItem>();item.useSourceDimensions=sourceDimensions;item.width=width;item.art.color=color;item.ApplyArtwork();
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
        }

        public static void UnpackedMapItemIsRejected()
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var go=(GameObject)PrefabUtility.InstantiatePrefab(World().gameObject,scene);
                PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.OutermostRoot,InteractionMode.AutomatedAction);
                var world=go.GetComponent<ModuleWorld>();
                PlacementPrefabLinks.ValidateWorld(world);
                var stove=go.GetComponentsInChildren<PlacementItem>().First(i=>i.name=="Stove0");
                PrefabUtility.UnpackPrefabInstance(stove.gameObject,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                bool rejected=false;
                try {PlacementPrefabLinks.ValidateWorld(world);}catch(InvalidOperationException){rejected=true;}
                Require(rejected,"An unpacked map item was accepted.");
            }
            finally {UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }

        public static void NewItemCanBeSavedAndConnected()
        {
            string folder="Assets/PlacementLinkTest_"+Guid.NewGuid().ToString("N");
            string path=folder+"/NewItem.prefab";
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
            try
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Stove.prefab").GetComponent<PlacementItem>();
                var item=PlacementTools.Create(source.art.sprite,source.standard,false,false,source.width);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(item.gameObject,scene);
                item.transform.position=new Vector3(2,3,0);
                PlacementPrefabLinks.SaveAndConnect(item.gameObject,path);
                Require(PrefabUtility.IsAnyPrefabInstanceRoot(item.gameObject),"New item was saved as an unconnected copy.");
                Require(item.transform.position==new Vector3(2,3,0),"Saving moved the placed instance.");
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Require(prefab.transform.position==Vector3.zero,"Library prefab has a scene-specific origin.");
                PlacementTools.ValidateTree(prefab);
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
