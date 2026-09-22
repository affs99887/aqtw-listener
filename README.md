# 行商听音助手 · Windows 预览版 v0.2.0

按格数展示候选缩略图的本地听音浮窗。当前为 **顶部浮窗、声音对比与个人音效库预览版，尚未完成实战准确率验收**。

本次版本：[v0.2.0 发布说明](docs/releases/v0.2.0.md)。保留回退后的分页与操作模式；[15:19 后需求](docs/handoff/REQUIREMENTS-20260922-1519.md)和[设计册](docs/design/README.md)属于后续开发资料，不代表本版已经实现。

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

1. 打开项目的 `portable` 文件夹，双击 `AqtwListener.exe`。无需解压，也无需安装 .NET 或 Go。
2. 先启动暗区突围，助手固定识别 `UAGame`，无需选择进程。选择游戏使用的播放设备，点击「保存设置」。首次保存后需要开启监听；已开启时保存会保留启用状态，切回游戏后恢复采音。旧设置中的进程名不再使用。
3. 到达行商，切回游戏后按 **Ctrl+Alt+L** 开启监听，也可以先在主窗口点击开启。默认开启「声音自动识别」，拖动物品发出声音后会自动分析，不依赖鼠标触发；收到鼠标事件时仍优先使用点击识别。
4. 再按一次组合键关闭。切出游戏会暂停采音并隐藏浮窗，切回后恢复。已匹配结果持续保留，显示匹配时间，直到下一次有效匹配或点击「清除当前结果」。主窗口最小化后从系统托盘打开；关闭主窗口会退出助手。

主窗口按「监听／采音／位置／浮窗／音效库／诊断」切换，无浮窗预览。默认浮窗在游戏画面顶部居中，上边距 16 逻辑像素，默认宽 470、最多占游戏宽度 40%。高度由内容决定，上限为游戏画面顶部约三分之一，更多候选分页；自定义位置则向下展开到剩余工作区。小／中／大字号与图片尺寸独立设置。

按 **Ctrl+Alt+C** 进入浮窗操作：试听当前声音和参考、切换完整候选、保存 WAV、补库或学习。点击「已锁定」解锁，拖动标题空白处后自动保存，Esc 取消移动或退出操作，「居中」恢复默认位置。主窗口与托盘有备用入口。普通监听仍鼠标穿透，操作期间暂停采音识别；退出停止播放、清空采音缓冲并等待 300 毫秒，再按开关和游戏前台状态恢复。

**Ctrl+Alt+PgUp / PgDn** 可在游戏前台、监听开启时翻页，也可点击操作模式或主窗口翻页按钮。候选总数与占比按全部结果计算。识别加载期间保留上次结果，失败明确标注旧结果。

「识别历史」保留本次运行最近 100 条，支持「在浮窗中对比」。最近 20 个有效声音片段及历史音频共用 32 MiB 内存上限，淘汰最旧且未选中的音频、保留文字；退出清空普通音频和本次历史，手动保存才写 WAV。每段声音绑定当时结果、全库评分及库版本。

「补库 / 学习」支持已有物品补样本、导入 WAV、试听裁剪、新物品名称/分类/格数及可选图片和参考价。引导学习需先选定物品，三轮参考加一轮单独试识别；每轮 3 秒准备、1.2 秒采音，**拿起并保持，采音结束后再放下**。无效轮次补录，切出暂停，可保留草稿续录。检查通过自动启用，失败保留原库；同音冲突需用户确认，普通识别不会自动成为训练标签。

个人资料与基础库分开，位于 `local-data/personal`，支持启停、删除、重采样、导入导出和撤销更新。发布包不含个人录音或设置；更新程序请保留自己的 `local-data`。详细操作、数据约定与验证边界见 [本版说明](docs/TOP-OVERLAY-PERSONAL-LIBRARY.md)。

### UU 远程或拖动没有反应

助手与游戏需要运行在同一台被控电脑。如果浮窗显示「鼠标触发 0 次 · 已收到声音」，请保持「声音自动识别」勾选。开启监听且游戏在前台时，新版直接从近期音频中识别，浮窗的「声音分析 N 次」会增加；鼠标事件仍使用事件与按键状态两路检测。

自动识别仅分析有声音的窗口，静音时等待，不累计识别任务；切出游戏、关闭监听或关闭自动识别都会取消正在计算的任务，但不删除已经接受的匹配。静音、分析中、未匹配和识别错误更新状态，不覆盖保留的候选。自动模式会分析背景音乐、界面和物品等通过所选设备播放的声音；实验音效库的覆盖范围和准确率限制仍适用。

监听控制区固定显示前台进程、输入计数、采音状态及最近识别结果。先确认已开启监听，再切回 `UAGame`；播放设备要与游戏实际输出一致，可能是 UU 虚拟声卡，不能只根据远程端能否听到声音来判断。

