using System.Windows.Media.Imaging;

namespace Listener.App;

internal static class Theme
{
    public static readonly Brush Background = Brush("#0D1118"), Panel = Brush("#161D27"), Muted = Brush("#8C9BB0"),
        Text = Brush("#F2F5F9"), Accent = Brush("#7CE6C2"), Gold = Brush("#F1C879"), Line = Brush("#2B3747");
    public static SolidColorBrush Brush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    public static TextBlock Label(string text, double size = 13, Brush? color = null) => new()
    { Text = text, FontSize = size, Foreground = color ?? Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7) };
    public static Border Box(UIElement child, double padding = 16) => new()
    { Child = child, Padding = new Thickness(padding), Background = Panel, CornerRadius = new CornerRadius(14), BorderBrush = Line, BorderThickness = new Thickness(1) };
    public static Button Button(string text, RoutedEventHandler click, bool primary = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 4, 8, 4),
            Background = primary ? Accent : Brush("#263347"), Foreground = primary ? Background : Text, BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand };
        button.Click += click; return button;
    }
}

internal sealed class CandidatePanel : Border
{
    private readonly TextBlock status = Theme.Label("监听已关闭", 12, Theme.Accent);
    private readonly TextBlock ratio = Theme.Label("—", 38, Theme.Gold);
    private readonly TextBlock count = Theme.Label("等待识别", 13, Theme.Muted);
    private readonly TextBlock phase = Theme.Label("", 11, Theme.Muted);
    private readonly TextBlock headline = Theme.Label("到达行商后，按快捷键开启", 13);
    private readonly TextBlock value = Theme.Label("", 12, Theme.Gold);
    private readonly StackPanel groups = new();
    private readonly Dictionary<string, BitmapImage> images = new();
    private readonly string libraryRoot;
    private readonly Settings settings;
    public CandidatePanel(string libraryRoot, Settings settings)
    {
        this.libraryRoot = libraryRoot; this.settings = settings;
        Background = Theme.Background; BorderBrush = Theme.Line; BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18); Padding = new Thickness(16);
        var body = new DockPanel(); Child = body;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); body.Children.Add(header);
        var titleRow = new DockPanel();
        var title = Theme.Label("行商听音", 18); title.FontWeight = FontWeights.Bold;
        var chip = Theme.Label("本地 · 音频识别", 10, Theme.Muted); chip.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(chip, Dock.Right); titleRow.Children.Add(chip); titleRow.Children.Add(title);
        header.Children.Add(titleRow); header.Children.Add(status);
        header.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 2, 0, 9) });
        header.Children.Add(Theme.Label("大金候选占比", 12, Theme.Muted));
        var ratioRow = new DockPanel();
        count.VerticalAlignment = VerticalAlignment.Bottom; count.Margin = new Thickness(12, 0, 0, 14);
        DockPanel.SetDock(ratio, Dock.Left); ratioRow.Children.Add(ratio); ratioRow.Children.Add(count); header.Children.Add(ratioRow);
        header.Children.Add(headline); header.Children.Add(value); header.Children.Add(phase);
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(Theme.Label("已知候选比例 ≠ 真实出货概率", 10, Theme.Muted));
        footer.Children.Add(Theme.Label("实验音效库 · 阈值和同音关系待实机验证", 10, Theme.Muted));
        footer.Children.Add(Theme.Label("回收参考价以核实后的目录记录为准", 10, Theme.Muted));
        DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        body.Children.Add(new ScrollViewer { Content = groups, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        ShowResult(null);
    }
    public void SetState(string text) => status.Text = text;
    public void ShowResult(RecognitionResult? result, bool demo = false)
    {
        groups.Children.Clear(); value.Text = ""; phase.Text = ""; value.Visibility = Visibility.Collapsed;
        if (result is null || result.CandidateCount == 0)
        {
            ratio.Text = "—"; count.Text = "无候选";
            headline.Text = result?.Message ?? "按快捷键开启，然后拖动一件货物";
            var empty = new StackPanel { Margin = new Thickness(0, 35, 0, 20), HorizontalAlignment = HorizontalAlignment.Center };
            empty.Children.Add(Theme.Label("◌", 48, Theme.Line));
            empty.Children.Add(Theme.Label("声音出现后，候选图片会显示在这里", 12, Theme.Muted)); groups.Children.Add(empty);
            return;
        }
        ratio.Text = $"{result.GoldCandidateRatio:P0}"; count.Text = $"{result.GoldCount} / {result.CandidateCount} 件候选为大金";
        headline.Text = demo ? "布局演示 · 非识别结果" : result.Message;
        phase.Text = demo ? "示例用于检查格数分组和缩略图展示" : $"{(result.IsFinal ? "稳定结果" : "初步结果")}  ·  {result.ElapsedMilliseconds:0} ms  ·  操作 #{result.OperationId}";
        var highest = result.HighestValue;
        value.Text = highest is null ? "" : $"已知参考价最高：{highest.Item.Name}  ¥{highest.Item.ReferenceValue:N0}";
        if (highest is not null) value.Visibility = Visibility.Visible;
        foreach (var group in result.Candidates.GroupBy(c => c.Item.Cells).OrderBy(g => g.Key))
        {
            var row = new DockPanel { Margin = new Thickness(0, 7, 0, 0) };
            var badge = Theme.Label($"{group.Count()} 件", 11, Theme.Muted); DockPanel.SetDock(badge, Dock.Right); row.Children.Add(badge);
            var label = Theme.Label($"{group.Key} 格", 13); label.FontWeight = FontWeights.SemiBold; row.Children.Add(label); groups.Children.Add(row);
            var wrap = new WrapPanel(); groups.Children.Add(wrap);
            foreach (var candidate in group.OrderByDescending(c => c.Item.IsGold).ThenByDescending(c => c.Item.ReferenceValue))
                wrap.Children.Add(Card(candidate, highest?.Item.Id == candidate.Item.Id,
                    candidate.Score >= result.BestMatch!.Score - .00001, demo));
        }
    }
    private UIElement Card(Candidate candidate, bool highest, bool best, bool demo)
    {
        var item = candidate.Item;
        var tile = new Grid { Width = 60, Height = 68 };
        if (item.Thumbnail is not null)
        {
            var path = Path.Combine(libraryRoot, item.Thumbnail);
            if (!images.TryGetValue(path, out var image))
            {
                image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 160; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); images[path] = image;
            }
            tile.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(5, 4, 5, 16) });
        }
        else tile.Children.Add(Theme.Label("缺少缩略图", 11, Theme.Muted));
        var tag = Theme.Label($"{item.GridWidth}×{item.GridHeight}", 10, Theme.Muted);
        tag.Margin = new Thickness(5, 0, 0, 2); tag.VerticalAlignment = VerticalAlignment.Bottom; tile.Children.Add(tag);
        var dot = new System.Windows.Shapes.Ellipse { Fill = item.IsGold ? Theme.Gold : Theme.Muted,
            Width = 5, Height = 5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 7, 7) };
        tile.Children.Add(dot);
        var stack = new StackPanel(); stack.Children.Add(tile);
        if (settings.ShowNames) stack.Children.Add(new TextBlock { Text = item.Name, Width = 60, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Theme.Text, FontSize = 10, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 5) });
        if (highest) stack.Children.Add(Theme.Label("参考价最高", 10, Theme.Gold));
        else if (best && !demo) stack.Children.Add(Theme.Label("最匹配", 9, Theme.Accent));
        return new Border { Child = stack, Background = Theme.Panel, BorderBrush = highest ? Theme.Gold : Theme.Line,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 7, 7),
            ToolTip = $"{item.Name}\n{item.GridWidth}×{item.GridHeight} · {(item.IsGold ? "红色收藏品 / 大金" : "非大金")}\n" +
                (item.ReferenceValue.HasValue ? $"联络人回收参考价：{item.ReferenceValue:N0}" : "联络人回收参考价待核实") };
    }
}

internal sealed class OverlayWindow : Window
{
    public CandidatePanel Panel { get; }
    public OverlayWindow(string root, Settings settings)
    {
        Title = "行商听音 · 浮窗"; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false;
        Topmost = true; ResizeMode = ResizeMode.NoResize;
        Panel = new(root, settings); Content = Panel; Apply(settings);
        SourceInitialized += (_, _) => NativeInput.MakeOverlay(this);
    }
    public void Apply(Settings settings)
    {
        Left = Math.Clamp(settings.Left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100);
        Top = Math.Clamp(settings.Top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100);
        Width = Math.Clamp(settings.Width, 300, 900); Height = Math.Clamp(settings.Height, 340, Math.Min(1200, SystemParameters.WorkArea.Height));
        Opacity = Math.Clamp(settings.Opacity, .35, 1);
    }
}
