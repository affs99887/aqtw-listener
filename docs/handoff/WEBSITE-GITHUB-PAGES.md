# 行商助手介绍页 · GitHub Pages 维护

- 介绍页：https://affs99887.github.io/aqtw-listener/
- 仓库：`affs99887/aqtw-listener`；网页发布分支：`gh-pages`，保留 `.nojekyll`。
- 本轮网页提交：`4c74b9de6f345d5eea6152f9e45badf166473ab8`，对应软件 v0.5.3.1（`9cd50ac`、`895e12d` 曾指向已撤下的 v0.5.4；`301d202` 对应 v0.5.3；`39e6f96` 曾指向已撤下的 v0.6.0；`54ddda2` 对应 v0.5.2.1）。
- 导航、首屏和“开始使用”的主按钮指向 Gitee 附件 `https://gitee.com/scape0goat/aqtw-listener/releases/download/<版本>/XingshangAssistant-<版本>-win-x64.zip`；两个“从 GitHub 下载”直达 Release 附件。站内仍放一份 `download/<安装包>`（只留当前版本）和 `update/latest.json`、`update/latest.json.sig`，供更新器作为第二来源。`dist/.gitattributes` 让 `update/` 与安装包原样提交：签名覆盖原始字节，git 的换行转换曾使这里的签名失效。
- 国内打开慢：GitHub Pages 在国内本身时好时坏，免费又免备案的国内网页托管基本没有（Gitee Pages 已下线；EdgeOne 大陆节点需备案域名）。另有一份相同内容的备用介绍页 https://aqtw-listener.pages.dev/ ，Cloudflare Pages 项目 `aqtw-listener`，账号 `laidexin121@gmail.com`，用 `wrangler pages deploy` 部署（本机已 `wrangler login`）。Pages 单文件上限 25 MiB，安装包放不上去，所以只部署页面，下载按钮仍指向 Gitee。两处页面要一起更新：先推 `gh-pages`，再从发布检出排除 `download/` 和 `.git` 后部署。国内用户打不开时，备用页和 Gitee 仓库首页都可以。介绍页首屏下载量从 1,077 KB 降到 84 KB。

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
