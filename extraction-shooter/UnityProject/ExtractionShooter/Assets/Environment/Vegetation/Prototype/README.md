# 2D 树草预制示例

这里保留树、草和组合摆放示例。预制体使用与餐厅相同的标准结构和 `CameraFacingVisual`，运行时自动面向当前镜头。

## 美术怎么创建新资源

1. 把原始透明 PNG 放在负责模块的美术素材目录；不要复制到本示例目录。
2. Unity 导入类型选择 `Sprite (2D and UI)`、`Single`。像素风资源使用 Point、关闭 Mipmap 和压缩。
3. 在 Project 窗口选中 Sprite，右键 **场景物件 → 创建场景物件预制体**。
4. 树、草通常选择“跟随镜头”；保存位置选择“统一 Prefab 目录”后会进入 `Assets/Modules/GeneratedPrefabs/Shared/Vegetation/`。
5. 把预制体拖到模块的二维布局平面，只移动物件根节点。不要调整 `VisualRoot` 或 `Art` 的旋转和缩放。

项目统一尺寸倍率由 `WorldViewStandard` 提供，创建工具自动计算。美术不需要计算 PPU 或世界宽度。需要左右变化时使用 SpriteRenderer 的 Flip X；不要随机旋转 Y 轴。

## 当前示例

- `Vegetation_Single_Tree.prefab`：直接引用 `Assets/NewVersion/map/地牢第一关卡素材1/树1.png`。
- `Vegetation_Single_Grass.prefab`：直接引用 `Assets/NewVersion/map/地牢第一关卡素材1/绿草1.png`。
- `Vegetation_Cluster_Sample.prefab`：引用上述单体预制体组成的打样。

示例不再保存原 PNG 副本。后续资源始终引用美术原文件。
