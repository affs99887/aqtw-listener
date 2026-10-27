# 当前接续状态 · 2026-10-27

## 2026-10-27：旧版本升级指引

GitHub `releases/latest` 与发布列表确认当前正式版仍为 **v0.5.3.1**，没有新的兼容补丁版本。已核对 v0.5.3 标签、撤下 v0.5.4 的历史提交与当前更新器：旧版本访问 GitHub、拒绝较低版本的行为仍在，解决方式是手动换用 v0.5.3.1。README 与发布说明现明确区分两类用户，详细操作见 [旧版本手动升级指南](../UPGRADING.md)。

指引包含诊断页判断版本、托盘退出、共享目录及自定义 `AQTW_DATA_DIR` 完整备份、官方 Gitee / GitHub 入口、固定版本 SHA-256、新空文件夹解压、首次 `local-data` 迁移条件、资料核对及异常恢复。手动计算哈希不等于 ECDSA 验签；未修改任何程序、版本号、发布 ZIP、签名清单、公钥或安全回退流程。

介绍页同步情况和本轮验证范围见 [介绍页维护记录](WEBSITE-GITHUB-PAGES.md)。以下保留 2026-10-16 及更早的历史记录。

## 2026-10-16：正式发布记录

**当前发布：行商助手 v0.5.3.1（国内下载与更新走 Gitee）**。用户反馈国内用 ghfast.top 仍下不动、介绍页也常打不开，只接受免费方案，并指定版本号 0.5.3.1、撤下 v0.5.4（v0.5.4 附件 GitHub 计数 3 次，多半是镜像测速产生；装过 0.5.4 的人收不到 0.5.3.1 的提示，需要手动重装）。

- 国内源：Gitee 仓库 https://gitee.com/scape0goat/aqtw-listener （公开，经接口 PATCH 改为公开；首页显示 README）。发行版附件和 `raw/master/latest.json` 未登录可读，附件跳转到国内 CDN `foruda.gitee.com`。免费附件单个 100 MB、仓库合计 1 GB，`scripts/publish-gitee.ps1` 只保留最近 3 个发行版的附件。Gitee Pages 已永久下线；123 云盘游客下载已收费、开放接口 20 元/月，未采用。
- 更新器：`Updater.Latest` 并行读取 Gitee 与 `https://affs99887.github.io/aqtw-listener/update/latest.json`，取签名有效且版本最新的一份，都不可用才查 GitHub Releases API。`latest.json` 用 ECDSA P-256 签名（`.sig` 为 Base64 的 P1363 签名），公钥是 `Updater.ManifestPublicKey`，私钥在 `%USERPROFILE%\.aqtw-release\update-signing-key.pem`（仅当前用户可读，提醒用户备份）。安装包按清单顺序 Gitee → 介绍页站内 → GitHub 下载，30 秒无数据换源，哈希须与清单一致。ghfast/gh-proxy 已去掉。
- 发版工具：`scripts/update-manifest.cs`（`dotnet run` 单文件；keygen / write / verify，write 固定 LF 换行，因为签名覆盖原始字节）、`scripts/save-gitee-token.ps1`（用户运行，DPAPI 加密保存令牌到 `%USERPROFILE%\.aqtw-release\gitee-token.txt`）、`scripts/publish-gitee.ps1`（建仓库与发行版、上传附件并未登录核对大小、上传 README 与签名清单并逐字节回读、清理旧附件）。PowerShell 脚本带 UTF-8 BOM，否则 5.1 会把中文按 GBK 读错；此前 `check-introduction-release.ps1` 因此对 README 的“来源/鸣谢/致谢”检查无效，已修正。
- 发布：标签 v0.5.3.1 对应 `7f9b6b1b4e081e3ff29f774f83c773ec551a4b4a`，Release：https://github.com/affs99887/aqtw-listener/releases/tag/v0.5.3.1 ，ZIP 82,714,834 字节，SHA-256 `9b988d3ae2656c41f98f0fc06a45e20e3134bbd563688b440d431698bdd6eaea`，与 GitHub 附件和 Gitee 附件一致。介绍页 `gh-pages` 提交 `dfc62af`（在 `4c74b9d` 上补充备用页说明）。备用介绍页 https://aqtw-listener.pages.dev/ （Cloudflare Pages，部署 `a1d14fbf`），页面与 GitHub Pages 相同，安装包因 25 MiB 上限未放上去。核心 44/44、应用 72/72、完整 `--ui-smoke` 通过；`AQTW_NETWORK_TESTS=1` 的线上演练从 Gitee 读到签名清单，下载、校验并解包 v0.5.3.1。现网检查通过：3 个 Gitee 按钮、2 个 GitHub 按钮，Gitee 与站内安装包大小一致，两份签名清单有效。portable 已同步为 `0.5.3.1+7f9b6b1`，旧程序在 `.tools/rollback/portable-20261016-before-v0.5.3.1`。
- 已装 v0.5.3 的用户：旧更新器只查 GitHub，国内这一次需要从 Gitee 手动下载；之后才走国内源。

**已撤下：v0.5.4（国内下载与更小的安装包）**。用户反馈官网下载慢，判断为国内访问 GitHub 慢。更新器按 `https://ghfast.top/`、`https://gh-proxy.com/`、GitHub 原地址的顺序下载，SHA256 仍取发布里的 `SHA256SUMS.txt`，校验失败换下一条。介绍页导航、首屏和“开始使用”的主按钮走 ghfast.top，另留两个“从 GitHub 下载”。发布包不再包含 `docs` 里的设计稿和截图，只留诊断页打开的 `COVERAGE.md` 与 `TEST-REPORT.md`，zip 从 129,370,239 字节降到 82,712,927 字节。其改动已并入 v0.5.3.1，发布说明文件改写为 [v0.5.3.1](../releases/v0.5.3.1.md)，GitHub Release 与标签已删除。原标签 v0.5.4 对应 `9868dd2efbc732809c75acc7c71797d0d1c89ba7`，Release：https://github.com/affs99887/aqtw-listener/releases/tag/v0.5.4 ，ZIP SHA-256 `e9e9b2acd462d6671d86f463511248239ec764e56aad92226eeb20e687a8fe96`，与 GitHub 附件摘要一致。介绍页 `gh-pages` 提交 `9cd50acb99d912607884f9e8e3e58f4da13b4e7a`，现网检查为 2 个 GitHub 按钮、3 个镜像按钮。用户关闭助手后 portable 已同步为 `0.5.4+9868dd2`，`local-data` 保留，旧程序在 `.tools/rollback/portable-20261016-before-v0.5.4`。已安装的 v0.5.3 这一次“立即更新”仍直接访问 GitHub，从 v0.5.4 起才走镜像。

