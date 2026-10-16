# 项目维护要求

- README 保持简短，面向软件使用者，只保留必要的下载、使用、快捷键、更新和反馈说明；不得加入来源、鸣谢或致谢内容。
- 每次发布 GitHub Release 后，都必须同步介绍页，至少更新全部下载按钮到最新已发布的 Windows 便携附件，并核对页面版本、名称和图标。
- 介绍页有两个地址，内容相同，都要保持可用：https://aqtw-listener.pages.dev/ （Cloudflare Pages）和 https://affs99887.github.io/aqtw-listener/ （`gh-pages` 分支）。`gh-pages` 同时是更新信息的备用来源。现有项目和发布检出的位置及维护方法见 `docs/handoff/WEBSITE-GITHUB-PAGES.md`。
- 国内用户的下载和一键更新走 Gitee。每次发布 GitHub Release 后，运行 `scripts/publish-gitee.ps1`，把安装包和签名的 `latest.json` 发到 Gitee；再把生成的 `artifacts/release-<版本>/update/` 和安装包同步到介绍页的 `update/` 与 `download/`，介绍页主下载按钮指向 Gitee 附件。
- 更新信息的签名私钥和 Gitee 令牌只放在 `%USERPROFILE%\.aqtw-release\`，不得提交到仓库或写进聊天。
- 网站更新后运行 `scripts/check-introduction-release.ps1`，确认现网下载链接、Gitee 附件和两份签名的 `latest.json` 都与最新 Release 一致，再报告发版完成。
