using UnityEngine;

namespace GourmetAbyss.CameraSystem
{
    [CreateAssetMenu(menuName = "料理地牢/Camera/Dungeon Perspective Profile")]
    public sealed class DungeonPerspectiveProfile : DungeonCameraProfile
    {
        [Header("透视镜头（XZ 玩法平面）")]
        public WorldViewStandard viewStandard;
        [Range(15, 70)] public float pitch = 45f;
        [Range(10, 70)] public float fieldOfView = 40f;
        [Min(1)] public float distance = 27.5f;
        public float EffectivePitch => viewStandard != null ? viewStandard.elevation : pitch;
        public float EffectiveFieldOfView => viewStandard != null ? viewStandard.verticalFieldOfView : fieldOfView;
        public override DungeonAimCameraSource CreateSource(Transform target, Vector3 offset, Quaternion rotation, float size)
        {
            var viewRotation = Quaternion.Euler(EffectivePitch, rotation.eulerAngles.y, 0);
            return new DungeonAimCameraSource(target, -(viewRotation * Vector3.forward) * distance,
                viewRotation, size, centerDeadZone, maxPointerOffset, responseExponent,
                pointerSmoothTime, damping, true, EffectiveFieldOfView);
        }
    }
}
