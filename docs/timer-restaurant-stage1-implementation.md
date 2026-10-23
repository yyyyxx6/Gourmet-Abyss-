# 餐厅第一批 食材仓库与自动转存

本批实现餐厅库存、进店转存、飞行动画、背包隐藏，以及制作菜单显示和扣料退款的仓库接线。

## 实际行为

- 进入餐厅前读取完整背包并校验全部槽位，只把正式食材转入餐厅仓库。金币、采集物及旧版非食材槽位不参与转存。
- 校验完成后一次提交仓库和背包数据，再刷新界面。重复进店不会再次增加同一批食材，拒绝的转存不改变任何数量。
- 转存动画从原背包位置飞向现有餐厅模块的 `Cabinet`。每个食材堆叠显示一个图标和数量，动画完成后隐藏原背包与餐厅 HUD 的装饰背包。
- 提前退出、禁用入口会清理动画并恢复原背包显示、玩家移动和位置；已经提交的食材仍留在仓库。
- 仓库使用 `long` 数量，无背包格数和原资源表 9999 容量限制。达到数值类型极限时整批拒绝，不截断数量。
- 菜单的食材列表、菜谱材料数和可制作判断均读取仓库。已有食材缺少图标或资源描述时，使用食材名称和数量兜底，仍列入仓库显示。
- 入队先预留完整材料，防止重复点击超支。动画结束、队列接受菜品后才完成消耗；入队失败或业务组件禁用时，按冻结的扣料记录精确返还，重复退款无效。
- 菜谱中重复出现同一种材料时，先合计需求再做整单校验。缺少任一种材料，不扣除其他材料。

## 文件与自动接线

相对于 `extraction-shooter/UnityProject/ExtractionShooter`：

| 文件 | 职责 |
| --- | --- |
| `Assets/Scripts/Restaurant/RestaurantIngredientStore.cs` | 不依赖 Unity 的库存、预留、提交和退款逻辑 |
| `Assets/Scripts/Restaurant/RestaurantIngredientStorage.cs` | 常驻库存服务、菜谱需求合计、库存通知及名称兜底 |
| `Assets/Scripts/Restaurant/RestaurantIngredientTransferPresentation.cs` | 临时转存图标与数量动画 |
| `Assets/Scripts/Bag/InventoryManager.cs` | 食材专用转存、完整槽位校验、展示刷新 |
| `Assets/Scripts/Restaurant/RestaurantEntryPoint.cs` | 真实进店业务入口与转存展示生命周期 |
| `Assets/Scripts/Restaurant/RestaurantPanel.cs` | 仓库菜单与订单扣料接线 |
| `Assets/Scripts/Restaurant/RestaurantModuleAdapter.cs` | 转存结束后隐藏装饰背包 |

库存服务自动创建，无需手工挂到场景。当前模块自动使用 `Cabinet` 为转存终点；入口的 `ingredientStorageAnchor` 可覆盖此位置。默认动画时长 0.6 秒。

## 飞行动画调整

进店转存与餐厅模块内的制作投料共用新的飞行表现：连续三次贝塞尔曲线配合平滑加减速，起步与到达时速度归零；途中轻微放大和倾转，到达时缩小淡出。不同食材具有少量固定弧度差异，并错开起飞，避免同点重叠。弧高随距离和屏幕高度调整，短距离不会绕大圈。

单件飞行继续使用现有时长配置。转存默认间隔 0.045 秒，整批额外延迟最多 0.18 秒；投料沿用间隔设置并应用相同上限。动画使用非缩放时间。数量转存、材料预留与退款仍由原业务入口处理。

## 验证记录

- `tests/RunRestaurantStage1Tests.ps1` 使用已安装 Unity 的 Mono/C# 编译器执行纯测试，覆盖 128 组数量守恒、9999 以上库存、混合材料不足、预留量保护、重复退款、快照隔离及数值边界。
- `tests/RestaurantStage1UnityValidation.cs` 在真实 UpGround 中执行进店与重复进出、两个食材类型和非食材保护、图标/名称动画、背包隐藏和退出恢复、实际菜单数量、异步入队与重复点击保护、队列满失败退款、配方中途变更、组件禁用与恢复、提前退出动画，以及 Layer1 死亡和返回小镇。
- 隔离工程 `L:/料理地牢/.validation/Batch1`、Unity `2022.3.62f2c1` 的 `restaurant-stage1.log` 已记录 `RESTAURANT_STAGE1_UNITY_PASS`，没有非预期脚本错误。
- 飞行动画调整后，`restaurant-flight-polish.log` 再次记录 `RESTAURANT_STAGE1_UNITY_PASS`，实际进店动画、制作入队、失败退款与提前退出清理均通过原有场景回归。
- `tests/FinalGameplayValidation.cs` 的旧餐厅扣料用例已更新为仓库供料，并继续检查背包与永久资源不会被二次扣除。旧结算断言同步为此前确认的顶部排列与带出总量口径，仍检查结果数据保留净增减。
- `restaurant-stage1-regression-rerun.log` 已记录 `FINAL_GAMEPLAY_REGRESSIONS_PASS`，覆盖 1/2/4/6/9/12 格背包、切关失败保护和往返、首次解锁、死亡结算、舔包、重新探索及返回小镇。槽位缺失异常为主动注入的预期用例。

可在仓库根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\RunRestaurantStage1Tests.ps1
```

如 Unity 安装位置不同，传入 `-UnityEditorPath` 指定 Editor 目录。运行真实场景测试时，将测试 C# 文件及 `Batch2UnityValidation.cs` 共享探针复制到隔离工程的 Editor 目录，调用 `RestaurantStage1UnityValidation.Run`。

## 手工验收

1. 从地牢带出食材并回到小镇，确认背包中有食材。
2. 按原有入口交互进入餐厅，观察食材图标和数量飞向柜子，动画结束后背包隐藏。
3. 点击屏幕顶部制作区或右下角“菜谱”，选中可制作菜品并点击“烹饪”。确认仓库减少对应材料：有空闲锅时开始制作，锅忙时在顶部等待格显示菜品与数量。
4. 退出再进入餐厅，确认仓库不重复增加；之后进入地牢死亡并回到小镇，确认已转存的仓库食材仍保留。

2026-10-21 补充了实际 UI 指针点击验收，并修复队列布局、餐厅地面遮挡、旧导航与重复货币栏，详见 [餐厅验收反馈修复](timer-restaurant-ui-acceptance-fixes.md)。

## 本批边界

仓库目前在同一次游戏运行中跨进店、退出及关卡加载保留。跨游戏重启保存与离线经营规则尚待策划明确，本批没有新增跨重启存档。

尚未增加主动取消已入队菜品的 × 按钮、设施建造、员工岗位和客流规则。这些属于后续批次；本批只对未成功入队的预留材料自动退款。

制作动画对普通配方逐单位展示；极端高材料需求最多生成 64 个表现图标，真实扣料数量仍精确执行。小蛋在现有资源表中缺少图标，动画与仓库列表已采用名称兜底，未编造美术资源。

飞书看板尚未同步更新。
