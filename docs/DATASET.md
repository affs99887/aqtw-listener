# 音效库与独立测试集

## 替换音效库

运行时 `library` 文件夹包含 `library.json`、`radar-index.bin`、`images/` 和来源记录。整个目录一起替换；不能只换索引。`EngineIndexSha256` 防止误配。程序重启后加载。

物品包括：唯一编号、名称、红色收藏品标记、宽和高、缩略图路径、联络人回收参考值及其来源/日期。未知参考价使用 `null`。同音组中只有经过实际区分验证的物品才能拆开，不因为价高而删除同音低价值候选。

建库清单结构见 `ImportManifest` / `ImportSample`。每段音频必须记录原始录制编号、来源 URL、开始与结束时间、游戏版本；目录项通过组的 `itemIds` 关联。拾起用 `action: "pickup"`；放下或尚未确认的样本不能误标为拾起。

已取得的 SoundRadar 资料可以重复导入：

```powershell
python scripts\import_soundradar.py PATH\library.srz .tools\research\imported --revision d94dc00e871eed3498e5e6be53edf1ad85cb383c
.\.tools\dotnet\dotnet.exe run --project src\Listener.Cli -- build .tools\research\imported\import.json data\library\library.json
python scripts\build-reference-archive.py .tools\research\imported\import.json .tools\research\reference.srz
.\data\engine\Listener.Engine.exe build .tools\research\reference.srz data\library\radar-index.bin
python scripts\activate-radar-library.py data\library
Copy-Item .tools\research\imported\images data\library\images -Recurse -Force
```

激活脚本设置的 0.75 仍是实验门限；不能把重新建库当成已校准。补充新素材可直接编写 import.json，沿相同流程生成两个索引和图片目录。

本项目引擎适配器建索引默认采用 240 Hz 高通。可给 `build` 追加一个频率参数用于工程对比；索引完整记录 DSP 参数，查询自动采用索引中的同一配置。不可只调整查询端而保留旧索引。

## 测试清单

```json
{
  "kind": "independent",
  "cases": [
    { "file": "audio/event001.wav", "recordingId": "original-recording-A", "expectedItemId": "537b0217-0", "isNonGoldOrNoise": false, "split": "test" },
    { "file": "audio/event002.wav", "recordingId": "original-recording-B", "expectedItemId": null, "isNonGoldOrNoise": true, "split": "test" }
  ]
}
```

`expectedItemId: null` 表示非目标声音，包括未知库外物品；测试物品必须在库目录中，目录内但缺少可用样本的物品应单独报告。文件放在清单所在目录之内，单事件不超过 5 秒。

原始录制来源相同的转载、裁剪、变速、增益和加噪版本保持同一个 `recordingId`，不得跨参考、校准、测试集合。工具会拒绝来源编号重叠、字节级重复、参考/校准哈希重叠，但不能自动识破人为填错的原始来源编号；素材整理时仍需核查出处。

校准使用 `split: "validation"`，每个启用组至少 5 正例、10 负例；选取同时符合召回与误报要求的门限，否则明确失败。校准来源和哈希写回库中，之后测试会排除它们。不得在 test 集上反复调门限再宣称独立达标。

验收要求至少 200 个独立事件、至少 100 个非大金/干扰，并实际包含负例。报告同时给候选命中、唯一结论正确率、唯一结论覆盖比例、候选数量分布和误报率。`scripts/make_stress_set.py` 仅生成工程压力样本，其 `kind` 被标为派生数据，不会获得验收通过标记。

待采集重点：同音低价值物品逐件拾起、队友语音、枪声、持续引擎/风声、蓝牙和有线设备、不同游戏音量、无声音、未知物品。真实点击延迟应同时记录鼠标按下和最终 UI 显示时刻，不能用纯算法耗时替代。
