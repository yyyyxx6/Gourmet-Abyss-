using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class DeathLootCrateBuilder
{
    private const string SourcePath = "Assets/Prefabas/SceneItem/Chest2A.prefab";
    private const string EffectPath = "Assets/Prefabas/FlyingPet/HCFX_Marble_02_Get.prefab";
    private const string OutputPath = "Assets/Resources/Loot/DeathLootCrate.prefab";

    [MenuItem("Tools/Chef Dungeon/Build Death Loot Crate")]
    public static void Build()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null) throw new InvalidOperationException("Missing existing chest model: " + SourcePath);
        GameObject effect = AssetDatabase.LoadAssetAtPath<GameObject>(EffectPath);
        if (effect == null) throw new InvalidOperationException("Missing existing collection effect: " + EffectPath);

        GameObject root = new GameObject("DeathLootCrate");
        try
        {
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            visual.name = "ChestVisual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * 1.6f;
            StripGameplayComponents(visual);
            foreach (Transform child in visual.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = 0;
                child.gameObject.tag = "Untagged";
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, (StaticEditorFlags)0);
            }
            if (visual.GetComponentsInChildren<Renderer>(true).Length == 0)
                throw new InvalidOperationException("The source chest has no visual renderers.");

            SphereCollider trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.55f, 0f);
            trigger.radius = 1.8f;
            DeathLootCrate crate = root.AddComponent<DeathLootCrate>();
            SerializedObject serialized = new SerializedObject(crate);
            serialized.FindProperty("pickupTrigger").objectReferenceValue = trigger;
            serialized.FindProperty("collectionEffectPrefab").objectReferenceValue = effect;
            serialized.FindProperty("collectionEffectLifetime").floatValue = 4f;
            serialized.FindProperty("overlapCheckInterval").floatValue = 0.2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Resources", "Loot"));
            AssetDatabase.Refresh();
            PrefabUtility.SaveAsPrefabAsset(root, OutputPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Built " + OutputPath + " from " + SourcePath + "; trigger radius=1.8, center=(0,0.55,0).");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void StripGameplayComponents(GameObject visual)
    {
        foreach (MonoBehaviour component in visual.GetComponentsInChildren<MonoBehaviour>(true))
            if (component != null) UnityEngine.Object.DestroyImmediate(component);
        foreach (Transform child in visual.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
        foreach (Joint component in visual.GetComponentsInChildren<Joint>(true))
            UnityEngine.Object.DestroyImmediate(component);
        foreach (Collider component in visual.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.DestroyImmediate(component);
        foreach (Rigidbody component in visual.GetComponentsInChildren<Rigidbody>(true))
            UnityEngine.Object.DestroyImmediate(component);
        foreach (Joint2D component in visual.GetComponentsInChildren<Joint2D>(true))
            UnityEngine.Object.DestroyImmediate(component);
        foreach (Collider2D component in visual.GetComponentsInChildren<Collider2D>(true))
            UnityEngine.Object.DestroyImmediate(component);
        foreach (Rigidbody2D component in visual.GetComponentsInChildren<Rigidbody2D>(true))
            UnityEngine.Object.DestroyImmediate(component);
        foreach (AudioSource component in visual.GetComponentsInChildren<AudioSource>(true))
            UnityEngine.Object.DestroyImmediate(component);
    }
}
