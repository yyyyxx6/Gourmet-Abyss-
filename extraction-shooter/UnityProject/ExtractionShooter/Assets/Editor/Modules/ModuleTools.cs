using System;
using System.Linq;
using Game.Modules;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Modules.Editor
{
    public static class ModuleTools
    {
        [MenuItem("Tools/Modules/Install Selected Definition")]
        public static void InstallSelected()
        {
            var definition=Selection.activeObject as ModuleDefinition;
            if(definition==null)throw new InvalidOperationException("Select a ModuleDefinition asset first.");
            Selection.activeGameObject=Install(definition,Vector3.zero).gameObject;
        }
        public static ModulePair Install(ModuleDefinition definition,Vector3 position)
        {
            ValidateDefinition(definition);
            var go=new GameObject(definition.moduleId+" Module");Undo.RegisterCreatedObjectUndo(go,"Install module");
            var pair=go.AddComponent<ModulePair>();pair.definition=definition;
            pair.world=((GameObject)PrefabUtility.InstantiatePrefab(definition.worldPrefab.gameObject,go.transform)).GetComponent<ModuleWorld>();
            pair.world.transform.position=position;
            pair.hud=((GameObject)PrefabUtility.InstantiatePrefab(definition.hudPrefab.gameObject,go.transform)).GetComponent<ModuleHUD>();
            pair.hud.gameObject.SetActive(false);return pair;
        }
        public static void ValidateDefinition(ModuleDefinition definition)
        {
            if(definition==null || definition.worldPrefab==null || definition.hudPrefab==null)
                throw new InvalidOperationException("A module requires both world and HUD prefabs.");
            if(definition.worldPrefab.view==null || definition.worldPrefab.view.profile==null)
                throw new InvalidOperationException("Missing perspective view/profile.");
            if(definition.worldPrefab.GetComponentInChildren<PlacementItem>(true)!=null)
                PlacementTools.ValidateTree(definition.worldPrefab.gameObject);
            PlacementPrefabLinks.ValidateWorld(definition.worldPrefab);
            var anchors=definition.worldPrefab.anchors;
            if(anchors.Any(a=>string.IsNullOrWhiteSpace(a.id)||a.point==null) || anchors.GroupBy(a=>a.id).Any(g=>g.Count()>1))
                throw new InvalidOperationException("Anchor IDs must be unique and have targets.");
            if(anchors.Any(a=>!a.point.IsChildOf(definition.worldPrefab.transform)))
                throw new InvalidOperationException("Every anchor must belong to this world prefab.");
            var hud=definition.hudPrefab;
            ValidateIds(hud.actions.Select(a=>a.id),"actions");
            ValidateIds(hud.texts.Select(a=>a.id),"texts");
            ValidateIds(hud.images.Select(a=>a.id),"images");
            ValidateIds(hud.regions.Select(a=>a.id),"regions");
            if(hud.actions.Any(a=>a.button==null)||hud.texts.Any(a=>a.text==null)||hud.images.Any(a=>a.image==null)||hud.regions.Any(a=>a.root==null))
                throw new InvalidOperationException("Missing HUD binding target.");
            foreach(var id in (definition.requiredAnchorIds??"").Split('\n').Select(s=>s.Trim()).Where(s=>s.Length>0))
                if(definition.worldPrefab.GetAnchor(id)==null)throw new InvalidOperationException("Missing anchor: "+id);
            foreach(var root in new[]{definition.worldPrefab.gameObject,definition.hudPrefab.gameObject})
            foreach(var c in root.GetComponentsInChildren<Component>(true))
            {
                if(c==null)throw new InvalidOperationException("Missing script in "+root.name);
                var so=new SerializedObject(c);var p=so.GetIterator();
                while(p.Next(true)) if(p.propertyType==SerializedPropertyType.ObjectReference && p.objectReferenceValue!=null)
                {
                    var target=p.objectReferenceValue as Component;
                    var targetGO=p.objectReferenceValue as GameObject;
                    var t=target!=null?target.transform:targetGO!=null?targetGO.transform:null;
                    if(t!=null && !t.IsChildOf(root.transform) && !AssetDatabase.Contains(t))
                        throw new InvalidOperationException("Scene reference in prefab: "+c.name+"."+p.propertyPath);
                }
            }
        }
        static void ValidateIds(System.Collections.Generic.IEnumerable<string> values,string group)
        {
            var ids=values.ToArray();
            if(ids.Any(string.IsNullOrWhiteSpace)||ids.Distinct().Count()!=ids.Length)
                throw new InvalidOperationException("HUD "+group+" IDs must be nonempty and unique.");
        }
        [MenuItem("Tools/Modules/Validate Selected Pair")]
        public static void ValidateSelected()
        {
            var pair=Selection.activeGameObject!=null?Selection.activeGameObject.GetComponentInParent<ModulePair>():null;
            if(pair==null)throw new InvalidOperationException("Select a module pair.");
            ValidateDefinition(pair.definition);
            if(pair.world==null||pair.hud==null)throw new InvalidOperationException("Missing scene instance.");
            Debug.Log("[Modules] PASS: "+pair.definition.moduleId+", anchors="+pair.world.anchors.Length);
        }
    }
}
