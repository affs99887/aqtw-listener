# 开源拾取音效来源核查 · 2026-09-23

## 已查资料

| 来源 | 找到的内容 | 能否据此扩充识别库 |
|---|---|---|
| [SoundRadar 源码和自带库](https://github.com/blood77458/soundradar) | 上游 README 明列 9 个听声鉴宝音效类。本项目固定的 [库归档](https://github.com/blood77458/soundradar/blob/eaa0348fa60e8b8477e82b72591f74f40f9574bf/soundradar/data/library.srz)导出 14 条参考，现有 51 件候选由同音类资料展开。 | 已导入；未找到可核实的新增拾取音效类。部分候选名称是同音类元数据，不能视为每件均有单独录音。 |
| [Arena Breakout Infinite Offline Database](https://github.com/fabiopsyduck/Arena-Breakout-Infinite-Offline-Database) | 仓库说明是物品、装备及统计数据的离线数据库，文件为程序和 CSV。 | 未见拾取音频样本或物品与音效的对应表。 |

仓库内的 `src/Listener.Engine/internal/library/tingsheng_seed.go` 对五个同音类明确写着“元数据预置，请补样本”；`tingsheng_merge.go` 把现有“拿起／拖动”录音合并到这些类。当前 [来源记录](../data/library/provenance.json)可追查每条录音的原文件与哈希，但这不能独立证明同类内每件物品实际发声相同。

## 本次处理

前一版凭物品列表或公告录入了 18 件名称，却没有查证拾取声音。这些条目现已从运行库撤回，版本回到 `0.1.1-pickup-source-fix`，保持 51 件、9 组、14 条参考和原有索引不变。此处记录检索结论，不将资料页里的名称视作识别样本。此轮也没有获得独立的实机准确率证据。

以后扩库至少需要能回溯的原始拾取音频、时间点、对应物品画面或可信的物品与音效映射，再比对现有同音组；只有名称的资料不能进入基础库。
