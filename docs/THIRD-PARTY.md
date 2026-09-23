# 第三方来源

用户明确指定复用的项目：[blood77458/soundradar](https://github.com/blood77458/soundradar)。

- 固定提交：`d94dc00e871eed3498e5e6be53edf1ad85cb383c`。
- 本轮音效资料导入固定于 `eaa0348fa60e8b8477e82b72591f74f40f9574bf`，并对照 `4f8289d5bb32813cd7c15922df1a2a769128740a` 的原始拿起/放下文件复核；未更新 Go 源码。详情见 [素材复核](COVERAGE-REVIEW.md)。
- 演示来源：[BLOOD味道的视频](https://www.bilibili.com/video/BV1HNeY6mEhH/)。视频说明提到素材贡献者六豪物语、QJHWC。
- `src/Listener.Engine/internal/{dsp,index,library,wav}`：保留该提交的原始 Go 文件及其测试，没有改写原始算法。
- `src/Listener.Engine/cmd/listener-engine`：本项目新增的 stdin/stdout 协议适配。
- `data/library/images`：来自上游 `soundradar/data/library.srz` 的物品图片；格数与同音关系来自 `displayHints` 和 `tingsheng_merge.go` 等资料。
- 大小、操作来源、时间范围与哈希记录于 `data/library/provenance.json`。长录音按能量区间裁剪，裁剪不视为新独立来源。原始音频留在本地研究目录，便携包包含预计算特征而不包含原始 WAV。
- 上游原始启动、构建脚本未用于成品；成品不启动上游 HTTP 服务、浏览器管理页或上游采音进程。

本轮对其他公开项目的拾取音效资料作了[来源核查](SOUND-SOURCE-AUDIT.md)。仅有物品名称而无可核实声音关系的条目已从基础库撤回。

本次固定提交未找到 `LICENSE` 文件，不能把它描述为已经授予某种标准开源许可，也没有为上游代码擅自添加许可证。来源和作者信息保留；仓库根目录的 Apache-2.0 许可证不覆盖这些第三方内容的原始权利和许可。

其他运行依赖：.NET 10 / WPF、NAudio.Wasapi 3.1.0，以及上游声明的 Go 模块（具体版本见 `src/Listener.Engine/go.mod`、`go.sum`）。自包含包包含相应 .NET 分发文件。

`docs/licenses/` 保留 .NET 分发声明、NAudio v3.1.0 的官方 LICENSE、Go 及所使用的 x/image、x/sys 许可文本。