**此前发布：行商助手 v0.5.3（展示物品、共享数据目录与更新器）**。用户要求提交并发布，同步在线介绍页；先按 0.6.0 发布后，用户指定版本号为 0.5.3，Release 标题格式为“v0.5.3 · 展示物品与一键更新”（不加“行商助手”前缀）。v0.6.0 的 Release 与标签已撤下（附件下载量为 0），代码与 v0.5.3 相同，仅版本号不同。发布说明见 [v0.5.3](../releases/v0.5.3.md)，便携附件 `XingshangAssistant-v0.5.3-win-x64.zip` 与 `SHA256SUMS.txt`，构建目录 `.tools/release/v0.5.3/package`。标签 v0.5.3 对应 `2bc7c1f4b734b7fef650e4d4b4070e736d482d15`，Release：https://github.com/affs99887/aqtw-listener/releases/tag/v0.5.3 ，ZIP SHA-256 `27b9a6c28b5448e99e2ad130fb4e8b4695d9010e387567267c52cebcd128be83`（129,370,239 字节），GitHub 返回的附件摘要一致。介绍页 `gh-pages` 提交 `301d202713d04aa2516786fc3ac80e807afd4a80`，现网检查通过。portable 已在助手未运行时同步为 `0.5.3+2bc7c1f`，`local-data` 保留，覆盖前旧程序在 `.tools/rollback/portable-20261016-before-v0.5.3`。以下为本轮实现记录。用户反馈候选太多，要求可预先勾选浮窗显示的物品；配置按声音分类、区分红金紫，默认全选；配置与全部用户数据跨版本通用；并加入更新器。用户选择：大红概率仍按全部候选计算并注明未显示件数；设置、展示配置、个人库、录音全部共享；启动自动检查、一键下载校验替换并重启。

- 数据：`UserData`（`src/Listener.App/UserData.cs`）根目录 `%LOCALAPPDATA%\AqtwListener`，`AQTW_DATA_DIR` 可覆盖；`--ui-smoke` / `--performance-smoke` 使用程序目录 `check-data` 隔离目录，应用测试启动即隔离到临时目录。首次正常启动时若共享目录为空，复制（不移动）程序目录 `local-data`，写 `migrated.json`。`Settings.FilePath`、个人库、诊断导出都已改到共享目录。
- 展示配置：`Listener.Core.DisplayFilter` 存隐藏 ID（新物品默认显示），`DisplayPreferences` 读写 `display.json` 并在变化时通知；`CandidatePanel` 只为勾选物品出卡片，`cellStats` 保留每格全部候选用于大红概率与“另有 N 件未显示”，整格隐藏仍保留分区。物品图鉴不过滤。
- 声音分类：`Listener.Core.SoundClasses` 合并共用物品或参考录音的拾取组（23 类，花瓶与变异体样本因共用视频参考合并），类内 红→金→紫→蓝、格数大优先；类名取自“…类”组名，否则首件物品名。
- 页面：`DisplayItemsPage`（侧栏“展示物品”）——汇总、按稀有度切换、三个预设、搜索、每类三态勾选与各稀有度计数；类内先按格数分区（整格切换，“N 件 · 显示 M”），格内按 红→金→紫→蓝 分行（整行切换），与浮窗分区一致；即时保存。物品图片统一由 `ItemPictures` 绘制（含透明图稀有度底色）。
- 更新器：`Updater` 读 `releases/latest`，找 `*-win-x64.zip` 与 `SHA256SUMS*`，校验后解包到 `%TEMP%\AqtwListener-update`，写 UTF-8 BOM 的 `install.ps1`：等待进程退出、备份程序文件（不含 local-data）、整体替换 `library`、robocopy 覆盖、失败回滚并启动旧版、成功启动新版，日志 `update.log` 在共享目录。主窗口顶部横幅（立即更新 / 更新说明 / 跳过此版本），“诊断”页有版本、自动检查开关、手动检查与数据目录。程序文件夹不可写时打开下载页。
- 验证：应用测试 71 项，含联网演练（`AQTW_NETWORK_TESTS=1`：取 v0.5.2.1、下载 132 MB、校验、解包通过）与安装脚本端到端（`AQTW_WINDOW_TESTS=1`：成功替换音效库并保留 local-data；复制失败时回滚旧版）；核心测试通过；用户允许后完整 `--ui-smoke` 通过，含展示物品页，未写入用户数据目录。离屏渲染 `display-items-page*.png`、`display-filter-overlay.png`。版本号改为 0.5.3 后重跑：核心 44/44、应用 71/71（含安装脚本端到端）、完整 `--ui-smoke` 通过，记录 `docs/measurements/release-v0.5.3-*.json`。介绍页三个下载按钮、首屏版本、页脚更新记录指向 v0.5.3，新增“候选太多，看不过来怎么办？”，“更新软件时，个人资料怎么办？”改为一键更新与共享数据目录；演示数据按 0.5.3 重新生成。`scripts/check-introduction-release.ps1` 的网页请求加 `-UseBasicParsing`，Windows PowerShell 5.1 不再因 IE 解析引擎报空引用。

以下为 2026-10-15 的状态记录。

**发布后同步介绍页与精简 README**：用户明确要求每次发版都同步介绍页，至少更新下载链接；README 大幅精简且不得包含来源或鸣谢。要求已写入根目录 `AGENTS.md`，现网版本与下载一致性检查为 `scripts/check-introduction-release.ps1`。README 从约 9,373 字符缩为 821 字符，仅保留下载、使用、快捷键、更新和反馈。

介绍页本轮同步“行商助手”名称、透明头盔图标、v0.5.2.1 版本、托盘与自由定位说明，以及三个直达 `XingshangAssistant-v0.5.2.1-win-x64.zip` 的下载入口。`gh-pages` 发布提交为 `54ddda27665a7fda00f4f4f138c922dbe28f2868`，维护位置和验证方法见 [介绍页维护记录](WEBSITE-GITHUB-PAGES.md)。这属于网站与文档同步，程序 Release 和 v0.5.2.1 标签继续对应 `9f254174d0d1548c9225cc5cabf701d5c175594b`。

**当前发布：行商助手 v0.5.2.1**。用户在图标清晰度、关闭到托盘与自由定位修正完成后，明确要求按 0.5.2.1 重新打包并发布。发布说明以 [v0.5.2.1](../releases/v0.5.2.1.md) 为准；发布构建目录为 `.tools/release/v0.5.2.1-xingshang-20261015/package`，便携附件为 `artifacts/XingshangAssistant-v0.5.2.1-win-x64.zip`，GitHub 目标为 https://github.com/affs99887/aqtw-listener/releases/tag/v0.5.2.1 。版本标签 v0.5.2.1 用于本轮发布提交，原拾点版本的提交保留在 Git 历史；先前的撤回与本地版记录均为历史。保留 portable/local-data，不关闭正常运行实例。

本轮核心回归 44/44、应用回归 63/63，记录为 `docs/measurements/release-v0.5.2.1-xingshang-{core,app}-20261015.json`。最终发布提交构建后使用隔离目录运行窗口检查，完整复核记录和发布包校验保存在 `artifacts/release-v0.5.2.1-xingshang-validation.json`；公开附件包含便携 ZIP 与 SHA256SUMS.txt。

以下为本轮发布内容的实现与预览检查记录。

