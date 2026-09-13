# 场景图片预制规范

这套规范用于所有需要放入 2D/2.5D 场景的图片资源，不限定餐厅场景。

## 美术交付

- 交付原始 PNG/Sprite，保持原图像素、透明留白和比例，不在项目外为了适配镜头改图。
- 图片按普通二维平面绘制。透视倾斜、面向镜头和排序由项目预制模板和组件处理。
- 需要替换图片时，替换 `Art` 上的 Sprite，保留接地点和预制体逻辑分支。

## 创建预制

在 Project 窗口选中 Sprite 或图片，右键选择“场景物件 / 创建场景物件预制体”。工具会询问：

1. 保存到源资源同目录，或保存到统一的 `Assets/Modules/GeneratedPrefabs/<模块>/<分类>` 目录。统一目录会根据源路径自动推断；跨模块的树、草等放到 `GeneratedPrefabs/Shared/Vegetation`。
2. 是否跟随镜头。

新预制使用资源本身的名称，不再创建 `_XY` 或 `_XZ` 后缀。预制结构固定为：

```text
物件根节点（布局节点）
├─ VisualRoot
│  └─ Art（SpriteRenderer、PlanarSprite）
├─ ContactRoot
├─ PhysicsRoot
└─ AnchorsRoot
```

美术只在物件根节点按二维方式摆放。`PhysicsRoot` 和 `AnchorsRoot` 保存碰撞、交互和业务锚点，不放到视觉分支。

## 统一组件规则

- 需要始终面向镜头时，由工具把 `CameraFacingVisual` 加到 `VisualRoot`。组件只在运行时跟随镜头，编辑态保持二维平面。
- 不需要跟随镜头时，不手动旋转 `VisualRoot`；使用“取消跟随镜头”，工具会恢复共享镜头规范的固定朝向。
- 图片宽度、接地点和占地由 `PlacementItem` 统一应用。换图后使用预制体工具应用配置，不通过手动缩放补偿镜头。
- 地图平面由场景和 `PlacementItem` 配置决定。普通平铺物件不需要制作或维护 XY/XZ 两份资源。

## 旧资源

`Assets/Modules/PlacementSamples` 只保留一份中性预制体，不再按 `_XY`/`_XZ` 复制资源。本项目现有方向变体已经统一迁移，旧后缀预制体和场景引用不再保留；以后发现外部旧资源时，必须通过 `Tools/Modules/Placement/迁移旧平面预制体到统一格式` 由程序一次性迁移，不能手动复制、改名或旋转资源。模块使用哪个逻辑平面由程序在实例层配置，不由美术复制或旋转资源。通用树草模板也使用同一套结构，分别归档到 `GeneratedPrefabs/Shared/Vegetation`。

迁移旧场景后执行 `Tools/Modules/Placement/清理场景旧视觉覆盖`。它只回退 PlacementItem、SpriteRenderer、PlanarSprite、VisualRoot 和 Art 的旧覆盖值，保留物件根节点的位置、旋转和布局；这样美术修改中性源预制体后，场景实例会正常继承。

新资源不再沿用这些后缀。旧预制需要转换时，使用右键“设置为跟随镜头”或“取消跟随镜头”，由工具统一处理；所有路径、场景引用和验收规则都以中性预制体为准。

## UI 与场景镜头

- 屏幕 UI 使用 Overlay 或 Screen Space - Camera Canvas，不挂到场景图片的 `VisualRoot` 下，也不跟随场景透视倾角。
- Screen Space - Camera 必须绑定当前 `MainCamera`，`Plane Distance` 使用正数；Canvas 根节点缩放保持 `(1,1,1)`。镜头切换由 `KeepMainCamera` 统一重绑。
- 在编辑器中执行“场景 / 验证 UI 显示配置”可检查启用 Canvas 的相机、平面距离和根节点缩放，发现问题先修配置，不用改图片资源。
