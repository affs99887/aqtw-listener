# 第三方来源

识别引擎仍复用用户明确指定的项目：[blood77458/soundradar](https://github.com/blood77458/soundradar)。v0.3.3 按用户要求换用另一款工具的素材，未换用它的代码、算法或界面。

- 固定提交：`d94dc00e871eed3498e5e6be53edf1ad85cb383c`。
- 旧版音效资料固定于 `eaa0348fa60e8b8477e82b72591f74f40f9574bf`，并对照 `4f8289d5bb32813cd7c15922df1a2a769128740a` 的原始文件复核；v0.3.3 没有更新 Go 源码。旧版资料见 [素材复核](COVERAGE-REVIEW.md)。
- 演示来源：[BLOOD味道的视频](https://www.bilibili.com/video/BV1HNeY6mEhH/)。视频说明提到素材贡献者六豪物语、QJHWC。
- `src/Listener.Engine/internal/{dsp,index,library,wav}`：保留该提交的原始 Go 文件及其测试，没有改写原始算法。
- `src/Listener.Engine/cmd/listener-engine`：本项目新增的 stdin/stdout 协议适配。
- `data/library/images`：34 件既有目录物品保留来自上游 `soundradar/data/library.srz` 的图片；28 件新增物品图来自用户提供的 v0.67 素材包。既有格数沿用 `displayHints` 等已核实资料，新条目用背包格网及用户确认补齐。
- v0.3.3 不再包含 `data/library/putdown`，也不包含放下辅助识别代码。
- 物品映射、格数来源、图片和参考 WAV 哈希记录于 `data/library/provenance.json`。用户实战录像、解码音频和个人数据没有加入便携包或参考库。
- 上游原始启动、构建脚本未用于成品；成品不启动上游 HTTP 服务、浏览器管理页或上游采音进程。

## 用户提供的 v0.67 素材包

- 来源：用户提供的 `蜀黍万岁.exe` 中静态提取的 `_lib/refs.npz`、`_lib/index.json` 和物品图标库。未运行对方 exe，便携包不含对方 exe、Python 字节码或反编译代码。
- 来源 exe SHA-256：`f421fc13e12dcb495ef143895e1398b9c44ac4c73ba3d94b74f3f17fb7cfec35`；原 `refs.npz` SHA-256：`d18ec01dd063745d5a7bed148ff438c9014bfbf079b9aad7a8ac812c4601fe3f`。
- 62 条原始 float32 参考转为 48 kHz、单声道、0.45 秒 PCM16 WAV，与已验证的换库实验保持字节一致。未追加本项目旧音效、放下参考或实战录音。
- 包内署名及相关线索曾用于查找来源，但未取得可核实的该版本源码仓库或许可证，不能声称这些素材已获得某种标准开源许可。原始权利保留，项目根目录 Apache-2.0 许可证不覆盖它们。
- 导入器只读取数据，原始索引中的作者本机录音路径不进入成品。详细转换和逐件来源哈希留在库内 provenance 文件。

此前公开来源查询结果保留在 [来源核查](SOUND-SOURCE-AUDIT.md)，用于记录旧版调查范围。

本次固定提交未找到 `LICENSE` 文件，不能把它描述为已经授予某种标准开源许可，也没有为上游代码擅自添加许可证。来源和作者信息保留；仓库根目录的 Apache-2.0 许可证不覆盖这些第三方内容的原始权利和许可。

其他运行依赖：.NET 10 / WPF、NAudio.Wasapi 3.1.0，以及上游声明的 Go 模块（具体版本见 `src/Listener.Engine/go.mod`、`go.sum`）。自包含包包含相应 .NET 分发文件。

`docs/licenses/` 保留 .NET 分发声明、NAudio v3.1.0 的官方 LICENSE、Go 及所使用的 x/image、x/sys 许可文本。
