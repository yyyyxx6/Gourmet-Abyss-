using System;
using System.IO;
using System.Linq;
using GourmetAbyss.CameraSystem;
using UnityEditor;
using UnityEngine;

namespace Game.Modules.Editor
{
    public static class PlacementProductionMigration
    {
        const string WorldPath = "Assets/Modules/Restaurant/RestaurantWorld.prefab";
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
            for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save scene edits first.");
            Directory.CreateDirectory("Library/PlacementWork/Backup");
            foreach(var path in new[]{WorldPath,"Assets/Modules/Restaurant/RestaurantPerspective.asset","Assets/Modules/Combat/DungeonPerspective.asset"})
            {
                var backup="Library/PlacementWork/Backup/"+Path.GetFileName(path);
                if(!File.Exists(backup)) File.Copy(path,backup);
            }
            if (!AssetDatabase.IsValidFolder("Assets/Modules/Shared")) AssetDatabase.CreateFolder("Assets/Modules","Shared");
            if (!AssetDatabase.IsValidFolder(PlacementTools.SampleFolder)) AssetDatabase.CreateFolder("Assets/Modules","PlacementSamples");
            var standard=AssetDatabase.LoadAssetAtPath<WorldViewStandard>(PlacementTools.StandardPath);
            if(standard==null)
            {
                standard=ScriptableObject.CreateInstance<WorldViewStandard>();
                AssetDatabase.CreateAsset(standard,PlacementTools.StandardPath);
            }
            standard.elevation=45;standard.verticalFieldOfView=40;EditorUtility.SetDirty(standard);
            var restaurant=AssetDatabase.LoadAssetAtPath<PlanarPerspectiveProfile>("Assets/Modules/Restaurant/RestaurantPerspective.asset");
            var combat=AssetDatabase.LoadAssetAtPath<DungeonPerspectiveProfile>("Assets/Modules/Combat/DungeonPerspective.asset");
            restaurant.viewStandard=standard;restaurant.tiltFromNormal=45;restaurant.fieldOfView=40;
            combat.viewStandard=standard;combat.pitch=45;combat.fieldOfView=40;
            EditorUtility.SetDirty(restaurant);EditorUtility.SetDirty(combat);
            var root=PrefabUtility.LoadPrefabContents(WorldPath);
            try
            {
                var world=root.GetComponent<ModuleWorld>();
                var positions=world.anchors.Select(a=>a.point.position).ToArray();
                foreach(var sorter in root.GetComponentsInChildren<PlanarSprite>(true))
                {
                    if(sorter.GetComponentInParent<PlacementItem>()!=null) continue;
                    Adopt(sorter,standard);
                }
                // Legacy adapter ownership mapping. The reusable placement components have no restaurant IDs.
                foreach(var anchor in world.anchors)
                {
                    PlacementItem owner=null;
                    string[] parts=anchor.id.Split('/');
                    string name=parts[0]=="cook"?"Stove":parts[0]=="plate"?"ServingTable":null;
                    if(name!=null&&parts.Length==2) owner=root.GetComponentsInChildren<PlacementItem>().First(i=>i.name==name+parts[1]);
                    if(anchor.id=="counter") owner=root.GetComponentsInChildren<PlacementItem>().First(i=>i.name=="DeliveryCounter");
                    if(owner!=null) anchor.point.SetParent(owner.anchorsRoot,true);
                }
                for(int i=0;i<positions.Length;i++)
                    if(Vector3.Distance(world.anchors[i].point.position,positions[i])>.0001f)
                        throw new InvalidOperationException("Anchor moved during migration: "+world.anchors[i].id);
                foreach(var item in root.GetComponentsInChildren<PlacementItem>()) item.ApplyArtwork();
                PlacementTools.ValidateTree(root);
                PrefabUtility.SaveAsPrefabAsset(root,WorldPath);
                ExportSamples(root,standard);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(WorldPath,ImportAssetOptions.ForceUpdate);
            Debug.Log("[Placement] 45° shared standard; restaurant hierarchy migrated; gameplay anchors preserved; samples exported.");
        }

        static void Adopt(PlanarSprite sorter,WorldViewStandard standard)
        {
            var old=sorter.transform;var parent=old.parent;
            Vector3 position=old.localPosition,scale=old.localScale;
            int index=old.GetSiblingIndex();
            var item=new GameObject(old.name).AddComponent<PlacementItem>();
            item.transform.SetParent(parent,false);item.transform.localPosition=position;item.transform.SetSiblingIndex(index);
            item.standard=standard;item.surface=sorter.ground?PlacementItem.Surface.Ground:PlacementItem.Surface.Artwork;
            item.contact=PlacementTools.Child(item.transform,"ContactRoot");
            item.visualRoot=PlacementTools.Child(item.transform,"VisualRoot");
            item.physicsRoot=PlacementTools.Child(item.transform,"PhysicsRoot");
            item.anchorsRoot=PlacementTools.Child(item.transform,"AnchorsRoot");
            old.SetParent(item.visualRoot,false);item.art=sorter.visual;item.sorter=sorter;
            item.width=sorter.visual.sprite.bounds.size.x*scale.x;
            item.groundDepth=sorter.visual.sprite.bounds.size.y*scale.y;
            var sprite=sorter.visual.sprite;
            // Preserve the explicitly authored legacy contact, including the front rail's non-bottom pivot.
            item.spriteContact=new Vector2(sprite.pivot.x/sprite.rect.width,sprite.pivot.y/sprite.rect.height);
            item.footprint=new Vector2(item.width,sorter.ground?item.groundDepth:Mathf.Max(.25f,item.width*.5f));
            item.ApplyArtwork();
        }

        static void ExportSamples(GameObject root,WorldViewStandard standard)
        {
            foreach(var pair in new[]{new[]{"Stove0","Stove"},new[]{"ServingTable0","ServingTable"},new[]{"Ground","Ground"}})
            {
                var source=root.GetComponentsInChildren<PlacementItem>().First(i=>i.name==pair[0]);
                SaveSample(source.gameObject,pair[1]);
            }
            var table=root.transform.Find("VisualRoot/TableSet0");
            SaveSample(table.gameObject,"TableSet");
        }
        static void SaveSample(GameObject source,string name)
        {
            string path=PlacementTools.SampleFolder+"/"+name+".prefab";
            if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)return; // Never overwrite authored samples on rerun.
            var copy=UnityEngine.Object.Instantiate(source);
            try
            {
                copy.name=name;copy.transform.position=Vector3.zero;copy.transform.rotation=Quaternion.identity;
                foreach(var sorter in copy.GetComponentsInChildren<PlanarSprite>())sorter.frame=null;
                foreach(var item in copy.GetComponentsInChildren<PlacementItem>()) item.ApplyArtwork();
                PlacementTools.ValidateTree(copy);
                PrefabUtility.SaveAsPrefabAsset(copy,path);
            }
            finally {UnityEngine.Object.DestroyImmediate(copy);}
        }
    }
}
