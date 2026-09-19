using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Modules.Editor
{
    public static class ModuleSupplementalChecks
    {
        public static string FlightStatus { get; private set; } = "Not started";

        public static string CheckArtworkProjection(Camera camera, PlanarSprite[] sprites)
        {
            int count=0;
            foreach(var placement in sprites.Where(s=>!s.ground))
            {
                var visual=placement.visual;var t=visual.transform;var b=visual.sprite.bounds;
                Vector2 bl=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(b.min.x,b.min.y,0)));
                Vector2 br=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(b.max.x,b.min.y,0)));
                Vector2 tl=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(b.min.x,b.max.y,0)));
                Vector2 tr=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(b.max.x,b.max.y,0)));
                Require(Mathf.Abs(br.y-bl.y)<.05f&&Mathf.Abs(tl.x-bl.x)<.05f,placement.name+": artwork edges are skewed.");
                Require(Mathf.Abs(Vector2.Distance(bl,br)-Vector2.Distance(tl,tr))<.05f,placement.name+": artwork became a trapezoid.");
                float expected=b.size.x*t.lossyScale.x/(b.size.y*t.lossyScale.y);
                Require(Mathf.Abs(Vector2.Distance(bl,br)/Vector2.Distance(bl,tl)-expected)<.001f,placement.name+": artwork aspect ratio changed.");
                count++;
            }
            Require(count>0,"No artwork found.");
            return count+" 张非地面图片：投影边框无倾斜、梯形变形和额外宽高压缩";
        }

        public static string CheckWorldPlanePerspective(Camera camera, PlanarSprite[] sprites)
        {
            int count=0;
            bool perspectiveObserved=false;
            foreach(var placement in sprites.Where(s=>!s.ground))
            {
                var visual=placement.visual;var t=visual.transform;var b=visual.sprite.bounds;
                var bl=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(b.min.x,b.min.y,0)));
                var br=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(b.max.x,b.min.y,0)));
                var tl=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(b.min.x,b.max.y,0)));
                var tr=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(b.max.x,b.max.y,0)));
                Require(Mathf.Min(Mathf.Min(bl.z,br.z),Mathf.Min(tl.z,tr.z))>0,
                    placement.name+": artwork projected behind the camera.");
                if(Mathf.Abs(Vector2.Distance(bl,br)-Vector2.Distance(tl,tr))>.05f ||
                    Mathf.Abs(tl.x-bl.x)>.05f)
                    perspectiveObserved=true;
                count++;
            }
            Require(count>0,"No artwork found.");
            Require(perspectiveObserved,"Restaurant world-plane artwork has no visible 2.5D perspective.");
            return count+" 张非地面图片：保持世界平面并呈现 2.5D 透视纵深";
        }

        public static string CheckPortablePrefabs()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ModuleDefinition>("Assets/Modules/Restaurant/RestaurantDefinition.asset");
            if (definition == null)
                definition = AssetDatabase.FindAssets("t:ModuleDefinition").Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<ModuleDefinition>).First(d => d.moduleId == "restaurant");
            ModuleTools.ValidateDefinition(definition);
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var worldGO = (GameObject)PrefabUtility.InstantiatePrefab(definition.worldPrefab.gameObject, scene);
                var hudGO = (GameObject)PrefabUtility.InstantiatePrefab(definition.hudPrefab.gameObject, scene);
                var world = worldGO.GetComponent<ModuleWorld>();
                var before = world.anchors.Select(a => a.point.position).ToArray();
                var move = new Vector3(17, 31, -9); worldGO.transform.position += move;
                for (int i = 0; i < before.Length; i++)
                    Require(Vector3.Distance(world.anchors[i].point.position, before[i] + move) < .001f, "Module root lost an anchor.");
                var table = worldGO.transform.Find("VisualRoot").Cast<Transform>().First(t => t.name.StartsWith("TableSet"));
                var tableAnchors = world.anchors.Where(a => a.point.IsChildOf(table)).ToArray();
                Require(tableAnchors.Length > 0, "Table group has no anchors.");
                before = tableAnchors.Select(a => a.point.position).ToArray(); table.position += Vector3.right * 3;
                for (int i = 0; i < before.Length; i++)
                    Require(Vector3.Distance(tableAnchors[i].point.position, before[i] + Vector3.right * 3) < .001f, "Table lost an anchor.");
                Require(hudGO.GetComponent<ModuleHUD>().GetAction("exit") != null, "Standalone HUD binding missing.");
                return "PASS: independent World/HUD instantiation; root translation; table child anchors; binding validation.";
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void BeginFlightProbe()
        {
            var adapter = Object.FindObjectOfType<RestaurantModuleAdapter>();
            Require(adapter != null && adapter.CanPresentQueueIngredients, "Enter restaurant first.");
            Require(FlightStatus != "Running", "Probe already running.");
            FlightStatus = "Running";
            adapter.restaurant.StartCoroutine(FlightProbe(adapter));
        }

        static IEnumerator FlightProbe(RestaurantModuleAdapter adapter)
        {
            // Synthetic display source only: never call enqueue/consume or change slot data.
            var icon = adapter.pair.hud.GetComponentsInChildren<UnityEngine.UI.Image>().First(i => i.sprite != null).sprite;
            var target = adapter.restaurant.allDishQueueSlots[0];
            var sourceGO = new GameObject("ModuleFlightProbeSource", typeof(RectTransform));
            sourceGO.transform.SetParent(adapter.pair.hud.transform, false);
            var source = sourceGO.GetComponent<RectTransform>(); source.anchorMin = source.anchorMax = new Vector2(.5f, 0);
            source.anchoredPosition = new Vector2(0, 140);
            adapter.restaurant.StartCoroutine(adapter.PlayQueueIngredients(new List<InventoryManager.IngredientFlySource> {
                new InventoryManager.IngredientFlySource { icon=icon, fromUITransform=source, fromWorldPos=source.position }
            }, target));
            yield return new WaitForSeconds(.25f);
            var host = GameObject.Find("ModuleScreenFlight");
            bool visible = host != null && host.GetComponentInChildren<UnityEngine.UI.Image>().enabled;
            bool bound = ReferenceEquals(adapter.restaurant.QueueIngredientPresentation, adapter);
            Directory.CreateDirectory("Library/ModuleAcceptance");
            ScreenCapture.CaptureScreenshot("Library/ModuleAcceptance/09-flight-probe.png");
            yield return new WaitForSeconds(1f);
            bool cleaned = GameObject.Find("ModuleScreenFlight") == null;
            Object.Destroy(sourceGO);
            FlightStatus = visible && bound && cleaned ? "PASS" : "FAIL";
            File.WriteAllText("Library/ModuleAcceptance/FlightReport.txt", FlightStatus +
                "\nPresentation bound=" + bound + "; visible=" + visible + "; cleaned=" + cleaned +
                "\nSynthetic display probe only; no ingredient consumption or queue mutation.\n");
            Debug.Log("[Module flight] " + FlightStatus);
        }

        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
