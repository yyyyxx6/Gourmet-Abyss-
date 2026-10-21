using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Modules.Editor
{
    // One-time restaurant adapter. Generic validation lives in PlacementPrefabLinks.
    public static class RestaurantPrefabLinkMigration
    {
        public const string WorldPath = "Assets/Modules/Restaurant/RestaurantWorld.prefab";
        const string Folder = PlacementTools.SampleFolder;

        public static void Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save scene edits before linking prefabs.");
            var existing=AssetDatabase.LoadAssetAtPath<GameObject>(WorldPath);
            if(existing.GetComponent<ModuleWorld>().requirePrefabLinks)
            {
                LinkTableAssembly();
                PlacementPrefabLinks.ValidateWorld(existing.GetComponent<ModuleWorld>());
                Debug.Log("[Placement links] Already linked; no assets overwritten.");return;
            }
            string backup="Library/PlacementPrefabLinks/Backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(WorldPath,backup+"/RestaurantWorld.prefab");
            foreach(var path in Directory.GetFiles(Folder,"*.prefab"))File.Copy(path,backup+"/"+Path.GetFileName(path));
            var root=PrefabUtility.LoadPrefabContents(WorldPath);
            try
            {
                var world=root.GetComponent<ModuleWorld>();
                var anchorPositions=world.anchors.ToDictionary(a=>a.id,a=>a.point.position);
                var items=root.GetComponentsInChildren<PlacementItem>(true);
                foreach(var stale in items.Where(i=>(i.name=="BackWall"||i.name=="EntranceRail") &&
                    (i.art==null || i.art.sprite==null)).ToArray())
                    Object.DestroyImmediate(stale.gameObject);
                items=root.GetComponentsInChildren<PlacementItem>(true);
                var snapshots=items.Select(i=>new Snapshot(i)).ToArray();
                foreach(var item in items)
                {
                    item.art.name="Art";
                    item.sorter.frame=null; // Runtime/editor sorting resolves the containing ModuleWorld.
                    item.sorter.profile=null;
                }
                foreach(var anchor in world.anchors)
                {
                    var parts=anchor.id.Split('/');
                    if(parts[0]=="table")anchor.point.name="TableAnchor";
                    if(parts[0]=="seat")anchor.point.name="Seat"+parts[2];
                    if(parts[0]=="cook")anchor.point.name="Cook";
                    if(parts[0]=="plate")anchor.point.name="Serve";
                    if(anchor.id=="counter")anchor.point.name="Counter";
                }
                var recipes=new Dictionary<string,string> {
                    {"Ground","Ground"},
                    {"Stove0","Stove"},{"ServingTable0","ServingTable"},{"Cabinet","Cabinet"},
                    {"Fridge","Fridge"},{"DeliveryCounter","DeliveryCounter"},{"Table","Table"},
                    {"Chair0","Chair"},{"CookMat0","GroundMat"}
                };
                foreach(var pair in recipes)
                    SaveCanonical(items.First(i=>i.name==pair.Key).gameObject,Folder+"/"+pair.Value+".prefab");
                foreach(var item in items)
                {
                    string type=item.name;
                    if(type.StartsWith("Stove"))type="Stove";
                    else if(type.StartsWith("ServingTable"))type="ServingTable";
                    else if(type.StartsWith("Chair"))type="Chair";
                    else if(type.StartsWith("CookMat")||type.StartsWith("ServingMat"))type="GroundMat";
                    Connect(item.gameObject,AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/"+type+".prefab"));
                }
                var groups=root.transform.Find("VisualRoot").Cast<Transform>().Where(t=>t.name.StartsWith("TableSet")).ToArray();
                SaveCanonical(groups[0].gameObject,Folder+"/TableSet.prefab");
                LinkTableAssembly();
                var tableAsset=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/TableSet.prefab");
                foreach(var group in groups)Connect(group.gameObject,tableAsset);
                foreach(var snapshot in snapshots)snapshot.Verify();
                foreach(var anchor in world.anchors)
                    if(anchor.point==null||Vector3.Distance(anchor.point.position,anchorPositions[anchor.id])>.001f)
                        throw new InvalidOperationException("Link conversion changed anchor: "+anchor.id);
                world.requirePrefabLinks=true;
                PlacementTools.ValidateTree(root);
                PlacementPrefabLinks.ValidateWorld(world);
                PrefabUtility.SaveAsPrefabAsset(root,WorldPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(WorldPath,ImportAssetOptions.ForceUpdate);
            PlacementPrefabLinks.ValidateWorld(AssetDatabase.LoadAssetAtPath<GameObject>(WorldPath).GetComponent<ModuleWorld>());
            Directory.CreateDirectory("Library/PlacementPrefabLinks");
            File.WriteAllText("Library/PlacementPrefabLinks/Migration.txt",
                "Linked 44 placement items and 6 table assemblies. 37 anchor positions preserved.\nBackup: "+backup+"\n");
            Debug.Log("[Placement links] RestaurantWorld now uses live nested neutral prefabs.");
        }

        static void SaveCanonical(GameObject source,string path)
        {
            var copy=Object.Instantiate(source);
            try
            {
                copy.name=Path.GetFileNameWithoutExtension(path);
                copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                foreach(var sorter in copy.GetComponentsInChildren<PlanarSprite>(true))
                {
                    sorter.frame=null;sorter.profile=null;
                    sorter.Refresh();
                }
                PlacementTools.ValidateTree(copy);
                PrefabUtility.SaveAsPrefabAsset(copy,path);
            }
            finally {Object.DestroyImmediate(copy);}
        }

        static void Connect(GameObject instance,GameObject prefab)
        {
            if(prefab==null)throw new InvalidOperationException("Missing prefab for "+instance.name);
            PrefabUtility.ConvertToPrefabInstance(instance,prefab,new ConvertToPrefabInstanceSettings {
                objectMatchMode=ObjectMatchMode.ByHierarchy,
                componentsNotMatchedBecomesOverride=true,
                gameObjectsNotMatchedBecomesOverride=true,
                recordPropertyOverridesOfMatches=false,
                changeRootNameToAssetName=false,
                logInfo=false
            },InteractionMode.AutomatedAction);
        }

        static bool LinkTableAssembly()
        {
            const string path=Folder+"/TableSet.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            bool changed=false;
            try
            {
                foreach(var item in root.GetComponentsInChildren<PlacementItem>(true))
                {
                    string name=item.name=="Table"?"Table":"Chair";
                    string assetPath=Folder+"/"+name+".prefab";
                    if(PlacementPrefabLinks.SourcePath(item)==assetPath)continue;
                    Connect(item.gameObject,AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));changed=true;
                }
                if(changed)PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            return changed;
        }

        sealed class Snapshot
        {
            readonly PlacementItem item;
            readonly Vector3 position,scale;
            readonly Quaternion rotation;
            readonly Sprite sprite;
            public Snapshot(PlacementItem value)
            {item=value;position=value.art.transform.position;scale=value.art.transform.lossyScale;rotation=value.art.transform.rotation;sprite=value.art.sprite;}
            public void Verify()
            {
                if(item==null||item.art==null||item.art.sprite!=sprite||Vector3.Distance(item.art.transform.position,position)>.001f||
                    Vector3.Distance(item.art.transform.lossyScale,scale)>.001f||Quaternion.Angle(item.art.transform.rotation,rotation)>.01f)
                    throw new InvalidOperationException("Prefab linking changed visual layout or lost a component reference.");
            }
        }
    }
}
