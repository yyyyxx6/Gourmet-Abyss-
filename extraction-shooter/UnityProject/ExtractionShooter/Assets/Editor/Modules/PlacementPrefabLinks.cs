using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Modules.Editor
{
    public static class PlacementPrefabLinks
    {
        public static string SourcePath(PlacementItem item)
        {
            var source=PrefabUtility.GetCorrespondingObjectFromOriginalSource(item);
            return source!=null?AssetDatabase.GetAssetPath(source):null;
        }

        public static void ValidateWorld(ModuleWorld world)
        {
            if(!world.requirePrefabLinks)return;
            string worldPath=AssetDatabase.GetAssetPath(world);
            if(string.IsNullOrEmpty(worldPath))worldPath=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(world.gameObject);
            foreach(var item in world.GetComponentsInChildren<PlacementItem>(true))
            {
                string path=SourcePath(item);
                if(string.IsNullOrEmpty(path)||path==worldPath)
                    throw new InvalidOperationException(item.name+": 不是可复用物件预制体实例，请保存为物件预制体后再摆入 World。");
            }
        }

        [MenuItem("Tools/Modules/Placement/Open Source Prefab")]
        public static void OpenSelectedSource()
        {
            var selection=Selection.activeGameObject;
            var item=selection!=null?selection.GetComponentInParent<PlacementItem>():null;
            if(item==null)throw new InvalidOperationException("请选择一个已摆放物件。");
            string path=SourcePath(item);
            if(string.IsNullOrEmpty(path))throw new InvalidOperationException("物件未连接源预制体。");
            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        }

        [MenuItem("Tools/Modules/Placement/Save New Prefab And Connect")]
        public static void SaveSelected()
        {
            var selected=Selection.activeGameObject;
            if(selected==null)throw new InvalidOperationException("先选择新创建的物件或组合。");
            var owner=selected.GetComponentInParent<PlacementItem>();
            var root=owner!=null?owner.gameObject:selected;
            string path=EditorUtility.SaveFilePanelInProject("保存物件并连接地图",root.name,"prefab","选择新的物件预制体路径",PlacementTools.SampleFolder);
            if(!string.IsNullOrEmpty(path))SaveAndConnect(root,path);
        }

        public static void SaveAndConnect(GameObject root,string path)
        {
            if(Application.isPlaying||EditorUtility.IsPersistent(root))throw new InvalidOperationException("请在编辑态选中场景中的新物件。");
            if(PrefabUtility.IsPartOfPrefabInstance(root) && PrefabUtility.GetNearestPrefabInstanceRoot(root)==root)
                throw new InvalidOperationException("已经是预制体实例；统一改动请打开源预制体，独特外观请制作变体。");
            if(root.GetComponent<ModuleWorld>()!=null)throw new InvalidOperationException("World 是地图，请选内部物件。");
            if(!path.StartsWith("Assets/",StringComparison.Ordinal)||PathExists(path))throw new InvalidOperationException("请选择 Assets 下未使用的新路径。");
            PlacementTools.ValidateTree(root);
            var oldPosition=root.transform.localPosition;
            foreach(var sorter in root.GetComponentsInChildren<PlanarSprite>(true))
            {
                Undo.RecordObject(sorter,"移除场景专属 Frame 引用");sorter.frame=null;sorter.profile=null;
            }
            PrefabUtility.SaveAsPrefabAssetAndConnect(root,path,InteractionMode.UserAction);
            var assetRoot=PrefabUtility.LoadPrefabContents(path);
            try
            {
                assetRoot.transform.localPosition=Vector3.zero;
                PrefabUtility.SaveAsPrefabAsset(assetRoot,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(assetRoot);}
            root.transform.localPosition=oldPosition;
            PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
            Debug.Log("[Placement links] 已保存并连接："+path);
        }
        static bool PathExists(string path)=>System.IO.File.Exists(path)||System.IO.Directory.Exists(path);
    }
}
