# 浮窗截图说明

`shidian-v0.5.2.1-main.png` 与 `shidian-v0.5.2.1-overlay.png` 为更名后的“拾点”桌面界面检查截图。窗口与浮窗使用新名称，候选为模拟数据。

`two-card-left-six-20261014.png`、`two-card-left-thirteen-20261014.png`、`two-card-left-thirty-one-20261014.png` 为 v0.5.2 最新候选面板：最小宽度为两张卡片，名称统一左对齐，排满行右侧只保留 3 逻辑像素边框余量。中字号尺寸为 582×325、740×552、约 824×732（不含标题栏）。这些图片由实际界面组件在后台渲染，未启动应用窗口。

`balanced-candidates-thirteen-20261014.png` 和 `balanced-candidates-thirty-one-20261014.png` 是当前四列上限方案的候选面板，中字号分别为 740×552 和约 824×732 逻辑像素（不含标题栏）。13 件用 3 列、31 件用 4 列；图片由实际界面组件在内存中渲染，没有启动窗口。10.0% 差异率仅用于布局检查。

`compact-candidates-thirteen-20261014.png` 和 `compact-candidates-thirty-one-20261014.png` 是上一轮收窄布局，分别来自成品的 13 件与最大 31 件候选界面检查。中字号完整浮窗为 630×766 和 834×782 逻辑像素；物品组合及 1.0%／10.0% 差异率用于布局检查，不代表实际游戏识别结果。

`overlay-row-variants-simulated.png` 是当前**每行 1／2／3／4 件**的卡片对照截图，`overlay-row-variants-thirteen-simulated.png` 是同一布局在 13 件候选浮窗中的截图。物品组合及 96%／99% 匹配度均为布局测试数据，不代表实战识别结果或准确率。

`overlay-unified-live-simulated.png` 是 UI 自动检查生成的**模拟候选布局**：13 件候选及 99% 匹配度用于检查名称、分组、按钮和尺寸，不代表实际游戏识别结果或实测准确率。实时运行时，同一浮窗接收 Ctrl+Alt+L 启动的识别结果；候选数量和分数由实际音频决定。

`overlay-per-size-red-simulated.png` 是上一版卡片样式的**模拟候选布局**：2／4／6 格候选分别为 3／9／3 件，每组独占整行并显示该格“大红概率”；96% 匹配度仅用于检查独立分区，不代表当前的 1／2 件卡片样式。

`overlay-grouped-fifteen-simulated.png` 是上一版布局的历史截图，仍显示顶部总百分比，不代表当前界面。
