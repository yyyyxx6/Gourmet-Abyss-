# Timer 两项需求：最终交付与验收记录

日期：2026-09-09。**两项需求的开发和自验已完成**，范围为“死亡掉落与舔包”和“结算界面制作”。已按用户确认的规则实现食材占背包格、连续探索保留背包。本文记录开发自验结果，需求提出人的人工签收尚未代办。

需求依据及完整规划见 [制作计划](timer-death-loot-settlement-plan.md)，算法和场景细节见 [第三批记录](timer-batch3-implementation.md)。

## 最终行为

| 需求 | 实际行为 |
| --- | --- |
| 死亡保留 | 对携带食材、本局采集物分别按类型汇总，默认保留 10% 并向上取整；余量进入单个死亡箱，历史永久仓库不扣除 |
| 保留率技能 | 初始配置及技能属性 71 已接入，限制 0–100%；未新增没有策划配置的购买节点或花费 |
| 跨局死亡箱 | 同图重进恢复原箱；回城保留记录；新死亡替换旧箱；进入不同关卡清除旧箱 |
| 舔包 | 实际玩家靠近后一次性领取；先填同类堆叠，再按商店单价降序使用空格，同价按资源 ID；满包溢出食材丢弃并提示，采集物全收 |
| 统一结算 | 死亡或撤离只结算一次，固定结果快照；期间停止战斗和拾取，两个按钮支持真实重进或回城，失败时恢复操作 |
| 背包展示 | 显示所有已拥有格子及空格，适配 1、2、4、6、9、12 格；不展示 HUD 的额外锁定预览格 |
| 获得物展示 | 新宠物、新菜谱、采集物按顺序排列，空类别隐藏；NEW 位于图标左上，仅首次解锁显示 |
| 结算数量 | 采集物显示本局实际获得量、当前拥有量；死亡另列携出/遗失；食材显示相对入场的净变化，负值红字 |
| 固定统计 | 食材净变化、时长、击杀数固定在奖励列表下方，滚动奖励不会移走统计或遮挡按钮 |

例：携带 8 份蘑菇和 4 份肉，收集 11 份木材后死亡，默认保留蘑菇 1、肉 1、木材 2，箱中留下蘑菇 7、肉 3、木材 9。若入场时已有这 12 份食材，结算食材净变化为 **-10**。木材的“本局 +11”和“带出 2 · 遗失 9”同时显示。

采集物拥有总量使用 `long`，包括永久仓库和暂未入库部分，避免仓库容量边界时少显示资源。底层单种资源存储仍沿用原工程的 `int` 上限；达到上限时保留未入库量并阻止丢失资源的继续探索操作。

## 界面与资源

已复用项目木框纸面底板、像素字体、背包框、食材/菜品图标及明暗按钮。木材和飞行随从缺少合适的现有二维图标，使用项目真实模型渲染透明 PNG：

- 木材：`Assets/ImportAsset/FarmCrops/Prefabs/PlanterModules/Log_1m_01.prefab`。
- 飞行随从：实际启用的 `Assets/Suriyun/Monster Pack Forest/Prefab/Planta/Planta_Queen.prefab`。
- 死亡箱：沿用项目 Chest2A 模型、现有粒子和音效，独立触发器不阻挡移动，也不随普通掉落物超时销毁。

项目内主要交付文件，相对于 `extraction-shooter/UnityProject/ExtractionShooter`：

| 文件 | 用途 |
| --- | --- |
| `Assets/Resources/UI/SettlementPanel.prefab` | 已绑定控制器、独立 Canvas/EventSystem、字体、按钮与展示配置的最终结算面板 |
| `Assets/Resources/UI/SettlementPresentationCatalog.asset` | 木材与宠物名称/图标映射，可继续在 Inspector 调整 |
| `Assets/Resources/UI/SettlementIcons/` | 两个透明 PNG 图标及 Sprite 导入配置 |
| `Assets/Resources/Loot/DeathLootCrate.prefab` | 死亡箱模型、范围触发器与反馈引用 |
| `Assets/Editor/SettlementPanelBuilder.cs` | 重建结算面板，读取现有 Catalog |
| `Assets/Editor/SettlementArtBuilder.cs` | 从现有模型重新渲染结算图标 |
| `Assets/Editor/DeathLootCrateBuilder.cs` | 重建死亡箱 |

无需把结算面板手工挂到各关卡：`LevelManager` 初始化运行管理器，运行管理器从 Resources 加载结算面板与死亡箱。原有场景和角色入口已经接线。

最终预览：

