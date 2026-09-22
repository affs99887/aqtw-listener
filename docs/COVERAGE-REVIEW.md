# 拾起素材复核 · 2026-09-22

用户报告「琥珀天星」「双颈琴」无反应后，检查发现旧导入脚本无条件禁用 `267afca3` 与 `e9f62d39`，理由为「放下来源冲突」。本次核对上游归档中的原始元数据及 WAV 字节，未发现该理由成立的证据。

## 可复核依据

- [原始启动音效库，4f8289d5](https://github.com/blood77458/soundradar/blob/4f8289d5bb32813cd7c15922df1a2a769128740a/soundradar/data/library.srz)
- [合并后的同音组库，eaa0348f](https://github.com/blood77458/soundradar/blob/eaa0348fa60e8b8477e82b72591f74f40f9574bf/soundradar/data/library.srz)
- 完整比对记录：[pickup-source-audit.json](measurements/pickup-source-audit.json)。旧版 `provenance.json` 中这两条 WAV 的哈希也与下表一致，本次没有将放下文件改标为拾起。

| 现有同音组 | 原始「拿起」目录 | WAV SHA-256（两边一致） |
| --- | --- | --- |
| 琥珀天心类 `e9f62d39` | `items/0c5ac384/samples/0001.wav` | `8117b13cfcd61e667f560d3b431a4c4113b5cc4be51470df4b9f1f66b17bf89c` |
| 定位组 `267afca3` | `items/99d97932/samples/0001.wav` | `cf3d1e09c17a5df040400e1fe03e29c137ce7cf00d7e8fa0fe4ca45ee1d3c7ec` |

原始「琥珀天心-放下」位于 `ad6b7dfa`；「目标定位-放下」位于 `ec9b8a7f`，所有文件哈希均与上面不同。上游 `tingsheng_merge.go` 的映射同样合并拿起素材、删除放下条目。

## 修复

恢复两个组的拾起参考并同时重建 C# 特征及 SoundRadar 索引、更新索引绑定哈希。保留完整 15 件琥珀类、11 件定位类候选，合计 9 组 / 14 段 / 51 件候选；不删同音低价物品、不降低 0.75 阈值、不改 Go 引擎和 240 Hz 高通。

导入脚本检查两组的明确来源字符串和已复核 WAV 哈希；以后上游替换素材时会重新隔离，避免继承本次复核结论。

14 段参考自匹配及两组各 4 个补零、降音量的 1.15 秒自动窗口检查共 22 项通过。报告：[coverage-reference-tests.json](measurements/coverage-reference-tests.json)。此外原始两类各一个完整放下文件均返回 `unknown`，这只是两条反例，不代表放下音效已全面验证。回归可用 `scripts/verify-reference-matches.py` 对导入清单执行；需要 `--dotnet` 指向本机 .NET，位置参数为导入清单、目标库、CLI DLL 和报告路径。

这些检查使用同源参考/派生声音，属于工程回归。哈希只能证明文件来源，不能证明上游录制标签在实机一定正确，也不能代替独立测试和用户反馈。音效库仍标记 `uncalibrated`。

## 未解决的反馈

「琥珀天星」与目录「琥珀天心」的名称对应仍待实机确认；本次恢复的是目录中的琥珀天心类。「双颈琴」在两个归档及当前 51 件目录中均无对应条目，不能当成「里拉琴」。需要该物品的真实拾起音效和可确认的物品资料才能补入；本版不声称已修好双颈琴。
