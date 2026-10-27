# 行商助手介绍页 · GitHub Pages 维护

## 2026-10-27：旧版本手动升级说明

- 正式版仍为 v0.5.3.1。本轮只改文档和静态介绍页，不重新打包、不更新签名清单或下载附件。
- `gh-pages` 的 `index.html` 在首屏下载区域之后新增旧版本提醒，FAQ 区分 v0.5.3 网络受限与已撤下 v0.5.4 不提示降级；新增 `upgrade.html`、`upgrade.css`，完整展示 [手动升级指南](../UPGRADING.md)。指南 HTML 从该 Markdown 生成；以后维护时保持一致。首页页脚的“预览版”改为“正式版”。原有 3 个 Gitee、2 个 GitHub 下载按钮保持当前正式版地址。
- **Cloudflare 待同步：**当前执行环境没有 Cloudflare 发布连接，直接访问该站返回 HTTP 403，无法确认或更新其线上内容。本轮提供基于 `gh-pages` 原提交 `6dfd680ccffe95475d4484b535f3027ca88bd9a9` 的介绍页补丁，包含上述 3 个文件。GitHub Pages 修改不代表 Cloudflare 已自动更新；仍需按下文既有流程，从更新后的 `gh-pages` 排除 `.git`、`download/` 后部署到原 Cloudflare 项目。已有新文件时不要重复应用补丁。
- 验证：GitHub `releases/latest` 为 v0.5.3.1；Gitee 与 GitHub Pages 线上原始清单均通过现有公钥的 ECDSA P-256 / SHA-256 验签，包名、大小、哈希一致；完整下载 Gitee ZIP，实测 82,714,834 字节，SHA-256 为 `9b988d3ae2656c41f98f0fc06a45e20e3134bbd563688b440d431698bdd6eaea`，并核对 GitHub `SHA256SUMS.txt`。Gitee 本版没有同名校验文件附件，指南提供固定值并链接 GitHub 校验文件。
- 静态检查覆盖 HTML 标签、重复 ID、本地链接、下载按钮与版本一致性、README 限制和 `git diff --check`。当前 Linux 环境没有 Windows PowerShell / 项目 .NET SDK，无法直接执行 `scripts/check-introduction-release.ps1`，故用上述等价联网核验；浏览器内核下载失败，未完成桌面与移动端视觉验收。未运行 Windows 助手或触碰个人数据。

以下为原部署约定与历史记录。

- 介绍页两个地址都可用，内容相同，对外都要写：https://aqtw-listener.pages.dev/ （Cloudflare Pages 项目 `aqtw-listener`）和 https://affs99887.github.io/aqtw-listener/ （`gh-pages`）。页脚的“另一地址”按当前网址自动指向对方。`gh-pages` 同时提供 `update/latest.json` 给更新器作第二来源。
- 仓库：`affs99887/aqtw-listener`；网页发布分支：`gh-pages`，保留 `.nojekyll`。
- 本轮网页提交：`4c74b9de6f345d5eea6152f9e45badf166473ab8`，对应软件 v0.5.3.1（`9cd50ac`、`895e12d` 曾指向已撤下的 v0.5.4；`301d202` 对应 v0.5.3；`39e6f96` 曾指向已撤下的 v0.6.0；`54ddda2` 对应 v0.5.2.1）。
- 导航、首屏和“开始使用”的主按钮指向 Gitee 附件 `https://gitee.com/scape0goat/aqtw-listener/releases/download/<版本>/XingshangAssistant-<版本>-win-x64.zip`；两个“从 GitHub 下载”直达 Release 附件。站内仍放一份 `download/<安装包>`（只留当前版本）和 `update/latest.json`、`update/latest.json.sig`，供更新器作为第二来源。`dist/.gitattributes` 让 `update/` 与安装包原样提交：签名覆盖原始字节，git 的换行转换曾使这里的签名失效。
- 国内打开慢：GitHub Pages 在国内本身时好时坏，免费又免备案的国内网页托管基本没有（Gitee Pages 已下线；EdgeOne 大陆节点需备案域名）。介绍页因此同时部署在 Cloudflare Pages 和 GitHub Pages，两个地址都对外。Cloudflare 账号 `laidexin121@gmail.com`，用 `wrangler pages deploy` 部署（本机已 `wrangler login`）。Pages 单文件上限 25 MiB，安装包放不上去，所以只部署页面，下载按钮仍指向 Gitee。每次更新两处都要发：先推 `gh-pages`，再从发布检出排除 `download/` 和 `.git` 后部署到 Cloudflare。国内用户打不开时用 Gitee 仓库首页。介绍页首屏下载量从 1,077 KB 降到 84 KB。