**补充：图标清晰度**。用户反馈放大后的图标发糊；源 PNG 为 1254×1254，旧界面直接用默认缩放显示。主窗口标题栏、72 侧栏和监听浮窗现通过 `BrandAssets.CreateMark` 按当前显示器物理像素尺寸加载，采用高质量缩放，加载和 DPI 变化时刷新，并按像素尺寸缓存；原图哈希及 ICO 保持不变。100 / 125 / 150 / 200% 的 36 个后台渲染用于比较缩放方式，不等同于真实跨屏拖动实测。候选对照网页默认比例由 75% 改为 100%，保留缩小查看选项。

上一轮本地成品 `.tools/release/xingshang-sharpness-20261015/package`，隔离检查目录同级 `smoke`；完整窗口与托盘退出检查通过，记录 `docs/measurements/xingshang-sharpness-*-20261015.json`。当前品牌预览截图已更新。此前仅同步 portable 与本地 ZIP；本轮重新发布安排见上文。

**行商助手 · IND70 头盔图标与窗口行为修复**。按选定的 D 版透明成品更新 PNG 和 ICO；PNG SHA-256 为 `F9B45B0D14A33652813152F71C10B3EAFFB4EC46DF700A38C2F9B623E17D771D`，未重绘，镜片暗部保留。主窗口、浮窗、托盘和产品元数据统一为“行商助手”，便携启动文件为 `portable/行商助手.exe`，内部程序集与数据路径保持兼容。侧栏图标由 47 放大到 72 逻辑像素。版本号保持 0.5.2.1；此前 GitHub Release 曾按用户要求删除，旧标签在本轮重新发布时更新到当前代码。

右上角关闭与普通窗口关闭恢复为隐藏到托盘，监听状态和资源保留；托盘菜单“退出”才释放资源并结束程序。自定义浮窗位置不再限制坐标或拖回屏幕内，允许负的显示器相对偏移；靠近底边不会压缩高度，托盘“恢复顶部居中”可找回浮窗。历史等内容窗口尺寸变化不再强制回收位置。

验证：应用回归 63/63、完整桌面 `--ui-smoke` 通过；关闭/恢复/最小化/托盘退出、浮窗四边越界后定时定位和设置重载、主窗口越界以及历史窗口尺寸变化的定位检查均通过。记录见 `docs/measurements/xingshang-window-*-20261015.json`。图标、九档 ICO 尺寸和更新后的实际窗口截图见 [当前品牌预览](../design/brand/ind70-2026-10-15/index.html)。本地候选对照页 `artifacts/candidate-width-study/preview.html` 使用新名称和头盔图标，保留 63 组、23 类、150 件物品预览。桌面检查不等于游戏内独占全屏验证。

**介绍网站交接（历史）**：原拾点介绍页采用独立 `gh-pages` 分支部署。之后已按用户要求同步为行商助手（现为 v0.5.3），下载链接和维护方法以上文与 [维护记录](WEBSITE-GITHUB-PAGES.md) 为准。

以下为此前的发布与设计记录。

**拾点 v0.5.2.1 · 软件更名（历史，Release 已撤下）**：用户要求将软件更名为“拾点”，改好后打包并发布 0.5.2.1。主窗口、侧栏、托盘、监听浮窗、提示与 Windows 产品元数据使用 BrandAssets.AppName“拾点”；便携启动文件为 拾点.exe。内部托管程序集继续使用 AqtwListener，以保持资源与数据兼容；单实例互斥标识保持兼容，查找已有窗口同时支持新旧进程名。设置、录音及个人库继续位于 portable/local-data。

发布说明：docs/releases/v0.5.2.1.md；构建目录 .tools/release/v0.5.2.1；压缩包 artifacts/Shidian-v0.5.2.1-win-x64.zip；目标 Release：https://github.com/affs99887/aqtw-listener/releases/tag/v0.5.2.1。界面与产品属性检查使用更名后的启动文件运行。portable 同步更新；旧启动文件转存 .tools/rollback/v0.5.2.1，保留用户数据。

**v0.5.2 · 发布版本（最新）**：候选最小宽度为格数栏加两张卡片，每行最多四件，名称在各卡片入口均显式左对齐；排满行右侧仅 3 逻辑像素边框余量。中字号、880 候选面板高度预算下：1 件 576×135，6 件两列 / 582×325，13 件三列 / 740×552，31 件四列 / 824×732（不含标题栏）。小试听按钮、两行长名称、88 / 80 / 56 图片与分组概率保持；空间不足可在操作模式滚动查看。包含首次启动快捷键恢复与拾点新图标，说明见 docs/releases/v0.5.2.md。

后台应用回归 63/63、24 个无窗口渲染组合通过，记录见 docs/measurements/candidate-two-card-left-*-20261014.json；截图见 docs/screenshots/two-card-left-*-20261014.png。对照网页 artifacts/candidate-width-study/preview.html 已更新，默认显示最新左对齐与两件最小宽度；历史两版明确标为历史，12 个版本与 36 个缩放切换检查通过。本轮未做新版完整窗口或游戏内覆盖验证。

用户已授权打包 0.5.2、同步 portable、提交并发布 GitHub Release。构建目录 .tools/release/v0.5.2；压缩包 artifacts/AqtwListener-v0.5.2-win-x64.zip，不包含个人 local-data。按发布提交构建成品并同步 portable，旧文件保存在 .tools/rollback/v0.5.2。发布目标：https://github.com/affs99887/aqtw-listener/releases/tag/v0.5.2；成品与发布的核验记录保存在 artifacts/release-v0.5.2-validation.json。打包与发布操作不启动、关闭或重启助手。

**测试约束**：用户游戏期间不要弹测试窗口或关闭、重启其正常助手。用户随后明确表示已经不在游戏中，允许窗口检查；v0.5.2 完整桌面检查已通过，记录在 artifacts/candidate-group-gallery/ui-smoke-v0.5.2.json，完整音效组预览已更新为 63 个组、23 个合并类。本次更名继续使用独立成品目录验证，不关闭正常助手。桌面检查不等同于游戏内独占全屏验证。

**本地试用 · 拾点 Logo**：用户选择极简方案 04。`Assets/listener-mark.svg` 是透明浅色界面标志，`listener-app.svg` 是深底系统图标；`scripts/export-icon.ps1` 逐尺寸渲染 PNG 与九帧 ICO，`draw-logo.py` 仅保留兼容入口。主窗口、浮窗使用 `BrandAssets.Mark`，窗口与任务栏使用 `WindowIcon`，托盘使用 ICO。预览见 `docs/design/brand/shidian-2026-10-14/index.html`。此项是 v0.5.1 之后的本地修改。

便携版已更新，`--ui-smoke` 通过；记录见 `docs/measurements/shidian-logo-ui-smoke.json`。预览中的主窗口与浮窗来自程序的模拟界面检查。

