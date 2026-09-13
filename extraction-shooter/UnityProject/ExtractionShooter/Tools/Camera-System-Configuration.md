# 镜头系统配置

## 场景相机

| 场景 | 投影 | X 轴俯角 | 镜头参数 |
|---|---|---:|---:|
| `UpGround` | Orthographic | 0° | 沿用场景值 |
| `Layer1` / `Layer2` / `Layer3` | Perspective | 45° | FOV 40°，距离 27.5 m |

小镇 `CameraFollow.defaultSource` 保持 `Auto`；三层地牢明确设为 `Dungeon`，共用 `Assets/Modules/Combat/DungeonPerspective.asset`。`target` 必须指向玩家。餐厅在进入期间单独请求透视镜头，见 `模块制作规范.md`。

餐厅与战斗的视线/地面夹角现统一为 45°、垂直 FOV 40°，由 `Assets/Modules/Shared/WorldViewStandard.asset` 统一提供。餐厅 XY 地面镜头 X=-45°；战斗 XZ 地面镜头 X=45°。配置里的旧角度/FOV 字段只在未绑定共享规范时生效。距离和输入仍由各模块独立配置。

## 默认参数

小镇沿用默认值；地牢使用共享 `DungeonPerspectiveProfile`，其鼠标参数如下。

| 模式 | 参数 | 值 |
|---|---|---:|
| 通用 | Position Smooth Time | 0.30 s |
| 小镇 | Look Ahead Distance | 1.50 m |
| 小镇 | Look Ahead Smooth Time | 0.20 s |
| 地牢 | Pointer Center Dead Zone | 0.10 |
| 地牢 | Pointer Max Offset | 3.00 m |
| 地牢 | Pointer Response Exponent | 1.40 |
| 地牢 | Pointer Smooth Time | 0.14 s |

玩家静止和移动时均使用同一个 `3 m` 鼠标偏移上限；指针位于 UI 上时不计算鼠标偏移。

新战斗关卡绑定同一 `DungeonPerspectiveProfile`；确需不同构图时复制该配置，不另写镜头算法。改变 FOV、俯角、距离后复跑瞄准与边缘验收。

## 透视接入

- 镜头写入仍由 `CameraDirector` 统一负责；叠加相机通过 `SetProjectionFollowers` 同步位置、投影、FOV、裁剪面，保留各自渲染层。
- `CameraStackPresentation` 注册叠加相机、停用只适用于正交的 `CameraSnapSRP`；保留 ProPixelizer 渲染器以兼容临时角色材质。
- `CameraAimUtility` 使用屏幕射线，检测 `aimLayerMask | groundLayerMask`；地面命中沿用 `+1.3 m` 瞄准高度，敌人命中不抬高；未命中时使用玩家所在高度的水平面。
- 武器朝向、水平发射、散射、伤害和弹体碰撞规则没有因相机改为透视而改写。

## 人物、怪物、建筑视觉规则

- 3D 模型不挂 `CameraFacingVisual`，由相机俯角产生空间纵深。
- 2D 对象使用两层视觉节点：`VisualRoot` 负责面向镜头，其子节点放渲染器和 Animator 并负责左右翻面。碰撞体、刚体、AI、交互脚本保留在物理根节点。
- 需要始终朝向镜头的 `VisualRoot` 挂 `CameraFacingVisual`。固定镜头使用 `OnceOnEnable`；运行中会改变旋转才使用 `EveryLateUpdate`。
- 2D 素材必须按三分之四俯视角绘制。朝向镜头只能调整贴片平面，不能把正面素材转换成俯视素材。
- 不在带 `Collider` 或 `Rigidbody` 的节点上挂 `CameraFacingVisual`。

当前已接入地牢使用的蘑菇、鼠、蜗牛和三份植物预制体，共 6 份资源。动态生成和复用时沿用这些预制体的视觉节点；3D 地形、建筑保留原模型，不强制变成面向镜头的贴片。美术正面图仍是正面图，需要正式俯视素材才能改变画出的顶面比例。