## 每次发版必须同步

1. 发布新版 Release，取得实际 Windows 便携 ZIP 的下载地址。
2. 运行 `scripts/publish-gitee.ps1`，生成并签名 `artifacts/release-<版本>/update/latest.json`，发到 Gitee。
3. 更新介绍页全部下载按钮（Gitee 为主、GitHub 为备）、版本文案、启动文件名称，以及需要变化的图标和功能说明；把安装包放进 `dist/download/`（删掉旧版），把 `update/` 两个文件放进 `dist/update/`。
4. 运行 `prepare-data.mjs`，再用主仓库的 `.tools/research/venv/Scripts/python.exe scripts/optimize-assets.py` 缩图并把物品图转为 WebP（必须在 prepare-data 之后，它每次复制原图），然后 `node scripts/check.mjs`，提交、推送 `gh-pages`。
5. 等 GitHub Pages 对应提交构建完成，运行主仓库的 `scripts/check-introduction-release.ps1` 核对现网版本、下载链接、Gitee 附件与两份签名清单；刚构建完 CDN 可能还没刷新，失败时隔半分钟重跑。通过后再报告发版完成。

根目录 `AGENTS.md` 保存这项要求和 README 精简规则。

## 维护目录

网页项目：`C:/Users/Dexin/.codex/visualizations/2026/10/14/01a13ad8-3047-7ab2-a8af-ad002fe991dc/shidian-introduction`

发布检出：`C:/Users/Dexin/.codex/visualizations/2026/10/14/01a13ad8-3047-7ab2-a8af-ad002fe991dc/shidian-pages-publish`

网页项目的 `dist/` 就是静态成品；无需额外构建。只同步 `dist/` 内容到发布检出，不上传 `.openai`、`qa` 或项目根目录。维护前先确认发布检出干净，再拉取并快进到远程 `gh-pages`。这两个目录在主工作区之外，写入需使用当次环境允许的工具权限。

`node scripts/prepare-data.mjs <软件工作区>` 按软件 `Directory.Build.props` 的版本生成数据，并复制当前 PNG 图标和物品原图；随后运行 `scripts/optimize-assets.py` 缩图，再运行 `node scripts/check.mjs`。本轮 23 个候选类、150 件物品的分组和演示状态检查通过。

## 页面与验证

页面使用 HTML/CSS/JavaScript 的交互演示，支持候选分组、字号、模拟回放、暂停与返回监听、历史和自动演示。声音流程与差异率均为模拟，网页不采集麦克风。

v0.5.3.1 轮主下载改为 Gitee，FAQ“下载很慢怎么办？”指向 Gitee 仓库；标志图 1254 px 缩到 96 px，物品图最长边 160 px 的 WebP（`check.mjs` 已改为接受 PNG 或 WebP）。浏览器核对现网：19 张 WebP 全部显示，无坏图。

已撤下的 v0.5.4 轮曾把主下载改为 ghfast.top，保留 GitHub 直链，并补充“下载很慢怎么办？”。浏览器核对桌面与 390 像素宽：首屏“国内下载”“从 GitHub 下载”和“开始使用”的两个按钮互不重叠。

v0.5.3 轮更新下载入口、首屏与页脚版本，新增展示物品问答，并把“更新软件时，个人资料怎么办？”改为一键更新与 `%LOCALAPPDATA%\AqtwListener` 共享数据。`check.mjs` 通过；浏览器打开现网核对名称“行商助手”、头盔图标、v0.5.3 文案与新问答，页面不含 0.6.0，图片全部加载。

检查脚本在 Windows PowerShell 5.1 下运行即可（本机无 pwsh）；网页请求使用 `-UseBasicParsing`。

v0.5.2.1 轮更新名称、版本、图标和下载入口，并补充关闭到托盘和自由定位说明；当时的现网检查记录在主工作区 `artifacts/introduction-release-sync-20261015.json`。