**v0.5.1**（已发布 GitHub Release）：`ItemDefinition.Rarity`（红/金/紫/蓝，红 ⇔ IsGold，`Validate` 检查）与 `RarityRank`；四处候选排序改为格数内按稀有度。150 件的稀有度：情报站资料 138 件，其余 12 件按目录大红与截图底色（`provenance.json` 的 `rarity`，`merge-aqintel-library.py` 的 `RARITY_FROM_TILES`）。新图标由 `scripts/draw-logo.py` 按尺寸矢量绘制（物品格 + 声波、正中大红），生成 `listener-mark.png` 与 `listener.ico`，预览 `docs/design/brand/listener-mark-preview.png`；旧的 `export-icon.ps1` 不再需要。

以下为 v0.5.0 时的状态记录。

当前版本为 **v0.5.0（合并暗区情报站素材库）**，已合入 `main`、打标签 `v0.5.0` 并发布为 GitHub Release（附件 `AqtwListener-v0.5.0-win-x64.zip`），`portable` 已更新。用户提供 `暗区情报站小助手-Setup-1.4.0.exe`（静态解包结果在 `artifacts/暗区情报站-1.4.0-素材库`，ChatGPT 交接在 `artifacts/material-merge-handoff-20261013`，均不入库）。研究脚本在 `.tools/research/merge-aqintel`（gitignored）。报告见 [素材合并报告](../MATERIAL-MERGE-AQINTEL-20261014.md)。

- 合并：`scripts/merge-aqintel-library.py`（只能在合并前的库上运行，`provenance.json` 已有 `aqintel140` 段时退出）按名称与 7 组图片确认的别名对应物品，按 `baseline-audio-probe.json` 把对方 17 个声音归入本库声音类；新物品 ID `aqintel140-<对方ID>`，加入该类所有拿起组与放下声组（只加新物品，不把类内已有物品互相加入，否则候选去重会合并组）。声音 11/14/16 新建组 `aqintel140-sound-NN`（门限 0.82），声音 06 加到宝石类；四段 WAV 经 `trim_refs.py` 裁到起振帧后用 CLI `add-reference` 入库。库版本 `0.5.0-aqintel-merge-20261014`，150 件、65 组、95 条样本。
- 试听：`PlaybackClip.Processing`（可空）标记非原始录音；`fix_playback.py` 只给没有 SoundRadar 原始录音的组保留对方 WAV（声音 10/11/12/14/15/16）。`OverlayWorkspace` 把 `Processing` 传给 `CandidatePanel.SetAudioPreview` 的第三个参数，提示与播放状态据此写明。
- 布局：`CandidatePanel.LargeClass`（20）到 `PackedCandidates`（48）之间按格数分区、每行最多 6 张、名称单行；`CandidateLayout` 新增可选 fallback，分区布局超高时改用 `BuildDenseDesignGroups`。`DenseDesignCard` 的「听样本」固定在第三列：`PlayWidth` 一行 1 件 96、2 件 76、大类 60、3 件以上 42（`StackedPlay` 两行文字）、密集 26；`prominent` 改为 `rowSize > 0`（密集卡片传 0）。名称宽度按 `NameWidth`（两行时取最窄切分）选列数，非大类最小 13 号字、可两行；文字列放不下「差异率 10.0%」时移到缩略图底边。`Card()`（图鉴、离线结果）按钮在格数行右端，差异率放到缩略图底边。冒烟新增 `PlayButtonsRight`、`tileButtonsRight`，全候选场景改为最大声音类（31 件）。
- 验证：`raids.py` 解码 26 场录像为 500 s 分段并以两库扫描，`raid_diff.py`/`raid_clusters.py` 对比；`provenance.py`/`fir_provenance.py`/`impact_probe.py` 复核对方音频；`frames_sheet.py`/`frames_zoom.py` 取画面。结果见报告第 3、4 节。
- 格数：胶囊电视 2×3、花瓶 2×2 按本库缩略图底色与对方资料更正（`GRID_CORRECTIONS`、`provenance.json` 的 `aqintel140.gridCorrected`）；燃料桶保留 3×2。冒烟 `ImagesKeepAspect` 检查所有物品图 `Stretch.Uniform` 且显示比例与位图一致，含全部 150 张图鉴图。
- 底色：`ItemDefinition.ThumbnailBackground`（#RRGGBB，可空）只给透明底的情报站图；`CandidatePanel.ItemPicture` 用 `Viewbox` 画按格数比例的底色块再放图，三种卡片共用。颜色为本库同稀有度截图底色中位数（红 #321612、金 #32220E、紫 #221A2F，`RARITY_BACKGROUND`）。新图只整体等比缩小，不裁剪（用户要求不改图，只加底色）。冒烟 `backgroundsShown` 检查 88 件底色与格数比例，应用测试检查大红物品必为红底。
- 紧凑卡片：`DesignCardWidth` 按图片、最长名称（8 字以内单行）、「差异率 10.0%」和按钮列算出全结果统一的卡片宽，`BuildSizeSection` 按宽度放满整列、不拉伸；`GetPreferredWidth` 取约 720×字号高度内放得下且宽 ≥ 1.2×高的最少列数。`VisibleArea` 在显示时按透明图的不透明区域裁出 `CroppedBitmap`（文件不变）。冒烟 `cardsCompact`（等宽、最紧卡片名称到按钮 ≤ 20）与 `widestRowRightGap` 取代原 `bottomRowRightGap`。
- 待办：新物品格数与两件更正的实机核对；显卡（1×2）的原图是横向的，放在竖条底色里偏小，未旋转；电脑硬件类模板在非目标声音上偏高（0.78–0.84 共 137 次），若后续出现误报，先把该组门限提到 0.90。

以下为 v0.4.5 时的状态记录。

当前版本为 **v0.4.5（多锚点对齐与多变体参考）**，发布为 GitHub Release。用户提供一段 B 站听声教学视频（`tests/9d769840a1f45598d0a5ab3cb125a96e.mp4`，未入库，gitignored；仅含拾取音效、无杂音、9 类 42 次播放）并反馈"仓库里也识别不到"。研究脚本在 `.tools/research/pickup10`（gitignored）：`features.py` 是 C# 特征的 Python 移植，`bench.py`/`sweep.py` 用 `dataset.json`（视频 42 + 9 月 24 日行商 45 + 9 月 22 日行商 10 个正样本，行商空闲 158 个负样本）跑特征对照。

