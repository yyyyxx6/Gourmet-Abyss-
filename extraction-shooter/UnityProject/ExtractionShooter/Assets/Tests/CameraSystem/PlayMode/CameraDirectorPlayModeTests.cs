using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GourmetAbyss.CameraSystem.Tests
{
    public class CameraDirectorPlayModeTests
    {
        private sealed class ConstantSource : ICameraShotSource
        {
            private readonly CameraPose _pose;
            public bool Valid = true;

            public ConstantSource(CameraPose pose)
            {
                _pose = pose;
            }

            public bool TryEvaluate(in CameraEvaluationContext context, out CameraShotResult result)
            {
                CameraPlane plane = CameraPlane.FromRotation(_pose.Rotation, Vector3.zero);
                result = new CameraShotResult(
                    _pose,
                    new CameraDamping(0f, 0f, 0f),
                    plane,
                    CameraShotPolicy.None);
                return Valid;
            }
        }

        private GameObject _cameraObject;
        private CameraDirector _director;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _cameraObject = new GameObject("CameraFrameworkTestCamera");
            _cameraObject.tag = "MainCamera";
            Camera camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            _director = _cameraObject.AddComponent<CameraDirector>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_cameraObject != null)
                Object.Destroy(_cameraObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PerspectiveRequest_RestoresProjectionAndNegativeOrthographicNearPlane()
        {
            var camera=_director.Camera;
            camera.nearClipPlane=-200;
            using(var baseline=_director.AcquireShot(_cameraObject,
                new ConstantSource(new CameraPose(new Vector3(0,0,-10),Quaternion.identity,5)),new CameraShotOptions(0,0,0,"baseline")))
            {
                var perspective=_director.AcquireShot(_cameraObject,
                    new ConstantSource(new CameraPose(new Vector3(0,0,-30),Quaternion.identity,5,true,40)),new CameraShotOptions(100,1,1,"perspective"));
                yield return null;
                Assert.IsFalse(camera.orthographic);
                Assert.Greater(camera.nearClipPlane,0);
                Assert.That(camera.fieldOfView,Is.EqualTo(40).Within(.001));
                Assert.That(camera.transform.position.z,Is.EqualTo(-30).Within(.001));
                perspective.Dispose();
                yield return null;
                Assert.IsTrue(camera.orthographic);
                Assert.That(camera.nearClipPlane,Is.EqualTo(-200).Within(.001));
                Assert.That(camera.transform.position.z,Is.EqualTo(-10).Within(.001));
            }
        }

        [UnityTest]
        public IEnumerator HigherPriorityWins_AndDisposeRestoresBase()
        {
            GameObject owner = new GameObject("Owner");
            CameraShotLease baseLease = _director.AcquireShot(
                owner,
                new ConstantSource(new CameraPose(Vector3.zero, Quaternion.identity, 5f)),
                new CameraShotOptions(0, 0f, 0f, "Base"));
            yield return null;

            CameraShotLease focusLease = _director.AcquireShot(
                owner,
                new ConstantSource(new CameraPose(Vector3.right * 10f, Quaternion.identity, 3f)),
                new CameraShotOptions(100, 0f, 0f, "Focus"));
            yield return null;
            Assert.That(_director.CurrentPose.Position.x, Is.EqualTo(10f).Within(0.001f));
            Assert.That(_director.CurrentPose.OrthographicSize, Is.EqualTo(3f).Within(0.001f));

            focusLease.Dispose();
            yield return null;
            Assert.That(_director.CurrentPose.Position.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(_director.CurrentPose.OrthographicSize, Is.EqualTo(5f).Within(0.001f));

            baseLease.Dispose();
            Object.Destroy(owner);
        }

        [UnityTest]
        public IEnumerator SamePriorityNewestWins_ThenRestoresPrevious()
        {
            GameObject owner = new GameObject("Owner");
            CameraShotLease first = _director.AcquireShot(
                owner,
                new ConstantSource(new CameraPose(Vector3.right, Quaternion.identity, 5f)),
                new CameraShotOptions(100, 0f, 0f, "First"));
            CameraShotLease second = _director.AcquireShot(
                owner,
                new ConstantSource(new CameraPose(Vector3.right * 2f, Quaternion.identity, 5f)),
                new CameraShotOptions(100, 0f, 0f, "Second"));
            yield return null;
            Assert.That(_director.CurrentPose.Position.x, Is.EqualTo(2f).Within(0.001f));

            second.Dispose();
            yield return null;
            Assert.That(_director.CurrentPose.Position.x, Is.EqualTo(1f).Within(0.001f));

            first.Dispose();
            Object.Destroy(owner);
        }

        [UnityTest]
        public IEnumerator DestroyedOwnerIsAutomaticallyRemoved()
        {
            GameObject baseOwner = new GameObject("BaseOwner");
            GameObject transientOwner = new GameObject("TransientOwner");
            _director.AcquireShot(
                baseOwner,
                new ConstantSource(new CameraPose(Vector3.zero, Quaternion.identity, 5f)),
                new CameraShotOptions(0, 0f, 0f, "Base"));
            _director.AcquireShot(
                transientOwner,
                new ConstantSource(new CameraPose(Vector3.right * 8f, Quaternion.identity, 5f)),
                new CameraShotOptions(100, 0f, 0f, "Transient"));
            yield return null;
            Assert.That(_director.CurrentPose.Position.x, Is.EqualTo(8f).Within(0.001f));

            Object.Destroy(transientOwner);
            yield return null;
            yield return null;
            Assert.That(_director.CurrentPose.Position.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(_director.RequestCount, Is.EqualTo(1));

            Object.Destroy(baseOwner);
        }

        [UnityTest]
        public IEnumerator InvalidSourceFallsBackWithoutRemovingRequest()
        {
            GameObject owner = new GameObject("Owner");
            ConstantSource baseSource = new ConstantSource(new CameraPose(Vector3.right, Quaternion.identity, 5f));
            ConstantSource invalidHigh = new ConstantSource(new CameraPose(Vector3.right * 9f, Quaternion.identity, 5f))
            {
                Valid = false
            };
            _director.AcquireShot(owner, baseSource, new CameraShotOptions(0, 0f, 0f, "Base"));
            _director.AcquireShot(owner, invalidHigh, new CameraShotOptions(100, 0f, 0f, "Invalid"));
            yield return null;
            Assert.That(_director.CurrentPose.Position.x, Is.EqualTo(1f).Within(0.001f));

            invalidHigh.Valid = true;
            yield return null;
            Assert.That(_director.CurrentPose.Position.x, Is.EqualTo(9f).Within(0.001f));

            Object.Destroy(owner);
        }

        [UnityTest]
        public IEnumerator DungeonPointerOffsetIsCapped()
        {
            GameObject owner = new GameObject("Owner");
            GameObject target = new GameObject("Target");
            CameraInputRouter router = _director.InputRouter;
            router.SetDebugOverride(new CameraInputFrame
            {
                PointerNormalized = Vector2.one,
                PointerBlockedByUi = false
            });

            DungeonAimCameraSource source = new DungeonAimCameraSource(
                target.transform,
                new Vector3(0f, 10f, -10f),
                Quaternion.Euler(45f, 0f, 0f),
                5f,
                0.1f,
                3f,
                1f,
                0f,
                new CameraDamping(0f, 0f, 0f));
            _director.AcquireShot(owner, source, new CameraShotOptions(100, 0f, 0f, "Dungeon"));
            yield return null;

            CameraPlane plane = CameraPlane.FromRotation(Quaternion.Euler(45f, 0f, 0f), target.transform.position);
            Vector3 basePosition = target.transform.position + new Vector3(0f, 10f, -10f);
            Vector3 delta = _director.CurrentPose.Position - basePosition;
            float planarMagnitude = new Vector2(Vector3.Dot(delta, plane.Right), Vector3.Dot(delta, plane.Up)).magnitude;
            Assert.That(planarMagnitude, Is.LessThanOrEqualTo(3.001f));
            Assert.That(planarMagnitude, Is.GreaterThan(2.9f));

            router.ClearDebugOverride();
            Object.Destroy(owner);
            Object.Destroy(target);
        }

        [UnityTest]
        public IEnumerator DungeonPointerOffsetUsesSameCapWhileTargetMoves()
        {
            GameObject owner = new GameObject("Owner");
            GameObject target = new GameObject("MovingTarget");
            CameraInputRouter router = _director.InputRouter;
            Quaternion rotation = Quaternion.Euler(45f, 0f, 0f);
            Vector3 baseOffset = new Vector3(0f, 10f, -10f);
            DungeonAimCameraSource source = new DungeonAimCameraSource(
                target.transform,
                baseOffset,
                rotation,
                5f,
                0.1f,
                3f,
                1f,
                0f,
                new CameraDamping(0f, 0f, 0f));
            _director.AcquireShot(owner, source, new CameraShotOptions(100, 0f, 0f, "DungeonMoving"));

            router.SetDebugOverride(new CameraInputFrame { PointerNormalized = Vector2.right });
            yield return null;
            CameraPlane plane = CameraPlane.FromRotation(rotation, target.transform.position);
            float stationaryOffset = PlanarOffsetFromTarget(target.transform, baseOffset, plane);

            target.transform.position += plane.Right * 6f;
            yield return null;
            float movingOffset = PlanarOffsetFromTarget(target.transform, baseOffset, plane);

            Assert.That(stationaryOffset, Is.EqualTo(3f).Within(0.001f));
            Assert.That(movingOffset, Is.EqualTo(3f).Within(0.001f),
                "目标移动后鼠标偏移上限发生变化或累计漂移。");

            router.ClearDebugOverride();
            Object.Destroy(owner);
            Object.Destroy(target);
        }

        private float PlanarOffsetFromTarget(Transform target, Vector3 baseOffset, CameraPlane plane)
        {
            Vector3 delta = _director.CurrentPose.Position - (target.position + baseOffset);
            return new Vector2(
                Vector3.Dot(delta, plane.Right),
                Vector3.Dot(delta, plane.Up)).magnitude;
        }

        [UnityTest]
        public IEnumerator RestaurantPanMovesWhenContentExceedsViewport_AndRemainsBounded()
        {
            GameObject owner = new GameObject("Owner");
            GameObject anchor = new GameObject("RestaurantAnchor");
            Quaternion rotation = Quaternion.Euler(45f, 0f, 0f);
            CameraPose referencePose = new CameraPose(new Vector3(0f, 10f, -10f), rotation, 5f);
            CameraPlanarBounds bounds = new CameraPlanarBounds(
                new Vector2(-50f, -50f),
                new Vector2(50f, 50f));
            RestaurantPanCameraSource source = new RestaurantPanCameraSource(
                anchor.transform,
                referencePose,
                5f,
                1f,
                true,
                new CameraDamping(0f, 0f, 0f),
                _ => bounds);

            CameraInputRouter router = _director.InputRouter;
            router.SetDebugOverride(new CameraInputFrame
            {
                PanHeld = true,
                PointerDeltaPixels = new Vector2(-240f, 0f)
            });
            _director.AcquireShot(owner, source, new CameraShotOptions(100, 0f, 0f, "Restaurant"));
            yield return null;

            CameraPlane plane = CameraPlane.FromRotation(rotation, anchor.transform.position);
            Vector3 centeredPosition = anchor.transform.position -
                                       (rotation * Vector3.forward) * Vector3.Distance(referencePose.Position, anchor.transform.position);
            float rightTravel = Vector3.Dot(_director.CurrentPose.Position - centeredPosition, plane.Right);
            Assert.That(rightTravel, Is.GreaterThan(0.1f), "超屏餐厅中键拖动没有移动镜头。");

            CameraPose constrained = CameraBoundsUtility.ConstrainOrthographicPose(
                _director.CurrentPose,
                _director.Camera.aspect,
                plane,
                bounds);
            Assert.That(Vector3.Distance(constrained.Position, _director.CurrentPose.Position), Is.LessThan(0.001f),
                "超屏餐厅拖动后镜头越过边界。");

            router.ClearDebugOverride();
            Object.Destroy(owner);
            Object.Destroy(anchor);
        }

        [UnityTest]
        public IEnumerator CameraFacingVisual_AlignsVisualPlaneToTiltedCamera()
        {
            _cameraObject.transform.rotation = Quaternion.Euler(45f, 25f, 0f);
            GameObject visual = new GameObject("BillboardVisual");
            CameraFacingVisual facingVisual = visual.AddComponent<CameraFacingVisual>();

            facingVisual.AlignToCamera();
            yield return null;

            Assert.That(
                Quaternion.Angle(visual.transform.rotation, _cameraObject.transform.rotation),
                Is.LessThan(0.01f),
                "纯视觉子节点没有保持垂直于倾斜镜头方向。");

            Object.Destroy(visual);
        }

        [UnityTest]
        public IEnumerator CameraFacingVisual_RebindsAfterAdditiveCameraHandoff()
        {
            yield return VerifyVisualCameraHandoff(CameraFacingVisual.UpdateMode.EveryLateUpdate);
        }

        [UnityTest]
        public IEnumerator CameraFacingVisual_OnceOnEnableRetriesAfterCameraHandoff()
        {
            yield return VerifyVisualCameraHandoff(CameraFacingVisual.UpdateMode.OnceOnEnable);
        }

        private IEnumerator VerifyVisualCameraHandoff(CameraFacingVisual.UpdateMode mode)
        {
            // Match first entry: the town camera survives, but is disabled only after
            // the additive dungeon's authored visuals have already been enabled.
            var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("BillboardDungeonTest");
            var dungeon = new GameObject("DungeonCamera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(dungeon, scene);
            dungeon.tag = "MainCamera";
            var camera = dungeon.AddComponent<Camera>();
            dungeon.transform.rotation = Quaternion.Euler(45f, 25f, 0f);
            dungeon.AddComponent<CameraDirector>();
            var root = new GameObject("TreeRoot");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var visual = new GameObject("VisualRoot");
            visual.transform.SetParent(root.transform, false);
            var facing = visual.AddComponent<CameraFacingVisual>();
            typeof(CameraFacingVisual).GetField("updateMode",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(facing, mode);
            AsyncOperation unload = null;
            try
            {
                Assert.AreSame(_director, CameraService.Active, "Town must still own the camera during additive loading.");
                yield return null;
                _cameraObject.SetActive(false);
                Assert.AreSame(camera, CameraService.Active.Camera);
                yield return null;
                Assert.That(Quaternion.Angle(visual.transform.rotation, camera.transform.rotation), Is.LessThan(.01f),
                    "Tree retained the disabled town camera after first dungeon entry.");
                Assert.That(Quaternion.Angle(root.transform.rotation, Quaternion.identity), Is.LessThan(.01f));

                // Restart: destroy the first dungeon camera, then create its replacement.
                Object.Destroy(dungeon);
                yield return null;
                dungeon = new GameObject("RestartedDungeonCamera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(dungeon, scene);
                dungeon.tag = "MainCamera";
                camera = dungeon.AddComponent<Camera>();
                dungeon.transform.rotation = Quaternion.Euler(35f, -20f, 0f);
                dungeon.AddComponent<CameraDirector>();
                yield return null;
                Assert.That(Quaternion.Angle(visual.transform.rotation, camera.transform.rotation), Is.LessThan(.01f));
            }
            finally
            {
                _cameraObject.SetActive(true);
                unload = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
            }
            yield return unload;
        }

        [UnityTest]
        public IEnumerator CameraFacingVisual_RetriesWhenCameraAppearsAfterEnable()
        {
            _cameraObject.SetActive(false);
            var visual = new GameObject("DelayedCameraVisual");
            visual.AddComponent<CameraFacingVisual>();
            try
            {
                yield return null;
                _cameraObject.transform.rotation = Quaternion.Euler(45f, 10f, 0f);
                _cameraObject.SetActive(true);
                yield return null;
                Assert.That(Quaternion.Angle(visual.transform.rotation, _cameraObject.transform.rotation), Is.LessThan(.01f));
            }
            finally
            {
                _cameraObject.SetActive(true);
                Object.Destroy(visual);
            }
        }

        [UnityTest]
        public IEnumerator CameraFacingVisual_PreservesExplicitCameraAndOnceMode()
        {
            var explicitCamera = new GameObject("ExplicitVisualCamera");
            var camera = explicitCamera.AddComponent<Camera>();
            explicitCamera.transform.rotation = Quaternion.Euler(30f, 15f, 0f);
            var visual = new GameObject("ExplicitCameraVisual");
            visual.SetActive(false);
            var facing = visual.AddComponent<CameraFacingVisual>();
            typeof(CameraFacingVisual).GetField("targetCamera",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(facing, camera);
            try
            {
                visual.SetActive(true);
                yield return null;
                Assert.That(Quaternion.Angle(visual.transform.rotation, camera.transform.rotation), Is.LessThan(.01f));
                var aligned = visual.transform.rotation;
                explicitCamera.transform.rotation = Quaternion.Euler(55f, 45f, 0f);
                yield return null;
                Assert.That(Quaternion.Angle(visual.transform.rotation, aligned), Is.LessThan(.01f),
                    "OnceOnEnable must not continuously follow an unchanged explicit camera.");
            }
            finally
            {
                Object.Destroy(visual);
                Object.Destroy(explicitCamera);
            }
        }
    }
}
