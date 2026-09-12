namespace Game.Core.SceneFlow
{
    /// <summary>饱和度过渡方向。</summary>
    public enum SaturationDirection { None, ToSaturated, ToUnsaturated }

    /// <summary>发光（Emission）过渡效果。</summary>
    public enum EmissionEffect { None, EnterLevel, ExitLevel }

    /// <summary>本次转场对本局采集物的处理；占格食材始终保留。</summary>
    public enum RunBagAction
    {
        /// <summary>不动。</summary>
        None,
        /// <summary>确认旧采集物已入库后，开始新的空采集记录。</summary>
        Clear,
        /// <summary>提交采集物，未能入库的数量继续保留。</summary>
        CommitToGameVal
    }

    /// <summary>本次转场对氧气消耗的处理。</summary>
    public enum OxygenAction { None, StartConsuming, StopConsuming }

    /// <summary>本次转场对餐厅物体位置的处理。</summary>
    public enum RestaurantPose
    {
        /// <summary>不动。</summary>
        Unchanged,
        /// <summary>移开（进入战斗时 x + 100）。</summary>
        MoveAwayForBattle,
        /// <summary>还原到地面场景的初始位置。</summary>
        RestoreToHome
    }

    /// <summary>地面主场景物体（homeSceneObject / postProcessObject）的显隐处理。</summary>
    public enum HomeSceneVisibility { Unchanged, Hide, Show }

    /// <summary>
    /// HUD 收尾（UITapBounce 复位 + 标题 + mainUI 关开一次）的时机。
    /// </summary>
    /// <remarks>
    /// 必须区分三档而不能合并：mainUI 关开会让子面板重跑 OnEnable，它相对于
    /// homeSceneObject 显隐的先后会改变这些面板启动时看到的状态。
    /// </remarks>
    public enum HudRefreshTiming
    {
        /// <summary>场景操作完成后、地面物体显隐之前（EnterLevel 的既有时机）。</summary>
        AfterSceneOpBeforeWorldSetup,

        /// <summary>地面物体显隐、餐厅归位、相机重绑之后（SwitchLevel / FromLevelToHome 的既有时机）。</summary>
        AfterWorldSetup,

        /// <summary>
        /// 卸载发起后、等待完成之前就复位 UITapBounce 与 mainUI，标题留到卸载完成后
        /// （ExitLevel 的既有时机，跨帧，不能与上面两档合并）。
        /// </summary>
        BeforeSceneOpCompletes
    }

    /// <summary>
    /// 一次转场的完整描述，用来把 LevelManager 里四个复制粘贴的协程收敛成一个。
    /// </summary>
    public sealed class TransitionRequest
    {
        /// <summary>转场类型，仅用于广播给监听者，不影响执行步骤。</summary>
        public TransitionKind Kind;

        /// <summary>要卸载的场景名。为空表示不卸载。</summary>
        public string SceneToUnload;

        /// <summary>要 Additive 加载的场景名。为空表示不加载。</summary>
        public string SceneToLoad;

        /// <summary>过渡 UI Animator 的 Trigger 名。为空表示不触发。</summary>
        public string AnimatorTrigger;

        /// <summary>淡出到白色的车辆所在场景名。为空表示跳过。</summary>
        public string VehicleFadeOutScene;

        /// <summary>从白色淡回原色的车辆所在场景名。为空表示跳过。</summary>
        public string VehicleFadeInScene;

        public SaturationDirection Saturation;
        public EmissionEffect Emission;
        public RunBagAction RunBag;
        public OxygenAction Oxygen;
        public RestaurantPose Restaurant;
        public HomeSceneVisibility HomeObjects;
        public HudRefreshTiming HudTiming = HudRefreshTiming.AfterSceneOpBeforeWorldSetup;

        /// <summary>淡入前空转一帧，等新场景完全就绪（EnterLevel 的既有行为）。</summary>
        public bool ExtraFrameBeforeFadeIn;

        /// <summary>转场结束时把玩家状态置为该值。</summary>
        public PlayerState TargetPlayerState;

        /// <summary>转场结束时战斗 UI 的开关。</summary>
        public bool BattleUiActive;

        /// <summary>是否调用 KeepMainCamera.tKeepMainCamera() 重新绑定主相机。</summary>
        public bool RefreshMainCamera;

        /// <summary>是否调用 BattleValManager.ResetValues()。</summary>
        public bool ResetBattleValues;

        /// <summary>是否播放背包格子入场动画。</summary>
        public bool PlaySlotsEntranceAnimation;

        /// <summary>是否强制重新启用玩家的 TopDownController。</summary>
        public bool ReenablePlayerController;

        /// <summary>由结算界面持有结束门禁；失败时保留界面与冻结的结果。</summary>
        public bool FromSettlement;

        /// <summary>旧场景完全卸载后才加载目标，保证同图重试产生新实例。</summary>
        public bool WaitForUnloadBeforeLoad;
    }

    /// <summary>
    /// 现有转场与结算重试共用的预设，保留各自的视觉和 HUD 刷新时机。
    /// </summary>
    public static class TransitionPresets
    {
        /// <summary>地面主场景名。LevelManager 里硬编码为 "UpGround"。</summary>
        public const string HomeSceneName = "UpGround";

        /// <summary>
        /// 进入关卡。对应 <c>LevelManager.EnterLevelProcess</c>。
        /// </summary>
        /// <remarks>[现状] 不播放背包格子入场动画（另外两条回家的路径都播）。</remarks>
        public static TransitionRequest EnterLevel(string levelName) => new TransitionRequest
        {
            Kind = TransitionKind.EnterLevel,
            SceneToLoad = levelName,
            AnimatorTrigger = "EnterLevel",
            VehicleFadeOutScene = HomeSceneName,
            VehicleFadeInScene = levelName,
            Saturation = SaturationDirection.ToUnsaturated,
            Emission = EmissionEffect.EnterLevel,
            RunBag = RunBagAction.Clear,
            Oxygen = OxygenAction.StartConsuming,
            Restaurant = RestaurantPose.MoveAwayForBattle,
            HomeObjects = HomeSceneVisibility.Hide,
            HudTiming = HudRefreshTiming.AfterSceneOpBeforeWorldSetup,
            ExtraFrameBeforeFadeIn = true,
            TargetPlayerState = PlayerState.Battle,
            BattleUiActive = true,
            RefreshMainCamera = true,
            ResetBattleValues = false,
            PlaySlotsEntranceAnimation = false,
            ReenablePlayerController = true
        };

        /// <summary>
        /// 退出关卡回地面。对应 <c>LevelManager.ExitLevelProcess</c>。
        /// </summary>
        /// <remarks>
        /// [现状] <see cref="TransitionRequest.RefreshMainCamera"/> 为 false ——
        /// 四条路径里唯独这条不调用 KeepMainCamera。<br/>
        /// [现状] <see cref="HudRefreshTiming.BeforeSceneOpCompletes"/> ——
        /// mainUI 关开发生在等待卸载完成之前，与其它三条不同。
        /// </remarks>
        public static TransitionRequest ExitLevel(string levelName) => new TransitionRequest
        {
            Kind = TransitionKind.ExitLevel,
            SceneToUnload = levelName,
            AnimatorTrigger = "ExitLevel",
            VehicleFadeOutScene = levelName,
            VehicleFadeInScene = HomeSceneName,
            Saturation = SaturationDirection.ToSaturated,
            Emission = EmissionEffect.ExitLevel,
            RunBag = RunBagAction.CommitToGameVal,
            Oxygen = OxygenAction.StopConsuming,
            Restaurant = RestaurantPose.RestoreToHome,
            HomeObjects = HomeSceneVisibility.Show,
            HudTiming = HudRefreshTiming.BeforeSceneOpCompletes,
            TargetPlayerState = PlayerState.UpGround,
            BattleUiActive = false,
            RefreshMainCamera = false,
            ResetBattleValues = true,
            PlaySlotsEntranceAnimation = true,
            ReenablePlayerController = false
        };

        /// <summary>
        /// 从关卡回家（矿车/死亡路径）。对应 <c>LevelManager.FromLevelToHomeProcess</c>。
        /// </summary>
        /// <remarks>
        /// AnimatorTrigger 用 "EnterLevel" 是对的：Transition Plus.controller 里只有这一个参数，
        /// "ExitLevel" / "SwitchLevel" 都不存在，填了也是空操作。<br/>
        /// [现状] 无 Emission 过渡——但场景里 emissionTransition 引用为空，整条链路本就不生效。
        /// </remarks>
        public static TransitionRequest LevelToHome(string levelName) => new TransitionRequest
        {
            Kind = TransitionKind.LevelToHome,
            SceneToUnload = levelName,
            AnimatorTrigger = "EnterLevel",
            VehicleFadeOutScene = levelName,
            // 原来填的是不存在的场景名 "HomeScene"，导致进本时变白的地面载具永远恢复不了原色。
            VehicleFadeInScene = HomeSceneName,
            Saturation = SaturationDirection.ToSaturated,
            Emission = EmissionEffect.None,
            RunBag = RunBagAction.CommitToGameVal,
            Oxygen = OxygenAction.StopConsuming,
            Restaurant = RestaurantPose.RestoreToHome,
            HomeObjects = HomeSceneVisibility.Show,
            HudTiming = HudRefreshTiming.AfterWorldSetup,
            TargetPlayerState = PlayerState.UpGround,
            BattleUiActive = false,
            RefreshMainCamera = true,
            ResetBattleValues = true,
            PlaySlotsEntranceAnimation = true,
            ReenablePlayerController = true
        };

        /// <summary>
        /// 关卡之间平级切换。对应 <c>LevelManager.SwitchLevelProcess</c>。
        /// </summary>
        /// <remarks>
        /// 保留占格食材、提交上一局采集物，新关卡重新开始统计与饥饿消耗。<br/>
        /// [现状] 不改动地面主场景物体与餐厅位置（此时它们本就处于战斗态）。
        /// </remarks>
        public static TransitionRequest SwitchLevel(string fromLevel, string toLevel) => new TransitionRequest
        {
            Kind = TransitionKind.SwitchLevel,
            SceneToUnload = fromLevel,
            SceneToLoad = toLevel,
            AnimatorTrigger = "SwitchLevel",
            VehicleFadeOutScene = fromLevel,
            VehicleFadeInScene = toLevel,
            Saturation = SaturationDirection.ToUnsaturated,
            Emission = EmissionEffect.ExitLevel,
            RunBag = RunBagAction.CommitToGameVal,
            Oxygen = OxygenAction.StartConsuming,
            Restaurant = RestaurantPose.Unchanged,
            HomeObjects = HomeSceneVisibility.Unchanged,
            HudTiming = HudRefreshTiming.AfterWorldSetup,
            TargetPlayerState = PlayerState.Battle,
            BattleUiActive = true,
            RefreshMainCamera = true,
            ResetBattleValues = false,
            PlaySlotsEntranceAnimation = false,
            ReenablePlayerController = true
        };

        public static TransitionRequest RestartLevel(string levelName)
        {
            TransitionRequest request = SwitchLevel(levelName, levelName);
            request.FromSettlement = true;
            request.WaitForUnloadBeforeLoad = true;
            request.HomeObjects = HomeSceneVisibility.Hide;
            request.Restaurant = RestaurantPose.MoveAwayForBattle;
            request.ExtraFrameBeforeFadeIn = true;
            request.PlaySlotsEntranceAnimation = true;
            return request;
        }
    }
}
