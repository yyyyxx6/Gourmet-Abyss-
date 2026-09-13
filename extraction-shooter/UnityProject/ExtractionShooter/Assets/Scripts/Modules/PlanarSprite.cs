using UnityEngine;
using GourmetAbyss.CameraSystem;

namespace Game.Modules
{
    // Sorting only. Authored layout stays in the normal 2D plane; an optional
    // CameraFacingVisual on VisualRoot owns runtime visual orientation.
    [ExecuteAlways]
    public sealed class PlanarSprite : MonoBehaviour
    {
        public Transform frame;
        [HideInInspector] public PlanarPerspectiveProfile profile; // Legacy serialized reference; no longer controls artwork orientation.
        public SpriteRenderer visual;
        public bool ground;
        public float orderOffset;
        public Transform contactPoint;
        public bool useWorldAxesWhenUnbound;
        public bool xzGround;
        private void LateUpdate() { Refresh(); }
        public void Refresh()
        {
            if (visual == null) return;
            var effectiveFrame = frame;
            if (effectiveFrame == null && useWorldAxesWhenUnbound)
            {
                var world = GetComponentInParent<ModuleWorld>();
                if (world != null && world.view != null) effectiveFrame = world.view.frame;
            }
            if (effectiveFrame == null && !useWorldAxesWhenUnbound) return;
            Vector3 point = contactPoint != null ? contactPoint.position : transform.position;
            float depth = effectiveFrame != null ? Vector3.Dot(point - effectiveFrame.position, effectiveFrame.up)
                : Vector3.Dot(point, xzGround ? Vector3.forward : Vector3.up);
            // A standalone XZ/XY template has no restaurant-local origin. Do not clamp all objects
            // beyond nine world units to the same order. Module-bound legacy layers retain their range.
            int limit = effectiveFrame == null ? 31000 : 900;
            int groundOrder = effectiveFrame == null ? -32000 : -1000;
            visual.sortingOrder = ground ? groundOrder + Mathf.RoundToInt(orderOffset)
                : Mathf.Clamp(Mathf.RoundToInt(-depth * 100f + orderOffset), -limit, limit);
        }
    }
}
