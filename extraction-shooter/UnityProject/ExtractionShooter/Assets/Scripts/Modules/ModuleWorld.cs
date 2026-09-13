using System;
using UnityEngine;
using GourmetAbyss.CameraSystem;

namespace Game.Modules
{
    public sealed class ModuleWorld : MonoBehaviour
    {
        [Serializable] public struct Anchor { public string id; public Transform point; }
        public PlanarPerspectiveView view;
        [Tooltip("编辑器校验：地图内标准物件必须连接可复用源预制体")]
        public bool requirePrefabLinks;
        public Anchor[] anchors = Array.Empty<Anchor>();
        public Transform GetAnchor(string id)
        {
            foreach (var anchor in anchors) if (anchor.id == id) return anchor.point;
            return null;
        }
    }
}
