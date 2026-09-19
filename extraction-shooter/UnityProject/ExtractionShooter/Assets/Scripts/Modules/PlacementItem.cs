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
        [Tooltip("使用源 Sprite 的导入尺寸。开启后 Art 始终保持 (1,1,1)，width/groundDepth 只记录布局尺寸。")]
        public bool useSourceDimensions = true;

        /// <summary>
        /// 视觉是否由统一的镜头朝向组件接管。该组件只能挂在 VisualRoot 上，
        /// 这样布局、接地点、碰撞和锚点仍然保持普通二维平面。
        /// </summary>
        public bool UsesCameraFacingVisual => visualRoot != null &&
            visualRoot.GetComponent<CameraFacingVisual>() != null;

        /// <summary>
        /// 不跟随镜头时，图片保持在美术编排的世界平面上，让透视相机自然产生 2.5D 效果。
        /// XZ 地面图片仍需转到水平地面；普通物件保持导入时的二维平面方向。
        /// </summary>
        public Quaternion ExpectedVisualRotation => surface == Surface.Ground && xzGround
            ? Quaternion.Euler(90f, 0f, 0f)
            : Quaternion.identity;

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
            // 两种模式在编辑态都保留美术的二维编排。跟随镜头模式仅在运行时由
            // CameraFacingVisual 接管；世界平面模式由透视相机自然呈现 2.5D 形变。
            visualRoot.localRotation = ExpectedVisualRotation;
            visualRoot.localScale = Vector3.one;
            visualRoot.position = contact.position;
            art.transform.localRotation = Quaternion.identity;
            if (useSourceDimensions)
            {
                // 源图尺寸模式是统一的美术交付规则：图片按导入 PPU 原样显示，
                // 不把“适配场景”的比例写进 Art。布局尺寸和占地仍由 PlacementItem 记录。
                art.transform.localScale = Vector3.one;
            }
            else
            {
                // 仅为明确保留的旧兼容数据提供比例模式；新预制不应关闭该选项。
                float scale = width / art.sprite.bounds.size.x;
                art.transform.localScale = new Vector3(scale,
                    surface == Surface.Ground ? groundDepth / art.sprite.bounds.size.y : scale, 1);
            }
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
