# 第三方来源

## SoundRadar

- 仓库：[blood77458/soundradar](https://github.com/blood77458/soundradar)。演示来源：[BLOOD味道的视频](https://www.bilibili.com/video/BV1HNeY6mEhH/)，视频说明提到素材贡献者六豪物语、QJHWC。
- `data/library/images`：34 件既有目录物品保留来自上游 `soundradar/data/library.srz` 的图片。该资料固定于 `eaa0348fa60e8b8477e82b72591f74f40f9574bf`，并对照 `4f8289d5bb32813cd7c15922df1a2a769128740a` 的原始文件复核；格数沿用 `displayHints` 等已核实资料。旧版资料见 [素材复核](COVERAGE-REVIEW.md)。
- 早期版本曾以固定提交 `d94dc00e871eed3498e5e6be53edf1ad85cb383c` 的 Go 特征提取、降噪和量化索引代码作为识别引擎。现在识别由本项目的局内匹配器完成，这部分 Go 代码、构建脚本及其依赖许可已从仓库和便携包移除，可在 git 历史中查阅。
- 所核对的提交未找到 `LICENSE` 文件，不能把它描述为已经授予某种标准开源许可。来源和作者信息保留；仓库根目录的 Apache-2.0 许可证不覆盖这些第三方内容的原始权利和许可。

## 用户提供的 v0.67 素材包

- 来源：用户提供的 `蜀黍万岁.exe` 中静态提取的 `_lib/refs.npz`、`_lib/index.json` 和物品图标库。未运行对方 exe，便携包不含对方 exe、Python 字节码或反编译代码，也未采用它的代码、算法或界面。
- 来源 exe SHA-256：`f421fc13e12dcb495ef143895e1398b9c44ac4c73ba3d94b74f3f17fb7cfec35`；原 `refs.npz` SHA-256：`d18ec01dd063745d5a7bed148ff438c9014bfbf079b9aad7a8ac812c4601fe3f`。
- 62 条原始 float32 参考转为 48 kHz、单声道、0.45 秒 PCM16 WAV，与已验证的换库实验保持字节一致。未追加本项目旧音效、放下参考或实战录音。
- `data/library/images`：28 件新增物品图来自该素材包，格数用背包格网及用户确认补齐。
- 包内署名及相关线索曾用于查找来源，但未取得可核实的该版本源码仓库或许可证，不能声称这些素材已获得某种标准开源许可。原始权利保留，项目根目录 Apache-2.0 许可证不覆盖它们。
- 导入器只读取数据，原始索引中的作者本机录音路径不进入成品。

## 其他

- 物品映射、格数来源、图片和参考 WAV 哈希记录于 `data/library/provenance.json`。用户实战录像、解码音频和个人数据没有加入便携包或参考库；基础库不含放下音效。
- 此前公开来源查询结果保留在 [来源核查](SOUND-SOURCE-AUDIT.md)，用于记录旧版调查范围。
- 运行依赖：.NET 10 / WPF、NAudio.Wasapi 3.1.0。自包含包包含相应 .NET 分发文件。`docs/licenses/` 保留 .NET 分发声明与 NAudio v3.1.0 的官方 LICENSE。
