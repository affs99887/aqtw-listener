# 行商助手介绍页 · GitHub Pages 维护

- 介绍页：https://affs99887.github.io/aqtw-listener/
- 仓库：`affs99887/aqtw-listener`；网页发布分支：`gh-pages`，保留 `.nojekyll`。
- 本轮网页提交：`54ddda27665a7fda00f4f4f138c922dbe28f2868`，对应软件 v0.5.2.1。
- 三个下载按钮统一指向 `https://github.com/affs99887/aqtw-listener/releases/download/v0.5.2.1/XingshangAssistant-v0.5.2.1-win-x64.zip`。

## 每次发版必须同步

1. 发布新版 Release，取得实际 Windows 便携 ZIP 的下载地址。
2. 更新介绍页全部下载按钮、版本文案、启动文件名称，以及需要变化的图标和功能说明。
3. 验证静态文件与演示数据，再提交、推送 `gh-pages`。
4. 等 GitHub Pages 对应提交构建完成，运行主仓库的 `scripts/check-introduction-release.ps1` 核对现网版本与下载链接；通过后再报告发版完成。

根目录 `AGENTS.md` 保存这项要求和 README 精简规则。

## 维护目录

网页项目：`C:/Users/Dexin/.codex/visualizations/2026/10/14/01a13ad8-3047-7ab2-a8af-ad002fe991dc/shidian-introduction`

发布检出：`C:/Users/Dexin/.codex/visualizations/2026/10/14/01a13ad8-3047-7ab2-a8af-ad002fe991dc/shidian-pages-publish`

网页项目的 `dist/` 就是静态成品；无需额外构建。只同步 `dist/` 内容到发布检出，不上传 `.openai`、`qa` 或项目根目录。维护前先确认发布检出干净，再拉取并快进到远程 `gh-pages`。这两个目录在主工作区之外，写入需使用当次环境允许的工具权限。

`node scripts/prepare-data.mjs <软件工作区>` 按软件 `Directory.Build.props` 的版本生成数据，并复制当前 PNG 图标和物品图；随后运行 `node scripts/check.mjs`。本轮 23 个候选类、150 件物品的分组和演示状态检查通过。

## 页面与验证

页面使用 HTML/CSS/JavaScript 的交互演示，支持候选分组、字号、模拟回放、暂停与返回监听、历史和自动演示。声音流程与差异率均为模拟，网页不采集麦克风。

本轮更新名称、版本、图标和下载入口，并补充关闭到托盘和自由定位说明。原有交互模型检查通过；浏览器工具因沙箱初始化异常未启动，本轮不宣称完成实际浏览器交互复测。现网上线和下载链接的检查记录保存在主工作区 `artifacts/introduction-release-sync-20261015.json`。
