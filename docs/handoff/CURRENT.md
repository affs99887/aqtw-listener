# 当前接续状态 · 2026-09-22

当前发布 v0.2.0，功能基于回退至 15:19 消息之前的状态，保留另一任务已接入的 GUI / 图标改进。[15:19 后需求](REQUIREMENTS-20260922-1519.md)仍待实施，不能根据设计册误判已完成。

用户此前明确要求实施「顶部浮窗、声音对比与个人音效库完整计划」。已完成代码、工程回归和便携打包。原始交接资料仍保存在 [v0.1.0-original.md](v0.1.0-original.md)，其中旧路径、旧数据覆盖和产品行为不是当前需求。

## 当前实现

- 默认 UAGame 客户区顶部居中，16 DIP 顶边距、470 DIP 默认宽、最多 40% 游戏宽。顶部高度上限为三分之一，已替换之前临时 45%。内容高度、分页、大图和极小区域摘要保留；主窗口六个设置分区，无预览。
- 普通监听穿透、不激活；Ctrl+Alt+C 进入浮窗操作，锁定/解锁、标题空白拖动、Esc 取消拖动或退出、恢复居中。位置按显示器保存；重启恢复锁定偏好，保持穿透。主窗口和托盘有入口。
- 独立加载状态与结果保留；自动扫描超过 150ms 再显示加载。快照绑定原音频、时间、评分、结果与库版本；最近 20 片段、100 条成功历史、32 MiB 内存上限。回放、参考切换、最多三组相近音效、保存 WAV、历史对比，退出操作停止播放并完整等待 300ms。
- 个人库与基础库分离；手动补样本、WAV 导入裁剪、新物品录入、图片和参考价可选。默认独立新组 0.75，原阈值不变，同音冲突须用户确认。物品/样本管理、导入导出和版本撤销已接入。
- 引导学习先选物品，三参考一单独试识别，每轮 3s 准备和 1.2s 采音。零鼠标可运行；静音、过短、削波、重复、重叠窗口拒绝，切出暂停，取消保存草稿。留出不入索引，参考回归与试识别通过才自动启用。普通匹配不成为标签。
- 构建整库新版本、核对索引、哈希、参考回归后切换；失败维持原库，损坏时回退。普通音频不自动落盘；明确学习的样本、原录音与草稿保存在 local-data/personal，发布包排除 local-data。

## 代码入口

- App：OverlayWindow（位置、交互）、OverlayWorkspace（对比/学习/个人库页面）、ListeningController（活动与模式）、GuidedLearning、AudioPreviewPlayer。
- Core：AnalysisSnapshot/SessionAudioStore、ReferenceAudio、PersonalLibraryStore；两个 Recognizer 的 Analyze 同时返回正式结果和全库评分。
- scripts/build-playback-references.py 根据现有模板逐条验证上游来源，再整理 14 段试听/重建 WAV。没有修改 Go DSP；继续使用原 engine 二进制。
- tests/Listener.App.Tests/PersonalLibraryTests.cs 覆盖真实引擎构建、同音确认、留出失败、版本与资产恢复。测试录音为工程派生或合成素材，不是用户实物录音。

## 验证与交付

- 24 项核心、27 项控制器/布局/个人库测试、22 项参考及派生窗口测试通过。
- docs/measurements/feature-{core-tests,app-tests,reference-tests,ui-smoke}.json 保存本轮记录。
- 已直接运行自包含包内 AqtwListener.exe --ui-smoke，退出码 0；布局自适应、51 件全部分页可达、100 条历史可达、操作页按钮边界、穿透切换和监听开关保持检查通过。
- 用户最新约定：所有后续便携程序固定放在项目的 `portable` 文件夹，只更新最新一份，不区分版本，本地不生成压缩包；本次 GitHub Release 按用户补充要求额外提供 ZIP 下载附件。入口为 `portable/AqtwListener.exe`；更新时保留 `portable/local-data`。此前 artifacts 中的历史产物未删除。本次提交并发布 GitHub v0.2.0 预览版，Release 额外提供 Windows ZIP 附件；本地 portable 仍保持未压缩。
- 本机 SDK .tools/dotnet 10.0.401，NuGet 缓存 .tools/nuget。运行 `scripts/publish.ps1` 直接更新 portable，禁用联网包审计以离线构建，不导入旧测试目录里的个人数据或 smoke 输出。

## 保留的限制

基础库仍是 9 组 / 14 段 / 51 件候选，版本 0.1.1-pickup-source-fix，未经独立准确率校准。「双颈琴」未擅自映射到里拉琴；「琥珀天星」与「琥珀天心」名称关系仍需用户确认，可用本版录入实际样本。候选占比不代表出货概率。

真实游戏录音准确率、UU 热键转发、实际播放设备试听、多屏热插拔、跨 DPI 原生拖动及 Esc 取消、独占全屏覆盖仍需实机验证。参考回归不能证明这些项目已完成。现有 Go build 不输出细分跳过原因；C# 会核对哪些样本未生成声纹并保留构建报告，不静默启用缺样本的新版本。

具体操作与数据约定见 [本版说明](../TOP-OVERLAY-PERSONAL-LIBRARY.md)，阶段验证见 [测试报告](../TEST-REPORT.md)。