- [成功撤离 1920×1080](previews/settlement-success.png)
- [成功撤离 1280×720](previews/settlement-success-1280x720.png)
- [死亡与长提示 1024×768](previews/settlement-death.png)
- [完整大数字 1024×768](previews/settlement-large-numbers-1024x768.png)
- [单格空背包 1280×720](previews/settlement-single-empty-1280x720.png)
- [死亡箱实际渲染](previews/death-loot-crate.png)

## 验证证据

使用 Unity **2022.3.62f2c1（92e6e6be66dc）**，在完整独立副本 `L:/料理地牢/.validation/Batch1` 编译和进入 Play Mode。副本名称沿用首批，后续批次继续在这里验证。原工程没有记录精确编辑器补丁号，因此该版本是本机实测可用版本。原工程的 `ProjectSettings`、`Packages` 没有升级或改写；测试使用独立公司/产品名隔离 PlayerPrefs。

| 验证 | 结果与证据 |
| --- | --- |
| 纯逻辑测试 | `tests/RunAllGameplayTests.ps1` 五组通过：装包、运行数据、资源规则、结束状态、死亡分配；装包与死亡分配各包含 128 组数量守恒场景 |
| Layer1/Layer2 死亡箱矩阵 | `batch3-tests-final.log`：`BATCH3_UNITY_SCENE_TESTS_PASS`；覆盖同图恢复、两种连续死亡、换图清除、远处拒领、近处一次领取、满包优先级与空掉落清旧箱 |
| 结算及失败恢复回归 | `batch3-regression.log`：`BATCH2_UNITY_SCENE_TESTS_PASS`；使用最终 10% 规则，验证实际 UI 射线、暂停恢复、真实重进、加载失败恢复和回城 |
| 最终真实玩法补测 | `final-gameplay.log`：`FINAL_GAMEPLAY_REGRESSIONS_PASS`；六档背包扩容、只读结算、真实餐厅扣料、实际首次解锁、Layer3 死亡→重进舔包→撤离→回城全部通过 |
| 最终多分辨率视觉 | `final-visual.log`：`SETTLEMENT_VISUAL_VALIDATION_PASS`；五张截图全部通过，分类标题在 1024×768 的截断已修复；实际字形像素、完整大数字、负收益、空类别、NEW、固定统计及格子边界均通过检查，并逐图查看 |
| 图标和死亡箱渲染 | `settlement-art.log`：`SETTLEMENT_ART_BUILD_PASS`；`batch3-visual.log`：`DEATH_CRATE_VISUAL_VALIDATION_PASS`；真实 URP 管线渲染并逐图查看 |

上述日志位于隔离工程根目录。视觉结构报告见 [最终截图检查数据](previews/settlement-visual-report.json)。纯逻辑测试在仓库根目录使用 PowerShell 7 执行 `./tests/RunAllGameplayTests.ps1`。Unity 测试源码保存在仓库 `tests/`；复制到隔离工程 `Assets/Editor/Batch1Validation/` 后，使用对应类的 `Run` 方法执行。

交付前已比较原工程与验证副本中的 193 个脚本/CSV 文件，内容哈希一致。最终生成的结算预制体、图标与展示配置已连同 `.meta` 文件复制回原工程；测试脚本保留在仓库 `tests/`，不混入正式玩家脚本。

测试通过以断言完成标志、图像检查和运行时错误检查为准。Unity 编辑器完成测试后在关闭阶段偶有滞留，已按进程 ID 清理对应隔离编辑器，未把进程正常退出当作通过证据。日志中的许可客户端握手重试发生在启动阶段，随后许可建立并完成运行；与游戏脚本报错区分处理。

第三批故障注入中，`BATCH3_EXPECTED_GATHERED_LISTENER_FAILURE` 和临时缺失背包槽引用是明确的预期异常：分别验证通知隔离和结束失败恢复。测试要求其余非预期运行时错误失败，不能仅因日志含有预期异常认定逻辑测试失败。

## 范围和人工签收

- 死亡箱只在**同一次游戏运行中**跨局、跨场景保留；没有新增跨游戏重启的存档功能。
- 木材在结算中已经使用正确图标；旧玩法中的南瓜占位拾取模型不属于本次两项界面的替换范围。
- 当前技能系统同一属性的多个节点沿用原有覆盖规则；保留率属性已接好，新的购买节点及统一叠加规则仍由后续技能配置决定。
- 交由需求提出人检查正常操作的移动/拾取手感、音效与特效、界面风格，再签收这两项需求。开发自验不替代提出人的验收，未修改飞书看板状态。

建议人工验收从 `Assets/Scenes/UpGround.unity` 开始：携带食材进入任一正式关卡，正常撤离一次；再死亡，确认保留/遗失明细；选择重新探索并走近原死亡箱；最后返回小镇，核对背包、仓库和餐厅扣料。