- 根因一：界面点击先于物品声 0.10–0.16 s，扫描器以点击为起点，`InMatchFeatures.SlideAfter`（24 帧 = 128 ms，减去 Lead 后有效 117 ms）够不到物品声。改动：`InMatchFeatures.QueryAnchors` 返回起点加起点后 `AnchorSpanFrames`（42 帧）内每个 ≥6 dB 的高频突起（`RiseDb`，取 1 帧或 2 帧差），`ScoreAudio` 对每个锚点各自 `Subtracted` 并滑动（起点 −6..+24，突起锚点 −4..+8），取最高；`AutomaticAudioScanner.TailSeconds` 与 `Focus(after)` 0.36 → 0.45。视频 42 次播放 29 → 42。
- 根因二：游戏对同一类声音随机使用几段录音（同一物品两次拿起相似度 0.72–0.75；音高/时长实验排除）。新增 `LibraryReferences.Add` 与 CLI `add-reference 库目录 音频.wav 录制编号 来源说明 组ID[,…]`（写 `library.json` 模板+特征、`references.json` 样本、重算 `engineIndexSha256`，一个文件可挂多个组）。入库 4 条：`audio/guide-high-class.wav`（视频 23.69 s，高概率类 11 组）、`audio/raid-high-class.wav`（9 月 24 日对局 1532.83 s，7 次对局播放的中位样本，同 11 组）、`audio/guide-vase-class.wav`（花瓶、变异体样本）、`audio/guide-jewel-class.wav`（宝石项链、卡莫纳之星、钻戒——库内三者参考相似度 0.998）。切参考时必须从物品声起振帧开始（与随包参考同一约定），若把前面的点击切进去会得到"点击模板"，能匹配一切带点击的拾取。provenance `additionalReferences` 记录来源与偏移。库版本 `0.4.5-variants-20260925`。
- 未采用（实验无收益）：3/5 帧时间平滑、频带平滑、2–3 倍时间降采样、信噪比加权余弦；燃料桶与镜头类的对局播放不聚簇，未收对局参考。
- 控制器：已接受拿起后 `RecognitionHistory.FollowUpMaxSeconds` 内的未匹配结果不进入 `pendingFailure`（两段式物品声的第二段）。主窗口参考数按模板 `AudioSha256` 去重。
- 验证：视频 42/42、播放之外 0 误匹配；9 月 24 日行商 45 次拖动 39 → 40，行商空闲 154 事件误报 0，整局匹配 75 → 82（新增为背包操作，画面确认）；核心 44/44、应用 54/54、`--ui-smoke` 通过。剩余未识别：库外或被盖住的低分（0.57–0.59）、燃料桶 0.743、干花书签 0.788（自身 0.90 门限）、0.17 s 极短拖动 0.796。
- 已知弱点：特征对 1 帧错位敏感（最佳 0.885 → 偏 1 帧 0.49），靶向补变体比改特征有效；其余类仍只有一条参考，遇到新变体需继续用 `add-reference` 补。

以下为 v0.4.4 时的状态记录。

v0.4.4（放下声不识别、拿起识别率）发布为 GitHub Release。用户在仓库用 v0.4.4 之前的便携版实测：放下胶囊电视、琥珀天心几乎每次触发一次识别；拿起这两件偶尔没有反应。9 月 24 日晚四场录像（`.tools/research/putdown-skip`，gitignored）里 33 分钟一局含两次行商访问，`trader_gt.py` 用行商界面中被拿起物品的红色高亮得到 45 次真实拖动的拿起/放下时刻（`docs/measurements/trader-drags-20260924-v0.4.4.json`）：

- 放下声：`peer-047`、`peer-051` 改为 `action: "putdown"`（库版本 `0.4.4-putdown-veto-20260924`），`InMatchRecognizer` 为放下声组也建模板，`Putdown()` 在放下声组过自身门限且高于所有拿起组时返回该组，`RecognitionAnalysis.Putdown` 非空即为放下声，`CandidateSelection` 仍只取拿起组。`ListeningController.PollAutomaticRecognition` 三层跳过：`MouseEdgeLog.Latest` 归到松开边沿的声音不分析（起点加 `AudioCaptureHealth.TimelineLagSeconds` 对齐到达时钟）；`PickupAnalysis.IsPutdown` 的结果不发布；无鼠标信号时 `RecognitionHistory.WouldHold`（拿起后 0.15–1.0 s 的不同候选）的结果不发布。同一次按下（`publishedPress`）内的第二段声音也走 `WouldHold`。三种都只计入 `SkippedPutdowns` 与 `RecentSounds`。录像中 13 次放下声组事件全部判为放下；行商 45 次放下 0 次产生匹配（行商界面放下几乎无声）。
- 拿起：`AutomaticAudioScanner.RefractorySeconds` 0.25 → 0.12（行商界面音先于物品声 0.12–0.19 s）；`PickupRecognition` 新增 `ConfidentScore` 0.80 + `ConfidentSeparation` 0.20（`RecognitionAnalysis.Separation` = 候选最高分 − 未入候选的最高拿起组分）；失败结果经 `pendingFailure` 延迟 `FailureDelaySeconds` 0.4 s 显示，期间匹配则取消，显示时附「最接近 X 差异率 Y%」。行商拖动识别 36/45 → 39/45，整局匹配 71 → 75 且无移除。剩余未识别：3 次被更响声音盖住或最高分仅 0.43–0.60，3 次接近门限（0.74、0.79、0.80）。
- 诊断：主窗口诊断页显示「放下声不识别 N 次」「音频时间偏差」（WASAPI 时间戳与到达时间之差的 2 秒最小值）与最近 5 段声音；导出诊断含 `RecentSounds`、`RecentInput`。
- 验证：核心测试 43/43，应用测试 54/54，`--ui-smoke` 通过，记录为 `docs/measurements/release-v0.4.4-*.json`。鼠标松开判定与时钟对齐未在游戏内实测。
- 未做：其余类的放下声没有参考，仍靠鼠标或 1 秒时序规则；被盖住的拿起声、接近门限的低分匹配没有进一步处理。

以下为 v0.4.3 时的状态记录。

v0.4.3（放下声更正、逐件差异率与原声试听）已合入 `main` 并打标签 `v0.4.3`。便携包 `AqtwListener-v0.4.3-win-x64.zip` 在 Linux 上交叉构建（自包含 win-x64，与 `publish.ps1` 相同的参数；PerMonitorV2 清单、图标和版本资源已嵌入，与 v0.4.2 官方包一致），解压后在 Wine 中通过界面检查与命令行识别。起因是用户反馈仓库中拖动胶囊电视多次报「红外线理疗灯 97%」、胶囊电视的「听样本」不像胶囊电视；用户确认原因是放下声被识别成红外线理疗灯，且样本声音经过处理。调查与数据见 [放下声更正](../PUTDOWN-RELABEL-20260924.md)：

