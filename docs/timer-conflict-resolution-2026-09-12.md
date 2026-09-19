# 2026-09-12 冲突修复记录

在 `B0NewVersion` 分支、`d696b4e7` 基础上合并先前的死亡掉落/舔包和结算改动。原有 6 个未合并文件已清除冲突并标记解决。

- `LevelManager` 沿用上游 `PersistentMonoSingleton` 和统一 `TransitionRequest` 管线，将结算重进、回城、结束门禁和异常恢复接入同一执行器。同名关卡先完成卸载再加载，按目标场景绑定现有玩家、标题及相机。
- 保留上游餐厅显隐、HUD 刷新和透视镜头配置。场景激活改为幂等操作，已处于目标场景时不会被误判为失败；结算目的地不再次提交资源。
- 普通换关在暂停或卸载前完成旧局快照；快照失败时保留源局运行状态，避免回城后被残留活跃局锁住。
- `InventoryManager` 同时保留基类销毁清理和背包动画取消，移除对新只读单例别名的赋值；`TopDownController.TOHome` 使用统一结算入口。
- 恢复 `Editor/Modules.meta` 和 `Resources/Loot.meta` 各自正确的 GUID。双方已删除的 Lumen 导入包元数据保持删除。

验证使用 Unity `2022.3.62f2c1` 和完整隔离工程 `L:/料理地牢/.validation/Batch1`，同步当前上游 Assets、Packages 与 ProjectSettings。原工程中用户新加入的美术元数据未纳入本次冲突暂存操作。

| 验证 | 结果 |
| --- | --- |
| `tests/RunAllGameplayTests.ps1` | 五组纯逻辑测试通过，包含两组各 128 个数量守恒场景 |
| `Batch2UnityValidation.Run` | 编译、撤离、防重复点击、重进失败恢复、死亡结算、回城和新版相机/单例绑定通过；日志 `conflict-batch2.log` |
| `Batch3UnityValidation.Run` | 同图恢复、连续死亡、回城保留、换图清除、满包排序及通知异常隔离通过；日志 `conflict-batch3.log` |
| `FinalGameplayValidation.Run` | 普通换关快照失败保持源局、`Layer3 → Layer2 → Layer3` 正常切换、餐厅扣料、六档扩容、首次解锁与第三层死亡/舔包流程全部通过；日志 `conflict-final.log` |

场景测试新增对当前相机服务、Director、跟随目标、三层透视参数、叠加相机及跨局单例的检查；等待实际镜头更新帧后断言。预期库存引用缺失、采集物监听器错误只用于故障注入，其余运行时错误仍使测试失败。

已核对原工程和验证副本的 233 个脚本、CSV 及程序集定义，内容哈希一致。源码差异检查通过，Git 未合并项和文件内冲突标记均为零。隔离编辑器完成测试后在关闭阶段滞留的进程按 PID 清理；以完整通过标志和断言作为验证依据。

改动标记为已解决并暂存，未创建提交或推送。
