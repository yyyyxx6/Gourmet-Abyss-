using System;
using System.Linq;
using Game.Modules;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Modules.Editor
{
    /// <summary>Repair the existing restaurant without rebuilding gameplay objects or artwork.</summary>
    public static class RestaurantAcceptanceRepair
    {
        const string Folder = "Assets/Modules/Restaurant/";
        [MenuItem("Tools/Modules/Repair Restaurant Acceptance UI")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/UpGround.unity")
                throw new InvalidOperationException("Open UpGround before repairing its restaurant.");
            FixWorld();
            FixHud();
            var adapter = Object.FindObjectOfType<RestaurantModuleAdapter>(true);
            if (adapter == null) throw new InvalidOperationException("Restaurant module is missing.");
            FixQueue(adapter);
            var nav = GameObject.Find("MainUI")?.transform.Find("MainTab");
            if (nav != null) Object.DestroyImmediate(nav.gameObject);
            var home = Object.FindObjectOfType<HomeUIManager>(true);
            var coin = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/NewVersion/UI/餐厅ui/金币图标·.png");
            if (home == null || coin == null) throw new InvalidOperationException("Currency UI or coin art is missing.");
            ReplaceIcon(home.textMoneyVal, coin);
            ReplaceIcon(home.STextMoneyVal, coin);
            // Reuse the existing catalog's wood art; no new currency panel or fixed currency cap.
            var wood = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/UI/SettlementIcons/Wood.png");
            ReplaceIcon(home.textF1, wood);
            ReplaceIcon(home.STextPKVal, wood);
            if (adapter.pair.presentation != null)
            {
                var hidden = adapter.pair.presentation.hideWhileOpen.Where(x => x != null).ToList();
                var main = GameObject.Find("MainUI").transform;
                // These old town overlays otherwise render over the restaurant or intercept its buttons.
                // Keep the real bag and borderless currency container available during entry/exit.
                foreach (string name in new[] { "healthTips", "LeftBottom", "RightBottom", "MiddleUp", "MiddleMiddle" })
                {
                    var obsoleteOverlay = main.Find(name);
                    if (obsoleteOverlay == null) continue;
                    var group = obsoleteOverlay.GetComponent<CanvasGroup>();
                    if (group == null) group = obsoleteOverlay.gameObject.AddComponent<CanvasGroup>();
                    if (!hidden.Contains(group)) hidden.Add(group);
                    Record(group);
                }
                adapter.pair.presentation.hideWhileOpen = hidden.ToArray();
                EditorUtility.SetDirty(adapter.pair.presentation);
            }
            EditorSceneManager.MarkSceneDirty(adapter.gameObject.scene);
            EditorSceneManager.SaveScene(adapter.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("RESTAURANT_ACCEPTANCE_REPAIR_SAVED");
        }
        public static void RunBatch()
        {
            try { EditorSceneManager.OpenScene("Assets/Scenes/UpGround.unity"); Apply(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        static void FixWorld()
        {
            string path = Folder + "RestaurantWorld.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var grass = root.transform.Find("VisualRoot/SurroundingGround");
                if (grass == null) throw new InvalidOperationException("Surrounding ground is missing.");
                foreach (var renderer in grass.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    renderer.sortingLayerName = PlanarSprite.GroundSortingLayer;
                    renderer.sortingOrder = -1300;
                    EditorUtility.SetDirty(renderer);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static void FixHud()
        {
            string path = Folder + "RestaurantHUD.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (string name in new[] { "MoneyPanel", "MoneyStrip", "SecondaryStrip", "CoinIcon", "Money" })
                {
                    var obsolete = root.transform.Find(name);
                    if (obsolete != null) Object.DestroyImmediate(obsolete.gameObject);
                }
                var hud = root.GetComponent<ModuleHUD>();
                hud.texts = hud.texts.Where(x => x.id != "money").ToArray();
                var queue = hud.GetRegion("cooking-queue");
                var image = queue.GetComponent<Image>();
                image.raycastTarget = true;
                var button = queue.GetComponent<Button>();
                if (button == null) button = queue.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.None;
                var actions = hud.actions.Where(x => x.id != "cooking").ToList();
                actions.Add(new ModuleHUD.ActionButton { id = "cooking", button = button });
                hud.actions = actions.ToArray();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static void FixQueue(RestaurantModuleAdapter adapter)
        {
            var slots = adapter.restaurant.allDishQueueSlots;
            var region = adapter.pair.hud.GetRegion("cooking-queue");
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null) throw new InvalidOperationException("A cooking queue slot is missing.");
                var rect = slot.transform as RectTransform;
                rect.SetParent(region, false);
                SetRect(rect, new Vector2(-268f + i * 134f, 0), new Vector2(112, 112));
                var so = new SerializedObject(slot);
                so.FindProperty("transparentNormalBackground").boolValue = true;
                var bg = so.FindProperty("slotBackground").objectReferenceValue as Image;
                var icon = so.FindProperty("dishIcon").objectReferenceValue as Image;
                var count = so.FindProperty("itemCountText").objectReferenceValue as Text;
                so.ApplyModifiedPropertiesWithoutUndo();
                if (bg != null) { SetRect(bg.rectTransform, Vector2.zero, new Vector2(104,104)); bg.color = Color.clear; bg.raycastTarget = false; Record(bg); }
                if (icon != null) { SetRect(icon.rectTransform, Vector2.zero, new Vector2(86,86)); icon.preserveAspect = true; icon.raycastTarget = false; Record(icon); }
                if (count != null) { SetRect(count.rectTransform, new Vector2(30,-36), new Vector2(56,32)); count.fontSize = 26; count.color = new Color32(65,35,12,255); count.alignment = TextAnchor.MiddleRight; count.raycastTarget = false; Record(count); }
                Record(slot);
            }
        }
        static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f);
            rect.sizeDelta = size; rect.anchoredPosition3D = new Vector3(position.x,position.y,0);
            rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
            Record(rect);
        }
        static void ReplaceIcon(Text amount, Sprite sprite)
        {
            if (amount == null || sprite == null) return;
            var icon = amount.transform.parent.GetComponent<Image>();
            if (icon == null) throw new InvalidOperationException("Currency icon parent is missing: " + amount.name);
            icon.sprite = sprite; icon.preserveAspect = true; Record(icon);
        }
        static void Record(Object target) { EditorUtility.SetDirty(target); PrefabUtility.RecordPrefabInstancePropertyModifications(target); }
    }
}
