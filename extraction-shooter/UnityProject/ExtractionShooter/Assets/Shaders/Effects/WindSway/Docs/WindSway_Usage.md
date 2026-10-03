# 风动 Shader 配置

1. 选中物件原材质，在 Shader 中选择 `GourmetAbyss → Effects → Wind Sway`。
2. 调整下表参数，保存材质。
3. 进入 Play 查看效果。草、树及其他需要同类摆动的图片都用这个 Shader。

| 参数 | 怎么调 |
| --- | --- |
| 摆动幅度 | 先用 0.04；越大摆动越明显，0 为关闭 |
| 摆动速度 | 先用 1.5；越大越快，0 为静止 |
| 底部固定比例 | 先用 0.1；底部仍晃动就调大 |

其他参数先保持默认。四顶点图片主要表现为整体倾斜。

如果原材质是 Unity 内置默认材质，先创建一个 Material，选择上述 Shader，再放进 SpriteRenderer 的 Materials 槽。SpriteRenderer 自动提供图片，无需手动填贴图。

单张 PNG 的图片 UV 范围保持 `(0,0,1,1)`；切图需填写在整张纹理中的归一化矩形 XYWH，旋转打包的图集暂不适用。

示例：`Assets/Shaders/Effects/WindSway/Demo/WindSwayDemo.unity`。中间一列的草和树共用同一个材质。
