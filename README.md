# 行商听音助手 · Windows 预览版

按格数展示候选缩略图的本地听音浮窗。当前为 **0.1.0 社区素材预览版，尚未完成实战准确率验收**。

## 项目来源与致谢

识别引擎复用 [blood77458/soundradar（SoundRadar）](https://github.com/blood77458/soundradar) 的实现，感谢作者及素材贡献者。桌面界面、点击触发控制、候选去重、大金候选占比和评测流程由本项目集成实现。

| 来源仓库 | 本项目使用的内容 |
|---|---|
| [blood77458/soundradar](https://github.com/blood77458/soundradar) | Go 音频特征提取、降噪、量化索引、相关测试；社区同音组、格数、音效参考素材和物品缩略图 |
| [naudio/NAudio](https://github.com/naudio/NAudio) | NAudio.Wasapi 3.1.0，Windows 系统回环采音 |
| [dotnet/wpf](https://github.com/dotnet/wpf) / [dotnet/runtime](https://github.com/dotnet/runtime) | WPF 桌面界面与 .NET 10 运行时 |
| [golang/go](https://github.com/golang/go)、[golang/image](https://github.com/golang/image)、[golang/sys](https://github.com/golang/sys) | Go 编译及运行支持、上游使用的图像和 Windows 系统依赖 |

SoundRadar 固定版本为 [`d94dc00e871eed3498e5e6be53edf1ad85cb383c`](https://github.com/blood77458/soundradar/tree/d94dc00e871eed3498e5e6be53edf1ad85cb383c)，原始代码保留在 `src/Listener.Engine/internal/`，本项目新增的工作进程协议位于 `src/Listener.Engine/cmd/listener-engine/`。素材出处、裁剪信息和哈希见 `data/library/provenance.json`。

仓库保留原有 [Apache-2.0 许可证](LICENSE)；第三方代码、图片与音效资料的权利和许可单独保留，不因纳入本仓库而自动改用 Apache-2.0。上述 SoundRadar 固定提交未附 `LICENSE`，这里不为其声明额外授权。完整来源说明见 [第三方说明](docs/THIRD-PARTY.md)，依赖许可文本见 [docs/licenses](docs/licenses)。

## 开始使用

1. 完整解压 `AqtwListener-0.1.0-win-x64.zip`，双击 `AqtwListener.exe`。无需安装 .NET 或 Go。
2. 先启动游戏，在助手设置中选择游戏进程和游戏使用的播放设备，点击「保存设置」。也可以手动填写进程名，不带 `.exe`。
3. 到达行商，切回游戏后按 **Ctrl+Alt+L** 开启监听。按下鼠标左键或开始拖动货物后，浮窗显示候选。
4. 再按一次组合键关闭。切出游戏会暂停并清除结果，切回后恢复。主窗口最小化后从系统托盘打开；关闭主窗口会退出助手。

浮窗默认鼠标穿透、不抢焦点。位置、大小、透明度及是否显示名称可在主窗口调整。默认 470×720；缩小后可能需要在主窗口滚动查看，候选总数和占比始终按完整结果计算。

默认使用适合静态浮窗的软件渲染以降低内存占用；高分辨率下滚动不流畅时可在设置中启用硬件加速，重启生效。

候选按总占用格数分组，卡片显示缩略图、具体宽×高、红色收藏品标记。**不显示每件物品的百分比。**「最匹配」表示并列最匹配音效组，不表示组内某一物品更可能出现。参考价完整时会标出已知参考价最高的候选。

**大金候选占比 = 去重后的红色收藏品候选数 / 全部候选数。** 例如 1/4 为 25%。它不是出货概率；没有候选时显示「—」。

## 当前数据边界

- 目录含 **51 件物品的真实缩略图和格数**；其中 7 个启用音效组关联 **25 件候选**，共 12 条参考样本。关联某同音组不代表该组每件物品都已单独录音验证。
- 另外 26 件物品所属的两组存在「放下」来源冲突，暂不参与拾起识别。
- 价格未取得可靠的当前联络人回收依据，全部留空；目前不会伪造最高价值结论。可在 `library/library.json` 中为物品补充 `referenceValue`、`valueSource`、`valueDate`，重启生效。
- 默认阈值是实验值，尚无独立验证集校准。没有足够的 200 个独立测试事件；不能宣称达到 95% 准确率。
- 工程压力集中的低频干扰已通过同步调整高通参数改善；该参数在派生样本上选定，队友语音、枪声和未知物品仍需独立实机补测。

详情见 [覆盖清单](docs/COVERAGE.md)、[方案比较](docs/ARCHITECTURE.md)、[测试报告](docs/TEST-REPORT.md)。

## 软件行为

采集选定播放设备的 WASAPI 系统回环音频，其他软件通过该设备播放的声音也会进入缓冲。不开麦克风，不读取游戏画面、内存或网络包。音频只保留于短时内存；关闭或暂停监听会释放采音设备并清空缓冲。没有账号、上传、自动更新和自动购买。

独占全屏覆盖尚未验证。浮窗看不见时，先将游戏改为无边框全屏；Windows 11、朋友电脑和不同分辨率仍待实测。

更新时退出软件，整体替换 `library` 文件夹或程序包。声纹索引和目录有 SHA-256 绑定，混用不同版本会明确报错。个人设置存放在程序目录 `local-data/settings.json`。

## 源码与构建

桌面：C# / .NET 10 / WPF / NAudio.Wasapi 3.1.0。识别工作进程：复用 SoundRadar 的 Go DSP 与量化索引，标准输入输出传递内存音频，整个进程生命周期内复用，不逐次启动。C# DTW 核心保留作离线基线。

需要 .NET 10 SDK、Go 1.27.1 和 Python 3；便携包使用者无需这些环境。SDK、缓存、IDE 配置和编译产物不提交到仓库。构建脚本优先使用本机 `.tools` 下的工具，不存在时使用已安装并加入 PATH 的工具。

```powershell
.\scripts\build-engine.ps1
dotnet run -c Release --project tests\Listener.Tests -- docs\measurements\core-tests.json
.\scripts\publish.ps1
```

离线工具：

便携包内也包含 `Listener.Cli.exe`，可直接使用以下参数；源码环境才需要 `dotnet run`。

```powershell
dotnet run -c Release --project src\Listener.Cli -- match data\library\library.json sample.wav
dotnet run -c Release --project src\Listener.Cli -- evaluate data\library\library.json dataset\test.json report.json
dotnet run -c Release --project src\Listener.Cli -- calibrate data\library\library.json dataset\validation.json calibrated.json
```

样本格式、更新流程和来源隔离见 [数据工作流](docs/DATASET.md)。