- 库数据：`peer-047`（原标红外线理疗灯）、`peer-051`（原标天线）经 SoundRadar 原始放下录音核实为琥珀天心类、定位组的放下/转移声（0.996、0.968），改为关联整类 13／9 件；红外线理疗灯并入 `peer-016`，天线并入 `peer-030`。库版本 `0.4.3-putdown-relabel-20260924`，`references.json` 与门限未变。
- 识别逻辑：`MouseEdgeLog` 把声音起点归到其前 0.35 s（容差 0.06 s）最近的鼠标按下/松开；`RecognitionHistory.Remember` 新增 `SoundPhase`：按住至少 `MouseEdgeLog.MinDragSeconds`（0.15 s）后松开的放下声由 `ListeningController` 直接跳过，不分析、不存片段、不记历史，只计入诊断的「跳过放下声」；更短按压后松开的放下声仍分析并总是扣留（`FollowUp`，历史标「放下声」），按下后的拿起声从不扣留，无鼠标信号时沿用 1 秒时序规则；候选物品相同的放下声不替换拿起结果。`NativeInput` 的钩子与按键检测都会报告松开。
- 界面：卡片逐件显示「差异率」（`CandidatePanel.DifferenceText`，100% − 相似度，一位小数），汇总行不再显示匹配度；密集卡片把差异率放在缩略图底边。
- 试听：`PlaybackAudio` + `data/library/playback.json`，17 段 SoundRadar 原始录音（`scripts/import-soundradar-playback.py` 逐字节导入）覆盖 50 件；识别参考不再用于试听。个人库建库时复制基础原声，并把学习样本的原始录音（`sourceFile`）登记为试听片段；旧个人库版本回退到基础库原声。其余 12 件没有原声，按钮禁用。
- 验证：核心测试 41/41，应用测试 52/52，`--ui-smoke` 布局全部通过，记录为 `docs/measurements/release-v0.4.3-*.json`。WPF 测试在 Linux 的 Wine 9.0 + Xvfb 运行（自包含 win-x64 构建、`EnableWindowsTargeting`，Noto Sans CJK 替代雅黑，空 ALSA 设备），未在 Windows 本机和游戏内复测。
- `.gitattributes` 固定 `references.json` 以 CRLF 检出，否则 LF 检出时 `engineIndexSha256` 不符、基础库拒绝加载。

以下为 v0.4.2 时的状态记录。

v0.4.2 便携版（监听浮窗改版与游戏风格界面），发布为 GitHub Release。识别逻辑与 v0.4.0 相同，改动在界面，并包含下文的 `remove-legacy-engines`：

- 信息层级：每个格数组以最大的数字显示大红概率并按比例着色（`Theme.RedShare`）；同音候选的匹配度只在汇总行显示一次，低于最高分的卡片单独标出；汇总与操作合成一行，免责说明移入 ⓘ 提示，去掉底栏。
- 时效：实时结果显示识别时间，新结果到达时汇总行闪亮约 1 秒，超过 `CandidatePanel.StaleSeconds`（10 秒）候选变暗。
- 视觉：59 张缩略图清理了游戏截图自带的名称文字与角标（`scripts/clean-thumbnails.py`，记录在 `provenance.json` 的 `thumbnailEdit`）；去掉名称后的小圆点；两件一行时试听按钮移到名称下方，大字号下名称不再断行。
- 状态：标题栏圆点按 `StatusTone` 变色；「停止」只在播放时可用；历史把拿起后 1 秒内的其他音效标为「疑似放下声」；主窗口显示引擎与音效库版本。
- 主题：配色与控件参照用户提供的游戏内菜单截图：中性近黑面板、`#222525` 按钮底、`#353E3E` 细描边、方角、常规字重；不可用按钮去描边并下沉；右键菜单为游戏式竖排按钮（`Theme.xaml` 的 `ContextMenu`／`MenuItem`），托盘菜单同色。主按钮（开启监听、保存设置）仍为浅色实底，截图未包含游戏的确认按钮样式。
- 验证：核心测试 34/34、应用测试 51/51、`--ui-smoke` 通过，记录为 `docs/measurements/release-v0.4.2-*.json`。

以下为 v0.4.0 时的状态记录。

v0.4.0 便携版（局内识别改造）：用户反馈“仓库能识别、进入对局几乎不识别”，本轮用 `C:\Users\Dexin\Videos\aqtw-highlight\17293822637845621439\record` 下五场完整录像（9 月 20–22 日）逐事件分析后重做识别链路，素材、目录、浮窗与个人库不变：

- 根因：参考在 600 Hz 以下几乎无能量，局内混音低频最响，窗内相对地板把物品声压掉；参考静音区被背景填满；宽带能量起点检测被前置声音屏蔽。另确认此前 8 次标注中 6 次“快速转移”是双击界面音，不是拿起声。
- 改动：新增进程内 `InMatchRecognizer`（`primaryEngine: "inmatch"`，起点锚定、扣背景谱、第 16–55 Mel 带、-32 ms..+128 ms 非对称搜索）；`AutomaticAudioScanner` 改用 700 Hz 高通块能量与前 300 ms 安静块背景；`PickupRecognition` 单次分析、自动最低 0.86；`RecognitionHistory` 对拿起后 0.15–1.0 s 内的不同音效匹配只记历史不换浮窗；`library.json` 组门限 0.82（耐冲击硬胶、军用电台、干花书签、硝化棉、红外线理疗灯 0.90），`engineIndexSha256` 改绑 `references.json`，`radar-index.bin` 已从基础库移除；Go 引擎与 DTW 保留为可选。个人库新组门限随引擎（0.82）。
- 结果：便携 CLI 回放五场录像，行商 10 次拿起 10/10 产生候选（0.94–0.98，重复拖动候选一致，此前 0/10）；已确认误报 10 处中 9 处消失（剩金魔方→金杯组属目录覆盖）；真实拖动 1/2；12,930 次分析 152 次输出。核心测试 33/33、应用测试 46/46，62 条参考全部可确认。详见 [局内识别复核](../INMATCH-VALIDATION-20260924.md)、[v0.4.0 说明](../releases/v0.4.0.md)与 `docs/measurements/inmatch-v0.4.0-*.json`。
- 限制：门限在同批录像校准，无独立验证集；黎明拖动仍不匹配（可能为随机变体）；快速转移双击音会显示红外线理疗灯；激光指示模块簇在背包操作中频繁出现，疑为游戏对更多电子物品共用音效。研究脚本、48 kHz 解码音频、帧图与研究 venv 在本地 `.tools/research/inmatch-v2`（gitignored）。
- 发布：v0.4.0 已发布 GitHub Release。
- v0.4.0 之后（分支 `remove-legacy-engines`）：移除已停用的 SoundRadar Go 引擎（`src/Listener.Engine`、`RadarRecognizer`、个人库 Go 建索引流程、`data/engine` 打包、专用脚本和 Go 许可文本）与 C# DTW 识别类及 `dtw` 选项；`RecognizerFactory` 只接受 `inmatch`，旧引擎个人库版本自动回退基础库。`AudioFeatures.Extract` 仍保留，供库格式中的模板特征与个人样本质检使用。识别结果与 v0.4.0 逐事件一致；核心测试 34/34、应用测试 46/46。README 去掉版本历史段落和 v0.67 素材行，来源说明留在 `docs/THIRD-PARTY.md`。

以下为 v0.3.3 时的状态记录。

v0.3.3 便携版：用户已决定保留我们的识别逻辑，采用对方 62 条拿起素材，并彻底删除放下辅助识别。源码、基础库、试听清单和元数据已切换，版本已升到 0.3.3；本轮交付方式为本地便携包，尚未发布新的 GitHub Release。机器人 2×3、决胜奖杯 1×2、骏骊逐浪图 2×3 已由用户确认。

