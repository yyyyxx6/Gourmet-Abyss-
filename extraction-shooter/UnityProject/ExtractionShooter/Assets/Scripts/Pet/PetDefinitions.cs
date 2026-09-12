using System;
using UnityEngine;

/// <summary>
/// 宠物成长数值（由 PetManager 在战斗进入时写入到“宠物系统”组件）。
/// </summary>
[Serializable]
public class PetGrowthValues
{
    [Header("攻击与射击")]
    public float attackRange = 12f;
    public float fireInterval = 0.45f;
    public float bulletDamage = 12f;

    [Header("子弹表现")]
    [Tooltip("追踪弹实例的缩放系数（作用于 transform.localScale = Vector3.one * bulletSize）")]
    public float bulletSize = 1f;
    [Tooltip("一次攻击发射的子弹数量")]
    public int burstBulletCount = 1;
    [Tooltip("扇形总角度（度）。当 burstBulletCount > 1 时，子弹会在该角度范围内均匀分布。")]
    public float burstFanAngle = 0f;
    public float bulletMoveSpeed = 18f;
    public float bulletRotateSpeed = 540f;
    public float bulletLifeTime = 4f;
    public float bulletHitDistance = 0.35f;

    [Header("命中减速（最终值 = 初始值 * 乘数）")]
    [Tooltip("初始减速比例，0~1。0.25 表示降低25%移速")]
    public float slowRatioBase = 0.2f;
    [Tooltip("减速持续时间初始值（秒）")]
    public float slowDurationBase = 1.5f;
    [Tooltip("减速比例乘数")]
    public float slowRatioMultiplier = 1f;
    [Tooltip("减速持续时间乘数")]
    public float slowDurationMultiplier = 1f;
}

/// <summary>
/// 宠物系统接口：PetManager 在生成预制体后会调用它，把成长数值写入到宠物逻辑里。
/// </summary>
public interface IPetSystem
{
    void ApplyGrowth(PetGrowthValues growth);
}