可点击「3 秒后试识别」，立即切回游戏，在倒计时结束时拖动一次货物。这会主动触发一次识别，帮助检查音频链路。仍无法定位时点击「导出诊断」，生成 `local-data/diagnostics-*.json`；文件仅包含状态与计数，不保存录音。详细说明见 [远程排查](docs/REMOTE-TROUBLESHOOTING.md)。

默认使用适合静态浮窗的软件渲染以降低内存占用；高分辨率下绘制不流畅时可在「浮窗」设置中启用硬件加速，重启生效。

候选按总占用格数分组，卡片显示缩略图、具体宽×高、红色收藏品标记。**不显示每件物品的百分比。** 绿色边框表示并列最匹配音效组，不表示组内某一物品更可能出现。参考价完整时会标出已知参考价最高的候选。

**大金候选占比 = 去重后的红色收藏品候选数 / 全部候选数。** 例如 1/4 为 25%。它不是出货概率；没有候选时显示「—」。

## 当前数据边界

- 目录含 **51 件物品的真实缩略图和格数**；9 个启用音效组关联 **51 件候选**，共 14 条参考样本。关联某同音组不代表该组每件物品都已单独录音验证。
- 修正了定位组、琥珀天心类的错误禁用：上游原始「拿起」文件与现有参考字节一致，与「放下」不同。复核依据见 [素材复核](docs/COVERAGE-REVIEW.md)。
- 「双颈琴」未在当前目录中找到，不能自动认定为「里拉琴」；用户所称「琥珀天星」与目录名「琥珀天心」也仍需实机对应确认。库外物品需要补充真实音效与物品资料。
- 价格未取得可靠的当前联络人回收依据，全部留空；目前不会伪造最高价值结论。可在 `library/library.json` 中为物品补充 `referenceValue`、`valueSource`、`valueDate`，重启生效。
- 默认阈值是实验值，尚无独立验证集校准。没有足够的 200 个独立测试事件；不能宣称达到 95% 准确率。
- 工程压力集中的低频干扰已通过同步调整高通参数改善；该参数在派生样本上选定，队友语音、枪声和未知物品仍需独立实机补测。

详情见 [覆盖清单](docs/COVERAGE.md)、[方案比较](docs/ARCHITECTURE.md)、[测试报告](docs/TEST-REPORT.md)。

## 软件行为

采集选定播放设备的 WASAPI 系统回环音频，其他软件通过该设备播放的声音也会进入缓冲。不开麦克风，不读取游戏画面、内存或网络包。普通识别音频只保留在内存；手动保存和明确开始的学习录音才写入本地。暂停监听释放采音设备并清空采音缓冲，历史回放缓存保留至退出。没有账号、上传、自动更新和自动购买。

独占全屏覆盖尚未验证。浮窗看不见时，先将游戏改为无边框全屏；Windows 11、朋友电脑和不同分辨率仍待实测。

更新时退出软件，整体替换 `library` 文件夹或程序包。声纹索引和目录有 SHA-256 绑定，混用不同版本会明确报错。个人设置存放在程序目录 `local-data/settings.json`。

## 源码与构建

桌面：C# / .NET 10 / WPF / NAudio.Wasapi 3.1.0。识别工作进程：复用 SoundRadar 的 Go DSP 与量化索引，标准输入输出传递内存音频，整个进程生命周期内复用，不逐次启动。C# DTW 核心保留作离线基线。

需要 .NET 10 SDK、Go 1.27.1 和 Python 3；便携包使用者无需这些环境。SDK、缓存、IDE 配置和编译产物不提交到仓库。构建脚本优先使用本机 `.tools` 下的工具，不存在时使用已安装并加入 PATH 的工具。

```powershell
.\scripts\build-engine.ps1
dotnet run -c Release --project tests\Listener.Tests -- docs\measurements\core-tests.json
dotnet run -c Release --project tests\Listener.App.Tests -- docs\measurements\controller-tests.json
.\scripts\publish.ps1
```

发布脚本固定更新项目下的 `portable`，只保留这一份最新便携程序，不创建版本目录或压缩包。后续更新覆盖程序文件，保留 `portable/local-data` 中的设置、录音、草稿和个人音效库。

离线工具：

便携包内也包含 `Listener.Cli.exe`，可直接使用以下参数；源码环境才需要 `dotnet run`。

```powershell
dotnet run -c Release --project src\Listener.Cli -- match data\library\library.json sample.wav
dotnet run -c Release --project src\Listener.Cli -- evaluate data\library\library.json dataset\test.json report.json
dotnet run -c Release --project src\Listener.Cli -- calibrate data\library\library.json dataset\validation.json calibrated.json
```

样本格式、更新流程和来源隔离见 [数据工作流](docs/DATASET.md)。