核心测试 28/28、应用测试 45/45、便携版 UI 检查通过；完整参考自身匹配通过，短窗口 59/62（三条首饰参考仍低于原门限）。已用新便携版复跑 8,156.6 秒，9,972 次分析与此前辅助库关闭实验一致，385 次候选输出不是准确率。当前库保留 34 个原物品 ID、28 件新增目录；没有沿用缺少对方参考的旧 17 件。新来源记录在 `data/library/provenance.json`，详情见 [v0.3.3](../releases/v0.3.3.md) 与 [成品测量](../measurements/production-peer-library-20260924.json)。旧便携版和原库备份在 `.tools/rollback/v0.3.2-before-peer-library`；用户当前 `portable/local-data` 只有设置，更新原样保留。成品位于 `portable/AqtwListener.exe`，ZIP 为 `artifacts/AqtwListener-v0.3.3-win-x64.zip`，UI 截图保存在 `.tools/release/v0.3.3/smoke`。

以下段落为此前评估阶段的历史，不再表示当前仍禁止正式换库。

侧会话补充（源任务 `01a0d117-2840-72d1-8bab-611f71bd3e97` 回传摘要；该临时任务不支持 read_thread 读取）：用户重视对方物品目录覆盖更多，认为拿起库和物品库都值得替换。后续必须分别评估物品覆盖完整度、有效候选集合输出和背景声误触发；8 次已标注操作的漏识别不能单独否定扩库价值。建议方向为目录合并去重、明确别名映射、保留已有格数和缩略图，并保留一声多物品的同音关系；参考条数不等于独立声音种类，也不能未经核对就把对方目录当作原库的完整超集。这是侧会话评估方向，本轮没有实施正式换库、合库或发布。

最新完成放下辅助库开关对照：保持 v0.3.2 逻辑与对方 62 条拿起参考不变，停用组新跑全部 8,156.6 秒，与哈希核验的启用组比较。9,972 次事件、10 份结果文件逐字节一致，候选、分数、起点和音频完成时间无差异。行商仍 4/10 匹配成功、2/10 在放下前；已标注操作仍 0/8，已核实误报不变。实录未触发放下辅助分支；5 条参考控制有 2 次仅在启用时进入该分支，证明并非加载失败。详见[开关报告](../PUTDOWN-ABLATION-20260924.md)及 `docs/measurements/putdown-ablation-20260924.json`。本地执行器在 `.tools/research/putdown-ablation-20260924/`，未修改便携版。实验的 peer 拿起组与原放下组编号不同，GUI 同组确认未验收；离线回放也不涵盖实时忙碌跳过扫描的开销。

最新完成用户要求的「我们的逻辑，换他的素材库」实验：直接运行冻结 v0.3.2 二进制，替换拿起库为对方 62 条参考，门限、特征参数和原放下辅助库不变。重新回放全部 7,546.6 秒及此前 610 秒开发素材，9,972 次分析时间与旧结果逐项一致。已标注 8 次操作仍无有效候选；完整录像匹配成功输出 23→312，原 10 处误报保留 4 处，固定抽查的 15 次新增输出均与画面操作不符。行商 10 次拿起匹配成功 4 次、2 次在放下前，同音组是否对应尚未核实。详见[换库对照](../OURS-PEER-LIBRARY-VALIDATION-20260924.md)及 `docs/measurements/ours-peer-library-20260924.json`。实验目录 `.tools/research/ours-peer-library-20260924/`；没有覆盖便携版或发布素材。注意新库为 62 件/62 组，原库为 51 件/9 组，库分组表示也随导入适配发生变化。

用户最新明确：一个声音可以对应多件同音物品，产生一组符合门限的候选即计匹配成功，不要求猜中唯一物品。后续区分“是否产生有效候选集合”与“候选所属音效组是否正确”；不能因多候选或缺少某个确切物品名直接判失败。本轮 0/8 是 5 次分析无候选、3 次未触发，不是唯一物品标准造成的；绕过自动门的固定裁剪诊断则为原库 0/8、新库 1/8，其同音对应关系未确认。

新增用户要求的对方方法对照：直接调用审阅过的 v0.67 原匹配函数及其 62 条参考，保持默认阈值，在旧报告全部标注案例附近的 187.64 秒与此前 610 秒开发素材上复跑。已知物品仍为 0/8（拖动 0/2），10 处旧误报无正式命中但 7 处仍有不确定提示。存在放下前返回候选的行商实例，货物身份未知。详见[对照报告](../PEER-METHOD-VALIDATION-20260924.md)及 `docs/measurements/peer-method-gameplay-20260924.json`。本轮不是再次回放全部 7,546.6 秒，不改生产代码、素材库或便携版。原始匹配函数执行器和音频留在 `.tools/research/peer-benchmark-20260924`，不分发对方素材。

最新已发布版本为 **v0.3.2**，提交 `8ee409e2ea2fe70bf370d515efb0bd1c3428515a`。下面的 v0.3.1 界面交付记录保留作历史；识别逻辑以 v0.3.2 源码与[发布说明](../releases/v0.3.2.md)为准：拿起优先，放下仅辅助，自动监听时鼠标事件不取消分析。

本轮按用户要求加入 9 月 20–22 日的 5 场完整录像做冻结基线复核，**没有修改便携程序或门限，也没有发布新版本**。共 7,546.6 秒，已标注物品操作 0/8 命中（拖动 0/2、快速转移 0/6）；一段行商 10 次拿起触发 7 次分析，仅 2 次在放下前完成，均未匹配。23 次接受输出中 10 次确认错误、13 次隐藏货物身份无法核实。详见[实战复核报告](../GAMEPLAY-VALIDATION-20260924.md)。不能把此前 610 秒的触发诊断或自匹配回归当成准确率验收。

原视频、解码音频、帧图和逐项回听页位于本地 `.tools/research/accuracy-20260924`，不提交；回听页为 `evidence/index.html`。可追踪标注、结果与诊断摘要在 `docs/measurements/gameplay-v0.3.2*.json`。当前基线已暴露触发与匹配问题，真实原因仍需进一步区分；不能降低门限硬接受或自动用隐藏货物补库。后续算法修改用本批失败案例做回归，并另选未参与修改的录像验收。

v0.3.1 交付时，识别音频已改为只采集 UAGame 及其子进程，独立语音软件走同一播放设备也不会进入识别；试听播放设备仍可单独选择。进程回环不可用时停止采音，不退回系统混音。第 07 张 v2 设计图用于监听浮窗的结构，三组并排仅是六件示例；当前用户要求每个格数组独占整行。当前代码接入标题栏、候选概览、四个声音操作按钮、按格数分组卡片与底栏，并提供组内“大红概率”、匹配度、逐件试听、小中大字号和单图钉亮暗切换。当前工作区另加入识别失败提示、加载动画和连续监听。色调采用近黑与蓝灰；另一款工具只参考功能与色调，不复制列表布局与文案。

用户此前要求的声音对比与个人音效库继续保留。[15:19 后需求](REQUIREMENTS-20260922-1519.md)与 [v0.1.0-original.md](v0.1.0-original.md)保留历史背景，其中的待实施、回退状态与旧产品行为不应覆盖本页当前实现。

## 当前实现

