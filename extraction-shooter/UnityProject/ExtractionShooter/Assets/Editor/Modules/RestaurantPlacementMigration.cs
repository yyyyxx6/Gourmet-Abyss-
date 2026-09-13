using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Modules.Editor
{
    // Bake a fixed artwork plane into the prefab. No runtime billboarding or gameplay transforms.
    public static class RestaurantPlacementMigration
    {
        [MenuItem("Tools/Modules/Fix Legacy Restaurant Furniture Placement")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save scene edits first.");
            const string path = "Assets/Modules/Restaurant/RestaurantWorld.prefab";
            Directory.CreateDirectory("Library/ModulePlacementAcceptance");
            const string backup = "Library/ModulePlacementAcceptance/RestaurantWorld-before-fixed-artwork.prefab";
            if (!File.Exists(backup)) File.Copy(path, backup);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int changed = 0;
                foreach (var sprite in root.GetComponentsInChildren<PlanarSprite>(true))
                {
                    bool furniture = sprite.name == "Table" || sprite.name.StartsWith("Chair") ||
                        sprite.name.StartsWith("Stove") || sprite.name.StartsWith("ServingTable") ||
                        sprite.name == "Cabinet" || sprite.name == "Fridge" || sprite.name == "DeliveryCounter";
                    if (!furniture) continue;
                    // Correct only the flat placement from the preceding migration, not custom artist rotations.
                    if (Quaternion.Angle(sprite.transform.rotation, sprite.frame.rotation) > .01f) continue;
                    sprite.transform.rotation = root.GetComponent<ModuleWorld>().view.Pose(Vector2.zero).Rotation;
                    changed++;
                }
                if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[Placement] Baked fixed artwork orientation for " + changed + " furniture sprites; layout/anchors/scale unchanged.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
    }
}
