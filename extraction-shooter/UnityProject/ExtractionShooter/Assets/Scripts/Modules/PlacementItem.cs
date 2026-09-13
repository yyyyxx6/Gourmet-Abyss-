using UnityEngine;
using GourmetAbyss.CameraSystem;

namespace Game.Modules
{
    // Authoring contract. No runtime position/rotation/scale writes.
    [DisallowMultipleComponent]
    public sealed class PlacementItem : MonoBehaviour
    {
        public enum Surface { Artwork, Ground }
        public WorldViewStandard standard;
        public Surface surface;
        public bool xzGround;
        public Transform contact;
        public Transform visualRoot;
        public SpriteRenderer art;
        public Transform physicsRoot;
        public Transform anchorsRoot;
        public PlanarSprite sorter;
        [Tooltip("在图片矩形内的归一化接地点；普通物件通常为底部中央，但透明留白应另行校正")]
        public Vector2 spriteContact = new Vector2(.5f, 0);
        [Min(.01f)] public float width = 1;
        [Min(.01f)] public float groundDepth = 1;
        [Tooltip("地面占地宽/深，仅供布局预览；不自动修改旧碰撞")]
        public Vector2 footprint = Vector2.one;

        /// <summary>
        /// 视觉是否由统一的镜头朝向组件接管。该组件只能挂在 VisualRoot 上，
        /// 这样布局、接地点、碰撞和锚点仍然保持普通二维平面。
        /// </summary>
        public bool UsesCameraFacingVisual => visualRoot != null &&
            visualRoot.GetComponent<CameraFacingVisual>() != null;

        public Quaternion ExpectedVisualRotation => surface == Surface.Ground
            ? Quaternion.Euler(xzGround ? 90 : 0, 0, 0) : standard.ArtworkRotation(xzGround);

        public Vector3 SpriteContactLocal
        {
            get
            {
                var sprite = art.sprite;
                return new Vector3((spriteContact.x * sprite.rect.width - sprite.pivot.x) / sprite.pixelsPerUnit,
                    (spriteContact.y * sprite.rect.height - sprite.pivot.y) / sprite.pixelsPerUnit, 0);
            }
        }

        // Called only by explicit editor operations. Never move gameplay/physics/anchor branches here.
        public void ApplyArtwork()
        {
            if (standard == null || contact == null || visualRoot == null || art == null || art.sprite == null)
                throw new System.InvalidOperationException(name + ": incomplete placement references.");
            // 跟随镜头的物件在编辑态保留二维平面，运行时由 CameraFacingVisual
            // 统一接管朝向；固定物件才把共享镜头倾角写入 VisualRoot。
            visualRoot.localRotation = UsesCameraFacingVisual ? Quaternion.identity : ExpectedVisualRotation;
            visualRoot.localScale = Vector3.one;
            visualRoot.position = contact.position;
            art.transform.localRotation = Quaternion.identity;
            float scale = width / art.sprite.bounds.size.x;
            art.transform.localScale = new Vector3(scale,
                surface == Surface.Ground ? groundDepth / art.sprite.bounds.size.y : scale, 1);
            art.transform.localPosition = -Vector3.Scale(SpriteContactLocal, art.transform.localScale);
            if (sorter != null)
            {
                sorter.visual = art; sorter.contactPoint = contact;
                sorter.ground = surface == Surface.Ground; sorter.xzGround = xzGround;
                sorter.useWorldAxesWhenUnbound = true; sorter.Refresh();
            }
        }
    }
}
