using System.Collections.Generic;
using UnityEngine;

namespace Game.Modules
{
    /// <summary>Authored brush ownership and ground coordinates; no runtime update or spawning.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("场景物件/装饰组")]
    public sealed class DecorationBrushGroup : MonoBehaviour
    {
        public enum GroundPlane { XY, XZ }
        public GroundPlane plane = GroundPlane.XZ;
        public Transform groundFrame;
        [HideInInspector] public List<GameObject> paintedObjects = new List<GameObject>();

        public Vector3 Right => groundFrame != null ? groundFrame.right : Vector3.right;
        public Vector3 Forward => groundFrame != null ? groundFrame.up : plane == GroundPlane.XZ ? Vector3.forward : Vector3.up;
        public Vector3 Normal => Vector3.Cross(Right, Forward).normalized;
        public bool IsXZ => Mathf.Abs(Vector3.Dot(Normal, Vector3.up)) > .99f;
        public Plane Ground => new Plane(Normal, transform.position);
        public Vector2 ToPlane(Vector3 point)
        {
            Vector3 offset = point - transform.position;
            return new Vector2(Vector3.Dot(offset, Right), Vector3.Dot(offset, Forward));
        }
        public Vector3 ToWorld(Vector2 point) => transform.position + Right * point.x + Forward * point.y;
        public bool Owns(GameObject item) => item != null && item.transform.parent == transform && paintedObjects.Contains(item);
        public bool IsDirectChild(GameObject item) => item != null && item.transform.parent == transform;
    }
}
