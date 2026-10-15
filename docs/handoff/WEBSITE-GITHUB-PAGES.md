# 拾点介绍网站 · GitHub Pages 交接

接收日期：2026-10-15。来源侧会话：`01a13ad8-3047-7ab2-a8af-ad002fe991dc`。以下为侧会话交接记录，本次未重复部署或复测。

后续变更：软件现名为“行商助手”，采用选定的 D 版 IND70 头盔透明图标；v0.5.2.1 Release 曾按要求删除，用户现已要求按同一版本号重新打包发布。官网名称、图标、截图与下载链接待下一次明确部署时核对，本次没有更新公网内容。

## 已发布网站

- 地址：https://affs99887.github.io/aqtw-listener/
- 仓库：`affs99887/aqtw-listener`。
- 发布分支：`gh-pages`，根目录含 `.nojekyll`；GitHub Pages 使用分支根目录发布，HTTPS 已开启。
- 发布提交：`9678b5e706bdd07d8f6bc7e111b3c715d1bdd7fe`。
- 侧会话验证记录：构建状态 `built`，公网 HTTP 200，远程发布树共 158 个文件，包含 150 张物品缩略图。
- 本网站独立于主程序的 `main` 分支与 Release 附件。

## 维护目录

网页项目：

`C:/Users/Dexin/.codex/visualizations/2026/10/14/01a13ad8-3047-7ab2-a8af-ad002fe991dc/shidian-introduction`

独立发布检出：

`C:/Users/Dexin/.codex/visualizations/2026/10/14/01a13ad8-3047-7ab2-a8af-ad002fe991dc/shidian-pages-publish`

发布检出跟踪 `origin/gh-pages`。维护时先核对远程最新状态，在网页项目修改并生成 `dist/`，再将 `dist/` 内容同步到发布检出、提交并推送 `gh-pages`。

仅发布 `dist/` 内的 `index.html`、`styles.css`、`app.js`、`model.js`、`data.js`、`assets/` 等成品。不要上传整个项目根目录；`.openai/hosting.json` 为早期未使用的 Sites 注册信息，最终部署采用 GitHub Pages。

项目内交接资料：

- `qa/github-pages-deployment.json`：部署记录。
- `qa/browser-checks.json`：浏览器交互检查。
- `qa/model-checks.json`：候选模型检查。
- `qa/introduction-desktop.png`：实际预览截图。
- `exports/shidian-introduction-github-pages.zip`：静态成品包。

以上目录在当前主工作区之外；后续修改前应确认当次环境的写入权限。

## 页面内容

暖白介绍页、深绿游戏背景与真实物品缩略图。浮窗由 HTML/CSS/JavaScript 组件构成，可点击并自动演示，并非整张界面截图。

支持 23 个候选类、150 件物品、6/1/27/31 件快捷场景、格数分组、最多四列、名称左对齐、字号切换、固定与收起恢复、模拟回放与录音选择、暂停及返回监听、大红比例演示和历史回放。自动演示依次推进监听、拿起、候选、回放、继续监听；手动操作暂停自动演示，并处理减少动态效果偏好与后台标签页。

网页不读取麦克风，也不播放真实录音；识别过程、录音与差异率均为模拟，页面已有说明。

## 验证边界

侧会话已完成本地浏览器验证：全部 23 个候选类、名称左对齐、按钮行为、自动推进、桌面与 390px 手机布局。31 件候选在内部滚动，舞台高度保持稳定，未发现控制台错误或横向溢出。

线上 HTML、CSS、全部 JavaScript、数据、图标及抽样物品图返回 200；线上交互浏览器复测因连接连续超时未完成。不要将本地交互检查表述为线上交互已实测。

专用本地服务 `127.0.0.1:43871` 已由侧会话停止；公网网站无需该服务。

## 后续同步项

交接时页面包含固定的 v0.5.2 Windows 压缩包链接和 latest Release 链接。主程序现已更名并发布“拾点 v0.5.2.1”，启动文件为 `拾点.exe`，Release 为：

https://github.com/affs99887/aqtw-listener/releases/tag/v0.5.2.1

后续维护官网时核对固定下载链接和启动说明是否仍指向旧版；此次仅记录交接，未修改网站。
