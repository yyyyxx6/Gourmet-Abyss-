using System;
using System.Linq;
using GourmetAbyss.CameraSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Modules.Editor
{
    public static class PlacementAcceptanceChecks
    {
        static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
        static WorldViewStandard Standard => AssetDatabase.LoadAssetAtPath<WorldViewStandard>(PlacementTools.StandardPath);

        public static void SharedCameraStandard()
        {
            var r=AssetDatabase.LoadAssetAtPath<PlanarPerspectiveProfile>("Assets/Modules/Restaurant/RestaurantPerspective.asset");
            var c=AssetDatabase.LoadAssetAtPath<DungeonPerspectiveProfile>("Assets/Modules/Combat/DungeonPerspective.asset");
            Require(Standard!=null && r.viewStandard==Standard && c.viewStandard==Standard,"Profiles must share one standard.");
            Require(Mathf.Abs(r.EffectiveTilt-45)<.001f && Mathf.Abs(c.EffectivePitch-45)<.001f,"Both elevations must be 45 degrees.");
            Require(r.EffectiveFieldOfView==40 && c.EffectiveFieldOfView==40,"Both FOVs must be 40.");
            var world=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Modules/Restaurant/RestaurantWorld.prefab").GetComponent<ModuleWorld>();
            Require(Quaternion.Angle(world.view.Pose(Vector2.zero).Rotation,Quaternion.Euler(-45,0,0))<.001f,"Restaurant evaluated pose must be -45 on XY.");
        }

        public static void PortableSampleLibrary()
        {
            int count=0;
            foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{PlacementTools.SampleFolder}))
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                PlacementTools.ValidateTree(prefab);
                foreach(var item in prefab.GetComponentsInChildren<PlacementItem>(true))
                {
                    Require(item.standard==Standard,"Sample standard mismatch");
                    Require(item.sorter.frame==null,"Standalone sample has external scene/frame reference");
                    Require(item.useSourceDimensions && item.art.transform.localScale == Vector3.one,
                        "Sample 的 Art 必须保持源图尺寸和 1 倍缩放: "+item.name);
                }
                count++;
            }
            Require(count>=8,"Missing neutral placement samples.");
            var legacy=AssetDatabase.FindAssets("t:Prefab",new[]{PlacementTools.SampleFolder})
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p=>p.EndsWith("_XY.prefab",StringComparison.OrdinalIgnoreCase)||p.EndsWith("_XZ.prefab",StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Require(legacy.Length==0,"Legacy XY/XZ prefab variants remain: "+string.Join(", ",legacy));

            foreach(string path in new[]{
                "Assets/Environment/Vegetation/Prototype/Prefabs/Vegetation_Single_Tree.prefab",
                "Assets/Environment/Vegetation/Prototype/Prefabs/Vegetation_Single_Grass.prefab"})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                PlacementTools.ValidateTree(prefab);
                var item=prefab.GetComponent<PlacementItem>();
                Require(item!=null&&item.UsesCameraFacingVisual,"Vegetation sample does not follow camera: "+path);
                Require(AssetDatabase.GetAssetPath(item.art.sprite).StartsWith("Assets/NewVersion/map/",StringComparison.Ordinal),
                    "Vegetation sample must reference the original art Sprite: "+path);
            }
        }

        public static void ContactAndLogicIsolationXY() => ContactAndLogicIsolation(false);
        public static void ContactAndLogicIsolationXZ() => ContactAndLogicIsolation(true);
        public static void StandaloneDepthBeyondRestaurantRange()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PlacementTools.SampleFolder+"/Stove.prefab");
            var go=Object.Instantiate(prefab);
            try
            {
                var item=go.GetComponent<PlacementItem>();
                item.xzGround=true;item.sorter.xzGround=true;item.ApplyArtwork();
                item.transform.position=new Vector3(0,0,30);item.sorter.Refresh();int before=item.art.sortingOrder;
                item.transform.position+=Vector3.forward;item.sorter.Refresh();
                Require(before-item.art.sortingOrder==100,"Standalone objects outside restaurant bounds collapsed to identical sorting order.");
                PlacementTools.ValidateTree(go);
            }
            finally {Object.DestroyImmediate(go);}
        }
        static void ContactAndLogicIsolation(bool xz)
        {
            var texture=new Texture2D(100,200);
            var sprite=Sprite.Create(texture,new Rect(0,0,100,200),new Vector2(.2f,.7f),100);
            PlacementItem item=null;
            try
            {
                item=PlacementTools.Create(sprite,Standard,false,xz,2);
                item.transform.position=xz?new Vector3(2,0,3):new Vector3(2,3,0);
                var anchor=PlacementTools.Child(item.anchorsRoot,"Interact");anchor.localPosition=new Vector3(.7f,.2f,.5f);
                var physics=PlacementTools.Child(item.physicsRoot,"Collision");var collider=physics.gameObject.AddComponent<BoxCollider>();collider.size=new Vector3(2,1,1);
                var rootPosition=item.transform.position;var anchorPosition=anchor.position;var colliderSize=collider.size;
                item.spriteContact=new Vector2(.4f,.1f);item.useSourceDimensions=false;item.width=3;item.ApplyArtwork();
                Require(Vector3.Distance(item.art.transform.TransformPoint(item.SpriteContactLocal),item.contact.position)<.0001f,"Contact differs from actual sprite point.");
                Require(item.transform.position==rootPosition && anchor.position==anchorPosition && collider.size==colliderSize,"Applying art changed gameplay.");
                item.sorter.Refresh();int order=item.sorter.visual.sortingOrder;
                item.art.transform.localPosition+=Vector3.up*5;item.sorter.Refresh();
                Require(order==item.art.sortingOrder,"Sorting used image center instead of contact.");
                item.transform.position+=xz?Vector3.forward:Vector3.up;item.sorter.Refresh();
                Require(item.art.sortingOrder==order-100,"Sorting did not follow moved contact.");
                item.ApplyArtwork();PlacementTools.ValidateTree(item.gameObject);
            }
            finally {if(item!=null)Object.DestroyImmediate(item.gameObject);Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);}
        }

        public static void InvalidAuthoringIsRejected()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PlacementTools.SampleFolder+"/Stove.prefab");
            var go=Object.Instantiate(prefab);
            try
            {
                var item=go.GetComponent<PlacementItem>();
                Require(PlacementTools.Validate(item).Count==0,"Valid template rejected.");
                item.transform.localScale=Vector3.one*2;
                Require(PlacementTools.Validate(item).Any(e=>e.Contains("缩放")),"Root scaling was not rejected.");
                item.transform.localScale=Vector3.one;
                var collider=item.visualRoot.gameObject.AddComponent<BoxCollider2D>();
                Require(PlacementTools.Validate(item).Any(e=>e.Contains("碰撞")),"Visual collider was not rejected.");
                Object.DestroyImmediate(collider);
                item.visualRoot.localRotation=Quaternion.Euler(15,0,0);
                Require(PlacementTools.Validate(item).Any(e=>e.Contains("视觉角度")),"Bad world-plane visual angle was not rejected.");
                item.ApplyArtwork();PlacementTools.ValidateTree(go);
            }
            finally {Object.DestroyImmediate(go);}
        }

        public static void RestaurantAnchorOwnership()
        {
            var p=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Modules/Restaurant/RestaurantWorld.prefab");
            var go=Object.Instantiate(p);
            try
            {
                var world=go.GetComponent<ModuleWorld>();PlacementTools.ValidateTree(go);
                Require(world.GetComponentsInChildren<PlacementItem>().Length==42,"Restaurant item coverage changed.");
                Require(world.anchors.Length==37,"Restaurant anchor count changed.");
                foreach(var id in new[]{"cook/0","cook/1","plate/0","plate/1","counter"})
                {
                    var anchor=world.GetAnchor(id);var item=anchor.GetComponentInParent<PlacementItem>();
                    Require(item!=null && anchor.IsChildOf(item.anchorsRoot),"Unowned gameplay anchor: "+id);
                    var before=anchor.position;item.transform.position+=Vector3.right;
                    Require(Vector3.Distance(anchor.position,before+Vector3.right)<.0001f,"Anchor did not move with furniture.");
                }
                Require(Vector3.Distance(world.GetAnchor("player-seat").position,new Vector3(-3,-4.5f,0))<.001f,"Player seat moved.");
                Require(Vector3.Distance(world.GetAnchor("entry").position,new Vector3(0,-7,0))<.001f,"Entry moved.");
            }
            finally {Object.DestroyImmediate(go);}
        }

        public static void ProjectionAtEdgesAndDepth()
        {
            foreach(bool xz in new[]{false,true})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PlacementTools.SampleFolder+"/Stove.prefab");
                var go=Object.Instantiate(prefab);var cameraGO=new GameObject("Placement projection acceptance");
                RenderTexture target=null;
                try
                {
                    var item=go.GetComponent<PlacementItem>();item.xzGround=xz;item.sorter.xzGround=xz;item.ApplyArtwork();
                    var camera=cameraGO.AddComponent<Camera>();
                    camera.orthographic=false;camera.fieldOfView=Standard.verticalFieldOfView;
                    var rotation=Standard.ArtworkRotation(xz);camera.transform.SetPositionAndRotation(-(rotation*Vector3.forward)*27.5f,rotation);
                    var facing=item.visualRoot.GetComponent<CameraFacingVisual>();
                    if(facing!=null)
                    {
                        var so=new SerializedObject(facing);so.FindProperty("targetCamera").objectReferenceValue=camera;
                        so.ApplyModifiedPropertiesWithoutUndo();facing.AlignToCamera();
                    }
                    bool observedWorldPlanePerspective=false;
                    foreach(float aspect in new[]{4f/3,16f/9,21f/9})
                    {
                        // A screen-backed Camera clamps pixelRect to the current Game view. Use a real
                        // independent target so the ultrawide test doesn't accidentally test a squeezed viewport.
                        camera.targetTexture=null;
                        if(target!=null)Object.DestroyImmediate(target);
                        target=new RenderTexture(Mathf.RoundToInt(1080*aspect),1080,0);
                        camera.targetTexture=target;camera.rect=new Rect(0,0,1,1);camera.aspect=aspect;
                        foreach(float pan in new[]{-1.5f,0,1.5f})
                        {
                            camera.transform.position=-(rotation*Vector3.forward)*27.5f+Vector3.right*pan;
                            float nearWidth=0,farWidth=0;
                            foreach(float depth in new[]{-5f,0,5f}) foreach(float x in new[]{-8f,0,8f})
                            {
                                go.transform.position=xz?new Vector3(x,0,depth):new Vector3(x,depth,0);
                                var b=item.art.sprite.bounds;var t=item.art.transform;
                                var bl=camera.WorldToScreenPoint(t.TransformPoint(b.min));
                                var br=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(b.max.x,b.min.y,0)));
                                var tl=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(b.min.x,b.max.y,0)));
                                var tr=camera.WorldToScreenPoint(t.TransformPoint(b.max));
                                if(facing!=null)
                                {
                                    try { ModuleSupplementalChecks.CheckArtworkProjection(camera,new[]{item.sorter}); }
                                    catch(Exception e) { throw new InvalidOperationException($"XZ={xz}, aspect={aspect}, pan={pan}, x={x}, depth={depth}: {e.Message}"); }
                                }
                                else if(Mathf.Abs(Vector2.Distance(bl,br)-Vector2.Distance(tl,tr))>.02f)
                                    observedWorldPlanePerspective=true;
                                float width=Vector2.Distance(bl,br);
                                if(x==0&&depth==-5)nearWidth=width;
                                if(x==0&&depth==5)farWidth=width;
                            }
                            Require(nearWidth>farWidth,"Perspective did not make near artwork larger.");
                        }
                    }
                    if(facing==null)
                        Require(observedWorldPlanePerspective,"World-plane artwork did not show perspective depth.");
                }
                finally {Object.DestroyImmediate(go);Object.DestroyImmediate(cameraGO);if(target!=null)Object.DestroyImmediate(target);}
            }
        }

        public static void TemplateInstancesAreIndependent()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PlacementTools.SampleFolder+"/TableSet.prefab");
            var a=Object.Instantiate(prefab);var b=Object.Instantiate(prefab);
            try
            {
                var ia=a.GetComponentInChildren<PlacementItem>();var ib=b.GetComponentInChildren<PlacementItem>();
                float original=ib.width;ia.useSourceDimensions=false;ia.width*=1.2f;ia.ApplyArtwork();
                Require(ib.width==original,"Editing instance changed sibling.");
                a.transform.position+=new Vector3(3,2,0);
                PlacementTools.ValidateTree(a);PlacementTools.ValidateTree(b);
            }
            finally {Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
        }

        public static void PreviewDoesNotCopyGameplayOrChangeSource()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PlacementTools.SampleFolder+"/Stove.prefab");
            var go=Object.Instantiate(prefab);
            try
            {
                go.transform.position=new Vector3(7,8,0);
                var item=go.GetComponent<PlacementItem>();
                var position=item.transform.position;var rotation=item.visualRoot.localRotation;
                var main=Camera.main;var mainPosition=main!=null?main.transform.position:Vector3.zero;
                PlacementPreviewWindow.Open(go);
                var window=Resources.FindObjectsOfTypeAll<PlacementPreviewWindow>().Single();
                var field=typeof(PlacementPreviewWindow).GetField("clone",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                var clone=(GameObject)field.GetValue(window);
                Require(clone!=null && clone.GetComponentsInChildren<SpriteRenderer>().Length==1,"Preview missing copied art.");
                Require(clone.GetComponentsInChildren<MonoBehaviour>().Length==0,"Preview instantiated gameplay scripts.");
                Require(item.transform.position==position && Quaternion.Angle(item.visualRoot.localRotation,rotation)<.001f,"Preview modified source.");
                Require(main==null||main.transform.position==mainPosition,"Preview modified gameplay camera.");
            }
            finally
            {
                foreach(var window in Resources.FindObjectsOfTypeAll<PlacementPreviewWindow>())window.Close();
                Object.DestroyImmediate(go);
            }
        }
    }
}
