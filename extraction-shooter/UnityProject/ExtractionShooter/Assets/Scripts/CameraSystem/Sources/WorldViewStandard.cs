using UnityEngine;

namespace GourmetAbyss.CameraSystem
{
    [CreateAssetMenu(menuName = "Game/Modules/World View Standard")]
    public sealed class WorldViewStandard : ScriptableObject
    {
        [Tooltip("视线与地面夹角；90 度为正俯视")]
        [Range(15, 75)] public float elevation = 45f;
        [Range(15, 70)] public float verticalFieldOfView = 40f;
        public Quaternion ArtworkRotation(bool xzGround) =>
            Quaternion.Euler(xzGround ? elevation : elevation - 90f, 0, 0);
    }
}
