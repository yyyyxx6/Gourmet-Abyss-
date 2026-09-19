# 树草图片交接说明

树、草、桌椅属于场景图片，使用 SpriteRenderer。PNG 只需导入成 `Sprite (2D and UI)`；这里的“Image”是旧节点名，不代表 Canvas 的 UI Image。

美术保留原 PNG 的像素、透明留白和比例。项目通过 `WorldViewStandard.artworkScale` 把 Sprite 尺寸统一换算到世界尺寸，右键创建预制体和换图工具会自动应用，美术不手调 Transform Scale。

运行时由 `CameraFacingVisual` 旋转各预制体的 `VisualRoot` 面向镜头；编辑时 `VisualRoot` 和 `Art` 保持零旋转，美术按普通二维效果图摆放预制体根节点。需要 2.5D 透视的完整场景保持世界平面，不挂跟随镜头组件。

新树草从原 Sprite 右键 **场景物件 → 创建场景物件预制体**，选择“跟随镜头”和统一目录。不要复制示例 PNG，也不要创建 `_XY`、`_XZ` 资源。大量连续地表使用 Tilemap；单株和组合继续使用标准场景物件预制体。
