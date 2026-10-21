using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GourmetAbyss.CameraSystem.Acceptance
{
    /// <summary>
    /// 直接加载正式场景的需求验收。这里不创建替代场景，验证的是策划实际会玩的
    /// UpGround / Layer1 以及其中真实序列化的 CameraFollow、玩家和餐厅入口。
    /// </summary>
    public sealed class CameraRequirementsAcceptanceTests
    {
        private const string TownScene = "UpGround";
        private const string DungeonScene = "Layer1";
        private readonly List<string> _cameraErrors = new List<string>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            LogAssert.ignoreFailingMessages = false;
            Application.logMessageReceived += CaptureCameraError;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            CameraDirector director = CameraService.Active;
            if (director != null && director.InputRouter != null)
                director.InputRouter.ClearDebugOverride();

            Application.logMessageReceived -= CaptureCameraError;
            LogAssert.ignoreFailingMessages = false;
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TownScene_ActuallyFollowsFacingDirectionWithSmoothLookAhead()
        {
            yield return LoadProductionScene(TownScene);
            CameraDirector director = RequireActiveDirector(TownScene);
            MonoBehaviour cameraFollow = RequireBehaviour("CameraFollow");
            Transform target = RequireDefaultTarget(cameraFollow);
            FreezePlayer(target);

            Assert.That(director.Camera.orthographic, Is.True, "小镇正式场景必须使用正交相机。");
            Assert.That(director.GetDebugSummary(), Does.Contain("Town"), "UpGround 未启用小镇镜头源。");

            CameraPlane plane = CameraPlane.FromRotation(director.CurrentPose.Rotation, target.position);
            MonoBehaviour controller = FindBehaviourOnTarget(target, "TopDownController");
            Assert.That(controller, Is.Not.Null, "小镇默认跟随目标缺少 TopDownController。");

            SetPrivateField(controller, "cameraFacingDirection", plane.Right);
            yield return WaitRealtime(1.4f);
            CameraPose facingRightPose = director.CurrentPose;

            SetPrivateField(controller, "cameraFacingDirection", -plane.Right);
            yield return WaitRealtime(1.4f);
            CameraPose facingLeftPose = director.CurrentPose;

            float facingRightPlanarOffset = PlanarDistance(
                facingRightPose.Position - target.position,
                plane);
            float facingLeftPlanarOffset = PlanarDistance(
                facingLeftPose.Position - target.position,
                plane);
            Assert.That(facingRightPlanarOffset, Is.LessThanOrEqualTo(1.8f),
                "小镇基础镜头保留了场景初始平面偏移，玩家没有处于朝向前瞻允许的构图范围内。");
            Assert.That(facingLeftPlanarOffset, Is.LessThanOrEqualTo(1.8f),
                "小镇反向前瞻后玩家仍超出允许的构图范围。");

            float lookAheadTravel = Mathf.Abs(Vector3.Dot(
                facingRightPose.Position - facingLeftPose.Position,
                plane.Right));
            Assert.That(lookAheadTravel, Is.GreaterThan(1.5f),
                "改变玩家朝向后，小镇镜头没有产生足够的朝向前瞻位移。");
            Assert.That(lookAheadTravel, Is.LessThan(3.6f),
                "小镇镜头朝向前瞻超过配置上限。");

            Vector3 cameraBeforeTargetMove = director.CurrentPose.Position;
            target.position += plane.Right * 4f;
            yield return null;
            float firstFrameTravel = Vector3.Dot(
                director.CurrentPose.Position - cameraBeforeTargetMove,
                plane.Right);
            Assert.That(firstFrameTravel, Is.GreaterThan(0.001f), "玩家移动后小镇镜头没有开始跟随。");
            Assert.That(firstFrameTravel, Is.LessThan(3.9f), "小镇镜头在首帧瞬移到了玩家位置，未执行平滑跟随。");

            yield return WaitRealtime(1.4f);
            float settledTravel = Vector3.Dot(
                director.CurrentPose.Position - cameraBeforeTargetMove,
                plane.Right);
            Assert.That(settledTravel, Is.EqualTo(4f).Within(0.35f), "小镇镜头平滑后没有稳定跟随玩家位移。");
            AssertNoCameraErrors();
        }

        [UnityTest]
        public IEnumerator DungeonScene_ActuallyUsesDeadZoneUiBlockingAndCappedMouseOffset()
        {
            yield return LoadProductionScene(DungeonScene);
            CameraDirector director = RequireActiveDirector(DungeonScene);
            MonoBehaviour cameraFollow = RequireBehaviour("CameraFollow");
            Transform target = RequireDefaultTarget(cameraFollow);
            FreezePlayer(target);

            Assert.That(director.Camera.orthographic, Is.False, "地牢正式场景必须使用透视相机。");
            Assert.That(director.Camera.nearClipPlane, Is.GreaterThan(0f));
            Assert.That(Mathf.Abs((director.CurrentPose.Rotation * Vector3.forward).y), Is.GreaterThan(0.1f),
                "地牢正式场景相机缺少俯角。");
            Assert.That(director.GetDebugSummary(), Does.Contain("Dungeon"), "Layer1 未启用地牢镜头源。");

            CameraInputRouter router = director.InputRouter;
            router.SetDebugOverride(new CameraInputFrame { PointerNormalized = Vector2.zero });
            yield return WaitRealtime(1.3f);
            CameraPose centeredPose = director.CurrentPose;

            router.SetDebugOverride(new CameraInputFrame { PointerNormalized = new Vector2(0.35f, 0f) });
            yield return WaitRealtime(0.9f);
            CameraPose nearPose = director.CurrentPose;

            router.SetDebugOverride(new CameraInputFrame { PointerNormalized = new Vector2(0.65f, 0f) });
            yield return WaitRealtime(0.9f);
            CameraPose middlePose = director.CurrentPose;

            router.SetDebugOverride(new CameraInputFrame { PointerNormalized = Vector2.right });
            yield return WaitRealtime(1.1f);
            CameraPose edgePose = director.CurrentPose;

            CameraPlane plane = CameraPlane.FromRotation(edgePose.Rotation, target.position);
            float nearOffset = PlanarDistance(nearPose.Position - centeredPose.Position, plane);
            float middleOffset = PlanarDistance(middlePose.Position - centeredPose.Position, plane);
            Vector3 edgeDelta = edgePose.Position - centeredPose.Position;
            float edgeOffset = PlanarDistance(edgeDelta, plane);
            Assert.That(nearOffset, Is.GreaterThan(0.05f), "鼠标离开中心死区后镜头没有产生偏移。");
            Assert.That(middleOffset, Is.GreaterThan(nearOffset + 0.2f), "鼠标距离增加后镜头偏移没有增加。");
            Assert.That(edgeOffset, Is.GreaterThan(middleOffset + 0.2f), "鼠标到达屏幕边缘后镜头偏移没有继续增加。");
            Assert.That(edgeOffset, Is.GreaterThan(2f), "鼠标移至屏幕边缘后地牢镜头偏移不足。");
            Assert.That(edgeOffset, Is.LessThanOrEqualTo(3.15f), "地牢镜头偏移超过 3 米配置上限。");

            router.SetDebugOverride(new CameraInputFrame
            {
                PointerNormalized = Vector2.right,
                PointerBlockedByUi = true
            });
            yield return WaitRealtime(1.5f);
            float blockedOffset = Vector3.ProjectOnPlane(
                director.CurrentPose.Position - centeredPose.Position,
                plane.Normal).magnitude;
            Assert.That(blockedOffset, Is.LessThan(0.35f), "鼠标位于 UI 上时地牢镜头仍在跟随鼠标。");

            router.SetDebugOverride(new CameraInputFrame { PointerNormalized = Vector2.zero });
            yield return WaitRealtime(1.1f);
            Vector3 cameraBeforeTargetMove = director.CurrentPose.Position;
            target.position += plane.Right * 4f;
            yield return null;
            float firstFrameTravel = Vector3.Dot(
                director.CurrentPose.Position - cameraBeforeTargetMove,
                plane.Right);
            Assert.That(firstFrameTravel, Is.GreaterThan(0.001f), "玩家移动后地牢镜头没有开始跟随。");
            Assert.That(firstFrameTravel, Is.LessThan(3.9f), "地牢镜头在首帧瞬移到了玩家位置，未执行平滑跟随。");
            yield return WaitRealtime(1.4f);
            float settledTravel = Vector3.Dot(
                director.CurrentPose.Position - cameraBeforeTargetMove,
                plane.Right);
            Assert.That(settledTravel, Is.EqualTo(4f).Within(0.35f), "地牢镜头平滑后没有稳定跟随玩家位移。");

            CameraPose movingCenteredPose = director.CurrentPose;
            router.SetDebugOverride(new CameraInputFrame { PointerNormalized = Vector2.right });
            yield return WaitRealtime(1.1f);
            CameraPose stationaryEdgePose = director.CurrentPose;
            float stationaryEdgeOffset = PlanarDistance(
                stationaryEdgePose.Position - movingCenteredPose.Position,
                plane);
            Assert.That(stationaryEdgeOffset, Is.InRange(2.8f, 3.15f),
                "玩家静止时鼠标边缘偏移不在 3 米上限范围内。");

            target.position += plane.Right * 4f;
            yield return null;
            yield return WaitRealtime(1.5f);
            CameraPose movingEdgePose = director.CurrentPose;
            Vector3 expectedCenteredAfterMove = movingCenteredPose.Position + plane.Right * 4f;
            float movingEdgeOffset = PlanarDistance(
                movingEdgePose.Position - expectedCenteredAfterMove,
                plane);
            Assert.That(movingEdgeOffset, Is.InRange(2.8f, 3.15f),
                "玩家移动并完成跟随后，鼠标偏移没有保持相同的 3 米上限。");
            AssertNoCameraErrors();
        }

        [UnityTest]
        public IEnumerator RestaurantInTown_ActuallyLocksCentersPansBoundsAndRestores()
        {
            yield return LoadProductionScene(TownScene);
            CameraDirector director = RequireActiveDirector(TownScene);
            MonoBehaviour cameraFollow = RequireBehaviour("CameraFollow");
            Transform player = RequireDefaultTarget(cameraFollow);
            MonoBehaviour controller = FindBehaviourOnTarget(player, "TopDownController");
            MonoBehaviour restaurant = RequireBehaviour("RestaurantEntryPoint");
            Assert.That(controller, Is.Not.Null, "餐厅验收找不到正式玩家控制器。");
            Assert.That((KeyCode)GetField(restaurant, "interactKey"), Is.EqualTo(KeyCode.E),
                "正式餐厅入口没有使用策划要求的 E 键。");
            Collider restaurantTrigger = restaurant.GetComponent<Collider>();
            Assert.That(restaurantTrigger, Is.Not.Null, "正式餐厅入口缺少 Collider。");
            Assert.That(restaurantTrigger.isTrigger, Is.True, "正式餐厅入口 Collider 不是触发器。");

            bool originalCanMove = (bool)GetField(controller, "canPlayerMove");
            int baseRequestCount = director.RequestCount;
            CameraPose basePose = director.CurrentPose;
            Vector3 originalPlayerPosition = player.position;
            Quaternion originalPlayerRotation = player.rotation;
            Transform seatAnchor = (Transform)InvokePrivate(restaurant, "ResolvePlayerSeatAnchor");
            Assert.That(seatAnchor, Is.Not.Null, "正式餐厅没有可用的玩家座位锚点。");

            SetPrivateField(restaurant, "_cachedPlayer", controller);
            InvokePrivate(restaurant, "EnterEntryState");
            yield return WaitRealtime(1.5f);

            Assert.That((bool)GetProperty(restaurant, "IsEntered"), Is.True, "正式餐厅入口未进入餐厅状态。");
            Assert.That((bool)GetField(controller, "canPlayerMove"), Is.False, "进入餐厅后玩家仍可移动。");
            Assert.That(Vector3.Distance(player.position, seatAnchor.position), Is.LessThan(0.05f),
                "进入餐厅后玩家没有固定到座位锚点。");
            Assert.That(Quaternion.Angle(player.rotation, seatAnchor.rotation), Is.LessThan(1f),
                "进入餐厅后玩家朝向没有对齐座位锚点。");
            Assert.That(director.RequestCount, Is.EqualTo(baseRequestCount + 1), "餐厅镜头请求没有正确入栈。");
            PlanarPerspectiveView moduleView=GetField(restaurant,"moduleCameraView") as PlanarPerspectiveView;
            Assert.That(director.GetDebugSummary(), Does.Contain(moduleView!=null?"PlanarPerspective":"Restaurant"), "餐厅镜头未取得控制权。");

            CameraInputRouter router = director.InputRouter;
            CameraPose beforeDrag = director.CurrentPose;
            if(moduleView!=null)
            {
                Assert.That(director.Camera.orthographic,Is.False,"模块餐厅应使用透视相机。");
                Assert.That(director.Camera.nearClipPlane,Is.GreaterThan(0));
                Assert.That(Vector3.Distance(beforeDrag.Position,moduleView.Pose(Vector2.zero).Position),Is.LessThan(.05f));
                router.SetDebugOverride(new CameraInputFrame{PanHeld=true,PointerPositionPixels=new Vector2(Screen.width*.5f,Screen.height*.5f),PointerDeltaPixels=new Vector2(100,0)});
                yield return WaitRealtime(.5f);
                Assert.That(moduleView.PanOffset.magnitude,Is.GreaterThan(.01f));
                Assert.That(moduleView.PanOffset.magnitude,Is.LessThanOrEqualTo(moduleView.profile.panLimit+.001f));
                Vector2 offset=moduleView.PanOffset;
                router.SetDebugOverride(new CameraInputFrame{PanHeld=true,PointerBlockedByUi=true,PointerDeltaPixels=new Vector2(100,100)});
                yield return WaitRealtime(.3f);
                Assert.That(Vector2.Distance(offset,moduleView.PanOffset),Is.LessThan(.001f));
                router.ClearDebugOverride();
            }
            else
            {
            GameObject runtimeCameraAnchor = GetField(restaurant, "_runtimeCameraAnchor") as GameObject;
            Assert.That(runtimeCameraAnchor, Is.Not.Null, "餐厅镜头没有创建运行时锚点。");
            CameraPlane plane = CameraPlane.FromRotation(
                beforeDrag.Rotation,
                runtimeCameraAnchor.transform.position);
            CameraPlanarBounds bounds = (CameraPlanarBounds)InvokePrivate(restaurant, "ResolveRestaurantBounds", plane);
            Assert.That(bounds.IsValid, Is.True, "正式餐厅没有可用的相机边界。");

            Vector3 centeredPosition = CalculateCenteredCameraPosition(basePose, runtimeCameraAnchor.transform.position);
            CameraPose expectedCenteredPose = CameraBoundsUtility.ConstrainOrthographicPose(
                new CameraPose(centeredPosition, beforeDrag.Rotation, beforeDrag.OrthographicSize),
                director.Camera.aspect,
                plane,
                bounds);
            Assert.That(Vector3.Distance(beforeDrag.Position, expectedCenteredPose.Position), Is.LessThan(0.2f),
                "餐厅镜头没有稳定在餐厅中心构图。");

            Vector2 dragInput = FindDragInputForLargestAvailablePan(
                beforeDrag,
                director.Camera.aspect,
                plane,
                bounds,
                out float availablePan);
            router.SetDebugOverride(new CameraInputFrame
            {
                PanHeld = true,
                PointerDeltaPixels = dragInput
            });
            yield return null;
            router.SetDebugOverride(new CameraInputFrame { PanHeld = true });
            yield return WaitRealtime(1.1f);

            CameraPose afterDrag = director.CurrentPose;
            float actualPan = Vector3.Distance(afterDrag.Position, beforeDrag.Position);
            if (availablePan > 0.1f)
            {
                Assert.That(actualPan, Is.GreaterThan(0.1f),
                    "正式餐厅内容超出一屏，但中键拖拽没有移动镜头。");
            }
            else
            {
                Assert.That(actualPan, Is.LessThan(0.1f),
                    "正式餐厅内容可完整显示时，镜头不应被拖离中心。");
            }

            CameraPose constrained = CameraBoundsUtility.ConstrainOrthographicPose(
                afterDrag,
                director.Camera.aspect,
                plane,
                bounds);
            Assert.That(Vector3.Distance(constrained.Position, afterDrag.Position), Is.LessThan(0.02f),
                "餐厅拖拽后镜头越过了餐厅边界。");
            }

            InvokePublic(restaurant, "LeaveRestaurant");
            yield return WaitRealtime(1.3f);
            Assert.That((bool)GetProperty(restaurant, "IsEntered"), Is.False, "离开餐厅后状态未恢复。");
            Assert.That((bool)GetField(controller, "canPlayerMove"), Is.EqualTo(originalCanMove),
                "离开餐厅后玩家移动状态没有恢复。");
            Assert.That(Vector3.Distance(player.position, originalPlayerPosition), Is.LessThan(0.05f),
                "离开餐厅后玩家没有回到进入前的门口位置。");
            Assert.That(Quaternion.Angle(player.rotation, originalPlayerRotation), Is.LessThan(1f),
                "离开餐厅后玩家朝向没有恢复。");
            Assert.That(director.RequestCount, Is.EqualTo(baseRequestCount), "离开餐厅后镜头请求残留。");
            CameraPlane restoredTownPlane = CameraPlane.FromRotation(
                director.CurrentPose.Rotation,
                player.position);
            float restoredPlanarOffset = PlanarDistance(
                director.CurrentPose.Position - player.position,
                restoredTownPlane);
            Assert.That(restoredPlanarOffset, Is.LessThanOrEqualTo(1.8f),
                "离开餐厅后镜头没有恢复到 Town 朝向前瞻允许的构图范围。");
            AssertNoCameraErrors();
        }

        private static Vector2 FindDragInputForLargestAvailablePan(
            CameraPose centeredPose,
            float aspect,
            CameraPlane plane,
            CameraPlanarBounds bounds,
            out float availablePan)
        {
            Vector2[] directions =
            {
                Vector2.right,
                Vector2.left,
                Vector2.up,
                Vector2.down
            };

            availablePan = 0f;
            Vector2 bestDirection = Vector2.right;
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2 direction = directions[i];
                CameraPose probe = centeredPose;
                probe.Position += plane.Right * (direction.x * 1000f) +
                                  plane.Up * (direction.y * 1000f);
                CameraPose constrained = CameraBoundsUtility.ConstrainOrthographicPose(
                    probe,
                    aspect,
                    plane,
                    bounds);
                Vector3 displacement = constrained.Position - centeredPose.Position;
                float planarDistance = new Vector2(
                    Vector3.Dot(displacement, plane.Right),
                    Vector3.Dot(displacement, plane.Up)).magnitude;
                if (planarDistance <= availablePan)
                    continue;

                availablePan = planarDistance;
                bestDirection = direction;
            }

            // RestaurantPanCameraSource uses grab-and-drag semantics, so pointer motion is
            // the inverse of the requested camera travel direction.
            return -bestDirection * 320f;
        }

        private static float PlanarDistance(Vector3 delta, CameraPlane plane)
        {
            return new Vector2(
                Vector3.Dot(delta, plane.Right),
                Vector3.Dot(delta, plane.Up)).magnitude;
        }

        private static Vector3 CalculateCenteredCameraPosition(CameraPose referencePose, Vector3 target)
        {
            CameraPlane plane = CameraPlane.FromRotation(referencePose.Rotation, target);
            Vector3 forward = referencePose.Rotation * Vector3.forward;
            float denominator = Vector3.Dot(forward, plane.Normal);
            if (Mathf.Abs(denominator) < 0.0001f)
                return target + (referencePose.Position - plane.Origin);

            float distance = Vector3.Dot(target - referencePose.Position, plane.Normal) / denominator;
            if (distance <= 0.01f)
                distance = Mathf.Max(1f, Vector3.Distance(referencePose.Position, target));
            return target - forward * distance;
        }

        [UnityTest]
        public IEnumerator ProductionLevelManagerPipeline_ActuallyRebindsTownDungeonTownWithoutLeaks()
        {
            yield return LoadProductionScene(TownScene);
            CameraDirector firstTown = RequireActiveDirector(TownScene);
            int firstTownId = firstTown.GetInstanceID();
            Assert.That(firstTown.GetDebugSummary(), Does.Contain("Town"));
            MonoBehaviour levelManager = RequireBehaviour("LevelManager");
            MonoBehaviour townFollow = RequireBehaviour("CameraFollow");
            Transform townPlayer = RequireDefaultTarget(townFollow);
            Vector3 townReturnPosition = townPlayer.position;

            MonoBehaviour homePortal = RequireBehaviour("HomeCavecar");
            MonoBehaviour mapUi = RequireBehaviour("MapUIManager");
            InvokePublic(homePortal, "OpenMapUI");
            yield return WaitRealtime(0.8f);
            Assert.That((bool)InvokePublic(homePortal, "IsMapUIActive"), Is.True,
                "地面传送点没有打开地图选择 UI。");

            object unlockedDungeonRegion = CreateUnlockedRegion(DungeonScene);
            InvokePublic(mapUi, "EnterRegion", unlockedDungeonRegion);
            yield return WaitForLevelTransition(levelManager, DungeonScene, true);
            Assert.That(SceneManager.GetSceneByName(TownScene).isLoaded, Is.True,
                "正式进入地牢应保留 Additive 加载的地面主场景。");
            CameraDirector dungeon = RequireActiveDirector(DungeonScene);
            Assert.That(dungeon.GetInstanceID(), Is.Not.EqualTo(firstTownId), "切到地牢后仍引用已卸载的小镇相机。");
            Assert.That(dungeon.GetDebugSummary(), Does.Contain("Dungeon"));
            Assert.That(dungeon.RequestCount, Is.EqualTo(1), "地牢场景基础镜头请求数量异常。");
            Assert.That(dungeon.Camera.orthographic, Is.False);
            System.IO.Directory.CreateDirectory("Library/CombatAcceptance");
            ScreenCapture.CaptureScreenshot("Library/CombatAcceptance/Town-to-Dungeon.png");
            yield return null;

            int dungeonId = dungeon.GetInstanceID();
            MonoBehaviour dungeonExit = RequireBehaviour("levelCaveCar");
            InvokePublic(dungeonExit, "ToHome");
            yield return WaitForLevelTransition(levelManager, DungeonScene, false);
            CameraDirector secondTown = RequireActiveDirector(TownScene);
            Assert.That(secondTown.GetInstanceID(), Is.Not.EqualTo(dungeonId), "返回小镇后仍引用已卸载的地牢相机。");
            Assert.That(secondTown.GetInstanceID(), Is.EqualTo(firstTownId), "返回小镇时不应重建地面基础镜头。");
            Assert.That(secondTown.GetDebugSummary(), Does.Contain("Town"));
            Assert.That(secondTown.Camera.orthographic, Is.True);
            Assert.That(secondTown.RequestCount, Is.EqualTo(1), "返回小镇后存在跨场景镜头请求泄漏。");
            yield return WaitRealtime(1.5f);
            CameraPlane townPlane = CameraPlane.FromRotation(secondTown.CurrentPose.Rotation, townPlayer.position);
            float returnPlanarOffset = PlanarDistance(
                secondTown.CurrentPose.Position - townPlayer.position,
                townPlane);
            Assert.That(returnPlanarOffset, Is.LessThanOrEqualTo(1.8f),
                "返回小镇后玩家超出 Town 朝向前瞻允许的构图范围。");
            Assert.That(Vector3.Distance(townPlayer.position, townReturnPosition), Is.LessThan(0.05f),
                "从地牢返回后玩家没有回到进入地牢前的传送点附近。");
            MonoBehaviour townController = FindBehaviourOnTarget(townPlayer, "TopDownController");
            Assert.That(townController, Is.Not.Null, "从地牢返回后找不到地面玩家控制器。");
            Assert.That(townController.enabled, Is.True, "从地牢返回后地面玩家控制器没有恢复。");
            AssertNoCameraErrors();
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator TreeTemplates_FaceCameraOnFirstEntryRestartAndReentry()
        {
            yield return LoadProductionScene(TownScene);
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Environment/Vegetation/Prototype/Prefabs/Vegetation_Single_Tree.prefab");
            Assert.IsNotNull(prefab);
            var trees = new List<CameraFacingVisual>();
            var observations = new List<string>();
            var manager = RequireBehaviour("LevelManager");
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> placeTrees = (scene, mode) =>
            {
                if (scene.name != DungeonScene || mode != LoadSceneMode.Additive) return;
                trees.Clear();
                // Spawn before the town is hidden, matching authored tree OnEnable timing.
                for (int i = 0; i < 3; i++)
                {
                    var tree = UnityEngine.Object.Instantiate(prefab);
                    SceneManager.MoveGameObjectToScene(tree, scene);
                    trees.Add(tree.GetComponentInChildren<CameraFacingVisual>());
                }
            };
            SceneManager.sceneLoaded += placeTrees;
            try
            {
                Assert.IsTrue((bool)InvokePublic(manager, "TryEnterLevel", DungeonScene));
                yield return WaitForLevelTransition(manager, DungeonScene, true);
                VerifyProductionTreeFacing(trees, observations, "first-entry");
                var runs = RequireBehaviour("RunSessionManager");
                InvokePublic(RequireBehaviour("levelCaveCar"), "ToHome");
                yield return null;
                Assert.IsTrue((bool)InvokePublic(runs, "RetryExploration"));
                yield return WaitForLevelTransition(manager, DungeonScene, true);
                VerifyProductionTreeFacing(trees, observations, "restart");

                InvokePublic(RequireBehaviour("levelCaveCar"), "ToHome");
                yield return null;
                Assert.IsTrue((bool)InvokePublic(runs, "ReturnToTown"));
                yield return WaitForLevelTransition(manager, DungeonScene, false);
                Assert.IsTrue((bool)InvokePublic(manager, "TryEnterLevel", DungeonScene));
                yield return WaitForLevelTransition(manager, DungeonScene, true);
                VerifyProductionTreeFacing(trees, observations, "reentry");
                AssertNoCameraErrors();
                System.IO.Directory.CreateDirectory("Library/TreeFacingVerification");
                System.IO.File.WriteAllLines("Library/TreeFacingVerification/production-angles.txt", observations);
            }
            finally
            {
                SceneManager.sceneLoaded -= placeTrees;
            }
        }

        private static void VerifyProductionTreeFacing(List<CameraFacingVisual> trees, List<string> observations, string stage)
        {
            var director = RequireActiveDirector(DungeonScene);
            Assert.AreEqual(DungeonScene, director.gameObject.scene.name);
            Assert.AreEqual(3, trees.Count);
            foreach (var tree in trees)
            {
                Assert.IsNotNull(tree);
                float angle = Quaternion.Angle(tree.transform.rotation, tree.RotationFor(director.Camera));
                observations.Add(stage + ": " + angle.ToString("F4") + " degrees");
                Assert.That(angle, Is.LessThan(.1f), stage + " tree is not facing the dungeon camera.");
                Assert.That(Quaternion.Angle(tree.transform.parent.rotation, Quaternion.identity), Is.LessThan(.01f));
            }
        }
#endif

        [UnityTest]
        public IEnumerator AllDungeonLayers_UsePerspectiveAimAndSpawnedVisuals()
        {
            foreach (var scene in new[] { "Layer1", "Layer2", "Layer3" })
            {
                yield return LoadProductionScene(scene);
                var director = RequireActiveDirector(scene);
                var follow = RequireBehaviour("CameraFollow");
                var profile = GetField(follow, "dungeonProfile") as DungeonPerspectiveProfile;
                Assert.IsNotNull(profile, scene + " missing shared perspective profile");
                var target = RequireDefaultTarget(follow); FreezePlayer(target);
                director.InputRouter.SetDebugOverride(default);
                yield return WaitRealtime(1.5f);
                var camera = director.Camera;
                Assert.IsFalse(camera.orthographic); Assert.That(camera.fieldOfView, Is.EqualTo(profile.fieldOfView).Within(.01));
                Assert.That(camera.nearClipPlane, Is.GreaterThan(0));
                Vector3 center = camera.WorldToViewportPoint(target.position);
                Assert.That(center.x, Is.EqualTo(.5f).Within(.02)); Assert.That(center.y, Is.EqualTo(.5f).Within(.02));
                var near = target.position - Vector3.forward * 5; var far = target.position + Vector3.forward * 5;
                float nw = (camera.WorldToViewportPoint(near+Vector3.right)-camera.WorldToViewportPoint(near)).magnitude;
                float fw = (camera.WorldToViewportPoint(far+Vector3.right)-camera.WorldToViewportPoint(far)).magnitude;
                Assert.Greater(nw, fw * 1.1f, scene + " no real depth scaling");
                var controller = FindBehaviourOnTarget(target, "TopDownController");
                int aimMask = ((LayerMask)GetField(controller,"aimLayerMask")).value;
                int groundMask = ((LayerMask)GetField(controller,"groundLayerMask")).value;
                float height = (float)GetField(controller,"aimHeightOffset");
                int floorHits = 0;
                foreach (var offset in new[] { new Vector3(3,0,0),new Vector3(-3,0,0),new Vector3(0,0,3),new Vector3(0,0,-3) })
                {
                    var screen = (Vector2)camera.WorldToScreenPoint(target.position+offset);
                    Assert.IsTrue(CameraAimUtility.TryResolve(camera,screen,aimMask,groundMask,target.position.y,height,out var aim,out var hit));
                    if(hit!=null && (groundMask & (1<<hit.gameObject.layer))!=0)
                    {
                        floorHits++;
                        Assert.That(Vector2.Distance(camera.WorldToScreenPoint(aim-Vector3.up*height),screen),Is.LessThan(.5f));
                    }
                }
                Assert.Greater(floorHits,0,scene+" no real floor raycast verified");
                var faces=UnityEngine.Object.FindObjectsOfType<CameraFacingVisual>();
                Assert.Greater(faces.Length,0,scene+" missing dynamically spawned standing visuals");
                foreach(var face in faces)
                {
                    Assert.IsNull(face.GetComponent<Collider>()); Assert.IsNull(face.GetComponent<Rigidbody>());
                    Assert.That(Quaternion.Angle(face.transform.rotation,camera.transform.rotation),Is.LessThan(.1f));
                }
                foreach(var childCamera in camera.GetComponentsInChildren<Camera>())
                    if(childCamera!=camera) { Assert.AreEqual(camera.orthographic,childCamera.orthographic); Assert.AreEqual(camera.fieldOfView,childCamera.fieldOfView); }
                foreach(var behaviour in camera.GetComponentsInChildren<MonoBehaviour>())
                    if(behaviour.GetType().Name=="CameraSnapSRP")Assert.IsFalse(behaviour.enabled);
                var before=director.CurrentPose.Position; director.PlayImpulse(.3f,.15f);
                yield return null;
                Assert.That(Vector3.Distance(camera.transform.position,director.CurrentPose.Position),Is.GreaterThan(.00001f));
                yield return WaitRealtime(.5f);
                Assert.That(Vector3.Distance(camera.transform.position,before),Is.LessThan(.02f));
                System.IO.Directory.CreateDirectory("Library/CombatAcceptance");
                ScreenCapture.CaptureScreenshot("Library/CombatAcceptance/"+scene+"-verified.png"); yield return null;
                director.InputRouter.ClearDebugOverride(); AssertNoCameraErrors();
            }
        }

        private static object CreateUnlockedRegion(string sceneName)
        {
            Type regionType = Type.GetType("RegionData, Assembly-CSharp");
            Assert.That(regionType, Is.Not.Null, "运行时找不到地图区域数据类型 RegionData。");
            return Activator.CreateInstance(
                regionType,
                "camera_acceptance",
                "Camera Acceptance",
                true,
                sceneName);
        }

        private static IEnumerator WaitForLevelTransition(
            MonoBehaviour levelManager,
            string levelSceneName,
            bool expectedLoaded)
        {
            float timeoutAt = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < timeoutAt)
            {
                bool transitioning = (bool)InvokePublic(levelManager, "IsTransitioning");
                Scene levelScene = SceneManager.GetSceneByName(levelSceneName);
                bool isLoaded = levelScene.IsValid() && levelScene.isLoaded;
                if (!transitioning && isLoaded == expectedLoaded)
                {
                    yield return WaitRealtime(0.5f);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"正式转场超时：{levelSceneName} 期望加载状态为 {expectedLoaded}。");
        }

        private IEnumerator LoadProductionScene(string sceneName)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null, $"无法加载正式场景 {sceneName}。");
            while (!operation.isDone)
                yield return null;

            float timeoutAt = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < timeoutAt)
            {
                CameraDirector director = CameraService.Active;
                MonoBehaviour follow = FindBehaviour("CameraFollow");
                if (director != null && follow != null && TryGetDefaultTarget(follow, out _))
                    break;
                yield return null;
            }

            yield return WaitRealtime(0.4f);
        }

        private static CameraDirector RequireActiveDirector(string sceneName)
        {
            CameraDirector director = CameraService.Active;
            Assert.That(director, Is.Not.Null, $"正式场景 {sceneName} 没有激活 CameraDirector。");
            Assert.That(director.RequestCount, Is.GreaterThanOrEqualTo(1), $"正式场景 {sceneName} 没有基础镜头请求。");
            return director;
        }

        private static MonoBehaviour RequireBehaviour(string typeName)
        {
            MonoBehaviour behaviour = FindBehaviour(typeName);
            Assert.That(behaviour, Is.Not.Null, $"正式场景中找不到 {typeName}。");
            return behaviour;
        }

        private static MonoBehaviour FindBehaviour(string typeName)
        {
            return UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true)
                .FirstOrDefault(item => item != null && item.GetType().Name == typeName);
        }

        private static MonoBehaviour FindBehaviourOnTarget(Transform target, string typeName)
        {
            if (target == null)
                return null;
            return target.GetComponentsInParent<MonoBehaviour>(true)
                .Concat(target.GetComponentsInChildren<MonoBehaviour>(true))
                .FirstOrDefault(item => item != null && item.GetType().Name == typeName);
        }

        private static Transform RequireDefaultTarget(MonoBehaviour cameraFollow)
        {
            Assert.That(TryGetDefaultTarget(cameraFollow, out Transform target), Is.True,
                "CameraFollow 没有绑定正式玩家目标。");
            return target;
        }

        private static bool TryGetDefaultTarget(MonoBehaviour cameraFollow, out Transform target)
        {
            target = cameraFollow?.GetType()
                .GetProperty("DefaultTarget", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(cameraFollow) as Transform;
            return target != null;
        }

        private static void FreezePlayer(Transform target)
        {
            MonoBehaviour controller = FindBehaviourOnTarget(target, "TopDownController");
            if (controller != null)
                controller.enabled = false;
            Rigidbody body = target.GetComponentInParent<Rigidbody>();
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }

        private static IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }

        private static object GetField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{target.GetType().Name} 缺少字段 {fieldName}。");
            return field.GetValue(target);
        }

        private static object GetProperty(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, $"{target.GetType().Name} 缺少属性 {propertyName}。");
            return property.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{target.GetType().Name} 缺少私有字段 {fieldName}。");
            field.SetValue(target, value);
        }

        private static object InvokePrivate(object target, string methodName, params object[] arguments)
        {
            return Invoke(target, methodName, BindingFlags.Instance | BindingFlags.NonPublic, arguments);
        }

        private static object InvokePublic(object target, string methodName, params object[] arguments)
        {
            return Invoke(target, methodName, BindingFlags.Instance | BindingFlags.Public, arguments);
        }

        private static object Invoke(object target, string methodName, BindingFlags flags, object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, flags);
            Assert.That(method, Is.Not.Null, $"{target.GetType().Name} 缺少方法 {methodName}。");
            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private void CaptureCameraError(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;

            bool belongsToCamera = condition.IndexOf("[Camera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   stackTrace.IndexOf("GourmetAbyss.CameraSystem", StringComparison.OrdinalIgnoreCase) >= 0;
            if (belongsToCamera)
                _cameraErrors.Add(condition);
        }

        private void AssertNoCameraErrors()
        {
            Assert.That(_cameraErrors, Is.Empty,
                "正式场景运行期间出现镜头框架错误：\n" + string.Join("\n", _cameraErrors));
        }
    }
}