- 默认 UAGame 客户区顶部居中，16 DIP 顶边距；7–20 件候选优先向下排布，宽度约 830–910 DIP，每个格数组独占整行。组标题直接写“2 格”等，并显示该格“大红概率”与大红件数；顶部不再显示跨格总百分比或进度条。每行 1／2 件使用横向信息卡和固定宽度试听按钮，3／4 件为等宽紧凑卡；5 件以上均衡分行。极端更多候选才按需加宽，最多为游戏画面的 94% 或 1600 DIP。全部候选一页显示，不分页，也不使用滚轮；监听结果缩略图下限为 80 DIP。主窗口六个设置分区，无预览。
- Ctrl+Alt+L 开启持续采音与自动识别；等待与结果共用唯一的监听浮窗样式，包含标题、候选概览、回放／保存工具栏、完整候选卡片和底栏。实时监听时控件可点击但不主动抢走游戏焦点；点击回放或逐件试听自动暂停采音，返回监听后继续自动识别。Ctrl+Alt+C 仍可主动进入／退出操作状态。顶部有小／中／大字号按钮、同一枚亮／暗图钉；灰暗的解锁图钉仍可点击。
- 每件候选突出完整名称与音效组匹配度并提供参考试听按钮，右键可选同组的其他参考录音，无可用样本时禁用并说明原因。共享音效的候选显示相同分数，不额外标记「同音候选」；低分仍可标「疑似」，匹配度不是单件确定率。已移除「展开相近音效」开关。历史的最近 100 条记录使用连续列表，更新记录时保留选中项。
- 主窗口、标题栏和托盘统一使用用户提供的透明图标 `src/Listener.App/Assets/listener-mark.png`；多尺寸 `listener.ico` 由该原图导出。
- 独立加载状态与结果保留；自动扫描超过 150ms 显示加载动画，未匹配时醒目显示「识别失败」。声音事件门排除持续声音和重叠窗口，自动识别对完整窗口与事件附近窗口交叉复核；单件高分且有区分度标为「精确识别」，共享音效的高分物品不显示额外标签，较低分标为「疑似」。快照绑定原音频、时间、评分、结果与库版本；最近 20 片段、100 条成功历史、32 MiB 内存上限。回放、参考切换、保存 WAV、历史对比，退出操作停止播放并完整等待 300ms。
- 个人库与基础库分离；手动补样本、WAV 导入裁剪、新物品录入、图片和参考价可选。默认独立新组 0.75，原阈值不变，同音冲突须用户确认。物品/样本管理、导入导出和版本撤销已接入。
- 引导学习先选物品，三参考一单独试识别，每轮 3s 准备和 1.2s 采音。零鼠标可运行；静音、过短、削波、重复、重叠窗口拒绝，切出暂停，取消保存草稿。留出不入索引，参考回归与试识别通过才自动启用。普通匹配不成为标签。
- 构建整库新版本、核对索引、哈希、参考回归后切换；失败维持原库，损坏时回退。普通音频不自动落盘；明确学习的样本、原录音与草稿保存在 local-data/personal，发布包排除 local-data。

## 代码入口

- App：OverlayWindow（位置、字号、单图钉交互）、OverlayWorkspace（对比/学习/个人库页面）、Presentation/CandidateLayout（全部候选、宽度和密度自适应）、HistoryWindow（连续历史列表）、ListeningController（活动与模式）、GuidedLearning、AudioPreviewPlayer。
- Core：AnalysisSnapshot/SessionAudioStore、ReferenceAudio、PersonalLibraryStore；两个 Recognizer 的 Analyze 同时返回正式结果和全库评分。
- scripts/build-playback-references.py 根据现有模板逐条验证上游来源，再整理 14 段试听/重建 WAV。没有修改 Go DSP；继续使用原 engine 二进制。
- tests/Listener.App.Tests/PersonalLibraryTests.cs 覆盖真实引擎构建、同音确认、留出失败、版本与资产恢复。测试录音为工程派生或合成素材，不是用户实物录音。

## 验证与交付

- 本轮 27 项核心、41 项控制器/布局/个人库测试通过，记录为 `docs/measurements/release-v0.3.1-core-tests.json` 与 `release-v0.3.1-app-tests.json`。验证覆盖按游戏 PID 采音、进程切换和采音失败时停止监听，以及短声音触发、两次分析确认、连续监听、1／6／13／15／51 件候选布局、每行 1–4 件样式、小／中／大字号、多分辨率与 DPI、逐件试听、100 条历史和选中项保持。撤回 18 件无音效依据的条目后重新执行的来源核查记录见 `docs/measurements/sound-source-audit-app-tests.json`。
- 便携程序 UI smoke 报告为 `docs/measurements/overlay-row-variants-ui-smoke.json`，`passed` 为 true。1920×1032 工作区中，13 件示例操作视图 910×816 DIP、缩图 96 DIP，4 个格数组标题和 13 件完整名称均可见；15 件示例 910×681 DIP、缩图 96 DIP，2／4／6 格各自整行显示，全部 15 件在五行内；51 件声音候选示例仍在一页且无滚轮、无分页。1280×720 的应用布局测试在三档字号下以至少 80 DIP 缩图一页展示。回放与参考播放、采音暂停、字号切换保留编辑内容、单图钉、穿透恢复与相近参考隔离均通过。当前模拟渲染截图见 `docs/screenshots/overlay-row-variants-*.png`。
- 已更新并重新启动 `portable/AqtwListener.exe`。发布版运行 `--ui-smoke` 通过；发布前后核对 `portable/local-data/settings.json` 的 SHA-256 相同，个人设置未变。历史 `feature-*` 报告属于上一版。
- 用户约定：日常便携程序固定放在项目的 `portable` 文件夹，只更新最新一份，不区分版本；入口为 `portable/AqtwListener.exe`，更新保留 `portable/local-data`。GitHub v0.3.1 Release 另附不含个人资料的 ZIP；本地 `portable` 仍保持单份未压缩程序。
- 本机 SDK .tools/dotnet 10.0.401，NuGet 缓存 .tools/nuget。运行 `scripts/publish.ps1` 直接更新 portable，禁用联网包审计以离线构建，不导入旧测试目录里的个人数据或 smoke 输出。

## 保留的限制

基础库现有 51 件、9 组、14 段参考，版本 0.1.1-pickup-source-fix，未经独立准确率校准。先前仅凭名称录入的 18 件已撤回；开源项目检索结果见 [音效来源核查](../SOUND-SOURCE-AUDIT.md)。「双颈琴」未擅自映射到里拉琴；「琥珀天星」与「琥珀天心」名称关系仍需用户确认。界面“大红概率”是同格候选比例，不代表游戏真实出货概率。

真实游戏录音准确率、Discord／QQ／UU 各版本的声音隔离、UU 热键与音频转发、多屏热插拔、跨 DPI 原生拖动及 Esc 取消、独占全屏覆盖仍需实机验证。参考回归不能证明这些项目已完成。现有 Go build 不输出细分跳过原因；C# 会核对哪些样本未生成声纹并保留构建报告，不静默启用缺样本的新版本。

具体操作与数据约定见 [本版说明](../TOP-OVERLAY-PERSONAL-LIBRARY.md)，阶段验证见 [测试报告](../TEST-REPORT.md)。
