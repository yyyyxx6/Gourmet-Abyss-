using UnityEngine;

namespace GourmetAbyss.CameraSystem
{
    // Frame: local X = ground right, local Y = depth, local -Z = elevation.
    // For an XZ world, rotate the frame +90 degrees around X.
    public sealed class PlanarPerspectiveView : MonoBehaviour, ICameraShotSource
    {
        public Transform frame;
        public PlanarPerspectiveProfile profile;
        private CameraShotLease lease;
        private Vector2 pan;

        public bool IsOpen => lease != null && lease.IsValid;
        public Vector2 PanOffset => pan;
        public void Open()
        {
            Close();
            if (frame == null || profile == null || CameraService.Active == null) return;
            pan = Vector2.zero;
            lease = CameraService.Active.AcquireShot(this, this,
                new CameraShotOptions(profile.priority, 0f, 0f, "PlanarPerspective"));
        }
        public void Close() { lease?.Dispose(); lease = null; }
        private void OnDisable() { Close(); }

        public CameraPose Pose(Vector2 offset)
        {
            Quaternion rotation = frame.rotation * Quaternion.Euler(-profile.EffectiveTilt, 0, 0);
            Vector3 center = frame.position + frame.right * offset.x + frame.up * offset.y;
            return new CameraPose(center - rotation * Vector3.forward * profile.distance,
                rotation, 9f, true, profile.EffectiveFieldOfView);
        }

        public bool TryEvaluate(in CameraEvaluationContext context, out CameraShotResult result)
        {
            result = default;
            if (frame == null || profile == null) return false;
            CameraPlane plane = new CameraPlane(frame.position, -frame.forward, frame.right, frame.up);
            if (context.Input.PanHeld && !context.Input.PointerBlockedByUi)
            {
                // Intersect both cursor rays with the same ground plane; works with perspective.
                Vector2 pointer = context.Input.PointerPositionPixels;
                Ray a = context.Camera.ScreenPointToRay(pointer);
                Ray b = context.Camera.ScreenPointToRay(pointer - context.Input.PointerDeltaPixels);
                if (plane.TryRaycast(a.origin, a.direction, out Vector3 ah) &&
                    plane.TryRaycast(b.origin, b.direction, out Vector3 bh))
                    pan += (plane.ToPlane(bh) - plane.ToPlane(ah)) * profile.dragSensitivity;
                pan = Vector2.ClampMagnitude(pan, Mathf.Max(0, profile.panLimit));
            }
            result = new CameraShotResult(Pose(pan), new CameraDamping(0.08f, 0f, 0f), plane,
                CameraShotPolicy.UseUnscaledTime);
            return true;
        }
    }
}
