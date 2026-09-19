# Timer 第三批：死亡保留与跨局舔包

日期：2026-09-09。

## 已实现规则

- 死亡时对背包内所有正式食材、本局获得的采集物分别按种类汇总，默认保留 10%，向上取整；掉落为原数量减保留数量。计算使用 decimal，避免浮点误差或拆格改变收益。
- 保留食材从原槽位顺序保留，非食材不参与死亡扣除；历史永久仓库资源不参与扣除。采集物携出部分仅提交一次。
- 死亡位置生成一个箱子，记录 ID、关卡、原坐标、食材和采集物清单。数据独立于场景对象保存。
- 同图重进恢复同一个箱子；回小镇不清掉记录。进入不同关卡后清除，后来回原关卡也不复活旧箱。
- 未舔包再次死亡：旧箱替换为本次掉落，不把旧箱内容合并进新箱。即使本次没有资源可掉，也会清理旧箱。
- 舔包后再次死亡：取回食材已经成为当前背包内容，取回采集物计入当前局，和本局其他资源一起重新计算。
- 靠近自动领取，检查实际玩家、同场景、真实距离、存活与当前游戏阶段；不支持远程按 ID 领取，生成时已在范围内也会正确检测。
- 领取时先为全部同类食材填已有堆叠，再按当前商店单价从高到低使用空格，同价按资源 ID 稳定排序。剩余食材丢弃并提示数量；采集物全部收取且不占格。
- 数据更新与箱子 ID 消费先完成，再通知界面。监听器出错不会让同一箱子再发一次资源，其他正常监听仍会得到通知。
- 死亡箱无普通掉落物的超时闪烁或销毁逻辑，只有领取、替换、换图或结束本次游戏运行时清理。

## 数值口径

例：4 份食材、11 份木材死亡后，保留 1 份食材和 2 份木材，箱中留下 3 份食材和 9 份木材。一次领取完后，旧箱 ID 立即失效。

保留率接入 `WeaponStatsManager.deathRetentionRate` 和初始配置 `statID 71`，默认 `0.1`。技能配置 `(71,0.05)` 按每级增加 5 个百分点处理，两级为 `0.1 + 0.05 × 2 = 0.2`，结果限制在 0–1。

没有虚构可购买的技能节点、位置或花费。当前技能框架中，同一属性的多个节点仍采用原有覆盖行为；如果将来让多个节点共同增加保留率，需要统一叠加规则。

食材价值复用 `ShopManager.GetResourcePrice`。当前显式单价包括蘑菇 10、小蛋 30、大蛋 100；肉、面团和其余未配置的正式食材采用现有默认单价 5。数值变化沿用原商店配置，无第二份价格表。

## 文件与接口

脚本目录：`extraction-shooter/UnityProject/ExtractionShooter/Assets/Scripts`。

| 模块 | 职责 |
| --- | --- |
| `Bag/DeathDropCalculator.cs` | 纯死亡分配算法及不可变结果 |
| `Bag/DeathLootRecord.cs` | 单箱记录、领取结果和丢弃清单 |
| `Manager/RunSessionManager.cs` | 替换死亡全损、跨局箱子生命周期、一次性领取 |
| `ItemInteration/DeathLootCrate.cs` | 同场景近距触发、重叠兜底与拾取反馈 |
| `Bag/InventoryManager.cs` | 一次写入背包/采集物，再刷新或通知；保留旧接口 |
| `Bag/RunIngredientStore.cs` | 静默替换完整数据、逐监听通知与异常报告 |
| `3C/WeaponStatsManager.cs`、`Excel/ExcelConfigReader.cs`、`SkillTree/SkillTreeInitializer.cs` | 初始保留率与技能属性 71 接线 |

供后续联调用的接口：`PendingDeathCrate`、`ActiveDeathCrate`、`LastDeathLootReceipt`、`TryCollectDeathCrate(id)`；场景组件提供 `Initialize(id)`、`IsPlayerInRange(player)`、`TryCollect(player)`。

`Assets/Editor/DeathLootCrateBuilder.cs` 生成并已交付 `Assets/Resources/Loot/DeathLootCrate.prefab`。它复用现有 Chest2A 模型和 ProPixelizer 材质，仅保留视觉，添加一个不阻挡移动的 SphereCollider 触发器（半径 1.8），复用现有拾取音效与粒子。

[死亡箱预览](previews/death-loot-crate.png)

## 验证结果

验证使用完整隔离工程 `L:/料理地牢/.validation/Batch1` 和 Unity `2022.3.62f2c1`，保持原工程配置及游戏存档隔离。

- `tests/RunBatch3Tests.ps1` 五组纯测试通过，包含背包装载和死亡分配各 128 组数量守恒场景，以及取整边界、非法输入、只读结果和通知故障测试。
- `tests/Batch3UnityValidation.cs` 通过真实 UpGround、Layer1、Layer2 链路，覆盖默认保留量、同图恢复、远处拒领、近处一次领取、两种再次死亡、满保留率清旧箱、回城保留、换图清除和返回原图不复活。
- 满包用例在测试运行中临时扩容三格，验证先补蘑菇已有堆叠，再优先装入高单价鸡蛋，准确记录丢弃食材并全收木材。
- 故意让一个采集物监听器抛异常，领取仍然完整且仅一次，正常监听继续收到通知。该命名异常为预期注入，其余运行时错误会使测试失败。
- 在小镇尝试为未加载关卡恢复箱子，验证抛错时原状态、结算结果与箱子记录仍保留。
- 第二批结算回归通过：默认保留策略下，三份食材死亡保留一份、净变化为 -2；UI 点击、失败恢复、重试与回城仍正常。
- 死亡箱在真实 URP/ProPixelizer 管线渲染通过：3 个 Renderer、1 个触发器，图像不是空白、没有错误洋红材质，边界完整；已人工查看预览。

日志：`batch3-tests-final.log`、`batch3-regression.log`、`batch3-visual.log`，均位于隔离工程根目录。测试断言完成后，退出阶段滞留的隔离编辑器进程会被清理，不以正常退出作为通过依据。

## 范围说明

箱子目前只在**同一次游戏运行中跨局保留**，没有新增跨游戏重启的存档读写。新进入不同关卡时丢弃旧箱，是明确的玩法规则。

结算数据分别保留“本局实际采集量”和“死亡后携出量”。最终 UI 已同时显示本局、拥有、携出及遗失，正式图标和综合验证见 [最终交付记录](timer-final-acceptance.md)。尚未改飞书看板状态。
