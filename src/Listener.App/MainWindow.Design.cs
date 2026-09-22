using System.Windows.Automation;
using System.Windows.Shell;

namespace Listener.App;

internal sealed partial class MainWindow
{
    private readonly TextBlock pageHeading = Theme.Label("监听控制", 26);
    private readonly TextBlock pageDescription = Theme.Label("听见线索，看清所有可能。", 12, Theme.Muted);
    private readonly TextBlock dashboardLibraryInfo = Theme.Label("", 19);
    private readonly TextBlock gameInfo = Theme.Label("切回游戏后开始采音", 11, Theme.Muted);
    private readonly TextBlock shortcutHint = Theme.Label("", 12, Theme.Muted);
    private Button? toggleListening;

    private UIElement BuildShell(out StackPanel navigation)
    {
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        { CaptionHeight = 43, ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(1), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        var shell = new DockPanel();
        var frame = new Border { Background = Theme.Background, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Child = shell };
        var caption = new DockPanel { Height = 43, Background = Theme.Brush("#15191B") };
        DockPanel.SetDock(caption, Dock.Top); shell.Children.Add(caption);
        var windowActions = new StackPanel { Orientation = Orientation.Horizontal };
        var minimize = CaptionButton("\uE921", "最小化到托盘", () => WindowState = WindowState.Minimized);
        var maximize = CaptionButton("\uE922", "最大化 / 还原", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        var close = CaptionButton("\uE8BB", "退出助手", Close);
        close.MouseEnter += (_, _) => close.Background = Theme.Brush("#813F4D");
        close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;
        windowActions.Children.Add(minimize); windowActions.Children.Add(maximize); windowActions.Children.Add(close);
        DockPanel.SetDock(windowActions, Dock.Right); caption.Children.Add(windowActions);
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(17, 0, 0, 0) };
        identity.Children.Add(new Image { Source = BrandAssets.Mark, Width = 23, Height = 23, Margin = new Thickness(0, 0, 10, 0) });
        var title = Theme.Label("行商听音助手", 12); title.Margin = new Thickness(0); title.VerticalAlignment = VerticalAlignment.Center; identity.Children.Add(title);
        caption.Children.Add(identity);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); columns.ColumnDefinitions.Add(new ColumnDefinition());
        shell.Children.Add(columns);
        var side = new DockPanel { Margin = new Thickness(14, 23, 14, 16) };
        var sidebar = new Border { Background = Theme.Brush("#15191B"), BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = side };
        columns.Children.Add(sidebar);
        var brand = new StackPanel { Margin = new Thickness(9, 0, 0, 25) };
        brand.Children.Add(new Image { Source = BrandAssets.Mark, Width = 47, Height = 47, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) });
        var name = Theme.Label("行商听音", 20); name.FontWeight = FontWeights.SemiBold; brand.Children.Add(name);
        brand.Children.Add(Theme.Label("听见更多可能", 11, Theme.Muted)); DockPanel.SetDock(brand, Dock.Top); side.Children.Add(brand);
        var footer = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        footer.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 0, 0, 15) });
        footer.Children.Add(Theme.Label("●  本地运行", 11, Theme.Accent));
        footer.Children.Add(Theme.Label("声音对比与个人库 · 预览版", 9, Theme.Muted)); DockPanel.SetDock(footer, Dock.Bottom); side.Children.Add(footer);
        navigation = new StackPanel(); side.Children.Add(navigation);
        var main = new DockPanel { Margin = new Thickness(26, 24, 26, 18) }; Grid.SetColumn(main, 1); columns.Children.Add(main);
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        pageHeading.FontWeight = FontWeights.SemiBold; heading.Children.Add(pageHeading); heading.Children.Add(pageDescription);
        DockPanel.SetDock(heading, Dock.Top); main.Children.Add(heading);
        main.Children.Add(new ScrollViewer { Content = settingsPage, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 3, 0) });
        return frame;
    }

    private static string SectionTitle(string section) => section switch
    { "监听" => "监听控制", "采音" => "采音与快捷键", "位置" => "浮窗位置", "浮窗" => "浮窗外观", "音效库" => "音效库", _ => "运行诊断" };
    private static string SectionDescription(string section) => section switch
    {
        "监听" => "听见线索，看清所有可能。",
        "采音" => "连接游戏声音，设置顺手的快捷键。",
        "位置" => "让候选信息停在你习惯的位置。",
        "浮窗" => "调整图片、字号与透明度，让线索清晰可见。",
        "音效库" => "查看声音参考，逐步完善自己的物品库。",
        _ => "检查输入、采音与识别状态。"
    };

    private Button CaptionButton(string glyph, string description, Action action)
    {
        var button = Theme.Button("", (_, _) => action()); button.Content = Theme.Glyph(glyph, 12);
        button.Width = 44; button.Margin = new Thickness(0); button.Padding = new Thickness(0);
        button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.ToolTip = description;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetName(button, description); WindowChrome.SetIsHitTestVisibleInChrome(button, true); return button;
    }

    private Button NavigationButton(string label)
    {
        var glyph = label switch { "监听" => "\uE7F6", "采音" => "\uE767", "位置" => "\uE707", "浮窗" => "\uE737", "音效库" => "\uE8F1", _ => "\uE9D9" };
        var row = new StackPanel { Orientation = Orientation.Horizontal }; var icon = Theme.Glyph(glyph, 17); icon.Margin = new Thickness(0, 0, 13, 0); row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var button = Theme.Button("", (_, _) => ShowSettingsSection(label)); button.Content = row; button.Height = 43;
        button.Margin = new Thickness(0, 0, 0, 7); button.Padding = new Thickness(13, 0, 0, 0); button.FontSize = 13;
        button.HorizontalContentAlignment = HorizontalAlignment.Left; AutomationProperties.SetName(button, label); return button;
    }

    private StackPanel BuildListeningPage()
    {
        var page = new StackPanel();
        var hero = new StackPanel();
        hero.Children.Add(Theme.Label("●  监听状态", 11, Theme.Accent));
        status.FontSize = 24; status.FontWeight = FontWeights.SemiBold; status.Foreground = Theme.Text; status.Margin = new Thickness(0, 3, 0, 6);
        hero.Children.Add(status);
        hero.Children.Add(Theme.Label("暗区突围专用 · 切出游戏暂停，切回后恢复。", 12, Theme.Muted));
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 9) };
        toggleListening = Theme.Button("开启监听", async (_, _) => await controller.Toggle(), true);
        toggleListening.Width = 168; toggleListening.FontSize = 14; toggleListening.HorizontalContentAlignment = HorizontalAlignment.Center;
        actionRow.Children.Add(toggleListening);
        shortcutHint.Margin = new Thickness(12, 0, 0, 0); shortcutHint.VerticalAlignment = VerticalAlignment.Center; actionRow.Children.Add(shortcutHint);
        hero.Children.Add(actionRow);
        hero.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 0, 0, 9) });
        automatic.Content = "声音自动识别"; automatic.FontSize = 12; automatic.Margin = new Thickness(0, 0, 0, 0);
        var automaticRow = new DockPanel(); DockPanel.SetDock(automatic, Dock.Left); automaticRow.Children.Add(automatic);
        var helper = Theme.Label("拖动物品发声后自动分析 · 适用于 UU 远程", 10, Theme.Muted); helper.Margin = new Thickness(14, 0, 0, 0); helper.VerticalAlignment = VerticalAlignment.Center;
        automaticRow.Children.Add(helper); hero.Children.Add(automaticRow);
        var heroCard = Theme.Box(hero, 20); heroCard.BorderBrush = Theme.Brush("#52605A"); page.Children.Add(heroCard);
        var facts = new Grid { Margin = new Thickness(0, 12, 0, 14) };
        facts.ColumnDefinitions.Add(new ColumnDefinition()); facts.ColumnDefinitions.Add(new ColumnDefinition());
        var game = new StackPanel(); game.Children.Add(Theme.Label("游戏进程", 10, Theme.Muted)); game.Children.Add(Theme.Label("UAGame", 19)); game.Children.Add(gameInfo);
        var gameCard = Theme.Box(game, 14); gameCard.Margin = new Thickness(0, 0, 6, 0); facts.Children.Add(gameCard);
        var catalog = new StackPanel(); catalog.Children.Add(Theme.Label("音效库", 10, Theme.Muted)); catalog.Children.Add(dashboardLibraryInfo); catalog.Children.Add(Theme.Label("本地参考 · 同音关系待实机验证", 11, Theme.Muted));
        var libraryCard = Theme.Box(catalog, 14); libraryCard.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(libraryCard, 1); facts.Children.Add(libraryCard); page.Children.Add(facts);
        page.Children.Add(Theme.Label("常用操作", 13));
        var actions = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        for (var i = 0; i < 3; i++) actions.ColumnDefinitions.Add(new ColumnDefinition());
        historyButton = Theme.Button("识别历史（0）", (_, _) => ShowHistory());
        var compare = Theme.Button("浮窗对比 / 操作", async (_, _) => await EnterInteraction());
        var test = Theme.Button("3 秒后试识别", async (_, _) => await controller.TestAfterCountdown());
        var buttons = new[] { historyButton, compare, test };
        for (var i = 0; i < buttons.Length; i++) { buttons[i].Padding = new Thickness(4, 12, 4, 12); buttons[i].FontSize = 11; buttons[i].HorizontalContentAlignment = HorizontalAlignment.Center; buttons[i].Margin = new Thickness(0, 0, i == 2 ? 0 : 8, 0); Grid.SetColumn(buttons[i], i); actions.Children.Add(buttons[i]); }
        page.Children.Add(actions); page.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 0, 0, 10) });
        var navigation = new DockPanel { LastChildFill = true };
        var clear = Theme.Button("清除结果", (_, _) => controller.ClearCurrentResult()); clear.Padding = new Thickness(9, 5, 9, 5); clear.FontSize = 11; clear.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(clear, Dock.Right); navigation.Children.Add(clear);
        previousCandidatePage = Theme.Button("上一页", (_, _) => overlay.Panel.MovePage(-1)); nextCandidatePage = Theme.Button("下一页", (_, _) => overlay.Panel.MovePage(1));
        foreach (var button in new[] { nextCandidatePage, previousCandidatePage }) { button.Padding = new Thickness(8, 5, 8, 5); button.FontSize = 11; button.Margin = new Thickness(5, 0, 0, 0); DockPanel.SetDock(button, Dock.Right); navigation.Children.Add(button); }
        candidatePageLabel.VerticalAlignment = VerticalAlignment.Center; candidatePageLabel.Margin = new Thickness(0); navigation.Children.Add(candidatePageLabel); page.Children.Add(navigation);
        var note = Theme.Label("匹配结果持续保留，直到新的匹配或手动清除。", 10, Theme.Muted); note.Margin = new Thickness(0, 10, 0, 0); page.Children.Add(note);
        RefreshListeningPresentation(); return page;
    }

    private void RefreshListeningPresentation()
    {
        if (toggleListening is null) return;
        toggleListening.Content = controller.Enabled ? "关闭监听" : "开启监听";
        shortcutHint.Text = settings.Hotkey;
        gameInfo.Text = controller.Foreground ? "游戏在前台" : "切回游戏后开始采音";
        if (tray is not null) tray.Text = "行商听音助手 · " + (controller.Enabled ? "监听已开启" : "监听已关闭");
    }
}
