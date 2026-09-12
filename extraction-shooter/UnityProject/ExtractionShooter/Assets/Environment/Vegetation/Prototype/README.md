# 植被打样目录

这个目录给关卡和美术用于快速铺设树、草、灌木。预制体均以“可复制、可替换材质、可批量摆放”为目标。

## 目录约定

- `Prefabs/Vegetation_Single_Grass.prefab`：单株草模板，适合散点摆放。
- `Prefabs/Vegetation_Single_Tree.prefab`：单株树模板，适合做远景/中景基准。
- `Prefabs/Vegetation_Cluster_Sample.prefab`：树+草组合打样，可复制成片后整体替换子预制体。
- `Docs/Vegetation_Art_Handoff.md`：美术替换和批量铺设规则。

## 替换方式

1. 复制目标预制体并重命名，例如 `Tree_Pine_A_01`、`Grass_Short_B_01`。
2. 打开复制后的预制体，保留根节点 Transform、碰撞体和层级结构。
3. 只替换 MeshRenderer/材质中的贴图或材质；同一批次尽量共用材质，便于合批。
4. 树按远近准备 LOD（近景完整模型，中景简化，远景 Billboard）；草优先使用低面数交叉片。
5. 批量铺设时使用随机 Y 旋转、0.85~1.15 随机缩放，避免明显重复。

## 当前参考资源

- 项目现用草：`Assets/Prefabas/SceneItem/Grass.prefab`、`Grass 1.prefab`
- 沙漠环境树：`Assets/ImportAsset/ZerinLabs_enviroKit_RetroDesert/Prefabs/retroDesert_props/deco_desert_treeDead_A.prefab`
- 沙漠环境草：`.../deco_desert_grass.prefab`、`deco_desert_grassDry.prefab`

这里的模板是“引用原始预制体”的包装层，不复制原始资源，方便后续统一替换和回滚。
