# 行商助手

暗区突围 PC 版听音辅助：拿起物品后显示同音候选，按格数分组，支持回放、试听和识别历史。

[国内下载（Gitee）](https://gitee.com/scape0goat/aqtw-listener/releases) · [GitHub 下载](https://github.com/affs99887/aqtw-listener/releases/latest) · [介绍与在线演示](https://aqtw-listener.pages.dev/) · [介绍页（GitHub Pages）](https://affs99887.github.io/aqtw-listener/)

## 开始使用

1. 下载并完整解压，运行 `行商助手.exe`，无需安装 .NET。
2. 启动游戏，按 **Ctrl+Alt+L** 开启监听，保持“声音自动识别”勾选。
3. 拿起货物，按格数查看候选；浮窗看不见时可尝试无边框全屏。

需要 64 位 Windows，系统 build 20348 或更高。助手仅在本地处理游戏进程声音。

## 常用操作

| 操作 | 快捷键或入口 |
| --- | --- |
| 开启 / 关闭监听 | Ctrl+Alt+L |
| 进入 / 退出浮窗操作 | Ctrl+Alt+C |
| 返回监听 / 取消移动 | Esc |
| 移动浮窗 | 解锁图钉后拖动标题栏 |
| 找回浮窗 | 托盘菜单 → 恢复顶部居中 |

关闭主窗口会隐藏到托盘；双击托盘图标恢复，通过托盘菜单“退出”结束程序。

候选每行最多四件。“大红概率”表示同格候选中的大红占比，不是游戏实际出货概率。

物品太多时，在主窗口“展示物品”页按声音分类勾选要显示的物品（默认全部显示）。不显示的物品仍计入大红概率，浮窗会注明“另有 N 件未显示”。

## 更新与反馈

助手启动时自动检查新版本，在主窗口顶部点“立即更新”即可下载、校验并替换程序，完成后自动重启；也可在“诊断”页手动检查。国内会从 Gitee 下载，签名和 SHA256 都对得上才会安装。设置、展示物品、录音和个人音效库保存在 `%LOCALAPPDATA%\AqtwListener`，所有下载的版本共用；首次运行新版会自动沿用旧版程序目录里的 `local-data`。

遇到问题可在“诊断”页导出诊断，再提交 [问题反馈](https://github.com/affs99887/aqtw-listener/issues)。版本变化见 [更新记录](https://github.com/affs99887/aqtw-listener/releases/latest)。
