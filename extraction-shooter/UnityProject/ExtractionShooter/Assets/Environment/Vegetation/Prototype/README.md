# 2D 树草预制示例

这里保留树、草和组合摆放示例。预制体使用与餐厅相同的标准结构和 `CameraFacingVisual`，运行时自动面向当前镜头。

## 美术怎么创建新资源

1. 把原始透明 PNG 放在负责模块的美术素材目录；不要复制到本示例目录。
2. Unity 导入类型选择 `Sprite (2D and UI)`、`Single`。像素风资源使用 Point、关闭 Mipmap 和压缩。
3. 在 Project 窗口选中 Sprite，右键 **场景物件 → 创建场景物件预制体**。
4. 树、草通常选择“跟随镜头”；保存位置选择“统一 Prefab 目录”后会进入 `Assets/Modules/GeneratedPrefabs/Shared/Vegetation/`。
5. 把预制体拖到模块的二维布局平面，只移动物件根节点。不要调整 `VisualRoot` 或 `Art` 的旋转和缩放。

项目统一尺寸倍率由 `WorldViewStandard` 提供，创建工具自动计算。美术不需要计算 PPU 或世界宽度。需要左右变化时使用 SpriteRenderer 的 Flip X；不要随机旋转 Y 轴。

## 策划复制和换图

1. 在 Project 窗口选中 `Vegetation_Single_Tree.prefab`，用 Ctrl+D 复制，给新预制体改名。
2. 双击新预制体，选中根节点，在 Inspector 的 `Placement Item` 中将新 PNG 的 Sprite 拖入“2D 图片”。PNG 的 Texture Type 必须为 `Sprite (2D and UI)`。
3. 换图后自动更新图片尺寸和底部接地点；保存后把新预制体拖入 Layer1。只移动根节点，保持 VisualRoot/Art 的编辑旋转为零。
4. 若只想改场景内的一棵树，可 Ctrl+D 复制场景物件，再在复制品的“2D 图片”字段换图；此修改仅影响该实例。需要所有同款树一起更新时，编辑它们共同引用的源预制体。

这里的 2D 图片使用 `SpriteRenderer`，直接引用原 PNG 的 Sprite。Inspector 子节点中的 Material 是 Unity 默认的图片渲染材质，不是换树图的入口，不需要为每棵树创建材质。场景树不使用 Canvas 的 UI Image。

## 在 Layer1 对照 Scene 和 Game

在 Scene 的“场景镜头”面板选择“游戏镜头”，再点“恢复正式镜头”。Layer1 会直接读取本场景的游戏相机；跟随镜头的图片自动开启预览，编辑态 Game 也会显示朝向相机的图片。

对照时看 Scene 中青色 Game 画框内的构图。Scene 和 Game 窗口比例不同时，Scene 会自动换算视野；网格、选中轮廓和操作手柄属于编辑辅助显示。自由视图和 2D 布局用于摆放，不代表最终游戏视角。预览只在渲染期间调整图片朝向，结束后恢复编辑数据。

## 当前示例

- `Vegetation_Single_Tree.prefab`：直接引用 `Assets/NewVersion/map/地牢第一关卡素材1/树1.png`。
- `Vegetation_Single_Grass.prefab`：直接引用 `Assets/NewVersion/map/地牢第一关卡素材1/绿草1.png`。
- `Vegetation_Cluster_Sample.prefab`：引用上述单体预制体组成的打样。

示例不再保存原 PNG 副本。后续资源始终引用美术原文件。
