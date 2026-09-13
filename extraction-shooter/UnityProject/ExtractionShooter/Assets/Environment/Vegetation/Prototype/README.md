# 2D 树草图片模板

这是 SpriteRenderer 图片预制体。根节点只负责摆放，Image 只负责显示图片；不含 3D 模型、碰撞体或运行脚本。

## 美术怎么用

1. 在 Project 中复制 `Prefabs/Vegetation_Single_Tree` 或 `Vegetation_Single_Grass`，重命名为新植物。
2. 打开复制的预制体，把新 Sprite 拖到 `Image > SpriteRenderer > Sprite`。
3. 图片默认使用原像素尺寸 / PPU。根节点和 Image 的 Scale 都从 `(1,1,1)` 开始，保留原图比例。换图不强制适配旧宽度。
4. 拖入场景，自己摆位置、层次和布局。`Sorting Layer / Order in Layer` 控制图片遮挡关系。

图片文件仍用透明 PNG，不用改成 JPG、UI Image 或材质球。Unity Import Settings 选 `Sprite (2D and UI)`、Single；示例 PPU=100。像素风图片使用 Point、关闭 Mipmap 和压缩，Max Size 不小于原图尺寸。示例保留居中轴心，换图后位置由美术调整。

## 目录

- `Textures/Tree.png`、`Grass.png`：来自本项目 `Assets/NewVersion/map/地牢第一关卡素材1/树1.png` 和 `绿草1.png` 的原像素副本；原资源没有被改写。
- `Prefabs/Vegetation_Single_Tree.prefab`、`Vegetation_Single_Grass.prefab`：普通 XY 平面 2D 图片模板。
- `Prefabs/Vegetation_Cluster_Sample.prefab`：一棵树和两丛草的简单组合，保留对子预制体的引用。只是打样，不会自动铺设或修改现有场景。

大量手工铺设可复制单株或组合。默认不要对立起的 2D 图片做随机 Y 轴旋转，否则会侧向镜头；左右变化可使用 SpriteRenderer 的 Flip X。不同地块/区域的图片排序由美术安排，需要自动遮挡排序时再接入项目的排序组件。
